﻿using Insight;
using UnityEngine;

//The server side of this load test will send data out to all clients using the PacketsPerSecond and PayloadSizeInBits
//This will work along with the data being echoed from the clients to all other clients.

public class LoadTestServerModule : InsightModule
{
    InsightCommon insight;
    ModuleManager manager;

    [Header("Broadcast From Server")]
    public float PacketDelay = 0.1f;
    public int PayloadSizeInBytes;

    private char[] dataArray;

    public override void Initialize(InsightCommon insight, ModuleManager manager)
    {
        this.insight = insight;
        this.manager = manager;

        RegisterHandlers();

        FillDataArray();

        InvokeRepeating("SendServerData", 1f, 0.1f);
    }

    public override void RegisterHandlers()
    {
        insight.RegisterHandler(ClientLoadTestMsg.MsgId, HandleClientLoadTestMsg);
    }

    private void SendServerData()
    {
        
        insight.SendMsgToAll(ServerLoadTestMsg.MsgId, new ServerLoadTestMsg() { Payload = dataArray });
    }

    private void FillDataArray()
    {
        dataArray = new char[PayloadSizeInBytes / 2]; //Since the payload is a char array that is 2 bytes we divide by 2.

        for (int i = 0; i < PayloadSizeInBytes / 2; i++)
        {
            dataArray[i] = 'a';
        }
    }

    private void HandleClientLoadTestMsg(InsightNetworkMessage netMsg)
    {

    }
}