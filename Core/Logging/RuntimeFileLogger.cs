using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace GeminiV26.Core.Logging
{
    public static class RuntimeFileLogger
    {
        private static readonly object Sync = new object();

        public static void Write(string message)
        {
            if (message == null)
                return;

            var utcNow = DateTime.UtcNow;
            var runtimeDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "GeminiV26",
                "Logs",
                "Runtime");

            Directory.CreateDirectory(runtimeDir);

            var path = Path.Combine(runtimeDir, $"runtime_{utcNow:yyyyMMdd}.log");
            var line = new StringBuilder()
                .Append('[')
                .Append(utcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                .Append("] ")
                .Append(message)
                .AppendLine()
                .ToString();

            lock (Sync)
            {
                File.AppendAllText(path, line);
            }
        }
    }
}
