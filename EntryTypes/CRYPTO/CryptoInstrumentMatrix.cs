using System;
using System.Collections.Generic;

namespace GeminiV26.EntryTypes.Crypto
{
    public static class CryptoInstrumentMatrix
    {
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
                    MaxAtrPips = 650,

                    // === Trend ===
                    MinAdxTrend = 22,
                    MinAdxStrong = 26,

                    // === Wick / chop ===
                    MaxWickRatio = 0.62,
                    ChopLookbackBars = 3,

                    // === Impulse / flag ===
                    ImpulseAtrMult_M5 = 0.55,
                    ImpulseAtrMult_M1 = 0.20,
                    MaxFlagAtrMult = 0.9,

                    // === Range ===
                    RangeMaxWidthAtr = 1.25,

                    // === Behaviour ===
                    AllowMeanReversion = false,
                    AllowRangeBreakout = true
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
                    MinAdxTrend = 21,
                    MinAdxStrong = 24,

                    // === Wick / chop ===
                    MaxWickRatio = 0.60,
                    ChopLookbackBars = 3,

                    // === Impulse / flag ===
                    ImpulseAtrMult_M5 = 0.52,
                    ImpulseAtrMult_M1 = 0.18,
                    MaxFlagAtrMult = 0.85,

                    // === Range ===
                    RangeMaxWidthAtr = 1.15,

                    // === Behaviour ===
                    AllowMeanReversion = false,
                    AllowRangeBreakout = true
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
