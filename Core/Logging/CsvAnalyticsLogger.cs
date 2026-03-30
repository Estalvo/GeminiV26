using cAlgo.API;
using GeminiV26.Core.Entry;
using System;
using System.Globalization;
using System.IO;

namespace GeminiV26.Core.Logging
{
    public sealed class CsvAnalyticsLogger : ITradeLogger
    {
        private static readonly string[] Header =
        {
            "Timestamp","Symbol","Direction",
            "SetupType","SetupQuality","TransitionQuality","StructureQuality",
            "ImpulseStrength","ImpulseBars","PullbackDepth","PullbackBars",
            "BreakDistanceATR",
            "MarketRegime","Session","ATR_percentile","TrendStrength","ADX",
            "EntryScore","LogicConfidence","FinalConfidence",
            "Outcome","MFE_R","MAE_R"
        };

        private readonly LogWriter _writer;
        private readonly string _rootPath;
        private readonly Action<string> _errorSink;

        public CsvAnalyticsLogger(LogWriter writer, Action<string> errorSink = null, string rootPath = null)
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
            // SSOT ENFORCEMENT: disabled duplicate analytics writer
            return;

            if (context == null)
                return;

            _writer.Enqueue(() => WriteRecord(context, result));
        }

        private void WriteRecord(TradeLogContext context, TradeLogResult result)
        {
            string path = null;
            try
            {
                var timestamp = result?.ExitTimeUtc ?? context.TimestampUtc;
                if (timestamp == default)
                    timestamp = DateTime.UtcNow;

                path = BuildPath(context.Symbol, timestamp, "Analytics", "analytics");
                EnsureHeader(path, Header);

                var pctx = context.PositionContext;
                var ectx = context.EntryContext;
                var transition = ectx?.Transition;

                string outcome = "FLAT";
                if (result?.NetProfit.HasValue == true)
                {
                    if (result.NetProfit.Value > 0) outcome = "WIN";
                    else if (result.NetProfit.Value < 0) outcome = "LOSS";
                }

                var values = new[]
                {
                    Csv(timestamp.ToString("O", CultureInfo.InvariantCulture)),
                    Csv(context.Symbol),
                    Csv(context.Direction),

                    Csv(context.PendingMeta?.EntryType ?? pctx?.EntryType),
                    CsvNum(pctx?.EntryScore),
                    CsvNum(transition?.QualityScore),
                    CsvNum(ectx?.TransitionScoreBonus),

                    CsvNum(ectx?.AdxSlope_M5),
                    CsvNum(ectx?.BarsSinceImpulse_M5),
                    CsvNum(ectx?.PullbackDepthAtr_M5),
                    CsvNum(ectx?.PullbackBars_M5),

                    CsvNum(ectx?.RangeBreakAtrSize_M5),

                    Csv(ectx?.MarketState?.ToString()),
                    Csv(ectx?.Session.ToString()),
                    Csv(null),
                    CsvNum(ectx?.Ema21Slope_M5),
                    CsvNum(ectx?.Adx_M5),

                    CsvNum(pctx?.EntryScore),
                    CsvNum(pctx?.LogicConfidence),
                    CsvNum(pctx?.FinalConfidence),

                    Csv(outcome),
                    CsvNum(pctx?.MfeR),
                    CsvNum(pctx?.MaeR)
                };

                File.AppendAllText(path, string.Join(",", values) + Environment.NewLine);
            }
            catch (Exception ex)
            {
                _errorSink?.Invoke($"[CSV LOGGER ERROR][ANALYTICS] path={path ?? "<null>"} ex={ex.GetType().Name} msg={ex.Message}");
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
    }
}
