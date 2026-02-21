namespace GeminiV26.EntryTypes.Crypto
{
    public sealed class CryptoInstrumentProfile
    {
        public string Symbol { get; init; }

        // === Vol / trend baseline ===
        public double MinAtrPips { get; init; }
        public double MaxAtrPips { get; init; }

        public double MinAdxTrend { get; init; }
        public double MinAdxStrong { get; init; }

        // === Wick / chop ===
        public double MaxWickRatio { get; init; }
        public int ChopLookbackBars { get; init; }

        // === Impulse / flag skálázás ===
        public double ImpulseAtrMult_M5 { get; init; }
        public double ImpulseAtrMult_M1 { get; init; }
        public double MaxFlagAtrMult { get; init; }

        // Require strong impulse before allowing pullback
        public bool RequireStrongImpulseForPullback { get; set; }

        // Minimum ADX required for pullback continuation
        // === Pullback entry thresholds ===
        public double MinAdxForPullback { get; init; }
        public double MinAdxSlopeForPullback { get; init; }
        public int MaxBarsSinceImpulseForPullback { get; init; }
        public bool AllowNeutralFlagWithStrongAdx { get; init; }
        public double NeutralFlagMinAdx { get; init; }

        // Block pullback if HighVol but no strong impulse (BTC trap)
        public bool BlockPullbackOnHighVolWithoutImpulse { get; set; }

        // ================================
        // PULLBACK VIABILITY (POST-ENTRY)
        // ================================

        // How many bars must show momentum after pullback
        public int PullbackMfeCheckBars { get; set; }

        // Minimum MFE (in R) required within that window
        public double PullbackMinMfeR { get; init; }

        // === Range ===
        public double RangeMaxWidthAtr { get; init; }

        // === Viselkedési engedélyek ===
        public bool AllowMeanReversion { get; init; }
        public bool AllowRangeBreakout { get; init; }
    }
}
