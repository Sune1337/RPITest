namespace Win32Sockets;

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

using Win32Sockets.Interop;

using SocketAddress = Win32Sockets.Interop.SocketAddress;

public class SocketTimestamp
{
    #region Fields

    private readonly Socket _socket;
    private DynamicWinsockMethods? _dynamicWinsockMethods;
    private uint _timestampId;
    private DynamicWinsockMethods.WSARecvMsgDelegate? _wsaRecvMsg;

    #endregion

    #region Constructors and Destructors

    public SocketTimestamp(Socket socket)
    {
        _socket = socket;
        _wsaRecvMsg = new DynamicWinsockMethods().GetWSARecvMsgDelegate(_socket.SafeHandle);
    }

    #endregion

    #region Enums

    public enum TimestampingFlag
    {
        Rx = 1,
        Tx = 2
    }

    #endregion

    #region Properties

    private DynamicWinsockMethods DynamicWinsockMethods => _dynamicWinsockMethods ??= new DynamicWinsockMethods();
    private DynamicWinsockMethods.WSARecvMsgDelegate WsaRecvMsg => _wsaRecvMsg ??= DynamicWinsockMethods.GetWSARecvMsgDelegate(_socket.SafeHandle);

    #endregion

    #region Public Methods and Operators

    public void ConfigureSocket(TimestampingFlag timestampingFlag)
    {
        try
        {
            // Value found at https://www.magnumdb.com/search?q=IOC_VENDOR.
            // Also: https://microsoft.github.io/windows-docs-rs/doc/windows/Win32/Networking/WinSock/constant.SIO_TIMESTAMPING.html
            const int SIO_TIMESTAMPING = unchecked((int)2550137067);

            var timestampingConfig = GetBytes(
                new Interop.Interop.Winsock.TimestampingConfig
                {
                    Flags = (uint)timestampingFlag,
                    TxTimestampsBuffered = (ushort)(timestampingFlag == TimestampingFlag.Tx ? 5 : 0)
                }
            );

            var result = _socket.IOControl(SIO_TIMESTAMPING, timestampingConfig, null);
            if (result != 0)
            {
                throw new Exception($"Failed to set SIO_TIMESTAMPING. result: {result}");
            }
        }

        catch (Exception ex)
        {
            if (NicClockCorrelation.ClockMode == ClockModeEnum.Hardware)
            {
                throw;
            }

            Console.Error.WriteLine($"Failed to set SIO_TIMESTAMPING. Exception: {ex.Message}");
        }
    }

    public unsafe SocketError Receive(Span<byte> buffer, out int bytesTransferred, out long rxTimestamp)
    {
        if (_socket.LocalEndPoint == null)
        {
            throw new ArgumentNullException(nameof(_socket.LocalEndPoint));
        }

        var ipEndpoint = (IPEndPoint)_socket.LocalEndPoint;
        var socketAddress = new SocketAddress(ipEndpoint.Address, ipEndpoint.Port);
        var controlBuffer = new byte[1024];

        fixed (byte* controlBufferPtr = &MemoryMarshal.GetReference(new Span<byte>(controlBuffer)))
        fixed (byte* bufferPtr = &MemoryMarshal.GetReference(buffer))
        fixed (byte* ptrSocketAddress = socketAddress.Buffer)
        {
            Interop.Interop.Winsock.WSAMsg wsaMsg;
            wsaMsg.socketAddress = (IntPtr)ptrSocketAddress;
            wsaMsg.addressLength = (uint)socketAddress.Size;
            wsaMsg.flags = SocketFlags.None;

            Interop.Interop.Winsock.WSABuffer wsaBuffer;
            wsaBuffer.Length = buffer.Length;
            wsaBuffer.Pointer = (IntPtr)bufferPtr;
            wsaMsg.buffers = (IntPtr)(&wsaBuffer);
            wsaMsg.count = 1;

            wsaMsg.controlBuffer.Length = controlBuffer.Length;
            wsaMsg.controlBuffer.Pointer = (IntPtr)controlBufferPtr;

            var socketResult = WsaRecvMsg(_socket.SafeHandle, (IntPtr)(&wsaMsg), out bytesTransferred, null, IntPtr.Zero);
            var appRxTimestamp = Stopwatch.GetTimestamp();
            rxTimestamp = appRxTimestamp;

            if (socketResult != SocketError.Success)
            {
                return (SocketError)Marshal.GetLastWin32Error();
            }

            var controlData = (Interop.Interop.Winsock.RxControlData*)controlBufferPtr;
            const int SOL_SOCKET = 0xffff;
            const int SO_TIMESTAMP = 0x300A;
            if (controlData->level == SOL_SOCKET && controlData->type == SO_TIMESTAMP && controlData->timestamp > 0)
            {
                rxTimestamp = (long)controlData->timestamp;
            }
            else if (NicClockCorrelation.ClockMode == ClockModeEnum.Hardware)
            {
                // We must have hw-clock timestamps.
                rxTimestamp = -1;
            }

            return socketResult;
        }
    }

    public unsafe SocketError Send(Span<byte> buffer, long timestampTimeout, out long txTimestamp)
    {
        #region Code is included in Tx latency.

        if (_socket.RemoteEndPoint == null)
        {
            throw new ArgumentNullException(nameof(_socket.RemoteEndPoint));
        }

        // Increase timestampId.
        _timestampId++;

        var ipEndpoint = (IPEndPoint)_socket.RemoteEndPoint;
        var socketAddress = new SocketAddress(ipEndpoint.Address, ipEndpoint.Port);
        var controlBuffer = new byte[sizeof(Interop.Interop.Winsock.TxControlData)];

        fixed (byte* controlBufferPtr = &MemoryMarshal.GetReference(new Span<byte>(controlBuffer)))
        fixed (byte* ptrSocketAddress = socketAddress.Buffer)
        fixed (byte* bufferPtr = &MemoryMarshal.GetReference(buffer))
        {
            Interop.Interop.Winsock.WSAMsg wsaMsg;
            wsaMsg.socketAddress = (IntPtr)ptrSocketAddress;
            wsaMsg.addressLength = (uint)socketAddress.Size;
            wsaMsg.flags = SocketFlags.None;

            Interop.Interop.Winsock.WSABuffer wsaBuffer;
            wsaBuffer.Length = buffer.Length;
            wsaBuffer.Pointer = (IntPtr)bufferPtr;
            wsaMsg.buffers = (IntPtr)(&wsaBuffer);
            wsaMsg.count = 1;

            var SOL_SOCKET = (uint)0xffff;
            var SO_TIMESTAMP_ID = (uint)0x300B;
            var controlData = (Interop.Interop.Winsock.TxControlData*)controlBufferPtr;
            controlData->length = (UIntPtr)20;
            controlData->level = SOL_SOCKET;
            controlData->type = SO_TIMESTAMP_ID;
            controlData->timestampId = _timestampId;
            wsaMsg.controlBuffer.Length = controlBuffer.Length;
            wsaMsg.controlBuffer.Pointer = (IntPtr)controlBufferPtr;

            var socketError = Interop.Interop.Winsock.WSASendMsg(_socket.SafeHandle, &wsaMsg, SocketFlags.None, out var bytesTransferred, null, IntPtr.Zero);
            var appTxTimestamp = Stopwatch.GetTimestamp();

            #endregion

            #region Code is included in Misfire if rpi is or will be exceeded.

            txTimestamp = appTxTimestamp;
            if (socketError != SocketError.Success)
            {
                return socketError;
            }

            const int SIO_GET_TX_TIMESTAMP = unchecked((int)2550137066);
            var timestampIdBytes = BitConverter.GetBytes(_timestampId);
            var timestampBytes = new byte[8];

            while (true)
            {
                // IOctl SIO_GET_TX_TIMESTAMP never blocks; which is why we poll it in a loop.
                // ref: https://docs.microsoft.com/en-us/windows/win32/winsock/winsock-timestamping
                var errorCode = Interop.Interop.Winsock.WSAIoctl_Blocking(
                    _socket.SafeHandle,
                    SIO_GET_TX_TIMESTAMP,
                    timestampIdBytes,
                    timestampIdBytes.Length,
                    timestampBytes,
                    timestampBytes.Length,
                    out var realOptionLength,
                    IntPtr.Zero,
                    IntPtr.Zero);
                var result = errorCode == SocketError.SocketError ? (SocketError)Marshal.GetLastWin32Error() : SocketError.Success;

                if (result == SocketError.Success)
                {
                    // Got timestamp.
                    var timestamp = BitConverter.ToInt64(timestampBytes);
                    if (timestamp > 0)
                    {
                        txTimestamp = timestamp;
                    }
                    else if (NicClockCorrelation.ClockMode == ClockModeEnum.Hardware)
                    {
                        // We must have hw-clock timestamps.
                        txTimestamp = -1;
                    }

                    break;
                }

                if (result != SocketError.WouldBlock)
                {
                    // Failed to call SIO_GET_TX_TIMESTAMP.
                    if (NicClockCorrelation.ClockMode == ClockModeEnum.Hardware)
                    {
                        // We must have hw-clock timestamps.
                        txTimestamp = -1;
                    }

                    break;
                }

                if (Stopwatch.GetTimestamp() > timestampTimeout)
                {
                    // SIO_GET_TX_TIMESTAMP timed out.
                    if (NicClockCorrelation.ClockMode == ClockModeEnum.Hardware)
                    {
                        // We must have hw-clock timestamps.
                        txTimestamp = -1;
                    }

                    Console.WriteLine("SIO_GET_TX_TIMESTAMP timed out.");
                    break;
                }

                // Calling Sleep(0) causes SIO_GET_TX_TIMESTAMP to complete faster.
                // Don't know how that works...
                Thread.Sleep(0);
            }

            return socketError;

            #endregion
        }
    }

    #endregion

    #region Methods

    private static byte[] GetBytes(Interop.Interop.Winsock.TimestampingConfig config)
    {
        var size = Marshal.SizeOf(config);
        var arr = new byte[size];

        IntPtr ptr = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(config, ptr, true);
        Marshal.Copy(ptr, arr, 0, size);
        Marshal.FreeHGlobal(ptr);
        return arr;
    }

    #endregion
}
