namespace Win32Sockets;

using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

using MathNet.Numerics;

using Vanara.PInvoke;

public enum ClockModeEnum
{
    Hardware,
    Software
}

public static class NicClockCorrelation
{
    #region Static Fields

    private static readonly List<Tuple<double, double>> _clocks = new();

    private static double _bestIntercept = 0;
    private static decimal _bestSlope = 0;
    private static ulong _firstHardwareTimestamp = 0;

    private static ulong _firstSystemTimestamp = 0;

    private static ManualResetEvent _inSyncEvent = new(false);
    private static IpHlpApi.NET_LUID _interfaceLuid;
    private static ulong _lastHardwareTimestamp = 0;

    private static Thread? _thread;
    private static ManualResetEvent _waitEvent = new(false);

    #endregion

    #region Public Properties

    public static ClockModeEnum ClockMode { get; private set; }

    #endregion

    #region Public Methods and Operators

    public static long GetTimestamp()
    {
        if (ClockMode == ClockModeEnum.Software)
        {
            return Stopwatch.GetTimestamp();
        }

        var error = CaptureInterfaceHardwareCrossTimestamp(_interfaceLuid, out var crossTimestamp);
        if (error != Win32Error.NERR_Success)
        {
            throw new Exception($"CaptureInterfaceHardwareCrossTimestamp failed: {error.ToString()}");
        }

        return (long)crossTimestamp.HardwareClockTimestamp;
    }

    public static void GetTimestamp(out long systemTimestamp, out long hwTimestamp)
    {
        if (ClockMode == ClockModeEnum.Software)
        {
            systemTimestamp = Stopwatch.GetTimestamp();
            hwTimestamp = systemTimestamp;
            return;
        }

        var error = CaptureInterfaceHardwareCrossTimestamp(_interfaceLuid, out var crossTimestamp);
        if (error != Win32Error.NERR_Success)
        {
            throw new Exception($"CaptureInterfaceHardwareCrossTimestamp failed: {error.ToString()}");
        }

        systemTimestamp = (long)crossTimestamp.SystemTimestamp2;
        hwTimestamp = (long)crossTimestamp.HardwareClockTimestamp;
    }

    public static void Initialize(string friendlyName)
    {
        // Iterate all network-interfaces to find the one we want.
        var nics = NetworkInterface.GetAllNetworkInterfaces();
        var nicIndex = -1;
        foreach (var nic in nics)
        {
            if (!string.Equals(nic.Name, friendlyName, StringComparison.InvariantCultureIgnoreCase))
            {
                continue;
            }

            var ipv4Properties = nic.GetIPProperties().GetIPv4Properties();
            if (ipv4Properties != null)
            {
                nicIndex = ipv4Properties.Index;
            }
        }

        if (nicIndex == -1)
        {
            throw new Exception($"Could not find NIC {friendlyName}");
        }

        // Convert the interface-index to Luid.
        var error = IpHlpApi.ConvertInterfaceIndexToLuid((uint)nicIndex, out _interfaceLuid);
        if (error != Win32Error.NERR_Success)
        {
            throw new Exception("ConvertInterfaceIndexToLuid failed.");
        }

        try
        {
            // Get a sample timestamp from Nic to ensure we can.
            error = CaptureInterfaceHardwareCrossTimestamp(_interfaceLuid, out var interfaceHardwareCrossTimestamp);
            if (error != Win32Error.NERR_Success)
            {
                throw new Exception("CaptureInterfaceHardwareCrossTimestamp failed.");
            }

            ClockMode = ClockModeEnum.Hardware;
        }

        catch
        {
            ClockMode = ClockModeEnum.Software;
            _bestSlope = 1;
        }
    }

    public static void Start(string friendlyName)
    {
        Initialize(friendlyName);

        if (_thread != null || ClockMode == ClockModeEnum.Software)
        {
            return;
        }

        _thread = new Thread(ThreadCallback);
        _thread.Start();
    }

    public static void Stop()
    {
        if (_thread == null || ClockMode == ClockModeEnum.Software)
        {
            return;
        }

        // Wait for thread to exit.
        _waitEvent.Set();
        _thread.Join();

        // Reset instance state.
        _thread = null;
        _waitEvent.Reset();
        _inSyncEvent.Reset();
    }

    public static decimal ToSystemTicks(decimal hwTicks)
    {
        return hwTicks * _bestSlope;
    }

    public static void WaitForSync()
    {
        if (ClockMode == ClockModeEnum.Software)
        {
            Console.Error.WriteLine("Using system-clock.");
            return;
        }

        _inSyncEvent.WaitOne();
    }

    #endregion

    #region Methods

    [DllImport(Lib.IpHlpApi, SetLastError = false, ExactSpelling = true)]
    private static extern Win32Error CaptureInterfaceHardwareCrossTimestamp(in IpHlpApi.NET_LUID interfaceLuid, out INTERFACE_HARDWARE_CROSSTIMESTAMP interfaceHardwareCrossTimestamp);

    private static void ThreadCallback()
    {
        while (_waitEvent.WaitOne(1000) == false)
        {
            var error = CaptureInterfaceHardwareCrossTimestamp(_interfaceLuid, out var crossTimestamp);
            if (error != Win32Error.NERR_Success)
            {
                Console.Error.WriteLine($"CaptureInterfaceHardwareCrossTimestamp failed: {error.ToString()}");
                continue;
            }

            var currentSystemTimestamp = crossTimestamp.SystemTimestamp2;
            if (_firstSystemTimestamp == 0)
            {
                _firstSystemTimestamp = currentSystemTimestamp;
                _firstHardwareTimestamp = crossTimestamp.HardwareClockTimestamp;
            }
            else if (crossTimestamp.HardwareClockTimestamp < _lastHardwareTimestamp)
            {
                // Nic time has been moving backwards.
                _firstSystemTimestamp = currentSystemTimestamp;
                _firstHardwareTimestamp = crossTimestamp.HardwareClockTimestamp;
            }
            else
            {
                // Save clocks.
                var elapsedSystemTime = crossTimestamp.SystemTimestamp2 - _firstSystemTimestamp;
                var hwElapsed = crossTimestamp.HardwareClockTimestamp - _firstHardwareTimestamp;
                _clocks.Add(new Tuple<double, double>(hwElapsed, elapsedSystemTime));
                if (_clocks.Count > 60)
                {
                    _clocks.RemoveAt(0);
                }

                double intercept = 0;
                decimal slope = 0;
                if (_clocks.Count > 1)
                {
                    var xdata = new double[_clocks.Count];
                    var ydata = new double[_clocks.Count];
                    for (var index = 0; index < _clocks.Count; index++)
                    {
                        var tuple = _clocks[index];
                        xdata[index] = tuple.Item1;
                        ydata[index] = tuple.Item2;
                    }

                    var p = Fit.Line(xdata, ydata);
                    intercept = p.Item1; // == 10; intercept
                    slope = (decimal)p.Item2; // == 0.5; slope
                }

                var systemElapsed = elapsedSystemTime / TimeSpan.TicksPerMillisecond;
                var bestEstimate = (hwElapsed * _bestSlope) / TimeSpan.TicksPerMillisecond;
                var currentEstimate = (decimal)((hwElapsed * slope) / TimeSpan.TicksPerMillisecond);
                var bestScore = Math.Min(bestEstimate, systemElapsed) / Math.Max(bestEstimate, systemElapsed);
                var currentScore = Math.Min(currentEstimate, systemElapsed) / Math.Max(currentEstimate, systemElapsed);

                if (currentScore > bestScore)
                {
                    _bestIntercept = intercept;
                    _bestSlope = slope;

                    if (currentScore > 0.99m)
                    {
                        _inSyncEvent.Set();
                    }
                }
            }

            _lastHardwareTimestamp = crossTimestamp.HardwareClockTimestamp;
        }
    }

    #endregion

    [StructLayout(LayoutKind.Sequential)]
    public struct INTERFACE_HARDWARE_CROSSTIMESTAMP
    {
        #region do not reorder

        public ulong SystemTimestamp1;
        public ulong HardwareClockTimestamp;
        public ulong SystemTimestamp2;

        #endregion
    }
}
