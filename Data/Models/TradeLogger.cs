using GeminiV26.Data.Models;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace GeminiV26.Data
{
    public class TradeLogger
    {
        private readonly string _symbol;
        private readonly string _baseDir;

        public TradeLogger(string symbol)
        {
            _symbol = symbol;

            _baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "GeminiV26",
                "Data",
                "Trades",
                _symbol
            );

            Directory.CreateDirectory(_baseDir);
        }

        public void Log(TradeRecord t)
        {
            // 🔒 Instrument guard
            if (!string.Equals(t.Symbol, _symbol, StringComparison.OrdinalIgnoreCase))
                return;
            string date = t.ExitTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string fileName = $"{_symbol}_Trades_{date}.csv";
            string path = Path.Combine(_baseDir, fileName);

            bool writeHeader = !File.Exists(path);

            var sb = new StringBuilder();

            if (writeHeader)
            {
                sb.AppendLine(
                    "CloseTimestamp,Symbol,PositionId,Direction,EntryType,EntryReason,MetaStatus," +
                    "EntryTime,ExitTime,EntryPrice,ExitPrice,VolumeInUnits," +
                    "EntryVolumeInUnits,Tp1ClosedVolumeInUnits,RemainingVolumeInUnits," +
                    "Confidence,Tp1Hit,Tp2Hit," +

                    // --- NEW ---
                    "RiskPercent,SlAtrMult,Tp1R,Tp2R,LotCapHit," +
                    "BeActivated,TrailingActivated,ExitMode," +

                    "ExitReason,NetProfit,GrossProfit,Commissions,Swap,Pips"
                );
            }

            sb.AppendLine(string.Join(",",
                t.CloseTimestamp.ToString("O", CultureInfo.InvariantCulture),
                t.Symbol,
                t.PositionId.ToString(CultureInfo.InvariantCulture),
                t.Direction,

                string.IsNullOrEmpty(t.EntryType) ? "__MISSING__" : t.EntryType,
                Escape(string.IsNullOrEmpty(t.EntryReason) ? "__MISSING__" : t.EntryReason),

                string.IsNullOrEmpty(t.EntryType) && string.IsNullOrEmpty(t.EntryReason)
                    ? "META_MISSING"
                    : "META_OK",

                t.EntryTime.ToString("O", CultureInfo.InvariantCulture),
                t.ExitTime.ToString("O", CultureInfo.InvariantCulture),
                t.EntryPrice.ToString(CultureInfo.InvariantCulture),
                t.ExitPrice.ToString(CultureInfo.InvariantCulture),
                t.VolumeInUnits.ToString(CultureInfo.InvariantCulture),
                (t.EntryVolumeInUnits?.ToString(CultureInfo.InvariantCulture) ?? ""),
                (t.Tp1ClosedVolumeInUnits?.ToString(CultureInfo.InvariantCulture) ?? ""),
                (t.RemainingVolumeInUnits?.ToString(CultureInfo.InvariantCulture) ?? ""),
                t.Confidence?.ToString() ?? "",
                t.Tp1Hit?.ToString() ?? "",
                t.Tp2Hit?.ToString() ?? "",
                t.RiskPercent?.ToString(CultureInfo.InvariantCulture) ?? "",
                t.SlAtrMult?.ToString(CultureInfo.InvariantCulture) ?? "",
                t.Tp1R?.ToString(CultureInfo.InvariantCulture) ?? "",
                t.Tp2R?.ToString(CultureInfo.InvariantCulture) ?? "",
                t.LotCapHit?.ToString() ?? "",
                t.BeActivated?.ToString() ?? "",
                t.TrailingActivated?.ToString() ?? "",
                Escape(t.ExitMode),
                Escape(t.ExitReason),
                t.NetProfit.ToString(CultureInfo.InvariantCulture),
                t.GrossProfit.ToString(CultureInfo.InvariantCulture),
                t.Commissions.ToString(CultureInfo.InvariantCulture),
                t.Swap.ToString(CultureInfo.InvariantCulture),
                t.Pips.ToString(CultureInfo.InvariantCulture)
            ));

            File.AppendAllText(path, sb.ToString());
        }

        private string Escape(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "";

            if (input.Contains(",") || input.Contains("\""))
                return $"\"{input.Replace("\"", "\"\"")}\"";

            return input;
        }
    }
}