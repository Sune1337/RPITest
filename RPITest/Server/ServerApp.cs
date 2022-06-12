namespace RPITest.Server;

using System.Net;
using System.Net.Sockets;

public class ServerApp
{
    #region Fields

    private readonly List<RemoteClient> _remoteClients = new();

    private readonly TcpListener _tcpListener = new(new IPEndPoint(IPAddress.Any, 1000));

    private bool _running = true;

    #endregion

    #region Constructors and Destructors

    public ServerApp(string[] args)
    {
        Console.CancelKeyPress += WhenCancelKeyPress;
    }

    #endregion

    #region Public Methods and Operators

    public void Run()
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

        Console.WriteLine("Quitted.");
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
