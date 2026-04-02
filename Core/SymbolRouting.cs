using System;
using System.Collections.Generic;

namespace GeminiV26.Core
{
    public enum InstrumentClass
    {
        UNKNOWN,
        FX,
        INDEX,
        METAL,
        CRYPTO
    }

    public static class SymbolRouting
    {
        private static readonly Dictionary<string, string[]> KnownRuntimeSymbolMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["NAS100"] = new[] { "NAS100", "US100", "USTECH100", "USTECH" },
            ["GER40"] = new[] { "GER40", "DE40", "DAX40", "DAX", "GERMANY40", "GER" },
            ["US30"] = new[] { "US30", "DJ30", "DOW" },
            ["BTCUSD"] = new[] { "BTCUSD", "XBTUSD" },
            ["XAUUSD"] = new[] { "XAUUSD", "GOLD" },
            ["XAGUSD"] = new[] { "XAGUSD", "SILVER" }
        };

        public static string NormalizeSymbol(string symbol)
        {
            var s = (symbol ?? string.Empty)
                .ToUpperInvariant()
                .Replace(" ", string.Empty)
                .Replace("_", string.Empty);

            return s switch
            {
                "US100" => "NAS100",
                "USTECH100" => "NAS100",
                "USTECH" => "NAS100",
                "GER" => "GER40",
                "DE40" => "GER40",
                "DAX" => "GER40",
                "DAX40" => "GER40",
                "GERMANY40" => "GER40",
                "DJ30" => "US30",
                "DOW" => "US30",
                "XBTUSD" => "BTCUSD",
                _ => s
            };
        }

        public static InstrumentClass ResolveInstrumentClass(string symbol)
        {
            var s = NormalizeSymbol(symbol);

            if (string.IsNullOrWhiteSpace(s))
                return InstrumentClass.UNKNOWN;

            if (s == "XAUUSD" || s == "XAGUSD")
                return InstrumentClass.METAL;

            if (s == "BTCUSD" || s == "ETHUSD")
                return InstrumentClass.CRYPTO;

            if (s == "NAS100" || s == "US30" || s == "GER40")
                return InstrumentClass.INDEX;

            if (s.Length == 6)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    if (s[i] < 'A' || s[i] > 'Z')
                        return InstrumentClass.UNKNOWN;
                }

                return InstrumentClass.FX;
            }

            return InstrumentClass.UNKNOWN;
        }

        public static IReadOnlyList<string> GetKnownRuntimeCandidates(string canonical)
        {
            var normalized = NormalizeSymbol(canonical);
            if (KnownRuntimeSymbolMap.TryGetValue(normalized, out var known))
                return known;

            return new[] { normalized };
        }
    }
}
