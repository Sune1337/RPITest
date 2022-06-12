namespace MultimediaTimer;

using System.ComponentModel;
using System.Runtime.InteropServices;

//Used https://docs.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke as a guide 
// This was also VERY useful: https://docs.microsoft.com/en-gb/windows/win32/multimedia/timer-operations

public sealed class Timer : IComponent
{
    #region Static Fields

    private static TimerCaps caps;

    #endregion

    #region Fields

    private volatile TimerMode mode;

    private volatile int period;

    private volatile int resolution;

    private ISynchronizeInvoke synchronizingObject;

    private TickDelegate tickRaiser;

    private TimeProc timeProcOneShot;

    private TimeProc timeProcPeriodic;


    private int timerID;

    #endregion

    #region Constructors and Destructors

    static Timer()
    {
        timeGetDevCaps(ref caps, Marshal.SizeOf<TimerCaps>(Timer.caps));
    }


    public Timer()
    {
        Initialize();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref='MultimediaTimer.Timer'/> class, setting the <see cref='MultimediaTimer.Timer.Interval'/> property to the specified period.
    /// </summary>
    public Timer(double interval)
    {
        Initialize();
        Interval = TimeSpan.FromMilliseconds(interval);
    }

    ~Timer()
    {
        if (IsRunning)
        {
            timeKillEvent(timerID);
        }
    }

    #endregion

    #region Delegates

    private delegate void TickDelegate(EventArgs e);

    /// <summary>
    /// The TimeProc function callback function that is called once upon the expiration of a single event or periodically upon the expiration of periodic events.
    /// </summary>
    /// <param name="id"></param>
    /// Identifier of the timer event. This identifier was returned by the timeSetEvent function when the timer event was set up.
    /// <param name="msg"></param>
    /// Reserved; do not use.
    /// <param name="user"></param>
    /// User instance data supplied to the dwUser parameter of timeSetEvent.
    /// <param name="param1"></param>
    /// Reserved; do not use.
    /// <param name="param2"></param>
    /// Reserved; do not use.
    private delegate void TimeProc(int id, int msg, int user, int param1, int param2);

    #endregion

    #region Public Events

    public event EventHandler Disposed;
    public event EventHandler Elapsed;

    public event EventHandler Started;
    public event EventHandler Stopped;

    #endregion

    #region Public Properties

    public static TimerCaps Capabilities => caps;

    /// <summary>
    /// Event delay, in milliseconds. 
    /// If this value is not in the range of the minimum and maximum event delays supported by the timer, the function returns an error.
    /// </summary>
    public TimeSpan Interval
    {
        get { return TimeSpan.FromMilliseconds(period); }
        set
        {
            if (value.TotalMilliseconds < Capabilities.PeriodMin || value.TotalMilliseconds > Capabilities.PeriodMax)
            {
                throw new ArgumentOutOfRangeException("Delay", value, "Multimedia Timer delay out of range.");
            }

            period = (int)value.TotalMilliseconds;
            if (IsRunning)
            {
                Stop();
                Start();
            }
        }
    }

    public bool IsRunning { get; private set; }
    public int MaxPeriod => Capabilities.PeriodMax;

    public int MinPeriod => Capabilities.PeriodMin;

    public TimerMode Mode
    {
        get { return mode; }
        set
        {
            mode = value;
            if (IsRunning)
            {
                Stop();
                Start();
            }
        }
    }

    /// <summary>
    /// Resolution of the timer event, in milliseconds. 
    /// The resolution increases with smaller values; a resolution of 0 indicates periodic events should occur with the greatest possible accuracy. 
    /// To reduce system overhead, however, you should use the maximum value appropriate for your application.
    /// </summary>
    public TimeSpan Resolution
    {
        get { return TimeSpan.FromMilliseconds(resolution); }
        set
        {
            resolution = (int)value.TotalMilliseconds;
            if (IsRunning)
            {
                Stop();
                Start();
            }
        }
    }

    public ISite Site { get; set; }

    public ISynchronizeInvoke SynchronizingObject
    {
        get { return synchronizingObject; }
        set { synchronizingObject = value; }
    }

    #endregion

    #region Public Methods and Operators

    public void Dispose()
    {
        throw new NotImplementedException();
    }


    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        if (Mode == TimerMode.Periodic)
        {
            timerID = timeSetEvent((int)Interval.TotalMilliseconds, resolution, timeProcPeriodic, 0, (int)Mode);
        }
        else
        {
            timerID = timeSetEvent((int)Interval.TotalMilliseconds, resolution, timeProcOneShot, 0, (int)Mode);
        }

        if (timerID == 0)
        {
            throw new Exception("Unable to start multimedia Timer.");
        }

        IsRunning = true;
        if (SynchronizingObject != null && SynchronizingObject.InvokeRequired)
        {
            SynchronizingObject.BeginInvoke(new TickDelegate(OnStarted), new object[]
            {
                EventArgs.Empty
            });
            return;
        }

        OnStarted(EventArgs.Empty);
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        timeKillEvent(timerID);
        IsRunning = false;
        if (SynchronizingObject != null && SynchronizingObject.InvokeRequired)
        {
            SynchronizingObject.BeginInvoke(new TickDelegate(OnStopped), new object[]
            {
                EventArgs.Empty
            });
            return;
        }

        OnStopped(EventArgs.Empty);
    }

    #endregion

    #region Methods

    /// <summary>
    /// The timeGetDevCaps function queries the timer device to determine its resolution.
    /// </summary>
    /// <param name="caps"></param>
    /// A pointer to a TIMECAPS structure. This structure is filled with information about the resolution of the timer device.
    /// <param name="sizeOfTimerCaps"></param>
    /// The size, in bytes, of the TIMECAPS structure.
    /// <returns></returns>
    [DllImport("winmm.dll")]
    private static extern int timeGetDevCaps(ref TimerCaps caps, int sizeOfTimerCaps);

    /// <summary>
    /// The timeKillEvent function cancels a specified timer event.
    /// </summary>
    /// <param name="id"></param>
    /// Identifier of the timer event to cancel. This identifier was returned by the timeSetEvent function when the timer event was set up.
    /// <returns></returns>
    [DllImport("winmm.dll")]
    private static extern int timeKillEvent(int id);

    /// <summary>
    /// The timeSetEvent function starts a specified timer event. The multimedia timer runs in its own thread. 
    /// After the event is activated, it calls the specified callback function or sets or pulses the specified event object.
    /// </summary>
    /// <param name="delay"></param>
    /// Event delay, in milliseconds. 
    /// If this value is not in the range of the minimum and maximum event delays supported by the timer, the function returns an error.    
    /// <param name="resolution"></param>
    /// Resolution of the timer event, in milliseconds. 
    /// The resolution increases with smaller values; a resolution of 0 indicates periodic events should occur with the greatest possible accuracy. 
    /// To reduce system overhead, however, you should use the maximum value appropriate for your application.
    /// <param name="proc"></param>
    /// Pointer to a callback function that is called once upon expiration of a single event or periodically upon expiration of periodic events.
    /// If fuEvent specifies the TIME_CALLBACK_EVENT_SET or TIME_CALLBACK_EVENT_PULSE flag, then the lpTimeProc parameter is interpreted as a handle to an event object. 
    /// The event will be set or pulsed upon completion of a single event or periodically upon completion of periodic events.
    /// <param name="user"></param>
    /// User-supplied callback data.
    /// <param name="mode"></param>
    /// Timer event type. Oneshot or Periodic
    /// <returns></returns>
    [DllImport("winmm.dll")]
    private static extern int timeSetEvent(int delay, int resolution, TimeProc proc, int user, int mode);

    private void Initialize()
    {
        mode = TimerMode.Periodic;
        period = Capabilities.PeriodMin;
        Resolution = TimeSpan.FromMilliseconds(1);
        IsRunning = false;
        timeProcPeriodic = new TimeProc(PeriodicEventCallback);
        timeProcOneShot = new TimeProc(OneShotEventCallback);
        tickRaiser = new TickDelegate(OnTick);
    }

    private void OneShotEventCallback(int id, int msg, int user, int param1, int param2)
    {
        if (synchronizingObject != null)
        {
            synchronizingObject.BeginInvoke(tickRaiser, new object[]
            {
                EventArgs.Empty
            });
            Stop();
            return;
        }

        OnTick(EventArgs.Empty);
        Stop();
    }


    private void OnStarted(EventArgs e)
    {
        Started?.Invoke(this, e);
    }

    private void OnStopped(EventArgs e)
    {
        Stopped?.Invoke(this, e);
    }

    private void OnTick(EventArgs e)
    {
        Elapsed?.Invoke(this, e);
    }

    private void PeriodicEventCallback(int id, int msg, int user, int param1, int param2)
    {
        if (synchronizingObject != null)
        {
            synchronizingObject.BeginInvoke(tickRaiser, new object[]
            {
                EventArgs.Empty
            });
            return;
        }

        OnTick(EventArgs.Empty);
    }

    #endregion
}
