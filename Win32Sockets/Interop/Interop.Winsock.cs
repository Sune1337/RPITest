namespace Win32Sockets.Interop;

using System.Net.Sockets;
using System.Runtime.InteropServices;

internal static partial class Interop
{
    internal static unsafe partial class Winsock
    {
        #region Methods

        [DllImport("ws2_32.dll", SetLastError = true)]
        internal static extern int WSAGetLastError();

        // Used with SIOGETEXTENSIONFUNCTIONPOINTER - we're assuming that will never block.
        [DllImport("ws2_32.dll", SetLastError = true)]
        internal static extern SocketError WSAIoctl(
            SafeSocketHandle socketHandle,
            int ioControlCode,
            ref Guid guid,
            int guidSize,
            out IntPtr funcPtr,
            int funcPtrSize,
            out int bytesTransferred,
            IntPtr shouldBeNull,
            IntPtr shouldBeNull2);

        [DllImport("ws2_32.dll", EntryPoint = "WSAIoctl", SetLastError = true)]
        internal static extern SocketError WSAIoctl_Blocking(
            SafeSocketHandle socketHandle,
            int ioControlCode,
            byte[]? inBuffer,
            int inBufferSize,
            byte[]? outBuffer,
            int outBufferSize,
            out int bytesTransferred,
            IntPtr overlapped,
            IntPtr completionRoutine);

        [DllImport("ws2_32.dll", SetLastError = true)]
        internal static extern SocketError WSASendMsg(
            SafeHandle socketHandle,
            WSAMsg* wsaMsg,
            SocketFlags socketFlags,
            out int bytesTransferred,
            NativeOverlapped* overlapped,
            IntPtr completionRoutine);

        #endregion
    }
}
