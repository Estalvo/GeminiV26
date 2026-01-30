namespace GeminiV26.Instruments.FX
{
    /// <summary>
    /// FX Market State – környezeti állapot
    ///
    /// FONTOS:
    /// - NEM entry logic
    /// - NEM gate
    /// - NEM dönt
    /// - csak állapotot ír le
    /// </summary>
    public sealed class FxMarketState
    {
        // === Nyers mérések ===
        public double AtrPips { get; init; }
        public double Adx { get; init; }

        // === Értelmezett állapotjelzők ===
        public bool IsLowVol { get; init; }
        public bool IsTrend { get; init; }
    }
}
