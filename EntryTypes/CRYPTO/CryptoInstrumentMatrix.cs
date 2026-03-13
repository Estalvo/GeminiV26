using System;
using System.Collections.Generic;

namespace GeminiV26.EntryTypes.Crypto
{
    public static class CryptoInstrumentMatrix
    {

        // ================================
        // PULLBACK MOMENTUM CONTROL
        // ================================
               
        private static readonly Dictionary<string, CryptoInstrumentProfile> _map =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // =========================
                // BTC
                // =========================
                ["BTCUSD"] = new CryptoInstrumentProfile
                {
                    Symbol = "BTCUSD",

                    // === Volatility ===
                    MinAtrPips = 90,
                    MaxAtrPips = 520,

                    // === Trend ===
                    MinAdxTrend = 19,
                    MinAdxStrong = 24,
                    MinAdxForPullback = 21,
                    MinAdxSlopeForPullback = -0.1,
                    MaxBarsSinceImpulseForPullback = 9,
                    AllowNeutralFlagWithStrongAdx = true,
                    NeutralFlagMinAdx = 24,

                    // === Wick / chop ===
                    MaxWickRatio = 0.65,
                    ChopLookbackBars = 4,

                    // === Impulse / flag ===
                    ImpulseAtrMult_M5 = 0.40,
                    ImpulseAtrMult_M1 = 0.35,
                    MaxFlagAtrMult = 1.35,

                    // === Range ===
                    RangeMaxWidthAtr = 0.95,

                    // === Behaviour ===
                    AllowMeanReversion = false,
                    AllowRangeBreakout = true,
                    BlockPullbackOnHighVolWithoutImpulse = false,
                    RequireStrongImpulseForPullback = false,

                    // === Score character ===
                    ScoreWeightMultiplier = 1.18
                },

                // =========================
                // ETH (BTC-light)
                // =========================
                ["ETHUSD"] = new CryptoInstrumentProfile
                {
                    Symbol = "ETHUSD",

                    // === Volatility ===
                    MinAtrPips = 35,
                    MaxAtrPips = 320,

                    // === Trend ===
                    MinAdxTrend = 19,
                    MinAdxStrong = 22,
                    MinAdxForPullback = 20,
                    MinAdxSlopeForPullback = -0.2,
                    MaxBarsSinceImpulseForPullback = 8,
                    AllowNeutralFlagWithStrongAdx = true,
                    NeutralFlagMinAdx = 21,

                    // === Wick / chop ===
                    MaxWickRatio = 0.62,
                    ChopLookbackBars = 5,

                    // === Impulse / flag ===
                    ImpulseAtrMult_M5 = 0.48,
                    ImpulseAtrMult_M1 = 0.35,
                    MaxFlagAtrMult = 1.40,

                    // === Range ===
                    RangeMaxWidthAtr = 1.30,

                    // === Behaviour ===
                    AllowMeanReversion = false,
                    AllowRangeBreakout = true,
                    BlockPullbackOnHighVolWithoutImpulse = false,
                    RequireStrongImpulseForPullback = false,

                    // === Score character ===
                    ScoreWeightMultiplier = 1.12
                }
            };

        public static CryptoInstrumentProfile Get(string symbol)
        {
            if (_map.TryGetValue(symbol, out var p))
                return p;

            throw new ArgumentException($"Crypto instrument not defined: {symbol}");
        }

        public static bool Contains(string symbol)
            => _map.ContainsKey(symbol);
    }
}
