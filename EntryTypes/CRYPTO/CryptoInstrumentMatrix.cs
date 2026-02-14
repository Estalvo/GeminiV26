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
                    MinAdxStrong = 22,

                    // === Wick / chop ===
                    MaxWickRatio = 0.55,
                    ChopLookbackBars = 5,

                    // === Impulse / flag ===
                    ImpulseAtrMult_M5 = 0.30,
                    ImpulseAtrMult_M1 = 0.30,
                    MaxFlagAtrMult = 1.1,

                    // === Range ===
                    RangeMaxWidthAtr = 1.05,

                    // === Behaviour ===
                    AllowMeanReversion = false,
                    AllowRangeBreakout = true,
                    BlockPullbackOnHighVolWithoutImpulse = false,
                    RequireStrongImpulseForPullback = false
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

                    // === Wick / chop ===
                    MaxWickRatio = 0.60,
                    ChopLookbackBars = 5,

                    // === Impulse / flag ===
                    ImpulseAtrMult_M5 = 0.45,
                    ImpulseAtrMult_M1 = 0.30,
                    MaxFlagAtrMult = 1.0,

                    // === Range ===
                    RangeMaxWidthAtr = 1.15,

                    // === Behaviour ===
                    AllowMeanReversion = false,
                    AllowRangeBreakout = true,
                    BlockPullbackOnHighVolWithoutImpulse = false,
                    RequireStrongImpulseForPullback = false
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
