namespace RPITest.Server;

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

using Google.Protobuf;

using MultimediaTimer;

using RPITest.Protocol;

using Win32Sockets;

public delegate void ClientDisconnect(RemoteClient remoteClient);

public class RemoteClient
{
    #region Constants

    private const int ReceiveBufferSize = 32 * 1024;
    private const int SendBufferSize = 1400;

    #endregion

    #region Fields

    public readonly EndPoint? RemoteEndpoint;
    public readonly TcpClient TcpClient;

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ManualResetEvent _clientStopped = new(false);

    private readonly byte[] _receiveBuffer = new byte[ReceiveBufferSize];
    private readonly byte[] _sendBuffer = new byte[SendBufferSize];

    private Socket? _clientSocket;

    private long _counter = 0;
    private decimal _lastTimestamp = 0;
    private decimal _lastTxLatency = 0;
    private int _rpi = 0;
    private SocketTimestamp? _socketTimestamp;

    private Timer? _timer;

    #endregion

    #region Constructors and Destructors

    public RemoteClient(TcpClient tcpClient)
    {
        TcpClient = tcpClient;
        RemoteEndpoint = tcpClient.Client.RemoteEndPoint;
    }

    #endregion

    #region Public Events

    public event ClientDisconnect? ClientDisconnect;

    #endregion

    #region Public Methods and Operators

    public async Task Start()
    {
        await using var networkStream = TcpClient.GetStream();

        var memory = _receiveBuffer.AsMemory(0, ReceiveBufferSize);
        RpiRequest? rpiRequest = null;
        while (_cancellationTokenSource.IsCancellationRequested == false)
        {
            using var timeoutToken = new CancellationTokenSource(Constants.KeepaliveInterval + 5000);
            try
            {
                using var linkedCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, timeoutToken.Token);

                var readBytes = await networkStream.ReadAsync(memory, linkedCancellationToken.Token);
                if (readBytes <= 0)
                {
                    // Reading 0 bytes means the client disconnected.
                    break;
                }

                if (rpiRequest == null)
                {
                    // Parse request from client.
                    rpiRequest = RpiRequest.Parser.ParseFrom(_receiveBuffer, 0, readBytes);

                    // Create socket to use when sending packets to client.
                    _clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    _clientSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    _clientSocket.Bind(new IPEndPoint(IPAddress.Any, 319));
                    _socketTimestamp = new SocketTimestamp(_clientSocket);
                    _socketTimestamp.ConfigureSocket(SocketTimestamp.TimestampingFlag.Tx);
                    _clientSocket.Connect(((IPEndPoint)RemoteEndpoint!).Address, 319);

                    // Initialize variables used measure timer misfire.
                    _rpi = rpiRequest.Rpi;
                    _lastTimestamp = NicClockCorrelation.GetTimestamp();

                    // Start timer to send UDP packets to client.
                    _timer = new Timer(rpiRequest.Rpi);
                    _timer.Elapsed += TimerOnElapsed;
                    _timer.Start();
                }
            }

            catch (OperationCanceledException)
            {
                if (timeoutToken.IsCancellationRequested)
                {
                    Console.Error.WriteLine("Client timed out.");
                }

                break;
            }

            catch
            {
                break;
            }
        }

        // Stop the timer.
        _timer?.Stop();

        if (_cancellationTokenSource.IsCancellationRequested == false)
        {
            // Tell manager the client disconnected so it can clean up after us.
            ClientDisconnect?.Invoke(this);
        }

        _clientSocket?.Close();
        _clientStopped.Set();
    }

    public void Stop()
    {
        _cancellationTokenSource.Cancel();
        TcpClient.Close();
        _clientStopped.WaitOne();
    }

    #endregion

    #region Methods

    private void TimerOnElapsed(object? sender, EventArgs e)
    {
        var currentHwTimestamp = NicClockCorrelation.GetTimestamp();

        #region Code is included in Tx latency.

        if (_socketTimestamp == null)
        {
            return;
        }

        var misfire = NicClockCorrelation.ToSystemTicks(currentHwTimestamp - _lastTimestamp) / TimeSpan.TicksPerMillisecond - _rpi;

        var rpiMessage = new RpiMessage
        {
            ServerMisfire = currentHwTimestamp < _lastTimestamp ? double.NaN : (double)misfire,
            PacketCounter = _counter++,
            LastTxLatency = (double)_lastTxLatency
        };

        // Serialize protobuf message.
        using var memoryStream = new MemoryStream(_sendBuffer, sizeof(int), _sendBuffer.Length - sizeof(int));
        rpiMessage.WriteTo(memoryStream);
        BitConverter.TryWriteBytes(_sendBuffer, (int)memoryStream.Position);

        // Create PtpSyncMessage.
        var ptpSyncMessage = new PtpSyncMessage(2);
        Array.Copy(_sendBuffer, ptpSyncMessage.Suffix, memoryStream.Position + sizeof(int));
        var ptpSyncMessageBytes = MarshalHelper.GetBytes(ptpSyncMessage);

        var timestampTimeout = Stopwatch.GetTimestamp() + TimeSpan.TicksPerMillisecond * 100;
        var socketError = _socketTimestamp.Send(new Span<byte>(ptpSyncMessageBytes), timestampTimeout, out var txTimestamp);

        #endregion

        #region Code is included in Misfire if rpi is or will be exceeded.

        if (socketError != SocketError.Success)
        {
            // Tell manager the client disconnected so it can clean up after us.
            ClientDisconnect?.Invoke(this);

            Stop();
        }

        // Calculate Tx latency.
        _lastTxLatency = NicClockCorrelation.ToSystemTicks(txTimestamp - currentHwTimestamp) / TimeSpan.TicksPerMillisecond;
        _lastTimestamp = currentHwTimestamp;

        #endregion
    }

    #endregion
}
