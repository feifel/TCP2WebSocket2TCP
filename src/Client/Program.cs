using System.Net;
using System.Net.Sockets;
using Websocket.Client;

namespace Client;

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
        Console.WriteLine($"Port forwarder listening on port {localPort}");
        Console.WriteLine($"Forwarding to {remoteHost}:{remotePort} via {wsUrl}");

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
            Console.WriteLine($"New client connected from {tcpClient.Client.RemoteEndPoint}");

            using (tcpClient)
            await using (var networkStream = tcpClient.GetStream())
            {
                var exitEvent = new ManualResetEvent(false);
                var url = new Uri(wsUrl);

                using (var websocket = new WebsocketClient(url))
                {
                    // Configure WebSocket client
                    websocket.ReconnectTimeout = null;
                    websocket.ErrorReconnectTimeout = TimeSpan.FromSeconds(10);

                    // Handle incoming WebSocket messages
                    websocket.MessageReceived.Subscribe(msg =>
                    {
                        try
                        {
                            if (msg.Binary != null)
                            {
                                networkStream.Write(msg.Binary, 0, msg.Binary.Length);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error writing to TCP stream: {ex.Message}");
                            exitEvent.Set();
                        }
                    });

                    // Handle disconnection
                    websocket.DisconnectionHappened.Subscribe(info =>
                    {
                        Console.WriteLine($"WebSocket disconnected: {info.Type}");
                        if (info.Type != DisconnectionType.Exit)
                        {
                            exitEvent.Set();
                        }
                    });

                    // Handle reconnection
                    websocket.ReconnectionHappened.Subscribe(info =>
                    {
                        Console.WriteLine($"WebSocket reconnected: {info.Type}");
                    });

                    await websocket.Start();
                    Console.WriteLine("WebSocket connected");

                    // Read from TCP and send to WebSocket
                    try
                    {
                        byte[] buffer = new byte[4096];
                        while (!exitEvent.WaitOne(0))
                        {
                            int bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length);
                            if (bytesRead == 0) break; // Client disconnected

                            // Create a new array with just the read bytes
                            byte[] dataToSend = new byte[bytesRead];
                            Array.Copy(buffer, 0, dataToSend, 0, bytesRead);

                            // Send the data using the correct method
                            websocket.Send(dataToSend);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error reading from TCP stream: {ex.Message}");
                    }

                    // Clean shutdown
                    await websocket.Stop(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "Closing");
                    await Task.Delay(1000); // Give it time to close cleanly
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling client: {ex.Message}");
        }
        finally
        {
            Console.WriteLine("Client disconnected");
        }
    }
}