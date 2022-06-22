namespace Win32Sockets;

using System.Runtime.InteropServices;

public static class MarshalHelper
{
    #region Public Methods and Operators

    public static T FromBytes<T>(byte[] arr) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var ptr = IntPtr.Zero;
        try
        {
            ptr = Marshal.AllocHGlobal(size);

            Marshal.Copy(arr, 0, ptr, size);

            return (T)Marshal.PtrToStructure(ptr, typeof(T));
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public static byte[] GetBytes<T>(T value) where T : struct
    {
        var size = Marshal.SizeOf(value);
        var arr = new byte[size];

        var ptr = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(value, ptr, true);
        Marshal.Copy(ptr, arr, 0, size);
        Marshal.FreeHGlobal(ptr);
        return arr;
    }

    #endregion
}
