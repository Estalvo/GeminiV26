using cAlgo.API;

namespace GeminiV26.Core.Logging
{
    public static class GlobalLogger
    {
        public static void Log(Robot bot, string msg)
        {
            if (bot != null && msg != null)
            {
                bot.Print(msg);
            }
        }
    }
}
