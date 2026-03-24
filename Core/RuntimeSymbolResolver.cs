using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Internals;

namespace GeminiV26.Core
{
    /// <summary>
    /// Central runtime symbol access layer.
    /// </summary>
    public sealed class RuntimeSymbolResolver
    {
        private static readonly Dictionary<string, string> CanonicalSymbolMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AUDUSD"] = "AUDUSD",
            ["EURUSD"] = "EURUSD",
            ["XAUUSD"] = "XAUUSD",
            ["NAS100"] = "US TECH 100",
            ["US30"] = "US 30",
            ["GER40"] = "GERMANY 40"
        };

        private readonly Robot _bot;
        private readonly Dictionary<string, Symbol> _cache = new(StringComparer.OrdinalIgnoreCase);

        public RuntimeSymbolResolver(Robot bot)
        {
            _bot = bot ?? throw new ArgumentNullException(nameof(bot));
            Refresh();
        }

        public void Refresh()
        {
            _cache.Clear();
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
            {
                _bot.Print("[RESOLVER][INPUT] input=");
                _bot.Print("[RESOLVER][FATAL] symbol=");
                _bot.Print("[CORE][BLOCK] symbol resolution failed");
                return false;
            }

            string requested = symbolReference.Trim().ToUpperInvariant();
            _bot.Print($"[RESOLVER][INPUT] input={requested}");

            if (_cache.ContainsKey(requested))
            {
                symbol = _cache[requested];
                return symbol != null;
            }

            if (!CanonicalSymbolMap.TryGetValue(requested, out string canonical))
            {
                _bot.Print($"[RESOLVER][FATAL] symbol={requested}");
                _bot.Print("[CORE][BLOCK] symbol resolution failed");
                return false;
            }

            _bot.Print($"[RESOLVER][CANONICAL] resolved={canonical}");

            symbol = _bot.Symbols.GetSymbol(canonical);
            if (!IsUsableSymbol(symbol) || !IsTradable(symbol))
            {
                _bot.Print($"[RESOLVER][FATAL] symbol={canonical}");
                _bot.Print("[CORE][BLOCK] symbol resolution failed");
                return false;
            }

            _cache[requested] = symbol;
            _cache[canonical] = symbol;
            _cache[symbol.Name] = symbol;
            _bot.Print($"[RESOLVER][SUCCESS] symbol={symbol.Name} tradable=TRUE");
            return true;
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
