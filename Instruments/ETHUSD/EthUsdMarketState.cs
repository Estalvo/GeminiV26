namespace GeminiV26.Instruments.ETHUSD
{
    public class EthUsdMarketState
    {
        public double AtrPips { get; set; }
        public double Adx { get; set; }

        public bool IsLowVol { get; set; }
        public bool IsExtremeVol { get; set; }

        public bool IsTrend { get; set; }
        public bool IsStrongTrend { get; set; }

        public bool IsChop { get; set; }
        public double WickRatioNow { get; set; }
    }
}
