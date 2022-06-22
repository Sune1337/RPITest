namespace RPITest.Client;

using System.CommandLine;
using System.CommandLine.Parsing;
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
        var nicOption = new Option<string>("--nic", getDefaultValue: () => "localhost", description: "Name of NIC to use for hw timestamps");
        var rootCommand = new RootCommand
        {
            hostOption,
            portOption,
            rpiOption,
            nicOption
        };

        rootCommand.SetHandler(ClientAppCommand, hostOption, portOption, rpiOption, nicOption);
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

    private async Task ClientAppCommand(string host, int port, int rpi, string nic)
    {
        if (string.IsNullOrEmpty(nic))
        {
            throw new Exception("You must specify which NIC to use using --nic parameter.");
        }

        // Start correlating NIC and System clock.
        NicClockCorrelation.Start(nic);

        Console.Error.WriteLine("Waiting for NIC/System clock sync...");
        NicClockCorrelation.WaitForSync();
        Console.Error.WriteLine("In sync!");

        // Start listening for UDP packets.
        using var udpClient = new UdpClient(319);
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
        var localPacketCounter = 0L;
        decimal lastRxTimestamp = NicClockCorrelation.GetTimestamp();
        ReceivedInfo? lastReceivedInfo = null;
        var lastKeepAlive = DateTime.Now;
        while (true)
        {
            var socketError = socketTimestamp.Receive(new Span<byte>(_receiveBuffer, 0, _receiveBuffer.Length), out var bytesTransferred, out var rxTimestamp);
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

            var ptpSyncMessage = MarshalHelper.FromBytes<PtpSyncMessage>(_receiveBuffer);
            var rpiMessageLength = BitConverter.ToInt32(ptpSyncMessage.Suffix);
            var rpiMessage = RpiMessage.Parser.ParseFrom(ptpSyncMessage.Suffix, sizeof(int), rpiMessageLength);
            var currentMissingPackets = rpiMessage.PacketCounter - localPacketCounter;
            var lastTxLatency = -1m;

            if (currentMissingPackets > 0)
            {
                Console.Error.WriteLine($"Missing {currentMissingPackets} packets.");
                rpiStatistics.FeedMissingPackets(currentMissingPackets);
            }

            if (currentMissingPackets == 0 && rpiMessage.LastTxLatency > 0)
            {
                // Adjust elapsed time with txLatency from server.
                lastTxLatency = (decimal)rpiMessage.LastTxLatency;

                // Now that server has sent TxLatency for last packet, update statistics.
                if (lastReceivedInfo != null)
                {
                    lastReceivedInfo.Elapsed -= lastTxLatency;

                    rpiStatistics.Feed(lastReceivedInfo.Elapsed, lastReceivedInfo.Misfire, (decimal)rpiMessage.LastTxLatency);
                }
            }

            var elapsed = rxTimestamp - lastRxTimestamp;

            if (elapsed <= 0 || lastTxLatency <= 0 || double.IsNaN(rpiMessage.ServerMisfire))
            {
                // Server has sent invalid data.
                lastReceivedInfo = null;
            }
            else
            {
                var misfire = (decimal)rpiMessage.ServerMisfire;

                // Update last ReceivedInfo.
                elapsed = NicClockCorrelation.ToSystemTicks(elapsed) / TimeSpan.TicksPerMillisecond - rpi - misfire + lastTxLatency;

                lastReceivedInfo ??= new ReceivedInfo();
                lastReceivedInfo.Elapsed = elapsed;
                lastReceivedInfo.Misfire = misfire;
            }

            // Update variables that hold information from last loop.
            lastRxTimestamp = rxTimestamp;
            localPacketCounter = rpiMessage.PacketCounter + 1;

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

        #endregion
    }
}
