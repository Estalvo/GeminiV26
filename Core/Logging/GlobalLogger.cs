using System.Diagnostics;
using cAlgo.API;

namespace GeminiV26.Core.Logging
{
    public static class GlobalLogger
    {
        public static void Log(Robot bot, string msg)
        {
            Log(msg, bot);
        }

        public static void Log(string msg, Robot bot = null)
        {
            Log(msg, bot, null);
        }

        public static void Log(string msg, Robot bot, string positionId)
        {
            if (msg == null)
                return;

            var finalMessage = WithPosition(msg, positionId);

            if (bot != null)
                bot.Print(finalMessage);
            else
                Debug.WriteLine(finalMessage);

            try
            {
                var instanceName = bot != null ? bot.SymbolName : "GLOBAL";
                RuntimeFileLogger.Write(finalMessage, instanceName);
            }
            catch
            {
                // never break trading
            }
        }

        public static void Log(string msg)
        {
            Log(msg, null, null);
        }

        public static void Log(object source, string msg)
        {
            if (msg == null)
                return;

            string prefix = source == null ? "[LOG]" : $"[{source.GetType().Name}]";
            Log($"{prefix} {msg}");
        }

        public static string WithPosition(string msg, string positionId)
        {
            if (msg == null || positionId == null)
                return msg;

            return $"[POS:{positionId}] {msg}";
        }
    }
}
