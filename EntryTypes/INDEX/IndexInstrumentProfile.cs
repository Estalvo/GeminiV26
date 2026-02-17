namespace GeminiV26.Instruments.INDEX
{
    public enum IndexVolatilityClass
    {
        Low,
        Medium,
        High,
        Extreme
    }

    public enum IndexSessionBias
    {
        Asia,
        London,
        NewYork,
        Mixed
    }

    public enum IndexPullbackStyle
    {
        Shallow,
        EMA21,
        Structure
    }

    /// <summary>
    /// Index instrumentum statikus viselkedési profilja.
    /// NEM számol, NEM dönt.
    /// Csak paraméterez.
    /// </summary>
    public class IndexInstrumentProfile
    {
        // ===== IDENTITY =====
        public string Symbol { get; set; }
        public IndexVolatilityClass Volatility { get; set; }
        public IndexSessionBias SessionBias { get; set; }
        public IndexPullbackStyle PullbackStyle { get; set; }

        // ===== VOL / TREND =====
        public double TypicalDailyRangePoints { get; set; }
        public double MinAdxTrend { get; set; }
        public double MinAtrPoints { get; set; }

        // ===== IMPULSE =====
        public double ImpulseAtrMult_M5 { get; set; }
        public double ImpulseAtrMult_M1 { get; set; }
        public int MaxBarsSinceImpulse_M5 { get; set; }

        // ===== FLAG =====
        public int FlagBars { get; set; }
        public double MaxFlagAtrMult { get; set; }
        public double BreakoutBufferAtr { get; set; }
        public double MaxEmaDistanceAtr { get; set; }

        // ===== SESSION =====
        public bool AllowAsianSession { get; set; }

        // ===== PROFIT =====
        public double Tp1R { get; set; }
        public double RunnerMinR { get; set; }
        public double MaxExtensionR { get; set; }

        // ===== TRAILING =====
        public double TrailStartR { get; set; }
        public double TrailAtrMult { get; set; }
        public double MinTrailImprovePts { get; set; }

        // ===== TRADE VIABILITY =====
        public double MaxAdverseRBeforeTP1 { get; set; }
        public int MaxBarsWithoutProgress_M5 { get; set; }
        public bool AllowEarlyExit { get; set; }

        // =====================================================
        // ===== NEW – MATRIX CONTROLLED BEHAVIOUR =====
        // =====================================================

        // --- Chop behaviour ---
        public double ChopAdxThreshold { get; set; }
        public double ChopDiDiffThreshold { get; set; }

        // --- Trend fatigue behaviour ---
        public int FatigueThreshold { get; set; }          // hány feltétel elég a rejecthez
        public double FatigueAdxLevel { get; set; }        // milyen ADX felett vizsgáljuk

        // --- Pullback behaviour (hybrid logic későbbre) ---
        public bool UseHybridPullbackDepth { get; set; }
        public double MaxPullbackPercentOfImpulse { get; set; }

        // --- Score character weighting ---
        public double ScoreWeightMultiplier { get; set; }
    }
}
