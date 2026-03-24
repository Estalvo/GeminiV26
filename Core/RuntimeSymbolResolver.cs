using System;
using System.Collections;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Internals;

namespace GeminiV26.Core
{
    /// <summary>
    /// Central runtime symbol access layer.
    ///
    /// Goals:
    /// - keep canonical instrument names inside the strategy
    /// - resolve broker/runtime symbol names from valid live context
    /// - prevent scattered direct string-based Symbols.GetSymbol / MarketData.GetBars calls
    /// </summary>
    public sealed class RuntimeSymbolResolver
    {
        private readonly Robot _bot;
        private readonly Dictionary<string, string> _runtimeNamesByCanonical = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _aliasResolvedLogged = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _aliasFallbackLogged = new(StringComparer.OrdinalIgnoreCase);

        public RuntimeSymbolResolver(Robot bot)
        {
            _bot = bot ?? throw new ArgumentNullException(nameof(bot));
            Refresh();
        }

        public void Refresh()
        {
            RegisterRuntimeName(_bot.SymbolName);
            RegisterRuntimeName(_bot.Symbol?.Name);

            foreach (var position in _bot.Positions)
            {
                RegisterRuntimeName(position?.SymbolName);
            }

            if (_bot.Symbols is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item is Symbol symbol)
                    {
                        RegisterRuntimeName(symbol.Name);
                    }
                }
            }
        }

        public bool TryResolveRuntimeName(string symbolReference, out string runtimeName)
        {
            runtimeName = null;

            try
            {
                if (string.IsNullOrWhiteSpace(symbolReference))
                    return false;

                Refresh();

                string requested = symbolReference.Trim();
                string canonical = SymbolRouting.NormalizeSymbol(requested);
                if (string.IsNullOrWhiteSpace(canonical))
                    return false;

                if (SymbolRouting.NormalizeSymbol(_bot.SymbolName) == canonical)
                {
                    runtimeName = _bot.SymbolName;
                    return true;
                }

                if (_runtimeNamesByCanonical.TryGetValue(canonical, out runtimeName) && !string.IsNullOrWhiteSpace(runtimeName))
                    return true;

                string aliasRuntime = SymbolAliasRegistry.Resolve(_bot.Symbols, canonical, out var aliasResolved, out var fallbackUsed);
                if (!string.IsNullOrWhiteSpace(aliasRuntime))
                {
                    if (aliasResolved && _aliasResolvedLogged.Add(canonical))
                        _bot.Print($"[RESOLVER][ALIAS_RESOLVED] {canonical} → {aliasRuntime}");

                    if (fallbackUsed && _aliasFallbackLogged.Add(canonical))
                        _bot.Print($"[RESOLVER][ALIAS_FALLBACK] {canonical} → {aliasRuntime} (NOT FOUND)");

                    var aliasSymbol = _bot.Symbols.GetSymbol(aliasRuntime);
                    if (aliasSymbol != null)
                    {
                        runtimeName = aliasSymbol.Name;
                        RegisterRuntimeName(runtimeName);
                        return true;
                    }
                }

                var directSymbol = _bot.Symbols.GetSymbol(requested);
                if (directSymbol != null)
                {
                    runtimeName = directSymbol.Name;
                    RegisterRuntimeName(runtimeName);
                    return true;
                }
            }
            catch
            {
                runtimeName = null;
                return false;
            }

            runtimeName = null;
            return false;
        }

        public bool TryResolveSymbol(string symbolReference, out Symbol symbol)
        {
            symbol = null;

            try
            {
                if (string.IsNullOrWhiteSpace(symbolReference))
                    return false;

                if (SymbolRouting.NormalizeSymbol(symbolReference) == SymbolRouting.NormalizeSymbol(_bot.SymbolName))
                {
                    symbol = _bot.Symbol;
                    return symbol != null;
                }

                if (!TryResolveRuntimeName(symbolReference, out var runtimeName))
                    return false;

                symbol = _bot.Symbols.GetSymbol(runtimeName);
                return symbol != null;
            }
            catch
            {
                symbol = null;
                return false;
            }
        }

        public bool TryGetBars(TimeFrame timeFrame, string symbolReference, out Bars bars)
        {
            bars = null;

            try
            {
                if (!TryResolveRuntimeName(symbolReference, out var runtimeName))
                    return false;

                bars = _bot.MarketData.GetBars(timeFrame, runtimeName);
                return bars != null;
            }
            catch
            {
                bars = null;
                return false;
            }
        }

        public bool TryGetPipSize(string symbolReference, out double pipSize)
        {
            pipSize = 0;

            if (!TryGetSymbolMeta(symbolReference, out var symbol))
                return false;

            pipSize = symbol.PipSize;
            return pipSize > 0;
        }

        public bool TryGetSymbolMeta(string symbolReference, out Symbol symbol)
        {
            return TryResolveSymbol(symbolReference, out symbol);
        }

        public Symbol ResolveSymbol(string symbolReference)
        {
            return TryResolveSymbol(symbolReference, out var symbol)
                ? symbol
                : null;
        }

        public Symbol ResolveSymbol(Position position)
        {
            return position == null ? null : ResolveSymbol(position.SymbolName);
        }

        public Bars GetBars(TimeFrame timeFrame, string symbolReference)
        {
            return TryGetBars(timeFrame, symbolReference, out var bars)
                ? bars
                : null;
        }

        private void RegisterRuntimeName(string runtimeName)
        {
            if (string.IsNullOrWhiteSpace(runtimeName))
                return;

            string canonical = SymbolRouting.NormalizeSymbol(runtimeName);
            if (string.IsNullOrWhiteSpace(canonical))
                return;

            _runtimeNamesByCanonical[canonical] = runtimeName;
        }
    }
}
