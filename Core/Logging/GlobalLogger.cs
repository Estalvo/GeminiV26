using System.Diagnostics;
using cAlgo.API;

namespace GeminiV26.Core.Logging
{
    public static class GlobalLogger
    {
        public static void Log(Robot bot, string msg)
        {
            if (msg == null)
                return;

            if (bot != null)
            {
                bot.Print(msg);
                return;
            }

            Debug.WriteLine(msg);
        }

        public static void Log(string msg)
        {
            if (msg == null)
                return;

            Debug.WriteLine(msg);
        }

        public static void Log(object source, string msg)
        {
            if (msg == null)
                return;

            string prefix = source == null ? "[LOG]" : $"[{source.GetType().Name}]";
            Debug.WriteLine($"{prefix} {msg}");
        }
    }
}
