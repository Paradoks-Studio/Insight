﻿using Mirror;
using System.Collections.Generic;
using Telepathy;
using UnityEngine;

namespace Insight
{
    public class InsightServer : InsightCommon
    {
        protected int serverHostId = -1;

        Server server; //Telepathy Server

        protected Dictionary<int, InsightNetworkConnection> connections;

        protected List<SendToAllFinishedCallbackData> sendToAllFinishedCallbacks = new List<SendToAllFinishedCallbackData>();

        public virtual void Start()
        {
            DontDestroyOnLoad(this);
            Application.runInBackground = true;

            // use Debug.Log functions for Telepathy so we can see it in the console
            Telepathy.Logger.LogMethod = Debug.Log;
            Telepathy.Logger.LogWarningMethod = Debug.LogWarning;
            Telepathy.Logger.LogErrorMethod = Debug.LogError;

            // create and start the server
            server = new Server();

            connections = new Dictionary<int, InsightNetworkConnection>();

            messageHandlers = new Dictionary<short, InsightNetworkMessageDelegate>();

            if (AutoStart)
            {
                StartInsight();
            }
        }

        public virtual void Update()
        {
            HandleNewMessages();
            CheckCallbackTimeouts();
        }

        public void StartInsight(int Port)
        {
            networkPort = Port;

            StartInsight();
        }

        public override void StartInsight()
        {
            if (logNetworkMessages) { Debug.Log("[InsightServer] - Start On Port: " + networkPort); }
            server.Start(networkPort);
            serverHostId = 0;

            connectState = ConnectState.Connected;

            OnStartInsight();
        }

        public override void StopInsight()
        {
            connections.Clear();

            // stop the server when you don't need it anymore
            server.Stop();
            serverHostId = -1;

            connectState = ConnectState.Disconnected;

            OnStopInsight();
        }

        // grab all new messages. do this in your Update loop.
        public void HandleNewMessages()
        {
            if (serverHostId == -1)
                return;

            Message msg;
            while (server.GetNextMessage(out msg))
            {
                switch (msg.eventType)
                {
                    case Telepathy.EventType.Connected:
                        HandleConnect(msg);
                        break;
                    case Telepathy.EventType.Data:
                        HandleData(msg.connectionId, msg.data, 0);
                        break;
                    case Telepathy.EventType.Disconnected:
                        HandleDisconnect(msg);
                        break;
                }
            }
        }

        void HandleConnect(Message msg)
        {
            Debug.Log("connectionID: " + msg.connectionId, this);

            // get ip address from connection
            string address = GetConnectionInfo(msg.connectionId);

            // add player info
            InsightNetworkConnection conn = new InsightNetworkConnection();
            conn.Initialize(this, address, serverHostId, msg.connectionId);
            AddConnection(conn);

            OnConnected(conn);
        }

        void HandleDisconnect(Message msg)
        {
            InsightNetworkConnection conn;
            if (connections.TryGetValue(msg.connectionId, out conn))
            {
                conn.Disconnect();
                RemoveConnection(msg.connectionId);

                OnDisconnected(conn);
            }
        }

        void HandleData(int connectionId, byte[] data, byte error)
        {
            InsightNetworkConnection conn;

            NetworkReader reader = new NetworkReader(data);
            var msgType = reader.ReadInt16();
            var callbackId = reader.ReadInt32();
            InsightNetworkConnection insightNetworkConnection;
            if(!connections.TryGetValue(connectionId, out insightNetworkConnection))
            {
                Debug.LogError("HandleData: Unknown connectionId: " + connectionId, this);
                return;
            }

            if (callbacks.ContainsKey(callbackId))
            {
                var msg = new InsightNetworkMessage(insightNetworkConnection, callbackId) { msgType = msgType, reader = reader };
                callbacks[callbackId].callback.Invoke(CallbackStatus.Ok, msg);
                callbacks.Remove(callbackId);

                CheckForFinishedCallback(callbackId);
            }
            else 
            {
                insightNetworkConnection.TransportReceive(data);
            }
        }

        public string GetConnectionInfo(int connectionId)
        {
            string address;
            server.GetConnectionInfo(connectionId, out address);
            return address;
        }

        public bool AddConnection(InsightNetworkConnection conn)
        {
            if (!connections.ContainsKey(conn.connectionId))
            {
                // connection cannot be null here or conn.connectionId
                // would throw NRE
                connections[conn.connectionId] = conn;
                conn.SetHandlers(messageHandlers);
                return true;
            }
            // already a connection with this id
            return false;
        }

        public bool RemoveConnection(int connectionId)
        {
            return connections.Remove(connectionId);
        }

        public bool SendToClient(int connectionId, short msgType, MessageBase msg, CallbackHandler callback)
        {
            if (server.Active)
            {
                NetworkWriter writer = new NetworkWriter();
                writer.Write(msgType);

                int callbackId = 0;
                if (callback != null)
                {
                    callbackId = ++callbackIdIndex; // pre-increment to ensure that id 0 is never used.
                    callbacks.Add(callbackId, new CallbackData() { callback = callback, timeout = Time.realtimeSinceStartup + CALLBACKTIMEOUT });
                }

                writer.Write(callbackId);

                msg.Serialize(writer);

                return connections[connectionId].Send(writer.ToArray());
            }
            Debug.Log("Server.Send: not connected!", this);
            return false;
        }

        public bool SendToClient(int connectionId, short msgType, MessageBase msg)
        {
            return SendToClient(connectionId, msgType, msg, null);
        }

        public bool SendToClient(int connectionId, byte[] data)
        {
            if (server.Active)
            {
                return server.Send(connectionId, data);
            }
            Debug.Log("Server.Send: not connected!", this);
            return false;
        }

        public bool SendToAll(short msgType, MessageBase msg, CallbackHandler callback, SendToAllFinishedCallbackHandler finishedCallback)
        {
            if (server.Active)
            {
                SendToAllFinishedCallbackData finishedCallbackData = new SendToAllFinishedCallbackData() { requiredCallbackIds = new HashSet<int>() };

                foreach (KeyValuePair<int, InsightNetworkConnection> conn in connections)
                {
                    SendToClient(conn.Key, msgType, msg, callback);
                    finishedCallbackData.requiredCallbackIds.Add(callbackIdIndex);
                }

                // you can't have _just_ the finishedCallback, although you _can_ have just
                // "normal" callback. 
                if (finishedCallback != null && callback != null)
                {
                    finishedCallbackData.callback = finishedCallback;
                    finishedCallbackData.timeout = Time.realtimeSinceStartup + CALLBACKTIMEOUT;
                    sendToAllFinishedCallbacks.Add(finishedCallbackData);
                }
                return true;
            }
            Debug.Log("Server.Send: not connected!", this);
            return false;
        }

        public bool SendToAll(short msgType, MessageBase msg, CallbackHandler callback)
        {
            return SendToAll(msgType, msg, callback, null);
        }

        public bool SendToAll(short msgType, MessageBase msg)
        {
            return SendToAll(msgType, msg, null, null);
        }

        public bool SendToAll(byte[] bytes)
        {
            if (server.Active)
            {
                foreach (var conn in connections)
                {
                    conn.Value.Send(bytes);
                }
                return true;
            }
            Debug.Log("Server.Send: not connected!", this);
            return false;
        }

        private void OnApplicationQuit()
        {
            if (logNetworkMessages) { Debug.Log("[InsightServer] Stopping Server"); }
            server.Stop();
        }

        private void CheckForFinishedCallback(int callbackId)
        {
            foreach (var item in sendToAllFinishedCallbacks)
            {
                if (item.requiredCallbackIds.Contains(callbackId)) item.callbacks++;
                if (item.callbacks >= item.requiredCallbackIds.Count)
                {
                    item.callback.Invoke(CallbackStatus.Ok);
                    sendToAllFinishedCallbacks.Remove(item);
                    return;
                }
            }
        }

        protected override void CheckCallbackTimeouts()
        {
            base.CheckCallbackTimeouts();
            foreach (var item in sendToAllFinishedCallbacks)
            {
                if (item.timeout < Time.realtimeSinceStartup)
                {
                    item.callback.Invoke(CallbackStatus.Timeout);
                    sendToAllFinishedCallbacks.Remove(item);
                    return;
                }
            }
        }

        //----------virtual handlers--------------//

        public virtual void OnConnected(InsightNetworkConnection conn)
        {
            if (logNetworkMessages) { Debug.Log("[InsightServer] - Client connected from: " + conn.address); }
        }

        public virtual void OnDisconnected(InsightNetworkConnection conn)
        {
            if (logNetworkMessages) { Debug.Log("[InsightServer] - OnDisconnected()"); }
        }

        public virtual void OnStartInsight()
        {
            if (logNetworkMessages) { Debug.Log("[InsightServer] - OnStartInsight()"); }
        }

        public virtual void OnStopInsight()
        {
            if (logNetworkMessages) { Debug.Log("[InsightServer] - OnStopInsight()"); }
        }
    }
}