using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Threading;
using Websocket.Client;

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length != 4)
        {
            Console.WriteLine("Usage: PortForwardClient <LocalPort> <ServerURL> <RemoteHost> <RemotePort>");
            return;
        }

        int localPort = int.Parse(args[0]);
        string serverUrl = args[1];
        string remoteHost = args[2];
        int remotePort = int.Parse(args[3]);

        // Ensure serverUrl starts with ws:// or wss://
        if (!serverUrl.StartsWith("ws://") && !serverUrl.StartsWith("wss://"))
        {
            serverUrl = "ws://" + serverUrl;
        }

        // Append remote host and port to the URL
        string wsUrl = $"{serverUrl}/{remoteHost}/{remotePort}";
        
        var tcpListener = new TcpListener(IPAddress.Any, localPort);
        tcpListener.Start();
        Console.WriteLine($"Listening on port {localPort}");

        while (true)
        {
            var client = await tcpListener.AcceptTcpClientAsync();
            _ = HandleClientAsync(client, wsUrl);
        }
    }

    static async Task HandleClientAsync(TcpClient tcpClient, string wsUrl)
    {
        try
        {
            using (tcpClient)
            using (var networkStream = tcpClient.GetStream())
            {
                var exitEvent = new ManualResetEvent(false);
                using (var websocket = new WebsocketClient(new Uri(wsUrl)))
                {
                    // Handle incoming WebSocket messages
                    websocket.MessageReceived.Subscribe(msg =>
                    {
                        if (msg.Binary != null)
                        {
                            networkStream.Write(msg.Binary, 0, msg.Binary.Length);
                        }
                    });

                    websocket.DisconnectionHappened.Subscribe(info =>
                    {
                        exitEvent.Set();
                    });

                    await websocket.Start();

                    // Read from TCP and send to WebSocket
                    byte[] buffer = new byte[4096];
                    while (true)
                    {
                        int bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead == 0) break;

                        await websocket.SendAsync(buffer[..bytesRead]);
                    }

                    await websocket.Stop(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}