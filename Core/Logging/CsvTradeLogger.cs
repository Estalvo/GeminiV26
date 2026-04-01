using cAlgo.API;
using GeminiV26.Core.Entry;
using System;
using System.Globalization;
using System.IO;

namespace GeminiV26.Core.Logging
{
    public sealed class CsvTradeLogger : ITradeLogger
    {
        private static readonly string[] Header =
        {
            "TradeId","Symbol","PositionId","Direction","StrategyVersion",
            "EntryType","EntryReason","EntryTime","EntryPrice","VolumeInUnits",
            "EntryScore","LogicConfidence","FinalConfidence",
            "MarketRegime","Session","ATR_M5","ATR_percentile","ADX","TrendStrength","Spread",
            "InitialRiskR","RiskPercent","StopDistanceATR","SlAtrMult","LotCapHit",
            "MFE_R","MAE_R","BarsInTrade","MinutesInTrade",
            "Tp1Hit","Tp2Hit","BeActivated","TrailingActivated",
            "PostTp1MaxR","PostTp1GivebackR","Tp1ProtectExitHit","Tp1ProtectExitR",
            "Tp1ProtectScoreAtExit","Tp1ProtectMode","Tp1SmartExitHit","Tp1SmartExitType","Tp1SmartExitReason","Tp1SmartExitR","Tp1SmartBarsSinceTp1",
            "ExitMode","ExitReason","ExitTime","ExitPrice",
            "NetProfit","GrossProfit","Commissions","Swap","Pips"
        };

        private readonly LogWriter _writer;
        private readonly string _rootPath;
        private readonly Action<string> _errorSink;

        public CsvTradeLogger(LogWriter writer, Action<string> errorSink = null, string rootPath = null)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _errorSink = errorSink;
            _rootPath = string.IsNullOrWhiteSpace(rootPath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GeminiV26")
                : rootPath;
        }

        public void OnTradeOpened(TradeLogContext context) { }

        public void OnTradeUpdated(TradeLogContext context) { }

        public void OnTradeClosed(TradeLogContext context, Position position, TradeLogResult result)
        {
            // NOTE: this logger is NOT part of analytics SSOT
            // SSOT ENFORCEMENT: disabled duplicate analytics/trade CSV writer
            return;

            if (context == null)
                return;

            var snapshot = TradeSnapshot.From(position);
            _writer.Enqueue(() => WriteRecord(context, snapshot, result));
        }

        private void WriteRecord(TradeLogContext context, TradeSnapshot snapshot, TradeLogResult result)
        {
            string path = null;
            try
            {
                var now = context.TimestampUtc == default ? DateTime.UtcNow : context.TimestampUtc;
                path = BuildPath(context.Symbol, now, "Trades", "trades");
                EnsureHeader(path, Header);

                var pctx = context.PositionContext;
                var ectx = context.EntryContext;

                var values = new[]
                {
                    Csv(context.TradeId ?? snapshot?.PositionIdText),
                    Csv(context.Symbol),
                    Csv(context.PositionId?.ToString(CultureInfo.InvariantCulture)),
                    Csv(context.Direction),
                    Csv(context.StrategyVersion),

                    Csv(context.PendingMeta?.EntryType ?? pctx?.EntryType),
                    Csv(context.PendingMeta?.EntryReason ?? pctx?.EntryReason),
                    Csv(pctx?.EntryTime.ToString("O", CultureInfo.InvariantCulture)),
                    CsvNum(pctx?.EntryPrice),
                    CsvNum(snapshot?.VolumeInUnits ?? pctx?.EntryVolumeInUnits),

                    CsvNum(pctx?.EntryScore),
                    CsvNum(pctx?.LogicConfidence),
                    CsvNum(pctx?.FinalConfidence),

                    Csv(ectx?.MarketState?.ToString()),
                    Csv(ectx?.Session.ToString()),
                    CsvNum(ectx?.AtrM5),
                    Csv(null),
                    CsvNum(ectx?.Adx_M5),
                    CsvNum(ectx?.Ema21Slope_M5),
                    Csv(null),

                    CsvNum(pctx?.InitialStopLossR),
                    Csv(null),
                    CsvNum((pctx != null && ectx != null && ectx.AtrM5 > 0) ? pctx.RiskPriceDistance / ectx.AtrM5 : (double?)null),
                    CsvNum(pctx?.StopLossAtrMultiplier),
                    Csv(null),

                    CsvNum(pctx?.MfeR),
                    CsvNum(pctx?.MaeR),
                    CsvNum(pctx?.BarsSinceEntryM5),
                    CsvNum((result?.ExitTimeUtc.HasValue == true && pctx != null) ? (result.ExitTimeUtc.Value - pctx.EntryTime).TotalMinutes : (double?)null),

                    CsvBool(pctx?.Tp1Hit),
                    CsvBool((pctx != null) ? (bool?)(pctx.Tp2Hit > 0) : null),
                    CsvBool(pctx?.BeActivated),
                    CsvBool(pctx?.TrailingActivated),
                    CsvNum(result?.PostTp1MaxR),
                    CsvNum(result?.PostTp1GivebackR),
                    CsvBool(result?.Tp1ProtectExitHit),
                    CsvNum(result?.Tp1ProtectExitR),
                    CsvInt(pctx?.Tp1ProtectExitHit == true ? pctx?.Tp1ProtectScoreAtExit : null),
                    Csv(pctx?.Tp1ProtectExitHit == true ? pctx?.Tp1ProtectMode : null),
                    CsvBool(result?.Tp1SmartExitHit),
                    Csv(result?.Tp1SmartExitType),
                    Csv(result?.Tp1SmartExitReason),
                    CsvNum(result?.Tp1SmartExitR),
                    CsvInt(result?.Tp1SmartBarsSinceTp1),

                    Csv(result?.ExitMode),
                    Csv(result?.ExitReason),
                    Csv(result?.ExitTimeUtc?.ToString("O", CultureInfo.InvariantCulture)),
                    CsvNum(result?.ExitPrice),

                    CsvNum(result?.NetProfit),
                    CsvNum(result?.GrossProfit),
                    CsvNum(result?.Commissions),
                    CsvNum(result?.Swap),
                    CsvNum(result?.Pips)
                };

                File.AppendAllText(path, string.Join(",", values) + Environment.NewLine);
            }
            catch (Exception ex)
            {
                _errorSink?.Invoke($"[CSV LOGGER ERROR][TRADE] path={path ?? "<null>"} ex={ex.GetType().Name} msg={ex.Message}");
            }
        }

        private string BuildPath(string symbol, DateTime utc, string category, string suffix)
        {
            string fileDir = Path.Combine(_rootPath, "Logs", category, utc.ToString("yyyy", CultureInfo.InvariantCulture), utc.ToString("MM", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(fileDir);
            string safeSymbol = string.IsNullOrWhiteSpace(symbol) ? "UNKNOWN" : symbol.ToUpperInvariant();
            return Path.Combine(fileDir, safeSymbol + "_" + suffix + ".csv");
        }

        private static void EnsureHeader(string path, string[] header)
        {
            if (!File.Exists(path))
                File.WriteAllText(path, string.Join(",", header) + Environment.NewLine);
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        private static string CsvNum(double? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "";
        }

        private static string CsvInt(int? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "";
        }

        private static string CsvBool(bool? value)
        {
            return value.HasValue ? value.Value.ToString() : "";
        }

        private sealed class TradeSnapshot
        {
            public string PositionIdText { get; set; }
            public double? VolumeInUnits { get; set; }

            public static TradeSnapshot From(Position position)
            {
                if (position == null)
                    return null;

                return new TradeSnapshot
                {
                    PositionIdText = position.Id.ToString(CultureInfo.InvariantCulture),
                    VolumeInUnits = position.VolumeInUnits
                };
            }
        }
    }
}
