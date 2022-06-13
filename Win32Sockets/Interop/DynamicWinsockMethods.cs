namespace Win32Sockets.Interop;

using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Runtime.InteropServices;

internal sealed class DynamicWinsockMethods
{
    #region Fields

    private WSARecvMsgDelegate? _recvMsg;

    #endregion

    #region Delegates

    internal unsafe delegate SocketError WSARecvMsgDelegate(
        SafeSocketHandle socketHandle,
        IntPtr msg,
        out int bytesTransferred,
        NativeOverlapped* overlapped,
        IntPtr completionRoutine);

    #endregion

    #region Methods

    internal unsafe WSARecvMsgDelegate GetWSARecvMsgDelegate(SafeSocketHandle socketHandle)
        => _recvMsg ?? CreateDelegate(ptr => new SocketDelegateHelper(ptr).WSARecvMsg, ref _recvMsg, socketHandle, "f689d7c86f1f436b8a53e54fe351c322");


    private static T CreateDelegate<T>(Func<IntPtr, T> functionPointerWrapper, [NotNull] ref T? cache, SafeSocketHandle socketHandle, string guidString) where T : Delegate
    {
        Guid guid = new Guid(guidString);
        IntPtr ptr;
        SocketError errorCode;

        unsafe
        {
            errorCode = Interop.Winsock.WSAIoctl(
                socketHandle,
                Interop.Winsock.IoctlSocketConstants.SIOGETEXTENSIONFUNCTIONPOINTER,
                ref guid,
                sizeof(Guid),
                out ptr,
                sizeof(IntPtr),
                out _,
                IntPtr.Zero,
                IntPtr.Zero);
        }

        if (errorCode != SocketError.Success)
        {
            throw new SocketException();
        }

        Interlocked.CompareExchange(ref cache, functionPointerWrapper(ptr), null);
        return cache;
    }

    #endregion

    /// <summary>
    /// The SocketDelegateHelper implements manual marshalling wrappers for the various delegates used for the dynamic Winsock methods.
    /// These wrappers were generated with LibraryImportGenerator and then manually converted to use function pointers as the target instead of a P/Invoke.
    /// </summary>
    private struct SocketDelegateHelper
    {
        #region Fields

        private readonly IntPtr _target;

        #endregion

        #region Constructors and Destructors

        public SocketDelegateHelper(IntPtr target)
        {
            _target = target;
        }

        #endregion

        #region Methods

        internal unsafe SocketError WSARecvMsg(SafeSocketHandle socketHandle, IntPtr msg, out int bytesTransferred, NativeOverlapped* overlapped, IntPtr completionRoutine)
        {
            IntPtr __socketHandle_gen_native = default;
            bytesTransferred = default;
            SocketError __retVal;
            //
            // Setup
            //
            bool socketHandle__addRefd = false;
            try
            {
                //
                // Marshal
                //
                socketHandle.DangerousAddRef(ref socketHandle__addRefd);
                __socketHandle_gen_native = socketHandle.DangerousGetHandle();
                fixed (int* __bytesTransferred_gen_native = &bytesTransferred)
                {
                    __retVal = ((delegate* unmanaged<IntPtr, IntPtr, int*, NativeOverlapped*, IntPtr, SocketError>)_target)(__socketHandle_gen_native, msg, __bytesTransferred_gen_native, overlapped, completionRoutine);
                }

                Marshal.SetLastPInvokeError(Marshal.GetLastSystemError());
            }
            finally
            {
                //
                // Cleanup
                //
                if (socketHandle__addRefd)
                    socketHandle.DangerousRelease();
            }

            return __retVal;
        }

        #endregion
    }
}
