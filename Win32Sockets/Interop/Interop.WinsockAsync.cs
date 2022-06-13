namespace Win32Sockets.Interop;

using System.Net.Sockets;
using System.Runtime.InteropServices;

internal static partial class Interop
{
    internal static partial class Winsock
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct TimestampingConfig {
            internal uint Flags;
            internal ushort TxTimestampsBuffered;
        } 
        
        [StructLayout(LayoutKind.Sequential)]
        internal struct RxControlData
        {
            internal UIntPtr length;
            internal uint level;
            internal uint type;
            internal long timestamp;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        internal struct TxControlData
        {
            internal UIntPtr length;
            internal uint level;
            internal uint type;
            internal uint timestampId;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WSAMsg
        {
            internal IntPtr socketAddress;
            internal uint addressLength;
            internal IntPtr buffers;
            internal uint count;
            internal WSABuffer controlBuffer;
            internal SocketFlags flags;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        internal struct WSABuffer
        {
            internal int Length; // Length of Buffer
            internal IntPtr Pointer; // Pointer to Buffer
        }
    }
}