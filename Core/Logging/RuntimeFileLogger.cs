using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;

namespace GeminiV26.Core.Logging
{
    public static class RuntimeFileLogger
    {
        private static readonly ConcurrentDictionary<string, object> _fileLocks = new ConcurrentDictionary<string, object>();

        public static void Write(string message)
        {
            Write(message, null);
        }

        public static void Write(string message, string instanceName = null)
        {
            if (message == null)
                return;

            var utcNow = DateTime.UtcNow;
            var normalizedInstanceName = NormalizeInstanceName(instanceName);
            var runtimeDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "GeminiV26",
                "Logs",
                "Runtime",
                normalizedInstanceName);

            Directory.CreateDirectory(runtimeDir);

            var path = Path.Combine(runtimeDir, $"runtime_{utcNow:yyyyMMdd}.log");
            var line = new StringBuilder()
                .Append('[')
                .Append(utcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                .Append("] ")
                .Append(message)
                .AppendLine()
                .ToString();

            var fileLock = _fileLocks.GetOrAdd(path, _ => new object());

            // TODO Phase 4+: introduce async buffered writer (channel/queue based)
            // to reduce IO pressure under high-frequency logging (16+ instances)
            lock (fileLock)
            {
                File.AppendAllText(path, line);
            }
        }

        private static string NormalizeInstanceName(string instanceName)
        {
            if (string.IsNullOrWhiteSpace(instanceName))
                return "GLOBAL";

            var cleaned = instanceName.Trim();
            foreach (var invalidChar in Path.GetInvalidFileNameChars())
                cleaned = cleaned.Replace(invalidChar.ToString(), string.Empty);

            if (string.IsNullOrWhiteSpace(cleaned))
                return "GLOBAL";

            return cleaned.ToUpperInvariant();
        }
    }
}
