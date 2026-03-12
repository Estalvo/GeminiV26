using cAlgo.API;
using cAlgo.API.Indicators;

namespace GeminiV26.Instruments.METAL
{
    /// <summary>
    /// METAL Market State – környezeti állapot
    /// (XAU mellett későbbi fémekhez is)
    /// </summary>
    public sealed class MetalMarketState
    {
        public double AtrPips { get; set; }
        public double Adx { get; set; }

        public bool IsLowVol { get; set; }
        public bool IsTrend { get; set; }
        public bool IsCompression { get; set; }
        public bool IsHardRange { get; set; }
        public bool IsRange { get; set; }

        public double EmaDistanceAtr { get; set; }

        // Backward-compatible aliases for old consumers
        public double Atr => AtrPips;
        public double RangeWidth => EmaDistanceAtr;
    }
}
