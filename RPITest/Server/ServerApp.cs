namespace RPITest.Server;

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net;
using System.Net.Sockets;

using Win32Sockets;

public class ServerApp
{
    #region Fields

    private readonly ParseResult _parsedCommand;

    private readonly List<RemoteClient> _remoteClients = new();

    private readonly TcpListener _tcpListener = new(new IPEndPoint(IPAddress.Any, 1000));

    private bool _running = true;

    #endregion

    #region Constructors and Destructors

    public ServerApp(string[] args)
    {
        Console.CancelKeyPress += WhenCancelKeyPress;

        var nicOption = new Option<string>("--nic", getDefaultValue: () => "localhost", description: "Name of NIC to use for hw timestamps");
        var rootCommand = new RootCommand
        {
            nicOption
        };

        rootCommand.SetHandler(ServerAppCommand, nicOption);
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

    private void AcceptClients()
    {
        while (_running)
        {
            try
            {
                // Wait for a client to connect.
                var tcpClient = _tcpListener.AcceptTcpClient();

                // Echo some verbose information.
                Console.WriteLine($"Client connected: {tcpClient.Client.RemoteEndPoint}");

                // Save the connected client to the list of connected clients.
                var remoteClient = new RemoteClient(tcpClient);
                remoteClient.ClientDisconnect += WhenClientDisconnect;
                lock (_remoteClients)
                {
                    _remoteClients.Add(remoteClient);
                }

                remoteClient.Start();
            }

            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.Interrupted)
                {
                    // A blocking listen has been cancelled.
                    break;
                }
            }
        }
    }

    private async Task ServerAppCommand(string nic)
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

        try
        {
            // Start client and start accepting clients.
            _tcpListener.Start();
            AcceptClients();

            // Stop timer and stop accepting clients.
            _tcpListener.Stop();

            // Disconnect all currently connected clients.
            lock (_remoteClients)
            {
                foreach (var remoteClient in _remoteClients)
                {
                    // Echo some verbose information and disconnect client.
                    Console.WriteLine($"Disconnecting client: {remoteClient.RemoteEndpoint}");
                    remoteClient.Stop();
                }
            }
        }

        finally
        {
            NicClockCorrelation.Stop();
        }

        Console.WriteLine("Quitted.");
    }

    private void WhenCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        Console.WriteLine("Quitting...");
        _running = false;
        _tcpListener.Stop();
        e.Cancel = true;
    }

    private void WhenClientDisconnect(RemoteClient remoteClient)
    {
        // Echo some verbose information.
        Console.WriteLine($"Client disconnected: {remoteClient.TcpClient.Client.RemoteEndPoint}");

        lock (_remoteClients)
        {
            _remoteClients.Remove(remoteClient);
        }
    }

    #endregion
}
