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
                    MinAtrPips = 80,
                    MaxAtrPips = 480,

                    // === Trend ===
                    MinAdxTrend = 20,
                    MinAdxStrong = 23,
                    MinAdxForPullback = 22,
                    MinAdxSlopeForPullback = 0.0,
                    MaxBarsSinceImpulseForPullback = 6,
                    AllowNeutralFlagWithStrongAdx = true,
                    NeutralFlagMinAdx = 24,

                    // === Wick / chop ===
                    MaxWickRatio = 0.55,
                    ChopLookbackBars = 5,

                    // === Impulse / flag ===
                    ImpulseAtrMult_M5 = 0.30,
                    ImpulseAtrMult_M1 = 0.30,
                    MaxFlagAtrMult = 1.45,

                    // === Range ===
                    RangeMaxWidthAtr = 1.20,

                    // === Behaviour ===
                    AllowMeanReversion = false,
                    AllowRangeBreakout = true,
                    BlockPullbackOnHighVolWithoutImpulse = false,
                    RequireStrongImpulseForPullback = false,

                    // === Score character ===
                    ScoreWeightMultiplier = 1.10
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
                    MaxBarsSinceImpulseForPullback = 7,
                    AllowNeutralFlagWithStrongAdx = true,
                    NeutralFlagMinAdx = 21,

                    // === Wick / chop ===
                    MaxWickRatio = 0.60,
                    ChopLookbackBars = 5,

                    // === Impulse / flag ===
                    ImpulseAtrMult_M5 = 0.45,
                    ImpulseAtrMult_M1 = 0.30,
                    MaxFlagAtrMult = 1.55,

                    // === Range ===
                    RangeMaxWidthAtr = 1.30,

                    // === Behaviour ===
                    AllowMeanReversion = false,
                    AllowRangeBreakout = true,
                    BlockPullbackOnHighVolWithoutImpulse = false,
                    RequireStrongImpulseForPullback = false,

                    // === Score character ===
                    ScoreWeightMultiplier = 1.08
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
