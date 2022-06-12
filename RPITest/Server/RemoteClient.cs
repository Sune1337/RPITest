namespace RPITest.Server;

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

using Google.Protobuf;

using MultimediaTimer;

using RPITest.Protocol;

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

    private readonly Stopwatch _stopwatch = new();
    private long _counter = 0;
    private double _lastElapsed = 0;
    private int _rpi = 0;

    private Timer? _timer;

    private UdpClient? _udpClient;

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
            try
            {
                var readBytes = await networkStream.ReadAsync(memory, _cancellationTokenSource.Token);
                if (readBytes <= 0)
                {
                    // Reading 0 bytes means the client disconnected.
                    break;
                }

                if (rpiRequest == null)
                {
                    // Parse request from client.
                    rpiRequest = RpiRequest.Parser.ParseFrom(_receiveBuffer, 0, readBytes);

                    // Create UDPClient to use when sending packets to client.
                    _udpClient = new UdpClient();
                    _udpClient.Connect(((IPEndPoint)RemoteEndpoint!).Address, rpiRequest.Port);

                    // Initialize timer-misfire counters.
                    _stopwatch.Start();
                    _rpi = rpiRequest.Rpi;

                    // Start timer to send UDP packets to client.
                    _timer = new Timer(rpiRequest.Rpi);
                    _timer.Elapsed += TimerOnElapsed;
                    _timer.Start();
                }
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

        _clientStopped.Set();
    }

    public void Stop()
    {
        _cancellationTokenSource.Cancel();
        TcpClient.Close();
        _stopwatch.Stop();
        _clientStopped.WaitOne();
    }

    #endregion

    #region Methods

    private void TimerOnElapsed(object? sender, EventArgs e)
    {
        var currentElapsed = _stopwatch.ElapsedTicks;
        var elapsed = (currentElapsed - _lastElapsed) / TimeSpan.TicksPerMillisecond - _rpi;
        _lastElapsed = currentElapsed;

        var rpiMessage = new RpiMessage
        {
            ServerMisfire = elapsed,
            PacketCounter = _counter++
        };

        using var memoryStream = new MemoryStream(_sendBuffer, sizeof(int), _sendBuffer.Length - sizeof(int));
        rpiMessage.WriteTo(memoryStream);
        BitConverter.TryWriteBytes(_sendBuffer, (int)memoryStream.Position);
        
        _udpClient?.Send(_sendBuffer);
    }

    #endregion
}
