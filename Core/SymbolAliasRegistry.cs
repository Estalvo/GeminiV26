using System;
using System.Collections.Generic;
using cAlgo.API.Internals;

namespace GeminiV26.Core
{
    public static class SymbolAliasRegistry
    {
        private static readonly Dictionary<string, string[]> AliasMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "GER40", new[] { "GERMANY 40", "GER40" } },
            { "NAS100", new[] { "US TECH 100", "NAS100" } },
            { "US30", new[] { "US 30", "US30" } }
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
                return canonical;

            if (Resolved.TryGetValue(canonical, out var cached))
            {
                aliasResolved = ResolvedViaAlias.TryGetValue(canonical, out var viaAlias) && viaAlias;
                fallbackUsed = !aliasResolved && string.Equals(cached, canonical, StringComparison.OrdinalIgnoreCase);
                return cached;
            }

            if (!AliasMap.TryGetValue(canonical, out var candidates))
            {
                Resolved[canonical] = canonical;
                ResolvedViaAlias[canonical] = false;
                fallbackUsed = true;
                return canonical;
            }

            foreach (var candidate in candidates)
            {
                if (!symbols.Exists(candidate))
                    continue;

                Resolved[canonical] = candidate;
                aliasResolved = !string.Equals(candidate, canonical, StringComparison.OrdinalIgnoreCase);
                ResolvedViaAlias[canonical] = aliasResolved;
                fallbackUsed = false;
                return candidate;
            }

            Resolved[canonical] = canonical;
            ResolvedViaAlias[canonical] = false;
            fallbackUsed = true;
            return canonical;
        }
    }
}
