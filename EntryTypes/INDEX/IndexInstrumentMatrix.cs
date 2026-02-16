using System;
using System.Collections.Generic;

namespace GeminiV26.Instruments.INDEX
{
    public static class IndexInstrumentMatrix
    {
        private static readonly Dictionary<string, IndexInstrumentProfile> _map =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // =========================
                // NAS100 / US100 / US TECH
                // =========================
                ["NAS100"] = new IndexInstrumentProfile
                {
                    Symbol = "NAS100",
                    Volatility = IndexVolatilityClass.High,
                    SessionBias = IndexSessionBias.NewYork,
                    PullbackStyle = IndexPullbackStyle.Shallow,

                    TypicalDailyRangePoints = 350,

                    // ===== IMPULSE =====
                    ImpulseAtrMult_M5 = 0.65,
                    ImpulseAtrMult_M1 = 0.26,
                    MaxBarsSinceImpulse_M5 = 4,

                    // ===== FLAG =====
                    FlagBars = 3,
                    MaxFlagAtrMult = 2.4,
                    BreakoutBufferAtr = 0.06,
                    MaxEmaDistanceAtr = 0.90,

                    // ===== TREND / FILTER =====
                    MinAdxTrend = 19,
                    MinAtrPoints = 9,

                    // ===== PROFIT EXTENSION =====
                    Tp1R = 0.45,
                    RunnerMinR = 0.9,
                    MaxExtensionR = 3.5,

                    // ===== TRAILING =====
                    TrailStartR = 1.0,
                    TrailAtrMult = 1.6,
                    MinTrailImprovePts = 8,

                    // ===== TRADE VIABILITY =====
                    MaxAdverseRBeforeTP1 = 0.35,
                    MaxBarsWithoutProgress_M5 = 4,
                    AllowEarlyExit = true,

                    // ===== SESSION =====
                    AllowAsianSession = true
                },

                // =========================
                // US30 / DOW JONES
                // =========================
                ["US30"] = new IndexInstrumentProfile
                {
                    Symbol = "US30",
                    Volatility = IndexVolatilityClass.Extreme,
                    SessionBias = IndexSessionBias.NewYork,
                    PullbackStyle = IndexPullbackStyle.Structure,

                    TypicalDailyRangePoints = 600,

                    // ===== IMPULSE =====
                    ImpulseAtrMult_M5 = 0.75,
                    ImpulseAtrMult_M1 = 0.32,
                    MaxBarsSinceImpulse_M5 = 5,

                    // ===== FLAG =====
                    FlagBars = 4,
                    MaxFlagAtrMult = 2.8,
                    BreakoutBufferAtr = 0.08,
                    MaxEmaDistanceAtr = 1.10,

                    // ===== TREND / FILTER =====
                    MinAdxTrend = 21,
                    MinAtrPoints = 14,

                    // ===== PROFIT EXTENSION =====
                    Tp1R = 0.50,
                    RunnerMinR = 1.2,
                    MaxExtensionR = 4.5,

                    // ===== TRAILING =====
                    TrailStartR = 1.3,
                    TrailAtrMult = 2.0,
                    MinTrailImprovePts = 14,

                    // ===== TRADE VIABILITY =====
                    MaxAdverseRBeforeTP1 = 0.45,
                    MaxBarsWithoutProgress_M5 = 5,
                    AllowEarlyExit = true,

                    // ===== SESSION =====
                    AllowAsianSession = false
                },

                // =========================
                // GER40 / DAX
                // =========================
                ["GER40"] = new IndexInstrumentProfile
                {
                    Symbol = "GER40",
                    Volatility = IndexVolatilityClass.High,
                    SessionBias = IndexSessionBias.London,
                    PullbackStyle = IndexPullbackStyle.EMA21,

                    TypicalDailyRangePoints = 250,

                    // ===== IMPULSE =====
                    ImpulseAtrMult_M5 = 0.58,
                    ImpulseAtrMult_M1 = 0.23,
                    MaxBarsSinceImpulse_M5 = 3,

                    // ===== FLAG =====
                    FlagBars = 3,
                    MaxFlagAtrMult = 2.2,
                    BreakoutBufferAtr = 0.05,
                    MaxEmaDistanceAtr = 0.75,

                    // ===== TREND / FILTER =====
                    MinAdxTrend = 18,
                    MinAtrPoints = 7,

                    // ===== PROFIT EXTENSION =====
                    Tp1R = 0.40,
                    RunnerMinR = 0.8,
                    MaxExtensionR = 3.0,

                    // ===== TRAILING =====
                    TrailStartR = 0.9,
                    TrailAtrMult = 1.4,
                    MinTrailImprovePts = 6,

                    // ===== TRADE VIABILITY =====
                    MaxAdverseRBeforeTP1 = 0.30,
                    MaxBarsWithoutProgress_M5 = 3,
                    AllowEarlyExit = true,

                    // ===== SESSION =====
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

            // ===== NAS100 =====
            if (s.Contains("USTECH100") ||
                s.Contains("USTECH") ||
                s.Contains("US100") ||
                s.Contains("NAS100"))
                return "NAS100";

            // ===== US30 =====
            if (s.Contains("US30") ||
                s.Contains("DOW"))
                return "US30";

            // ===== GER40 =====
            if (s.Contains("GER40") ||
                s.Contains("DE40") ||
                s.Contains("DAX") ||
                s.Contains("GERMANY40"))   // ← JAVÍTVA (nincs space)
                return "GER40";

            return s;
        }

        public static bool Contains(string symbol)
            => _map.ContainsKey(symbol);
    }
}
