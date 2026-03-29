using cAlgo.API;

namespace GeminiV26.Core.Logging
{
    public static class GlobalLogger
    {
        public static Robot Bot;

        public static void Log(string msg)
        {
            if (Bot != null)
            {
                Bot.Print(msg);
            }
        }
    }
}
