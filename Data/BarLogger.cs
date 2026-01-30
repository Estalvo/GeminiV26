using cAlgo.API;
using GeminiV26.Data.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace GeminiV26.Data
{
    /// <summary>
    /// Folyamatos M1 / M5 bar logger.
    /// - MEMORY-FIRST (M1 trigger innen dolgozik)
    /// - CSV csak log / audit célra
    /// - Multi-instance SAFE (FileShare.ReadWrite)
    /// </summary>
    public class BarLogger
    {
        private readonly Robot _bot;
        private readonly string _symbol;
        private readonly string _baseDir;

        private readonly Bars _barsM1;
        private readonly Bars _barsM5;

        private DateTime _lastM1BarTime = DateTime.MinValue;
        private DateTime _lastM5BarTime = DateTime.MinValue;

        // =========================
        // 🧠 GLOBAL M1 BAR CACHE
        // =========================
        public static readonly Dictionary<string, List<BarLogRecord>> M1Cache
            = new Dictionary<string, List<BarLogRecord>>();

        private const int MaxCachedBars = 300;

        public BarLogger(Robot bot)
        {
            _bot = bot;
            _symbol = bot.SymbolName;

            _barsM1 = bot.MarketData.GetBars(TimeFrame.Minute);
            _barsM5 = bot.MarketData.GetBars(TimeFrame.Minute5);

            _baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "GeminiV26",
                "Data",
                "Bars",
                _symbol
            );

            Directory.CreateDirectory(_baseDir);
        }

        /// <summary>
        /// Hívás OnTick()-ből.
        /// </summary>
        public void OnTick()
        {
            LogBarsInternal(_barsM1, "M1", ref _lastM1BarTime);
            LogBarsInternal(_barsM5, "M5", ref _lastM5BarTime);
        }

        // =====================================================
        // INTERNAL
        // =====================================================

        private void LogBarsInternal(Bars bars, string tf, ref DateTime lastLoggedBarTime)
        {
            if (bars == null || bars.Count < 2)
                return;

            int idx = bars.Count - 2;
            DateTime barTime = bars.OpenTimes[idx];

            if (barTime <= lastLoggedBarTime)
                return;

            lastLoggedBarTime = barTime;

            var record = new BarLogRecord
            {
                LogTimestamp = DateTime.UtcNow,
                BarTimestamp = barTime,
                Symbol = _symbol,
                Timeframe = tf,
                BarOpen = bars.OpenPrices[idx],
                BarHigh = bars.HighPrices[idx],
                BarLow = bars.LowPrices[idx],
                BarClose = bars.ClosePrices[idx],
                BarVolume = bars.TickVolumes[idx],
                BarSpread = GetSpreadSafe()
            };

            // =========================
            // 🧠 MEMORY CACHE (M1)
            // =========================
            if (tf == "M1")
                UpdateM1Cache(record);

            // =========================
            // 💾 CSV LOG (SAFE)
            // =========================
            WriteCsvSafe(record);
        }

        private void UpdateM1Cache(BarLogRecord record)
        {
            if (!M1Cache.ContainsKey(_symbol))
                M1Cache[_symbol] = new List<BarLogRecord>();

            var list = M1Cache[_symbol];
            list.Add(record);

            if (list.Count > MaxCachedBars)
                list.RemoveAt(0);
        }

        private double GetSpreadSafe()
        {
            try
            {
                return _bot.Symbol.Spread;
            }
            catch
            {
                return -1;
            }
        }

        // =====================================================
        // CSV – MULTI INSTANCE SAFE
        // =====================================================
        private void WriteCsvSafe(BarLogRecord r)
        {
            string date = r.BarTimestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string fileName = $"{_symbol}_{r.Timeframe}_{date}.csv";
            string path = Path.Combine(_baseDir, fileName);

            bool writeHeader = !File.Exists(path);

            var sb = new StringBuilder();

            if (writeHeader)
            {
                sb.AppendLine(
                    "LogTimestamp,BarTimestamp,Symbol,Timeframe,BarOpen,BarHigh,BarLow,BarClose,BarVolume,BarSpread"
                );
            }

            sb.AppendLine(string.Join(",",
                r.LogTimestamp.ToString("O", CultureInfo.InvariantCulture),
                r.BarTimestamp.ToString("O", CultureInfo.InvariantCulture),
                r.Symbol,
                r.Timeframe,
                r.BarOpen.ToString(CultureInfo.InvariantCulture),
                r.BarHigh.ToString(CultureInfo.InvariantCulture),
                r.BarLow.ToString(CultureInfo.InvariantCulture),
                r.BarClose.ToString(CultureInfo.InvariantCulture),
                r.BarVolume.ToString(CultureInfo.InvariantCulture),
                r.BarSpread.ToString(CultureInfo.InvariantCulture)
            ));

            using (var fs = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite))
            using (var sw = new StreamWriter(fs))
            {
                sw.Write(sb.ToString());
            }
        }
    }
}
