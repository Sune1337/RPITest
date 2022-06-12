namespace RPITest.Client;

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

using Google.Protobuf;

using RPITest.Protocol;
using RPITest.Statistics;

public class ClientApp
{
    #region Fields

    private readonly ParseResult _parsedCommand;

    private readonly MultimediaTimer.Timer _timer = new MultimediaTimer.Timer(10);

    #endregion

    #region Constructors and Destructors

    public ClientApp(string[] args)
    {
        var hostOption = new Option<string>("--host", getDefaultValue: () => "localhost", description: "IP address or hostname to conenct to");
        var portOption = new Option<int>("--port", getDefaultValue: () => 1000, description: "Port to connect to");
        var rpiOption = new Option<int>("--rpi", getDefaultValue: () => 10, description: "Interval between packets to receive");
        var rootCommand = new RootCommand
        {
            hostOption,
            portOption,
            rpiOption
        };

        rootCommand.SetHandler(ClientAppCommand, hostOption, portOption, rpiOption);
        _parsedCommand = rootCommand.Parse(args);
    }

    #endregion

    #region Public Methods and Operators

    public void Run()
    {
        _parsedCommand.Invoke();
    }

    #endregion

    #region Methods

    private async Task ClientAppCommand(string host, int port, int rpi)
    {
        // Start listening for UDP packets.
        using var udpClient = new UdpClient(0);

        // Open control connection.
        using var tcpClient = new TcpClient();
        tcpClient.Connect(host, port);

        // Send the requested RPI to the server.
        var rpiRequest = new RpiRequest
        {
            Port = ((IPEndPoint)udpClient.Client.LocalEndPoint!).Port,
            Rpi = rpi
        };

        using var networkStream = tcpClient.GetStream();
        rpiRequest.WriteTo(networkStream);

        // Start receiving UDP packets.
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        decimal lastElapsed = stopwatch.ElapsedTicks;
        var rpiStatistics = new RpiStatistics();
        var localCounter = 0L;
        while (true)
        {
            var data = await udpClient.ReceiveAsync();
            var currentElapsed = stopwatch.ElapsedTicks;

            var rpiMessageLength = BitConverter.ToInt32(data.Buffer);
            var rpiMessage = RpiMessage.Parser.ParseFrom(data.Buffer, sizeof(int), rpiMessageLength);
            
            var elapsed = (currentElapsed - lastElapsed) / TimeSpan.TicksPerMillisecond - rpi - (decimal)rpiMessage.ServerMisfire;

            rpiStatistics.Feed(elapsed, rpiMessage.PacketCounter - localCounter);

            lastElapsed = currentElapsed;
            localCounter++;
        }

        stopwatch.Stop();
    }

    #endregion
}
