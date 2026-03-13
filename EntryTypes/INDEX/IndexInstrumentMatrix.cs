using System;
using System.Collections.Generic;
using GeminiV26.Core;

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
                    ImpulseAtrMult_M5 = 0.62,
                    ImpulseAtrMult_M1 = 0.24,
                    MaxBarsSinceImpulse_M5 = 3,

                    // =========================
                    // FLAG
                    // =========================
                    FlagBars = 3,
                    MaxFlagAtrMult = 3.0,
                    BreakoutBufferAtr = 0.12,
                    MaxEmaDistanceAtr = 0.75,

                    // =========================
                    // TREND / CHOP
                    // =========================
                    MinAdxTrend = 21,
                    MinAtrPoints = 10,
                    ChopAdxThreshold = 18,
                    ChopDiDiffThreshold = 8,

                    // =========================
                    // FATIGUE CONTROL
                    // =========================
                    FatigueThreshold = 3,
                    FatigueAdxLevel = 42,

                    // =========================
                    // HYBRID PULLBACK CONTROL
                    // =========================
                    UseHybridPullbackDepth = false,
                    MaxPullbackPercentOfImpulse = 0.48,

                    // =========================
                    // SCORE CHARACTER
                    // =========================
                    ScoreWeightMultiplier = 1.08,

                    // =========================
                    // PROFIT STRUCTURE
                    // =========================
                    Tp1R = 0.50,
                    RunnerMinR = 1.2,
                    MaxExtensionR = 3.8,

                    // =========================
                    // TRAILING
                    // =========================
                    TrailStartR = 1.2,
                    TrailAtrMult = 1.8,
                    MinTrailImprovePts = 10,

                    // =========================
                    // TRADE VIABILITY
                    // =========================
                    MaxAdverseRBeforeTP1 = 0.40,
                    MaxBarsWithoutProgress_M5 = 3,
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
                    ImpulseAtrMult_M5 = 0.72,
                    ImpulseAtrMult_M1 = 0.30,
                    MaxBarsSinceImpulse_M5 = 3,

                    // =========================
                    // FLAG
                    // =========================
                    FlagBars = 4,
                    MaxFlagAtrMult = 3.0,
                    BreakoutBufferAtr = 0.15,
                    MaxEmaDistanceAtr = 1.00,

                    // =========================
                    // TREND / CHOP
                    // =========================
                    MinAdxTrend = 20,
                    MinAtrPoints = 16,
                    ChopAdxThreshold = 17,
                    ChopDiDiffThreshold = 7,

                    // =========================
                    // FATIGUE CONTROL
                    // =========================
                    FatigueThreshold = 2,      // US30 hamar kifullad
                    FatigueAdxLevel = 38,

                    // =========================
                    // HYBRID PULLBACK CONTROL
                    // =========================
                    UseHybridPullbackDepth = true,
                    MaxPullbackPercentOfImpulse = 0.48,

                    // =========================
                    // SCORE CHARACTER
                    // =========================
                    ScoreWeightMultiplier = 1.20,  // agresszívebb karakter

                    // =========================
                    // PROFIT STRUCTURE
                    // =========================
                    Tp1R = 0.50,
                    RunnerMinR = 1.4,
                    MaxExtensionR = 4.8,

                    // =========================
                    // TRAILING
                    // =========================
                    TrailStartR = 1.2,
                    TrailAtrMult = 1.8,
                    MinTrailImprovePts = 16,

                    // =========================
                    // TRADE VIABILITY
                    // =========================
                    MaxAdverseRBeforeTP1 = 0.45,
                    MaxBarsWithoutProgress_M5 = 2,
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
                    ImpulseAtrMult_M5 = 0.65,
                    ImpulseAtrMult_M1 = 0.26,
                    MaxBarsSinceImpulse_M5 = 2,

                    // =========================
                    // FLAG
                    // =========================
                    FlagBars = 3,
                    MaxFlagAtrMult = 3.0,
                    BreakoutBufferAtr = 0.11,
                    MaxEmaDistanceAtr = 0.70,

                    // =========================
                    // TREND / CHOP
                    // =========================
                    MinAdxTrend = 22,
                    MinAtrPoints = 9,
                    ChopAdxThreshold = 19,
                    ChopDiDiffThreshold = 8,

                    // =========================
                    // FATIGUE CONTROL
                    // =========================
                    FatigueThreshold = 3,
                    FatigueAdxLevel = 42,

                    // =========================
                    // HYBRID PULLBACK CONTROL
                    // =========================
                    UseHybridPullbackDepth = false,
                    MaxPullbackPercentOfImpulse = 0.52,

                    // =========================
                    // SCORE CHARACTER
                    // =========================
                    ScoreWeightMultiplier = 1.02,

                    // =========================
                    // PROFIT STRUCTURE
                    // =========================
                    Tp1R = 0.45,
                    RunnerMinR = 1.15,
                    MaxExtensionR = 3.2,

                    // =========================
                    // TRAILING
                    // =========================
                    TrailStartR = 1.1,
                    TrailAtrMult = 1.6,
                    MinTrailImprovePts = 8,

                    // =========================
                    // TRADE VIABILITY
                    // =========================
                    MaxAdverseRBeforeTP1 = 0.30,
                    MaxBarsWithoutProgress_M5 = 2,
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

            return SymbolRouting.NormalizeSymbol(symbol);
        }

        public static bool Contains(string symbol)
            => _map.ContainsKey(Normalize(symbol));
    }
}