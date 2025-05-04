using System.Net.WebSockets;
using System.Net.Sockets;
using NLog;
using NLog.Web;

// Early init of NLog to allow startup logging  
var logger = LogManager.Setup()
    .LoadConfigurationFromFile("nlog.config")
    .GetCurrentClassLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    var instance = 1;

    // Add NLog  
    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    builder.Services.Configure<IISServerOptions>(options =>
    {
        options.MaxRequestBodySize = int.MaxValue;
    });

    var app = builder.Build();

    // Test logging  
    app.Logger.LogInformation("Application Started");

    var webSocketOptions = new WebSocketOptions
    {
        KeepAliveInterval = TimeSpan.FromSeconds(20)
    };
    app.UseWebSockets(webSocketOptions);

    // Add basic health check endpoint
    app.MapGet("/health", () => "Healthy");

    // Explicitly specify RequestDelegate to resolve the ambiguity using Microsoft.AspNetCore.Http;  
    app.Use(async (context, next) =>
    {
        app.Logger.LogInformation($"Received request for {context.Request.Path}");

        // Skip processing for health endpoint  
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        app.Logger.LogInformation($"IsWebSocketRequest: {context.WebSockets.IsWebSocketRequest}");
        app.Logger.LogInformation($"Headers: {string.Join(", ", context.Request.Headers.Select(h => $"{h.Key}={h.Value}"))}");
    
        var path = context.Request.Path.Value?.TrimStart('/').Split('/');
        if (path?.Length != 2)
        {
            context.Response.StatusCode = 400;
            app.Logger.LogError("Invalid path format. Use /<remoteHost>/<remotePort>");
            await context.Response.WriteAsync("Invalid path format. Use /<remoteHost>/<remotePort>");
            return;
        }
    
        string remoteHost = path[0];
        if (!int.TryParse(path[1], out int remotePort))
        {
            context.Response.StatusCode = 400;
            app.Logger.LogError($"Invalid port number: {path[1]}");
            await context.Response.WriteAsync("Invalid port number");
            return;
        }
    
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            app.Logger.LogError("Not a WebSocket request");
            await context.Response.WriteAsync("Not a WebSocket request");
            return;
        }
    
        try
        {
            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            app.Logger.LogInformation("WebSocket connection established");
            await HandleWebSocketConnection(webSocket, remoteHost, remotePort);
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Error handling WebSocket connection");
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Internal Server Error");
            }
        }
    });

    app.Run();

    async Task HandleWebSocketConnection(WebSocket webSocket, string remoteHost, int remotePort)
    {
        var name = $"{remoteHost}:{remotePort}({instance++})";
        try
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(remoteHost, remotePort);
            await using var networkStream = tcpClient.GetStream();

            var cancellation = new CancellationTokenSource();
            var receiveTask = ReceiveWebSocketMessages(name, webSocket, networkStream, cancellation.Token);
            var sendTask = SendTcpDataToWebSocket(name, webSocket, networkStream, cancellation.Token);
            app.Logger.LogInformation($"{name} connection established");
            await Task.WhenAny(receiveTask, sendTask);
            cancellation.Cancel();

            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure,
                    "Connection closed", CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, $"{name} Error in WebSocket connection");
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError,
                    $"{name} Connection error", CancellationToken.None);
            }
        }
    }

    async Task ReceiveWebSocketMessages(string name, WebSocket webSocket, NetworkStream networkStream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        try
        {
            app.Logger.LogTrace($"{name} Start forwarding to Server...");
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                app.Logger.LogTrace($"{name} Forward {result.Count} bytes to Server...");
                await networkStream.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation, ignore
        }
    }

    async Task SendTcpDataToWebSocket(string name, WebSocket webSocket, NetworkStream networkStream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        try
        {
            app.Logger.LogTrace($"{name} Start forwarding to Client...");
            while (!cancellationToken.IsCancellationRequested)
            {
                int bytesRead = await networkStream.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (bytesRead == 0)
                    break;

                app.Logger.LogTrace($"{name} Forward {bytesRead} bytes to Client...");
                await webSocket.SendAsync(buffer.AsMemory(0, bytesRead),
                    WebSocketMessageType.Binary, true, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation, ignore
        }
    }
}
catch (Exception ex)
{
    logger.Error(ex, "Stopped program because of exception");
    throw;
}
finally
{
    LogManager.Shutdown();
}