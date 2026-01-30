using cAlgo.API;
using cAlgo.API.Indicators;

namespace GeminiV26.Instruments.CRYPTO
{
    /// <summary>
    /// CRYPTO Market State – környezeti állapot
    /// </summary>
    public sealed class CryptoMarketState
    {
        // meglévő (NEM töröljük)
        public double AtrUsd { get; init; }
        public bool IsHighVol { get; init; }

        // === BTC executor által elvárt mezők ===
        public double AtrPips { get; init; }
        public double Adx { get; init; }

        public bool IsLowVol { get; init; }
        public bool IsTrend { get; init; }
    }
}
