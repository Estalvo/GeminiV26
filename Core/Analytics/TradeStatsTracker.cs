using System;
using System.Collections.Generic;
using System.Globalization;
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

        public sealed class TradeCloseSnapshot
        {
            public DateTime TimestampUtc;
            public string Symbol;
            public string PositionId;
            public string Direction;
            public double EntryPrice;
            public double ExitPrice;
            public double Profit;
            public DateTime OpenTimeUtc;
            public DateTime CloseTimeUtc;
            public int? Score;
            public int? Confidence;
            public string SetupType;
            public string EntryType;
            public string InstrumentClass;
            public string MarketRegime;
            public double MfeR;
            public double MaeR;
            public double RMultiple;
            public double TransitionQuality;
            public double AccountBalanceAtEntry;
        }

        private sealed class ScalingStats
        {
            public int Trades;
            public int Wins;
            public readonly List<double> RValues = new List<double>();
            public double SumMfe;
            public double SumMae;
        }

        private readonly Dictionary<string, InstrumentStats> _instrumentStats = new Dictionary<string, InstrumentStats>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<long, TradeMeta> _activeByPositionId = new Dictionary<long, TradeMeta>();
        private readonly Dictionary<string, Queue<TradeMeta>> _activeBySymbol = new Dictionary<string, Queue<TradeMeta>>(StringComparer.OrdinalIgnoreCase);

        private readonly Action<string> _log;
        private int _closedTrades;
        private readonly Dictionary<string, ScalingStats> _scalingByAccountSize = new Dictionary<string, ScalingStats>(StringComparer.OrdinalIgnoreCase);

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

            RegisterTradeClose(ctx, pnl, null);
        }

        public void RegisterTradeClose(EntryContext ctx, double pnl, TradeCloseSnapshot snapshot)
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

            WriteTradeRow(snapshot ?? new TradeCloseSnapshot
            {
                TimestampUtc = DateTime.UtcNow,
                Symbol = symbol,
                PositionId = "UNKNOWN",
                Direction = string.Empty,
                EntryPrice = 0,
                ExitPrice = 0,
                Profit = pnl,
                OpenTimeUtc = DateTime.UtcNow,
                CloseTimeUtc = DateTime.UtcNow,
                Score = null,
                Confidence = null,
                SetupType = string.Empty,
                EntryType = string.Empty,
                InstrumentClass = string.Empty,
                MarketRegime = string.Empty,
                MfeR = 0,
                MaeR = 0,
                RMultiple = 0,
                TransitionQuality = 0
            });

            _closedTrades++;
            if (snapshot != null)
                UpdateScalingStats(snapshot);
            if (_closedTrades % 20 == 0)
                PrintSummary();
        }

        public void RegisterTradeClose(long positionId, EntryContext fallbackCtx, double pnl)
        {
            RegisterTradeClose(positionId, fallbackCtx, pnl, null);
        }

        public void RegisterTradeClose(long positionId, EntryContext fallbackCtx, double pnl, TradeCloseSnapshot snapshot)
        {
            if (_activeByPositionId.TryGetValue(positionId, out var meta) && meta != null)
            {
                _activeByPositionId.Remove(positionId);
                RegisterTradeClose(ToContext(meta), pnl, snapshot);
                return;
            }

            if (fallbackCtx != null)
            {
                if (_activeBySymbol.TryGetValue(fallbackCtx.Symbol, out var queue) && queue.Count > 0)
                    queue.Dequeue();

                RegisterTradeClose(fallbackCtx, pnl, snapshot);
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

            PrintScalingSummary();
        }

        private void UpdateScalingStats(TradeCloseSnapshot snapshot)
        {
            string bucket = ResolveAccountSizeBucket(snapshot.AccountBalanceAtEntry);
            if (!_scalingByAccountSize.TryGetValue(bucket, out var stats))
            {
                stats = new ScalingStats();
                _scalingByAccountSize[bucket] = stats;
            }

            stats.Trades++;
            if (snapshot.Profit > 0)
                stats.Wins++;
            stats.RValues.Add(snapshot.RMultiple);
            stats.SumMfe += snapshot.MfeR;
            stats.SumMae += snapshot.MaeR;
        }

        private void PrintScalingSummary()
        {
            foreach (var kv in _scalingByAccountSize.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var bucket = kv.Key;
                var stats = kv.Value;
                if (stats.Trades <= 0)
                    continue;

                var orderedR = stats.RValues.OrderBy(x => x).ToList();
                double avgR = orderedR.Average();
                double medianR = orderedR.Count % 2 == 1
                    ? orderedR[orderedR.Count / 2]
                    : (orderedR[orderedR.Count / 2 - 1] + orderedR[orderedR.Count / 2]) / 2.0;
                double winrate = (double)stats.Wins / stats.Trades;
                double avgMfe = stats.SumMfe / stats.Trades;
                double avgMae = stats.SumMae / stats.Trades;

                _log($"[SCALING][SUMMARY] accountSize={bucket} avgR={avgR:0.####} medianR={medianR:0.####} winrate={winrate:0.####} avgMFE={avgMfe:0.####} avgMAE={avgMae:0.####} trades={stats.Trades}");
            }
        }

        private static string ResolveAccountSizeBucket(double balance)
        {
            if (balance <= 0)
                return "UNKNOWN";

            double[] anchors = { 10000, 25000, 50000, 100000 };
            double nearest = anchors[0];
            double nearestDistance = Math.Abs(balance - nearest);
            for (int i = 1; i < anchors.Length; i++)
            {
                double d = Math.Abs(balance - anchors[i]);
                if (d < nearestDistance)
                {
                    nearest = anchors[i];
                    nearestDistance = d;
                }
            }

            return nearest.ToString("0", CultureInfo.InvariantCulture);
        }

        private void ExportInstrumentStatsToFile(string symbol, InstrumentStats stats)
        {
            // SSOT ENFORCEMENT: direct CSV writing disabled
            // All analytics routed through UnifiedAnalyticsWriter
            _ = symbol;
            _ = stats;
            return;
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

        private void WriteTradeRow(TradeCloseSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.Symbol))
                return;

            try
            {
                // Active analytics SSOT path:
                // TradeStatsTracker -> UnifiedAnalyticsWriter.
                var record = new TradeAnalyticsRecord
                {
                    Symbol = snapshot.Symbol.Trim(),
                    PositionId = string.IsNullOrWhiteSpace(snapshot.PositionId) ? "UNKNOWN" : snapshot.PositionId,
                    SetupType = snapshot.SetupType ?? string.Empty,
                    EntryType = snapshot.EntryType ?? string.Empty,
                    InstrumentClass = snapshot.InstrumentClass ?? string.Empty,
                    MarketRegime = snapshot.MarketRegime ?? string.Empty,
                    MfeR = snapshot.MfeR,
                    MaeR = snapshot.MaeR,
                    RMultiple = snapshot.RMultiple,
                    TransitionQuality = snapshot.TransitionQuality,
                    FinalConfidence = snapshot.Confidence ?? 0,
                    Confidence = snapshot.Confidence ?? 0,
                    Profit = snapshot.Profit,
                    OpenTimeUtc = snapshot.OpenTimeUtc,
                    CloseTimeUtc = snapshot.CloseTimeUtc
                };

                var writeOk = UnifiedAnalyticsWriter.Write(record);
                if (writeOk)
                {
                    _log($"[TRADESTATS] trade logged {record.Symbol}");
                }
                else
                {
                    _log($"[TRADESTATS][ANALYTICS_DEGRADED] symbol={record.Symbol} posId={record.PositionId}");
                }
            }
            catch (Exception ex)
            {
                _log($"[TRADESTATS][ERROR] {ex.Message}");
            }
        }
    }
}
