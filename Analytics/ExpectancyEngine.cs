using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace GeminiV26.Analytics
{
    public sealed class ExpectancyReport
    {
        public string GroupName { get; set; } = string.Empty;
        public int SampleSize { get; set; }
        public double WinRate { get; set; }
        public double AvgR { get; set; }
        public double MedianR { get; set; }
        public double AvgWin { get; set; }
        public double AvgLoss { get; set; }
        public double Expectancy { get; set; }
        public double AvgMFE { get; set; }
        public double AvgMAE { get; set; }
        public bool IsStatisticallyRelevant { get; set; }
        public double Efficiency { get; set; }
    }

    public sealed class ExpectancyEngine
    {
        public List<ExpectancyReport> Analyze(string csvPath)
        {
            if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
                return new List<ExpectancyReport>();

            var rows = ParseRows(csvPath);
            if (rows.Count == 0)
                return new List<ExpectancyReport>();

            var reports = new List<ExpectancyReport>
            {
                BuildReport("GLOBAL", rows)
            };

            reports.AddRange(rows
                .GroupBy(r => NormalizeLabel(r.SetupType))
                .Select(g => BuildReport($"SetupType={g.Key}", g)));

            reports.AddRange(rows
                .GroupBy(r => NormalizeLabel(r.MarketRegime))
                .Select(g => BuildReport($"MarketRegime={g.Key}", g)));

            reports.AddRange(rows
                .GroupBy(r => new
                {
                    SetupType = NormalizeLabel(r.SetupType),
                    MarketRegime = NormalizeLabel(r.MarketRegime)
                })
                .Select(g => BuildReport($"SetupType={g.Key.SetupType}|MarketRegime={g.Key.MarketRegime}", g)));

            var filteredEdge = rows.Where(r =>
                r.TransitionQuality > 0.7 &&
                string.Equals(r.SetupType, "Flag", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.MarketRegime, "Trend", StringComparison.OrdinalIgnoreCase));

            reports.Add(BuildReport("EDGE:TransitionQuality>0.7|SetupType=Flag|MarketRegime=Trend", filteredEdge));

            return reports
                .OrderByDescending(r => r.Expectancy)
                .ThenByDescending(r => r.SampleSize)
                .ToList();
        }

        private static ExpectancyReport BuildReport(string groupName, IEnumerable<TradeRecord> source)
        {
            var trades = source as IList<TradeRecord> ?? source.ToList();
            var sampleSize = trades.Count;

            if (sampleSize == 0)
            {
                return new ExpectancyReport
                {
                    GroupName = groupName,
                    SampleSize = 0,
                    WinRate = 0,
                    AvgR = 0,
                    MedianR = 0,
                    AvgWin = 0,
                    AvgLoss = 0,
                    Expectancy = 0,
                    AvgMFE = 0,
                    AvgMAE = 0,
                    Efficiency = 0,
                    IsStatisticallyRelevant = false
                };
            }

            var rValues = trades.Select(t => t.RMultiple).OrderBy(v => v).ToArray();
            var winValues = trades.Where(t => t.RMultiple > 0).Select(t => t.RMultiple).ToArray();
            var lossValues = trades.Where(t => t.RMultiple < 0).Select(t => t.RMultiple).ToArray();

            var winRate = (double)winValues.Length / sampleSize;
            var avgWin = winValues.Length > 0 ? winValues.Average() : 0;
            var avgLoss = lossValues.Length > 0 ? Math.Abs(lossValues.Average()) : 0;
            var avgR = rValues.Average();
            var avgMfe = trades.Average(t => t.MfeR);
            var avgMae = trades.Average(t => t.MaeR);
            var expectancy = (winRate * avgWin) - ((1.0 - winRate) * avgLoss);
            var efficiency = avgMfe == 0 ? 0 : avgR / avgMfe;

            return new ExpectancyReport
            {
                GroupName = groupName,
                SampleSize = sampleSize,
                WinRate = winRate,
                AvgR = avgR,
                MedianR = Median(rValues),
                AvgWin = avgWin,
                AvgLoss = avgLoss,
                Expectancy = expectancy,
                AvgMFE = avgMfe,
                AvgMAE = avgMae,
                Efficiency = efficiency,
                IsStatisticallyRelevant = sampleSize >= 30
            };
        }

        private static double Median(double[] sortedValues)
        {
            if (sortedValues == null || sortedValues.Length == 0)
                return 0;

            var middle = sortedValues.Length / 2;
            if (sortedValues.Length % 2 == 0)
                return (sortedValues[middle - 1] + sortedValues[middle]) / 2.0;

            return sortedValues[middle];
        }

        private static List<TradeRecord> ParseRows(string csvPath)
        {
            var rows = new List<TradeRecord>();
            using (var reader = new StreamReader(csvPath))
            {
                if (reader.EndOfStream)
                    return rows;

                var headerLine = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(headerLine))
                    return rows;

                var headers = ParseCsvLine(headerLine);
                var indexMap = BuildIndexMap(headers);

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var cells = ParseCsvLine(line);

                    if (!TryGetDouble(cells, indexMap, "rmultiple", out var rMultiple))
                        continue;

                    TryGetDouble(cells, indexMap, "mfer", out var mfeR);
                    TryGetDouble(cells, indexMap, "maer", out var maeR);
                    TryGetDouble(cells, indexMap, "transitionquality", out var transitionQuality);

                    rows.Add(new TradeRecord
                    {
                        SetupType = GetString(cells, indexMap, "setuptype"),
                        MarketRegime = GetString(cells, indexMap, "marketregime"),
                        RMultiple = rMultiple,
                        MfeR = mfeR,
                        MaeR = maeR,
                        TransitionQuality = transitionQuality
                    });
                }
            }

            return rows;
        }

        private static Dictionary<string, int> BuildIndexMap(IReadOnlyList<string> headers)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Count; i++)
            {
                var normalized = NormalizeHeader(headers[i]);
                if (!map.ContainsKey(normalized))
                    map[normalized] = i;
            }

            return map;
        }

        private static string GetString(IReadOnlyList<string> cells, IReadOnlyDictionary<string, int> indexMap, string key)
        {
            if (!indexMap.TryGetValue(key, out var idx))
                return string.Empty;

            if (idx < 0 || idx >= cells.Count)
                return string.Empty;

            return cells[idx]?.Trim() ?? string.Empty;
        }

        private static bool TryGetDouble(IReadOnlyList<string> cells, IReadOnlyDictionary<string, int> indexMap, string key, out double value)
        {
            value = 0;
            if (!indexMap.TryGetValue(key, out var idx))
                return false;

            if (idx < 0 || idx >= cells.Count)
                return false;

            var raw = cells[idx]?.Trim();
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            return double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
        }

        private static string NormalizeHeader(string header)
        {
            return (header ?? string.Empty)
                .Replace("_", string.Empty)
                .Replace(" ", string.Empty)
                .Trim()
                .ToLowerInvariant();
        }

        private static string NormalizeLabel(string label)
        {
            return string.IsNullOrWhiteSpace(label) ? "UNKNOWN" : label.Trim();
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            if (line == null)
                return result;

            var current = string.Empty;
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current += '"';
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (c == ',' && !inQuotes)
                {
                    result.Add(current);
                    current = string.Empty;
                    continue;
                }

                current += c;
            }

            result.Add(current);
            return result;
        }

        private sealed class TradeRecord
        {
            public string SetupType { get; set; } = string.Empty;
            public string MarketRegime { get; set; } = string.Empty;
            public double MfeR { get; set; }
            public double MaeR { get; set; }
            public double RMultiple { get; set; }
            public double TransitionQuality { get; set; }
        }
    }
}
