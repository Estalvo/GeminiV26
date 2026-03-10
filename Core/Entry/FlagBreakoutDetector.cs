using System;

namespace GeminiV26.Core.Entry
{
    public sealed class FlagBreakoutDetector
    {
        private readonly Action<string> _log;

        public FlagBreakoutDetector(Action<string> log)
        {
            _log = log;
        }

        public void Evaluate(EntryContext ctx)
        {
            if (ctx == null)
                return;

            ctx.FlagBreakoutConfirmed = false;
            ctx.FlagHigh = 0.0;
            ctx.FlagLow = 0.0;

            if (!ctx.TransitionValid || ctx.Transition == null)
                return;

            if (ctx.M5 == null || ctx.M5.Count < 5 || ctx.AtrM5 <= 0)
                return;

            int flagBars = Math.Max(0, ctx.Transition.FlagBars);
            int last = ctx.M5.Count - 2;
            if (flagBars <= 0 || last <= 0)
                return;

            int flagEnd = last - 1;
            int start = Math.Max(0, flagEnd - flagBars + 1);
            if (start > flagEnd)
                return;

            double flagHigh = double.MinValue;
            double flagLow = double.MaxValue;

            for (int i = start; i <= flagEnd; i++)
            {
                flagHigh = Math.Max(flagHigh, ctx.M5.HighPrices[i]);
                flagLow = Math.Min(flagLow, ctx.M5.LowPrices[i]);
            }

            double breakoutBuffer = ctx.AtrM5 * 0.10;
            bool breakout = false;

            if (ctx.TrendDirection == TradeDirection.Long)
            {
                breakout = ctx.M5.ClosePrices[last] > flagHigh + breakoutBuffer;
            }
            else if (ctx.TrendDirection == TradeDirection.Short)
            {
                breakout = ctx.M5.ClosePrices[last] < flagLow - breakoutBuffer;
            }

            ctx.FlagHigh = flagHigh;
            ctx.FlagLow = flagLow;
            ctx.FlagBreakoutConfirmed = breakout;

            _log?.Invoke($"[FLAG_BREAKOUT][RANGE] high={flagHigh:0.#####} low={flagLow:0.#####} bars={flagBars}");
            _log?.Invoke($"[FLAG_BREAKOUT][CHECK] direction={ctx.TrendDirection} breakout={breakout} buffer={breakoutBuffer:0.#####}");
        }
    }
}
