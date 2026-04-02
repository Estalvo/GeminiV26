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
        private readonly Robot _bot;
        private readonly Dictionary<string, Symbol> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Symbol> _cycleCache = new(StringComparer.OrdinalIgnoreCase);

        public RuntimeSymbolResolver(Robot bot)
        {
            _bot = bot ?? throw new ArgumentNullException(nameof(bot));
            Refresh();
        }

        public void Refresh()
        {
            _cache.Clear();
            _cycleCache.Clear();
        }

        public void BeginExecutionCycle()
        {
            _cycleCache.Clear();
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
                GlobalLogger.Log(_bot, "[RESOLVER][INPUT] input=");
                GlobalLogger.Log(_bot, "[RESOLVER][SKIP] reason=empty_input");
                return false;
            }

            string requested = symbolReference.Trim();
            if (_cycleCache.TryGetValue(requested, out symbol))
                return IsUsableSymbol(symbol);

            GlobalLogger.Log(_bot, $"[RESOLVER][INPUT] input={requested}");

            string canonical = SymbolRouting.NormalizeSymbol(requested);
            var resolvedClass = SymbolRouting.ResolveInstrumentClass(canonical);
            var classLogBucket = resolvedClass == InstrumentClass.UNKNOWN ? "UNSUPPORTED" : resolvedClass.ToString();
            GlobalLogger.Log(_bot, $"[RESOLVER][CANONICAL] input={requested} canonical={canonical}");
            GlobalLogger.Log(_bot, $"[SYMBOL][CLASSIFY][{classLogBucket}] raw={requested} normalized={canonical} resolved={resolvedClass}");

            if (TryResolveCurrentBotSymbol(canonical, out symbol))
            {
                CacheAliases(requested, canonical, symbol);
                GlobalLogger.Log(_bot, $"[RESOLVER][RUNTIME] source=current_bot runtime={symbol.Name}");
                GlobalLogger.Log(_bot, $"[RESOLVER][SUCCESS] input={requested} canonical={canonical} runtime={symbol.Name}");
                _cycleCache[requested] = symbol;
                return true;
            }

            if (!IsGeminiSupportedCanonical(canonical))
            {
                GlobalLogger.Log(_bot, $"[RESOLVER][SKIP] reason=unsupported_canonical canonical={canonical}");
                _cycleCache[requested] = null;
                return false;
            }

            if (_cache.TryGetValue(requested, out symbol) && IsUsableSymbol(symbol))
            {
                GlobalLogger.Log(_bot, $"[RESOLVER][RUNTIME] source=cache runtime={symbol.Name}");
                GlobalLogger.Log(_bot, $"[RESOLVER][SUCCESS] input={requested} canonical={canonical} runtime={symbol.Name}");
                _cycleCache[requested] = symbol;
                return true;
            }

            symbol = _bot.Symbols.GetSymbol(canonical);
            if (IsUsableSymbol(symbol))
            {
                CacheAliases(requested, canonical, symbol);
                GlobalLogger.Log(_bot, $"[RESOLVER][RUNTIME] source=direct runtime={symbol.Name}");
                GlobalLogger.Log(_bot, $"[RESOLVER][SUCCESS] input={requested} canonical={canonical} runtime={symbol.Name}");
                _cycleCache[requested] = symbol;
                return true;
            }

            if (TryResolveKnownRuntimeMapping(requested, canonical, out symbol))
            {
                GlobalLogger.Log(_bot, $"[RESOLVER][RUNTIME] source=known_map runtime={symbol.Name}");
                GlobalLogger.Log(_bot, $"[RESOLVER][SUCCESS] input={requested} canonical={canonical} runtime={symbol.Name}");
                _cycleCache[requested] = symbol;
                return true;
            }

            GlobalLogger.Log(_bot, "[SYMBOL][SKIP] " + canonical);
            GlobalLogger.Log(_bot, $"[RESOLVER][SKIP] reason=runtime_not_found canonical={canonical}");
            _cycleCache[requested] = null;
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

        private static bool IsUsableSymbol(Symbol symbol)
        {
            return symbol != null && !string.IsNullOrWhiteSpace(symbol.Name);
        }

        private static bool IsGeminiSupportedCanonical(string canonical)
        {
            if (string.IsNullOrWhiteSpace(canonical))
                return false;

            var instrumentClass = SymbolRouting.ResolveInstrumentClass(canonical);
            if (instrumentClass == InstrumentClass.UNKNOWN)
                return false;

            if (instrumentClass != InstrumentClass.FX)
                return true;

            if (canonical.Length != 6)
                return false;

            for (int i = 0; i < canonical.Length; i++)
            {
                if (canonical[i] < 'A' || canonical[i] > 'Z')
                    return false;
            }

            return true;
        }

        private void CacheAliases(string requested, string canonical, Symbol symbol)
        {
            if (!IsUsableSymbol(symbol))
                return;

            _cache[requested] = symbol;
            _cache[canonical] = symbol;
            _cache[symbol.Name] = symbol;
        }

        private bool TryResolveCurrentBotSymbol(string canonical, out Symbol symbol)
        {
            symbol = _bot.Symbol;
            if (!IsUsableSymbol(symbol))
                return false;

            string botCanonicalFromName = SymbolRouting.NormalizeSymbol(_bot.SymbolName);
            string botCanonicalFromSymbol = SymbolRouting.NormalizeSymbol(symbol.Name);

            return string.Equals(canonical, botCanonicalFromName, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(canonical, botCanonicalFromSymbol, StringComparison.OrdinalIgnoreCase);
        }

        private bool TryResolveKnownRuntimeMapping(string requested, string canonical, out Symbol symbol)
        {
            symbol = null;

            var candidates = SymbolRouting.GetKnownRuntimeCandidates(canonical);
            for (int i = 0; i < candidates.Count; i++)
            {
                string candidate = candidates[i];
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                var mapped = _bot.Symbols.GetSymbol(candidate);
                if (!IsUsableSymbol(mapped))
                    continue;

                CacheAliases(requested, canonical, mapped);
                symbol = mapped;
                return true;
            }

            return false;
        }
    }
}
