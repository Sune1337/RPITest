namespace RPITest.Protocol;

using System.Net;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PtpHeader
{
    #region Constructors and Destructors

    public PtpHeader(byte ptpVersion)
    {
        MessageType = 0;
        PtpVersion = ptpVersion;
        MessageLength = 0;
        DomainNumber = 0;
        Reserved1 = 0;
        Flags = 0;
        CorrectionField = 0;
        Reserved2 = 0;
        SourcePortIdentity1 = 0;
        SourcePortIdentity2 = 0;
        SourcePortIdentity3 = 0;
        SequenceId = 0;
        Controlfield = 0;
        LogMessageInterval = 0;
    }

    #endregion

    #region do not reorder

    public byte MessageType;
    public byte PtpVersion;
    public short MessageLength;
    public byte DomainNumber;
    public byte Reserved1;
    public ushort Flags;
    public ulong CorrectionField;
    public uint Reserved2;
    public uint SourcePortIdentity1;
    public uint SourcePortIdentity2;
    public ushort SourcePortIdentity3;
    public ushort SequenceId;
    public byte Controlfield;
    public byte LogMessageInterval;

    #endregion
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PtpSync
{
    #region do not reorder

    public ushort Filler;
    public ulong OriginTimestamp;

    #endregion
}

public static class PtpSizes
{
    #region Static Fields

    public static int SizeOfPtpHeader = Marshal.SizeOf<PtpHeader>();

    public static int SizeOfPtpSync = Marshal.SizeOf<PtpSync>();

    #endregion
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PtpSyncMessage
{
    #region Constructors and Destructors

    public PtpSyncMessage(byte ptpVersion)
    {
        Message = default;
        Suffix = new byte[100];
        PtpHeader = new PtpHeader(ptpVersion);
        PtpHeader.MessageLength = IPAddress.NetworkToHostOrder((short)(PtpSizes.SizeOfPtpHeader + PtpSizes.SizeOfPtpSync));
    }

    #endregion

    #region do not reorder

    public readonly PtpHeader PtpHeader;

    public readonly PtpSync Message;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 100)]
    public byte[] Suffix;

    #endregion
}
