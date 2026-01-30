using GeminiV26.Data.Models;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace GeminiV26.Data
{
    public class EventLogger
    {
        private readonly string _symbol;
        private readonly string _baseDir;

        public EventLogger(string symbol)
        {
            _symbol = symbol;

            _baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "GeminiV26",
                "Data",
                "Events",
                _symbol
            );

            Directory.CreateDirectory(_baseDir);
        }

        public void Log(EventRecord e)
        {
            string date = e.EventTimestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string fileName = $"{_symbol}_Events_{date}.csv";
            string path = Path.Combine(_baseDir, fileName);

            bool writeHeader = !File.Exists(path);

            var sb = new StringBuilder();

            if (writeHeader)
            {
                sb.AppendLine(
                    "EventTimestamp,Symbol,EventType,PositionId,Confidence,Reason,Extra,RValue"
                );
            }

            sb.AppendLine(string.Join(",",
                e.EventTimestamp.ToString("O", CultureInfo.InvariantCulture),
                e.Symbol,
                e.EventType,
                e.PositionId.ToString(),
                e.Confidence?.ToString() ?? "",
                Escape(e.Reason),
                Escape(e.Extra),
                e.RValue?.ToString(CultureInfo.InvariantCulture) ?? ""
            ));

            File.AppendAllText(path, sb.ToString());
        }

        private string Escape(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "";

            // CSV safe
            if (input.Contains(",") || input.Contains("\""))
                return $"\"{input.Replace("\"", "\"\"")}\"";

            return input;
        }
    }
}
