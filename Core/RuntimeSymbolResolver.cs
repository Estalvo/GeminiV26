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

            var directSymbol = _bot.Symbols.GetSymbol(requested);
            if (directSymbol != null)
            {
                runtimeName = directSymbol.Name;
                RegisterRuntimeName(runtimeName);
                return true;
            }

            return false;
        }

        public Symbol ResolveSymbol(string symbolReference)
        {
            if (string.IsNullOrWhiteSpace(symbolReference))
                return null;

            if (SymbolRouting.NormalizeSymbol(symbolReference) == SymbolRouting.NormalizeSymbol(_bot.SymbolName))
                return _bot.Symbol;

            return TryResolveRuntimeName(symbolReference, out var runtimeName)
                ? _bot.Symbols.GetSymbol(runtimeName)
                : null;
        }

        public Symbol ResolveSymbol(Position position)
        {
            return position == null ? null : ResolveSymbol(position.SymbolName);
        }

        public Bars GetBars(TimeFrame timeFrame, string symbolReference)
        {
            return TryResolveRuntimeName(symbolReference, out var runtimeName)
                ? _bot.MarketData.GetBars(timeFrame, runtimeName)
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
