# TCP port forwarder via WebSockets 
This TCP port forwarding via WebSockets is useful whenever direct TCP connections are not possible, e.g. because of Firewall configuration that can't be changed for various reasons. By using WebSockets only HTTP Port must be permitted which is often the case. The server is deployed via IIS and forwards WebSocket streams to the desired Host and Port. The Client is listening to a local TCP port and forwards the TCP streams via WebSocket to the server. The whole process is demonstrated by a simple chat server and client, on client side you send messages to the server that sends back an echo.

## How this project was setup:
```
# create project directory
mkdir TCP2WebSocket2TCP
cd TCP2WebSocket2TCP

# create README.md
echo "# TCP port forwarder via WebSockets" > README.md

# git init (run on git bash)
git init 
dotnet new gitignore

# Create the solution  
dotnet new sln -n TCP2WebSocket2TCP  
  
# create folders
mkdir src tests  
  
# Create the server (web API)  
cd src  
dotnet new webapi -n Server  
  
# Create the client (console app)  
dotnet new console -n Client  
  
cd ../tests  
  
# Create test projects  
dotnet new console -n ChatServer
dotnet new console -n ChatClient
  
cd ..  
  
# Add projects to the solution  
dotnet sln add src/Server/Server.csproj  
dotnet sln add src/Client/Client.csproj  
dotnet sln add tests/ChatServer/ChatServer.csproj  
dotnet sln add tests/ChatClient/ChatClient.csproj

# commit initial project setup  (run on git bash)
git add .
git commit -m "Initial project setup"
```

After that I generated the code for all 4 Programs.cs in this porject via ChatLLM from abacus.ai using Claude Sonnet 3.5 by using the following prompts:

To generate the port forwarder:
```
Please write a C# Client/Server app, that does TCP portforwarding via WebSocket. The client should accept the following arguments:
1. LocalPort
2. ServerURL
3. RemoteHost
4. RemotePort
The Client should then append /<RemoteHost>/<RemotePort> to the server URL and create a WebSocket to that URL. 
The Server should be deployed in IIS and read the RemoteHost and RemotePort and create a TCP connection to that host and port and forward the data between the WebSocket and the TCP connection.
```

To generate a Client/Server test application:
```
Please also create a sample client/server app that communicates via TCP socket and to demonstrate that the TCP port forwarding is working. The Client should be a command line app that forwards the user input to the Server via TCP and displays the response of the server on the command line. The Server should just return "Echo: " followed by the users input and write this also on the System.out.
```

## Usage
1. Deploy the server in IIS  (Note: WebSocket Protocol needs to be installed)
2. Start the Client with the following parameters:
    - LocalPort, e.g. 12344
    - ServerURL, wss://localhost/WebSocket2TCP
    - RemoteHost, localhost
    - RemotePort, 12345

-> This forwards the TCP traffic from localhost:12344 to localhost:12345 but uses the WebSocket on localhost/WebSocket2TCP inbetween. 

## Test app
To test the TCP forwarding over WebSocket you can use the ChatClient and ChatServer located in the tests folder.

To test it do this:
1. Deploy the server in IIS (Note: WebSocket Protocol needs to be installed)
2. Start the Client with: Client.exe 12344 wss://localhost/WebSocket2TCP localhost 12345
3. Start the ChatServer with: ChatServer.exe 12345
4. Start the ChatClient with: ChatClient.exe localhost 12344

```mermaid
sequenceDiagram  
    participant ChatClient.exe  
    participant Client.exe  
    participant Server.exe  
    participant ChatServer.exe  
  
    ChatClient.exe->>Client.exe: TCP  
    Client.exe->>Server.exe: WebSocket  
    Server.exe->>ChatServer.exe: TCP
```