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
            if (msg == null)
                return;

            if (bot != null)
                bot.Print(msg);
            else
                Debug.WriteLine(msg);

            try
            {
                RuntimeFileLogger.Write(msg);
            }
            catch
            {
                // never break trading
            }
        }

        public static void Log(string msg)
        {
            Log(msg, null);
        }

        public static void Log(object source, string msg)
        {
            if (msg == null)
                return;

            string prefix = source == null ? "[LOG]" : $"[{source.GetType().Name}]";
            Log($"{prefix} {msg}");
        }
    }
}
