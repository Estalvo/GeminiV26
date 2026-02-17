using System;
using System.Collections.Generic;

namespace GeminiV26.Instruments.INDEX
{
    public static class IndexInstrumentMatrix
    {
        private static readonly Dictionary<string, IndexInstrumentProfile> _map =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // ======================================================
                // NAS100 / US100 / US TECH
                // ======================================================
                ["NAS100"] = new IndexInstrumentProfile
                {
                    Symbol = "NAS100",
                    Volatility = IndexVolatilityClass.High,
                    SessionBias = IndexSessionBias.NewYork,
                    PullbackStyle = IndexPullbackStyle.Shallow,

                    TypicalDailyRangePoints = 400,

                    // =========================
                    // IMPULSE
                    // =========================
                    ImpulseAtrMult_M5 = 0.58,
                    ImpulseAtrMult_M1 = 0.24,
                    MaxBarsSinceImpulse_M5 = 3,

                    // =========================
                    // FLAG
                    // =========================
                    FlagBars = 3,
                    MaxFlagAtrMult = 2.2,
                    BreakoutBufferAtr = 0.08,
                    MaxEmaDistanceAtr = 0.75,

                    // =========================
                    // TREND / CHOP
                    // =========================
                    MinAdxTrend = 20,
                    MinAtrPoints = 10,
                    ChopAdxThreshold = 20,
                    ChopDiDiffThreshold = 7,

                    // =========================
                    // FATIGUE CONTROL
                    // =========================
                    FatigueThreshold = 3,
                    FatigueAdxLevel = 42,

                    // =========================
                    // HYBRID PULLBACK CONTROL
                    // =========================
                    UseHybridPullbackDepth = false,
                    MaxPullbackPercentOfImpulse = 0.60,

                    // =========================
                    // SCORE CHARACTER
                    // =========================
                    ScoreWeightMultiplier = 1.00,

                    // =========================
                    // PROFIT STRUCTURE
                    // =========================
                    Tp1R = 0.45,
                    RunnerMinR = 1.1,
                    MaxExtensionR = 3.8,

                    // =========================
                    // TRAILING
                    // =========================
                    TrailStartR = 1.1,
                    TrailAtrMult = 1.8,
                    MinTrailImprovePts = 10,

                    // =========================
                    // TRADE VIABILITY
                    // =========================
                    MaxAdverseRBeforeTP1 = 0.40,
                    MaxBarsWithoutProgress_M5 = 4,
                    AllowEarlyExit = true,

                    AllowAsianSession = true
                },

                // ======================================================
                // US30 / DOW JONES
                // ======================================================
                ["US30"] = new IndexInstrumentProfile
                {
                    Symbol = "US30",
                    Volatility = IndexVolatilityClass.Extreme,
                    SessionBias = IndexSessionBias.NewYork,
                    PullbackStyle = IndexPullbackStyle.Structure,

                    TypicalDailyRangePoints = 700,

                    // =========================
                    // IMPULSE
                    // =========================
                    ImpulseAtrMult_M5 = 0.68,
                    ImpulseAtrMult_M1 = 0.30,
                    MaxBarsSinceImpulse_M5 = 3,

                    // =========================
                    // FLAG
                    // =========================
                    FlagBars = 4,
                    MaxFlagAtrMult = 2.2,
                    BreakoutBufferAtr = 0.08,
                    MaxEmaDistanceAtr = 1.00,

                    // =========================
                    // TREND / CHOP
                    // =========================
                    MinAdxTrend = 18,
                    MinAtrPoints = 16,
                    ChopAdxThreshold = 18,
                    ChopDiDiffThreshold = 6,

                    // =========================
                    // FATIGUE CONTROL
                    // =========================
                    FatigueThreshold = 2,      // US30 hamar kifullad
                    FatigueAdxLevel = 38,

                    // =========================
                    // HYBRID PULLBACK CONTROL
                    // =========================
                    UseHybridPullbackDepth = true,
                    MaxPullbackPercentOfImpulse = 0.55,

                    // =========================
                    // SCORE CHARACTER
                    // =========================
                    ScoreWeightMultiplier = 1.15,  // agresszívebb karakter

                    // =========================
                    // PROFIT STRUCTURE
                    // =========================
                    Tp1R = 0.45,
                    RunnerMinR = 1.3,
                    MaxExtensionR = 4.8,

                    // =========================
                    // TRAILING
                    // =========================
                    TrailStartR = 1.0,
                    TrailAtrMult = 1.8,
                    MinTrailImprovePts = 16,

                    // =========================
                    // TRADE VIABILITY
                    // =========================
                    MaxAdverseRBeforeTP1 = 0.45,
                    MaxBarsWithoutProgress_M5 = 3,
                    AllowEarlyExit = true,

                    AllowAsianSession = false
                },

                // ======================================================
                // GER40 / DAX
                // ======================================================
                ["GER40"] = new IndexInstrumentProfile
                {
                    Symbol = "GER40",
                    Volatility = IndexVolatilityClass.High,
                    SessionBias = IndexSessionBias.London,
                    PullbackStyle = IndexPullbackStyle.Structure,

                    TypicalDailyRangePoints = 280,

                    // =========================
                    // IMPULSE
                    // =========================
                    ImpulseAtrMult_M5 = 0.62,
                    ImpulseAtrMult_M1 = 0.26,
                    MaxBarsSinceImpulse_M5 = 2,

                    // =========================
                    // FLAG
                    // =========================
                    FlagBars = 3,
                    MaxFlagAtrMult = 2.0,
                    BreakoutBufferAtr = 0.07,
                    MaxEmaDistanceAtr = 0.70,

                    // =========================
                    // TREND / CHOP
                    // =========================
                    MinAdxTrend = 20,
                    MinAtrPoints = 9,
                    ChopAdxThreshold = 20,
                    ChopDiDiffThreshold = 7,

                    // =========================
                    // FATIGUE CONTROL
                    // =========================
                    FatigueThreshold = 3,
                    FatigueAdxLevel = 42,

                    // =========================
                    // HYBRID PULLBACK CONTROL
                    // =========================
                    UseHybridPullbackDepth = false,
                    MaxPullbackPercentOfImpulse = 0.65,

                    // =========================
                    // SCORE CHARACTER
                    // =========================
                    ScoreWeightMultiplier = 0.95,

                    // =========================
                    // PROFIT STRUCTURE
                    // =========================
                    Tp1R = 0.40,
                    RunnerMinR = 1.0,
                    MaxExtensionR = 3.2,

                    // =========================
                    // TRAILING
                    // =========================
                    TrailStartR = 1.0,
                    TrailAtrMult = 1.6,
                    MinTrailImprovePts = 8,

                    // =========================
                    // TRADE VIABILITY
                    // =========================
                    MaxAdverseRBeforeTP1 = 0.30,
                    MaxBarsWithoutProgress_M5 = 3,
                    AllowEarlyExit = true,

                    AllowAsianSession = false
                }
            };

        public static IndexInstrumentProfile Get(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("Symbol is null or empty");

            string s = Normalize(symbol);

            if (_map.TryGetValue(s, out var profile))
                return profile;

            throw new ArgumentException(
                $"Index instrument not defined in matrix: {symbol}"
            );
        }

        private static string Normalize(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return symbol;

            var s = symbol
                .ToUpperInvariant()
                .Replace(" ", "")
                .Replace("_", "");

            if (s.Contains("USTECH100") ||
                s.Contains("USTECH") ||
                s.Contains("US100") ||
                s.Contains("NAS100"))
                return "NAS100";

            if (s.Contains("US30") ||
                s.Contains("DOW"))
                return "US30";

            if (s.Contains("GER40") ||
                s.Contains("DE40") ||
                s.Contains("DAX") ||
                s.Contains("GERMANY40"))
                return "GER40";

            return s;
        }

        public static bool Contains(string symbol)
            => _map.ContainsKey(symbol);
    }
}
