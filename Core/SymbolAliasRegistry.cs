using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Internals;

namespace GeminiV26.Core
{
    public static class SymbolAliasRegistry
    {
        private static readonly Dictionary<string, string> AliasMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "AUDNZD", "AUDNZD" },
            { "AUDUSD", "AUDUSD" },
            { "BTCUSD", "BTCUSD" },
            { "ETHUSD", "ETHUSD" },
            { "EURJPY", "EURJPY" },
            { "EURUSD", "EURUSD" },
            { "GBPJPY", "GBPJPY" },
            { "GBPUSD", "GBPUSD" },
            { "GER40", "GERMANY 40" },
            { "NAS100", "US TECH 100" },
            { "NZDUSD", "NZDUSD" },
            { "US30", "US 30" },
            { "USDCAD", "USDCAD" },
            { "USDCHF", "USDCHF" },
            { "USDJPY", "USDJPY" },
            { "XAUUSD", "GOLD" }
        };

        private static readonly Dictionary<string, string> Resolved = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, bool> ResolvedViaAlias = new(StringComparer.OrdinalIgnoreCase);

        public static string Resolve(Symbols symbols, string canonical)
        {
            ResolveInternal(symbols, canonical, out _, out _);
            return Resolved.TryGetValue(canonical ?? string.Empty, out var runtime) ? runtime : canonical;
        }

        public static string Resolve(Symbols symbols, string canonical, out bool aliasResolved, out bool fallbackUsed)
        {
            return ResolveInternal(symbols, canonical, out aliasResolved, out fallbackUsed);
        }

        private static string ResolveInternal(Symbols symbols, string canonical, out bool aliasResolved, out bool fallbackUsed)
        {
            aliasResolved = false;
            fallbackUsed = false;

            if (string.IsNullOrWhiteSpace(canonical))
                return null;

            if (Resolved.TryGetValue(canonical, out var cached))
            {
                aliasResolved = ResolvedViaAlias.TryGetValue(canonical, out var viaAlias) && viaAlias;
                return cached;
            }

            string resolved = AliasMap.TryGetValue(canonical, out var alias)
                ? alias
                : canonical;

            var symbol = symbols.GetSymbol(resolved);
            if (symbol == null)
                return null;

            Resolved[canonical] = symbol.Name;
            aliasResolved = !string.Equals(resolved, canonical, StringComparison.OrdinalIgnoreCase);
            ResolvedViaAlias[canonical] = aliasResolved;
            return symbol.Name;
        }
    }
}
