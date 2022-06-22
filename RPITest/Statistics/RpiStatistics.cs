namespace RPITest.Statistics;

public class RpiStatistics
{
    #region Fields

    private readonly Statistics _absoluteJitterStatistics = new();
    private readonly Statistics _jitterStatistics = new();
    private readonly Statistics _misfireStatistics = new();
    private readonly Statistics _txLatencyStatistics = new();

    private DateTime _dateTime = DateTime.Now;

    #endregion

    #region Constructors and Destructors

    public RpiStatistics()
    {
        ResetStatistics(_jitterStatistics);
        ResetStatistics(_absoluteJitterStatistics);
        ResetStatistics(_misfireStatistics);
        ResetStatistics(_txLatencyStatistics);
    }

    #endregion

    #region Public Methods and Operators

    public void Feed(decimal diff, decimal misfire, decimal txLatency)
    {
        UpdateStatistics(diff, _jitterStatistics);
        UpdateStatistics(Math.Abs(diff), _absoluteJitterStatistics);
        UpdateStatistics(misfire, _misfireStatistics);
        UpdateStatistics(txLatency, _txLatencyStatistics);

        if (DateTime.Now.Second != _dateTime.Second)
        {
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss};Jitter;{_jitterStatistics.Count:0.######}; {_jitterStatistics.Min:0.######}; {_jitterStatistics.Max:0.######}; {_jitterStatistics.Avg:0.######}; {_jitterStatistics.MissingPackets}");
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss};AbsoluteJitter;{_absoluteJitterStatistics.Count:0.######}; {_absoluteJitterStatistics.Min:0.######}; {_absoluteJitterStatistics.Max:0.######}; {_absoluteJitterStatistics.Avg:0.######}; {_jitterStatistics.MissingPackets}");
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss};Misfire;{_misfireStatistics.Count:0.######}; {_misfireStatistics.Min:0.######}; {_misfireStatistics.Max:0.######}; {_misfireStatistics.Avg:0.######}; {_jitterStatistics.MissingPackets}");
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss};TxLatency;{_txLatencyStatistics.Count:0.######}; {_txLatencyStatistics.Min:0.######}; {_txLatencyStatistics.Max:0.######}; {_txLatencyStatistics.Avg:0.######}; {_jitterStatistics.MissingPackets}");

            ResetStatistics(_jitterStatistics);
            ResetStatistics(_absoluteJitterStatistics);
            ResetStatistics(_misfireStatistics);
            ResetStatistics(_txLatencyStatistics);
            _dateTime = DateTime.Now;
        }
    }

    public void FeedMissingPackets(long missingPackets)
    {
        _jitterStatistics.MissingPackets = missingPackets;
        _absoluteJitterStatistics.MissingPackets = missingPackets;
        _misfireStatistics.MissingPackets = missingPackets;
        _txLatencyStatistics.MissingPackets = missingPackets;
    }

    #endregion

    #region Methods

    private static void ResetStatistics(Statistics statistics)
    {
        statistics.Min = decimal.MaxValue;
        statistics.Max = decimal.MinValue;
        statistics.Avg = 0;
        statistics.Count = 0;
        statistics.Sum = 0;
        statistics.MissingPackets = 0;
    }

    private static void UpdateStatistics(decimal value, Statistics statistics)
    {
        if (value < statistics.Min) statistics.Min = value;
        if (value > statistics.Max) statistics.Max = value;
        statistics.Count++;
        statistics.Sum += value;
        statistics.Avg = statistics.Sum / statistics.Count;
    }

    #endregion

    private class Statistics
    {
        #region Fields

        public decimal Avg;
        public decimal Count;
        public decimal Max;
        public decimal Min;
        public long MissingPackets;
        public decimal Sum;

        #endregion
    }
}
