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

        if (!int.TryParse(args[0], out int localPort))
        {
            Console.WriteLine("Invalid local port number");
            return;
        }

        string serverUrl = args[1];
        string remoteHost = args[2];

        if (!int.TryParse(args[3], out int remotePort))
        {
            Console.WriteLine("Invalid remote port number");
            return;
        }

        // Ensure serverUrl starts with ws:// or wss://
        if (!serverUrl.StartsWith("ws://") && !serverUrl.StartsWith("wss://"))
        {
            serverUrl = "ws://" + serverUrl;
        }

        // Remove trailing slash if present
        serverUrl = serverUrl.TrimEnd('/');

        // Append remote host and port to the URL
        string wsUrl = $"{serverUrl}/{remoteHost}/{remotePort}";

        try
        {
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
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex.Message}");
        }
    }

    static async Task HandleClientAsync(TcpClient tcpClient, string wsUrl)
    {
        var cts = new CancellationTokenSource();
        try
        {
            Console.WriteLine($"New client connected from {tcpClient.Client.RemoteEndPoint}");

            using (tcpClient)
            {
                var networkStream = tcpClient.GetStream();
                var url = new Uri(wsUrl);
                using var websocket = new WebsocketClient(url);

                // Configure WebSocket client
                websocket.ReconnectTimeout = null;
                websocket.ErrorReconnectTimeout = TimeSpan.FromSeconds(10);
                websocket.MessageReceived.Subscribe(
                    msg =>
                    {
                        try
                        {
                            if (msg.Binary != null)
                            {
                                networkStream.Write(msg.Binary, 0, msg.Binary.Length);
                                Console.WriteLine($"Received {msg.Binary.Length} bytes from WebSocket");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error writing to TCP stream: {ex.Message}");
                            cts.Cancel();
                        }
                    },
                    ex =>
                    {
                        Console.WriteLine($"Error in WebSocket receive: {ex.Message}");
                        cts.Cancel();
                    });

                websocket.DisconnectionHappened.Subscribe(info =>
                {
                    Console.WriteLine($"WebSocket disconnected: {info.Type} - {info.Exception?.Message}");
                    if (info.Type != DisconnectionType.Exit)
                    {
                        cts.Cancel();
                    }
                });

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
                    while (!cts.Token.IsCancellationRequested)
                    {
                        int bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                        if (bytesRead == 0) break; // Client disconnected

                        byte[] dataToSend = new byte[bytesRead];
                        Array.Copy(buffer, 0, dataToSend, 0, bytesRead);

                        websocket.Send(dataToSend);
                        Console.WriteLine($"Sent {bytesRead} bytes to WebSocket");
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation, ignore
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading from TCP stream: {ex.Message}");
                }
                finally
                {
                    // Clean shutdown
                    try
                    {
                        await websocket.Stop(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "Closing");
                        await Task.Delay(1000, CancellationToken.None); // Give it time to close cleanly
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error during WebSocket shutdown: {ex.Message}");
                    }
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