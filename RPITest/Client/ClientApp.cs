namespace RPITest.Client;

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

using Google.Protobuf;

using RPITest.Protocol;
using RPITest.Statistics;

using Win32Sockets;

public class ClientApp
{
    #region Fields

    private readonly ParseResult _parsedCommand;

    private readonly byte[] _receiveBuffer = new byte[64 * 1024];

    #endregion

    #region Constructors and Destructors

    public ClientApp(string[] args)
    {
        var hostOption = new Option<string>("--host", getDefaultValue: () => "localhost", description: "IP address or hostname to conenct to");
        var portOption = new Option<int>("--port", getDefaultValue: () => 1000, description: "Port to connect to");
        var rpiOption = new Option<int>("--rpi", getDefaultValue: () => 100, description: "Interval between packets to receive");
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
        var socketTimestamp = new SocketTimestamp(udpClient.Client);
        socketTimestamp.ConfigureSocket(SocketTimestamp.TimestampingFlag.Rx);
        udpClient.Client.ReceiveTimeout = Constants.KeepaliveInterval;

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
        var rpiStatistics = new RpiStatistics();
        var localCounter = 0L;
        decimal lastRxTimestamp = Stopwatch.GetTimestamp();
        long lastMissingPackets = 0;
        ReceivedInfo? lastReceivedInfo = null;
        var lastKeepAlive = DateTime.Now;
        while (true)
        {
            var socketError = socketTimestamp.Receive(new Span<byte>(_receiveBuffer, 0, _receiveBuffer.Length), out var bytesTransferred, out var rxTimestamp, out var rxLatency);
            if (socketError == SocketError.TimedOut)
            {
                // Send keepalive.
                rpiRequest.WriteTo(networkStream);
                lastKeepAlive = DateTime.Now;
                continue;
            }
            
            if (socketError != SocketError.Success)
            {
                throw new Exception($"SocketError: {socketError}");
            }

            var rpiMessageLength = BitConverter.ToInt32(_receiveBuffer);
            var rpiMessage = RpiMessage.Parser.ParseFrom(_receiveBuffer, sizeof(int), rpiMessageLength);
            var currentMissingPackets = rpiMessage.PacketCounter - localCounter;
            var lastTxLatency = 0m;

            // Now that server has sent TxLatency for last packet, update statistics.
            if (lastReceivedInfo != null)
            {
                if (currentMissingPackets == lastMissingPackets)
                {
                    // Adjust elapsed time with txLatency from server.
                    lastTxLatency = (decimal)rpiMessage.LastTxLatency;
                }
                
                lastReceivedInfo.Elapsed -= lastTxLatency;
                rpiStatistics.Feed(lastReceivedInfo.Elapsed, lastReceivedInfo.Misfire, lastReceivedInfo.RxLatency, (decimal)rpiMessage.LastTxLatency, lastMissingPackets);
            }

            var misfire = (decimal)rpiMessage.ServerMisfire;
            var elapsed = (rxTimestamp - lastRxTimestamp) / TimeSpan.TicksPerMillisecond - rpi - misfire + lastTxLatency;

            // Update last ReceivedInfo.
            lastReceivedInfo ??= new ReceivedInfo();
            lastReceivedInfo.Elapsed = elapsed;
            lastReceivedInfo.Misfire = misfire;
            lastReceivedInfo.RxLatency = rxLatency;

            // Update variables that hold information from last loop.
            lastRxTimestamp = rxTimestamp;
            lastMissingPackets = currentMissingPackets;
            localCounter++;

            if ((DateTime.Now - lastKeepAlive).TotalMilliseconds > Constants.KeepaliveInterval)
            {
                // Send keepalive.
                rpiRequest.WriteTo(networkStream);
                lastKeepAlive = DateTime.Now;
            }
        }
    }

    #endregion

    private class ReceivedInfo
    {
        #region Fields

        public decimal Elapsed;
        public decimal Misfire;
        public decimal RxLatency;

        #endregion
    }
}
