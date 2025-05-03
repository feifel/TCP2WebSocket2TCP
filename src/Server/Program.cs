using System.Net.WebSockets;
using System.Net.Sockets;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddConsole(); // Add console logger

// Add any additional services if needed
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = int.MaxValue; // Or set an appropriate limit
});

var app = builder.Build();

// Configure websocket options
var webSocketOptions = new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20)
};
app.UseWebSockets(webSocketOptions);

// Add basic health check endpoint
app.MapGet("/health", () => "Healthy");

app.Use(async (context, next) =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var path = context.Request.Path.Value?.TrimStart('/').Split('/');
        if (path?.Length != 2)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Invalid path format. Use /<remoteHost>/<remotePort>");
            return;
        }

        string remoteHost = path[0];
        if (!int.TryParse(path[1], out int remotePort))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Invalid port number");
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await HandleWebSocketConnection(webSocket, remoteHost, remotePort);
    }
    else
    {
        await next();
    }
});

app.Run();

async Task HandleWebSocketConnection(WebSocket webSocket, string remoteHost, int remotePort)
{
    try
    {
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(remoteHost, remotePort);
        await using var networkStream = tcpClient.GetStream();

        var cancellation = new CancellationTokenSource();
        var receiveTask = ReceiveWebSocketMessages(webSocket, networkStream, cancellation.Token);
        var sendTask = SendTcpDataToWebSocket(webSocket, networkStream, cancellation.Token);

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
        app.Logger.LogError(ex, "Error in WebSocket connection");
        if (webSocket.State == WebSocketState.Open)
        {
            await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError,
                "Connection error", CancellationToken.None);
        }
    }
}

async Task ReceiveWebSocketMessages(WebSocket webSocket, NetworkStream networkStream,
    CancellationToken cancellationToken)
{
    var buffer = new byte[4096];
    try
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                break;

            await networkStream.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
        }
    }
    catch (OperationCanceledException)
    {
        // Normal cancellation, ignore
    }
}

async Task SendTcpDataToWebSocket(WebSocket webSocket, NetworkStream networkStream,
    CancellationToken cancellationToken)
{
    var buffer = new byte[4096];
    try
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            int bytesRead = await networkStream.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (bytesRead == 0)
                break;

            await webSocket.SendAsync(buffer.AsMemory(0, bytesRead),
                WebSocketMessageType.Binary, true, cancellationToken);
        }
    }
    catch (OperationCanceledException)
    {
        // Normal cancellation, ignore
    }
}