using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using GeminiV26.Core.Logging;

namespace GeminiV26.Core.Analytics
{
    public static class UnifiedAnalyticsWriter
    {
        private static readonly ConcurrentDictionary<string, object> _locks = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private const string Header = "Symbol,PositionId,SetupType,EntryType,MarketRegime,MfeR,MaeR,RMultiple,TransitionQuality,Confidence,Profit,OpenTimeUtc,CloseTimeUtc";
        private const string FallbackHeader = "Symbol,PositionId,SetupType,EntryType,MarketRegime,MfeR,MaeR,RMultiple,TransitionQuality,Confidence,Profit,OpenTimeUtc,CloseTimeUtc,FailureReason";
        private static int _degradedState;

        public static bool IsDegraded => _degradedState == 1;

        public static bool Write(TradeAnalyticsRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.Symbol))
            {
                GlobalLogger.Log("[ANALYTICS][WRITE_FAIL] symbol=NA posId=NA ex=InvalidRecord msg=Record or Symbol is missing", null, null);
                MarkDegraded();
                return false;
            }

            try
            {
                var basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GeminiV26", "Logs", "Analytics");
                Directory.CreateDirectory(basePath);

                var filePath = Path.Combine(basePath, $"trades_{DateTime.UtcNow:yyyyMMdd}.csv");
                var fileLock = _locks.GetOrAdd(filePath, _ => new object());

                lock (fileLock)
                {
                    var writeHeader = !File.Exists(filePath);
                    using (var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read))
                    using (var writer = new StreamWriter(stream))
                    {
                        var resolvedPositionId = string.IsNullOrWhiteSpace(record.PositionId) ? "UNKNOWN" : record.PositionId;
                        if (resolvedPositionId == "UNKNOWN")
                        {
                            GlobalLogger.Log($"[ANALYTICS][WARNING] Missing PositionId symbol={record.Symbol}", null, null);
                        }

                        if (writeHeader)
                            writer.WriteLine(Header);

                        var row = string.Join(",",
                            Csv(record.Symbol),
                            Csv(resolvedPositionId),
                            Csv(record.SetupType),
                            Csv(record.EntryType),
                            Csv(record.MarketRegime),
                            record.MfeR.ToString("0.####", CultureInfo.InvariantCulture),
                            record.MaeR.ToString("0.####", CultureInfo.InvariantCulture),
                            record.RMultiple.ToString("0.####", CultureInfo.InvariantCulture),
                            record.TransitionQuality.ToString("0.####", CultureInfo.InvariantCulture),
                            record.Confidence.ToString("0.####", CultureInfo.InvariantCulture),
                            record.Profit.ToString(CultureInfo.InvariantCulture),
                            record.OpenTimeUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                            record.CloseTimeUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

                        writer.WriteLine(row);
                    }
                }

                var normalizedPositionId = string.IsNullOrWhiteSpace(record.PositionId) ? "UNKNOWN" : record.PositionId;
                GlobalLogger.Log($"[ANALYTICS][WRITE_OK] symbol={record.Symbol} posId={normalizedPositionId} R={record.RMultiple}", null, normalizedPositionId);
                return true;
            }
            catch (Exception ex)
            {
                var normalizedPositionId = string.IsNullOrWhiteSpace(record.PositionId) ? "UNKNOWN" : record.PositionId;
                GlobalLogger.Log($"[ANALYTICS][WRITE_FAIL] symbol={record.Symbol} posId={normalizedPositionId} ex={ex.GetType().Name} msg={ex.Message}", null, normalizedPositionId);
                MarkDegraded();
                TryWriteFallback(record, ex.Message);
                return false;
            }
        }

        private static void TryWriteFallback(TradeAnalyticsRecord record, string failureReason)
        {
            try
            {
                var basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GeminiV26", "Logs", "Analytics");
                Directory.CreateDirectory(basePath);

                var filePath = Path.Combine(basePath, $"fallback_trades_{DateTime.UtcNow:yyyyMMdd}.csv");
                var fileLock = _locks.GetOrAdd(filePath, _ => new object());
                var resolvedPositionId = string.IsNullOrWhiteSpace(record.PositionId) ? "UNKNOWN" : record.PositionId;

                lock (fileLock)
                {
                    var writeHeader = !File.Exists(filePath);
                    using (var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read))
                    using (var writer = new StreamWriter(stream))
                    {
                        if (writeHeader)
                            writer.WriteLine(FallbackHeader);

                        var row = string.Join(",",
                            Csv(record.Symbol),
                            Csv(resolvedPositionId),
                            Csv(record.SetupType),
                            Csv(record.EntryType),
                            Csv(record.MarketRegime),
                            record.MfeR.ToString("0.####", CultureInfo.InvariantCulture),
                            record.MaeR.ToString("0.####", CultureInfo.InvariantCulture),
                            record.RMultiple.ToString("0.####", CultureInfo.InvariantCulture),
                            record.TransitionQuality.ToString("0.####", CultureInfo.InvariantCulture),
                            record.Confidence.ToString("0.####", CultureInfo.InvariantCulture),
                            record.Profit.ToString(CultureInfo.InvariantCulture),
                            record.OpenTimeUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                            record.CloseTimeUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                            Csv(failureReason ?? "PrimaryWriteFailed"));

                        writer.WriteLine(row);
                    }
                }

                GlobalLogger.Log($"[ANALYTICS][FALLBACK_OK] symbol={record.Symbol} posId={resolvedPositionId}", null, resolvedPositionId);
            }
            catch (Exception fallbackEx)
            {
                var normalizedPositionId = string.IsNullOrWhiteSpace(record?.PositionId) ? "UNKNOWN" : record.PositionId;
                GlobalLogger.Log($"[ANALYTICS][FALLBACK_FAIL] symbol={record?.Symbol ?? "NA"} posId={normalizedPositionId} ex={fallbackEx.GetType().Name} msg={fallbackEx.Message}", null, normalizedPositionId);
            }
        }

        private static void MarkDegraded()
        {
            _degradedState = 1;
        }

        private static string Csv(string value)
        {
            return value ?? string.Empty;
        }
    }
}
