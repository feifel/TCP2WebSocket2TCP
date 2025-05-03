using Microsoft.AspNetCore.Server.IIS;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = int.MaxValue; // Or set an appropriate limit
});

var app = builder.Build();

// Configure websocket options
var webSocketOptions = new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromMinutes(2),
    ReceiveBufferSize = 4096
};
app.UseWebSockets(webSocketOptions);

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
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("Only WebSocket requests are supported");
    }
});

app.Run();

async Task HandleWebSocketConnection(WebSocket webSocket, string remoteHost, int remotePort)
{
    try
    {
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(remoteHost, remotePort);
        using var networkStream = tcpClient.GetStream();

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
        // Log the error
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