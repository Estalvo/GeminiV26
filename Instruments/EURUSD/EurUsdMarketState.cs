namespace GeminiV26.Instruments.EURUSD
{
    public class EurUsdMarketState
    {
        public double AtrPips { get; set; }
        public double Adx { get; set; }

        // már volt
        public bool IsLowVol { get; set; }

        // ÚJ – szükséges az executorhoz
        public bool IsTrend { get; set; }
    }

}
