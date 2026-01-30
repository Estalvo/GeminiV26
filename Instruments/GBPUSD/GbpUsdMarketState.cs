namespace GeminiV26.Instruments.GBPUSD
{
    public class GbpUsdMarketState
    {
        public double AtrPips { get; set; }
        public double Adx { get; set; }

        // már volt
        public bool IsLowVol { get; set; }

        // ÚJ – szükséges az executorhoz
        public bool IsTrend { get; set; }
    }

}
