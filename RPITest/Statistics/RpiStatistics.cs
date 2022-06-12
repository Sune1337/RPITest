namespace RPITest.Statistics;

public class RpiStatistics
{
    #region Fields

    private decimal _avg;
    private decimal _count;
    private DateTime _dateTime = DateTime.Now;
    private decimal _max;
    private decimal _min;
    private decimal _sum;
    private long _numberOfMissingPackets;

    #endregion

    #region Public Methods and Operators

    public void Feed(decimal diff, long numberOfMissingPackets)
    {
        if (diff < _min) _min = diff;
        if (diff > _max) _max = diff;
        _count++;
        _sum += diff;
        _avg = _sum / _count;
        _numberOfMissingPackets = numberOfMissingPackets;

        if (DateTime.Now.Second != _dateTime.Second)
        {
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss};{_count:0.###}; {_min:0.###}; {_max:0.###}; {_avg:0.###}; {_numberOfMissingPackets}");

            _min = 0;
            _max = 0;
            _avg = 0;
            _count = 0;
            _sum = 0;
            _dateTime = DateTime.Now;
        }
    }

    #endregion
}
