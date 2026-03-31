using cAlgo.API;
using GeminiV26.Core.Entry;
using System;
using System.Collections.Generic;

namespace GeminiV26.Core.Logging
{
    public interface ITradeLogger
    {
        void OnTradeOpened(TradeLogContext context);
        void OnTradeUpdated(TradeLogContext context);
        void OnTradeClosed(TradeLogContext context, Position position, TradeLogResult result);
    }

    public sealed class TradeLogContext
    {
        public DateTime TimestampUtc { get; set; }
        public string Symbol { get; set; }
        public string Direction { get; set; }
        public string StrategyVersion { get; set; }
        public long? PositionId { get; set; }
        public string TradeId { get; set; }

        public PositionContext PositionContext { get; set; }
        public EntryContext EntryContext { get; set; }
        public PendingEntryMeta PendingMeta { get; set; }
    }

    public sealed class TradeLogResult
    {
        public string ExitMode { get; set; }
        public string ExitReason { get; set; }
        public DateTime? ExitTimeUtc { get; set; }
        public double? ExitPrice { get; set; }
        public double? NetProfit { get; set; }
        public double? GrossProfit { get; set; }
        public double? Commissions { get; set; }
        public double? Swap { get; set; }
        public double? Pips { get; set; }
        public double? PostTp1MaxR { get; set; }
        public double? PostTp1GivebackR { get; set; }
        public bool? Tp1ProtectExitHit { get; set; }
        public double? Tp1ProtectExitR { get; set; }
        public int? Tp1ProtectScoreAtExit { get; set; }
        public string Tp1ProtectMode { get; set; }
    }

    public sealed class CompositeTradeLogger : ITradeLogger
    {
        private readonly IReadOnlyList<ITradeLogger> _loggers;

        public CompositeTradeLogger(params ITradeLogger[] loggers)
        {
            _loggers = loggers ?? Array.Empty<ITradeLogger>();
        }

        public void OnTradeOpened(TradeLogContext context)
        {
            foreach (var logger in _loggers)
                logger?.OnTradeOpened(context);
        }

        public void OnTradeUpdated(TradeLogContext context)
        {
            foreach (var logger in _loggers)
                logger?.OnTradeUpdated(context);
        }

        public void OnTradeClosed(TradeLogContext context, Position position, TradeLogResult result)
        {
            foreach (var logger in _loggers)
                logger?.OnTradeClosed(context, position, result);
        }
    }
}
