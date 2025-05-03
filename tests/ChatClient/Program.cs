// EchoClient.cs
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

class EchoClient
{
    static async Task Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: EchoClient <host> <port>");
            return;
        }

        string host = args[0];
        int port = int.Parse(args[1]);

        try
        {
            using (TcpClient client = new TcpClient())
            {
                await client.ConnectAsync(host, port);
                Console.WriteLine($"Connected to {host}:{port}");

                using (NetworkStream stream = client.GetStream())
                {
                    // Start a task to read responses
                    var readTask = ReadResponsesAsync(stream);

                    // Handle user input
                    Console.WriteLine("Type your messages (press Ctrl+C to exit):");
                    while (true)
                    {
                        string input = Console.ReadLine();
                        if (string.IsNullOrEmpty(input)) continue;

                        byte[] data = Encoding.UTF8.GetBytes(input + "\n");
                        await stream.WriteAsync(data, 0, data.Length);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    static async Task ReadResponsesAsync(NetworkStream stream)
    {
        byte[] buffer = new byte[1024];
        try
        {
            while (true)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break; // Server disconnected

                string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                Console.Write(response);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nConnection lost: {ex.Message}");
            Environment.Exit(1);
        }
    }
}