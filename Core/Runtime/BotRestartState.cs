using System;

namespace GeminiV26.Core.Runtime
{
    internal static class BotRestartState
    {
        public static DateTime BotStartTime { get; private set; } = DateTime.MinValue;

        public static int BarsSinceStart { get; private set; }

        private static int _startBarCount = -1;

        public static void Initialize(DateTime botStartTime, int currentBarCount)
        {
            BotStartTime = botStartTime;
            _startBarCount = Math.Max(0, currentBarCount);
            BarsSinceStart = 0;
        }

        public static void Update(DateTime serverTime, int currentBarCount)
        {
            if (BotStartTime == DateTime.MinValue)
                Initialize(serverTime, currentBarCount);

            if (_startBarCount < 0)
                _startBarCount = Math.Max(0, currentBarCount);

            BarsSinceStart = Math.Max(0, currentBarCount - _startBarCount);
        }

        public static bool IsHardProtectionPhase => BarsSinceStart <= 2;

        public static bool IsSoftProtectionPhase => BarsSinceStart > 2 && BarsSinceStart <= 6;
    }
}
