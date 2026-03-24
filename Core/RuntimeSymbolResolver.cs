using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;

namespace GeminiV26.Core
{
    /// <summary>
    /// Central runtime symbol access layer.
    /// </summary>
    public sealed class RuntimeSymbolResolver
    {
        private readonly Robot _bot;
        private readonly Dictionary<string, Symbol> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _resolverOkLogged = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _resolverErrorLogged = new(StringComparer.OrdinalIgnoreCase);

        public RuntimeSymbolResolver(Robot bot)
        {
            _bot = bot ?? throw new ArgumentNullException(nameof(bot));
            Refresh();
        }

        public void Refresh()
        {
            _cache.Clear();
            _resolverOkLogged.Clear();
            _resolverErrorLogged.Clear();

            if (IsUsableSymbol(_bot.Symbol))
                _cache[_bot.Symbol.Name] = _bot.Symbol;
        }

        public bool TryResolveRuntimeName(string symbolReference, out string runtimeName)
        {
            runtimeName = null;
            return TryResolveSymbol(symbolReference, out var symbol) && (runtimeName = symbol.Name) != null;
        }

        public bool TryResolveSymbol(string symbolReference, out Symbol symbol)
        {
            symbol = null;

            if (string.IsNullOrWhiteSpace(symbolReference))
                return false;

            string requested = symbolReference.Trim();

            if (_cache.ContainsKey(requested))
            {
                symbol = _cache[requested];
                return symbol != null;
            }

            if (string.Equals(requested, _bot.Symbol?.Name, StringComparison.OrdinalIgnoreCase))
            {
                symbol = _bot.Symbol;
                if (IsUsableSymbol(symbol))
                {
                    CacheAndLogOk(requested, symbol);
                    return true;
                }
            }

            var symbols = _bot.Symbols;
            symbol = symbols.GetSymbol(requested);
            if (IsUsableSymbol(symbol) && IsTradable(symbol))
            {
                CacheAndLogOk(requested, symbol);
                return true;
            }

            Symbol fallback = null;
            foreach (var symbolEntry in symbols)
            {
                object raw = symbolEntry;
                string symbolName = raw is Symbol entrySymbol
                    ? entrySymbol.Name
                    : raw?.ToString();

                if (string.IsNullOrWhiteSpace(symbolName))
                    continue;

                var candidate = symbols.GetSymbol(symbolName);
                if (IsUsableSymbol(candidate) &&
                    IsTradable(candidate) &&
                    candidate.Name.StartsWith(requested, StringComparison.OrdinalIgnoreCase))
                {
                    fallback = candidate;
                    break;
                }
            }

            if (IsUsableSymbol(fallback))
            {
                if (_resolverOkLogged.Add($"FALLBACK:{requested}:{fallback.Name}"))
                    _bot.Print($"[RESOLVER][FALLBACK] {requested} → {fallback.Name}");

                CacheAndLogOk(requested, fallback);
                return true;
            }

            if (_resolverErrorLogged.Add(requested))
                _bot.Print($"[RESOLVER][ERROR] Invalid symbol: {requested}");

            return false;
        }

        public bool TryGetBars(TimeFrame timeFrame, string symbolReference, out Bars bars)
        {
            bars = null;

            if (!TryResolveRuntimeName(symbolReference, out var runtimeName))
                return false;

            bars = _bot.MarketData.GetBars(timeFrame, runtimeName);
            return bars != null;
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

        private void CacheAndLogOk(string requested, Symbol symbol)
        {
            if (!IsUsableSymbol(symbol))
                return;

            _cache[requested] = symbol;
            _cache[symbol.Name] = symbol;

            if (_resolverOkLogged.Add($"OK:{requested}:{symbol.Name}"))
                _bot.Print($"[RESOLVER][OK] {requested} → {symbol.Name}");
        }

        private static bool IsUsableSymbol(Symbol symbol)
        {
            return symbol != null && !string.IsNullOrWhiteSpace(symbol.Name);
        }

        private static bool IsTradable(Symbol symbol)
        {
            return symbol != null && symbol.Bid != 0 && symbol.Ask != 0;
        }
    }
}
