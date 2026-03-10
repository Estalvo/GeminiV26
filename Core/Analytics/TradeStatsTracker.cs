using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GeminiV26.Core.Entry;

namespace GeminiV26.Core.Analytics
{
    public sealed class TradeStatsTracker
    {
        public sealed class TradeStats
        {
            public int Total;
            public int Wins;
            public int Losses;
            public double NetProfit;

            public double WinRate =>
                Total > 0 ? (double)Wins / Total : 0.0;
        }

        public sealed class InstrumentStats
        {
            public TradeStats All = new TradeStats();
            public TradeStats Transition = new TradeStats();
            public TradeStats NonTransition = new TradeStats();
            public TradeStats Breakout = new TradeStats();
        }

        private sealed class TradeMeta
        {
            public string Symbol;
            public bool Transition;
            public bool Breakout;
        }

        private readonly Dictionary<string, InstrumentStats> _instrumentStats = new Dictionary<string, InstrumentStats>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<long, TradeMeta> _activeByPositionId = new Dictionary<long, TradeMeta>();
        private readonly Dictionary<string, Queue<TradeMeta>> _activeBySymbol = new Dictionary<string, Queue<TradeMeta>>(StringComparer.OrdinalIgnoreCase);

        private readonly Action<string> _log;
        private int _closedTrades;

        public TradeStatsTracker(Action<string> log)
        {
            _log = log ?? (_ => { });
        }

        public void RegisterTradeOpen(EntryContext ctx)
        {
            RegisterTradeOpen(ctx, null);
        }

        public void RegisterTradeOpen(EntryContext ctx, long? positionId)
        {
            if (ctx == null || string.IsNullOrWhiteSpace(ctx.Symbol))
                return;

            var meta = new TradeMeta
            {
                Symbol = ctx.Symbol,
                Transition = ctx.TransitionValid,
                Breakout = ctx.FlagBreakoutConfirmed
            };

            if (positionId.HasValue && positionId.Value > 0)
            {
                _activeByPositionId[positionId.Value] = meta;
                return;
            }

            if (!_activeBySymbol.TryGetValue(meta.Symbol, out var queue))
            {
                queue = new Queue<TradeMeta>();
                _activeBySymbol[meta.Symbol] = queue;
            }

            queue.Enqueue(meta);
        }

        public void RegisterTradeClose(EntryContext ctx, double pnl)
        {
            if (ctx == null || string.IsNullOrWhiteSpace(ctx.Symbol))
                return;

            var symbol = ctx.Symbol;
            var stats = GetOrCreateSymbolStats(symbol);

            UpdateStats(stats.All, pnl);

            if (ctx.TransitionValid)
                UpdateStats(stats.Transition, pnl);
            else
                UpdateStats(stats.NonTransition, pnl);

            if (ctx.FlagBreakoutConfirmed)
                UpdateStats(stats.Breakout, pnl);

            _closedTrades++;
            if (_closedTrades % 20 == 0)
                PrintSummary();
        }

        public void RegisterTradeClose(long positionId, EntryContext fallbackCtx, double pnl)
        {
            if (_activeByPositionId.TryGetValue(positionId, out var meta) && meta != null)
            {
                _activeByPositionId.Remove(positionId);
                RegisterTradeClose(ToContext(meta), pnl);
                return;
            }

            if (fallbackCtx != null)
            {
                if (_activeBySymbol.TryGetValue(fallbackCtx.Symbol, out var queue) && queue.Count > 0)
                    queue.Dequeue();

                RegisterTradeClose(fallbackCtx, pnl);
                return;
            }

        }

        public void PrintSummary()
        {
            _log("[TRADE_STATS]");

            foreach (var kv in _instrumentStats.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var symbol = kv.Key;
                var s = kv.Value;

                _log(string.Empty);
                _log(symbol);
                _log($"AllTrades: total={s.All.Total} winrate={FormatWinRate(s.All.WinRate)} net={Math.Round(s.All.NetProfit, 2).ToString(CultureInfo.InvariantCulture)}");
                _log($"Transition: total={s.Transition.Total} winrate={FormatWinRate(s.Transition.WinRate)}");
                _log($"NonTransition: total={s.NonTransition.Total} winrate={FormatWinRate(s.NonTransition.WinRate)}");
                _log($"Breakout: total={s.Breakout.Total} winrate={FormatWinRate(s.Breakout.WinRate)}");

                ExportInstrumentStatsToFile(symbol, s);
            }
        }

        private void ExportInstrumentStatsToFile(string symbol, InstrumentStats stats)
        {
            const string basePath = @"C:\Users\Administrator\Documents\GeminiV26\Data\Trades";
            var instrumentPath = Path.Combine(basePath, symbol);
            Directory.CreateDirectory(instrumentPath);

            var filePath = Path.Combine(instrumentPath, "TradeStats.csv");
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            if (!File.Exists(filePath))
            {
                const string header = "Timestamp,Symbol,AllTrades,AllWinrate,AllNetProfit,TransitionTrades,TransitionWinrate,NonTransitionTrades,NonTransitionWinrate,BreakoutTrades,BreakoutWinrate";
                File.AppendAllText(filePath, header + Environment.NewLine);
            }

            var row = string.Join(",",
                timestamp,
                symbol,
                stats.All.Total.ToString(CultureInfo.InvariantCulture),
                stats.All.WinRate.ToString("0.00", CultureInfo.InvariantCulture),
                Math.Round(stats.All.NetProfit, 2).ToString("0.00", CultureInfo.InvariantCulture),
                stats.Transition.Total.ToString(CultureInfo.InvariantCulture),
                stats.Transition.WinRate.ToString("0.00", CultureInfo.InvariantCulture),
                stats.NonTransition.Total.ToString(CultureInfo.InvariantCulture),
                stats.NonTransition.WinRate.ToString("0.00", CultureInfo.InvariantCulture),
                stats.Breakout.Total.ToString(CultureInfo.InvariantCulture),
                stats.Breakout.WinRate.ToString("0.00", CultureInfo.InvariantCulture));

            File.AppendAllText(filePath, row + Environment.NewLine);
        }

        private static EntryContext ToContext(TradeMeta meta)
        {
            return new EntryContext
            {
                Symbol = meta.Symbol,
                TransitionValid = meta.Transition,
                FlagBreakoutConfirmed = meta.Breakout
            };
        }

        private InstrumentStats GetOrCreateSymbolStats(string symbol)
        {
            if (!_instrumentStats.TryGetValue(symbol, out var stats))
            {
                stats = new InstrumentStats();
                _instrumentStats[symbol] = stats;
            }

            return stats;
        }

        private static void UpdateStats(TradeStats stats, double pnl)
        {
            stats.Total++;
            stats.NetProfit += pnl;

            if (pnl > 0)
                stats.Wins++;
            else if (pnl < 0)
                stats.Losses++;
        }

        private static string FormatWinRate(double winRate)
        {
            return winRate.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
