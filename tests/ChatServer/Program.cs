using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ChatServer;

class EchoServer
{
    static async Task Main(string[] args)
    {
        int port = 12345; // Default port
        if (args.Length > 0)
        {
            port = int.Parse(args[0]);
        }

        var server = new TcpListener(IPAddress.Any, port);
        server.Start();
        Console.WriteLine($"Chat Server listening on port {port}");

        while (true)
        {
            TcpClient client = await server.AcceptTcpClientAsync();
            _ = HandleClientAsync(client);
        }
    }

    static async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            Console.WriteLine($"Client connected: {client.Client.RemoteEndPoint}");
            using (client)
            await using (NetworkStream stream = client.GetStream())
            {
                byte[] buffer = new byte[1024];
                while (true)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break; // Client disconnected

                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead).TrimEnd();
                    string response = $"Echo: {message}";
                    Console.WriteLine(response);

                    byte[] responseData = Encoding.UTF8.GetBytes(response + "\n");
                    await stream.WriteAsync(responseData, 0, responseData.Length);
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