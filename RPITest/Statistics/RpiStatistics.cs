namespace RPITest.Statistics;

public class RpiStatistics
{
    #region Fields

    private readonly Statistics _diffStatistics = new();
    private readonly Statistics _misfireStatistics = new();
    private readonly Statistics _rxLatencyStatistics = new();
    private readonly Statistics _txLatencyStatistics = new();

    private DateTime _dateTime = DateTime.Now;
    private long _numberOfMissingPackets;

    #endregion

    #region Constructors and Destructors

    public RpiStatistics()
    {
        ResetStatistics(_diffStatistics);
        ResetStatistics(_misfireStatistics);
        ResetStatistics(_rxLatencyStatistics);
        ResetStatistics(_txLatencyStatistics);
    }

    #endregion

    #region Public Methods and Operators

    public void Feed(decimal diff, decimal misfire, decimal rxLatency, decimal txLatency, long numberOfMissingPackets)
    {
        UpdateStatistics(diff, _diffStatistics);
        UpdateStatistics(misfire, _misfireStatistics);
        UpdateStatistics(rxLatency, _rxLatencyStatistics);
        UpdateStatistics(txLatency, _txLatencyStatistics);
        _numberOfMissingPackets = numberOfMissingPackets;

        if (DateTime.Now.Second != _dateTime.Second)
        {
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss};Diff;{_diffStatistics.Count:0.###}; {_diffStatistics.Min:0.###}; {_diffStatistics.Max:0.###}; {_diffStatistics.Avg:0.###}; {_numberOfMissingPackets}");
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss};Misfire;{_misfireStatistics.Count:0.###}; {_misfireStatistics.Min:0.###}; {_misfireStatistics.Max:0.###}; {_misfireStatistics.Avg:0.###}");
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss};RxLatency;{_rxLatencyStatistics.Count:0.###}; {_rxLatencyStatistics.Min:0.###}; {_rxLatencyStatistics.Max:0.###}; {_rxLatencyStatistics.Avg:0.###}");
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss};TxLatency;{_txLatencyStatistics.Count:0.###}; {_txLatencyStatistics.Min:0.###}; {_txLatencyStatistics.Max:0.###}; {_txLatencyStatistics.Avg:0.###}");

            ResetStatistics(_diffStatistics);
            ResetStatistics(_misfireStatistics);
            ResetStatistics(_rxLatencyStatistics);
            ResetStatistics(_txLatencyStatistics);
            _dateTime = DateTime.Now;
        }
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
        public decimal Sum;

        #endregion
    }
}
