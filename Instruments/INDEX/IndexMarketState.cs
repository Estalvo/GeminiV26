using cAlgo.API;
using cAlgo.API.Indicators;

namespace GeminiV26.Instruments.INDEX
{
    /// <summary>
    /// INDEX Market State – környezeti állapot
    ///
    /// - NEM entry logic
    /// - NEM gate
    /// - NEM dönt
    /// - csak állapotot ír le
    /// </summary>
    public sealed class IndexMarketState
    {
        public bool IsLowVol { get; set; }
        public bool IsTrend { get; set; }
        public bool IsRange { get; set; }

        public double AtrPoints { get; set; }
        public double RangePoints { get; set; }
        public double Adx { get; set; }

    }
}
