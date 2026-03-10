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

            if (ctx.FlagBreakoutConfirmed)
            {
                ctx.BreakoutBarsSince++;
                if (ctx.BreakoutBarsSince > 3)
                {
                    ctx.FlagBreakoutConfirmed = false;
                    _log?.Invoke($"[FLAG_BREAKOUT][EXPIRE] barsSince={ctx.BreakoutBarsSince} breakoutExpired=true");
                }
            }

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
            bool highBreak = false;
            bool lowBreak = false;
            bool closeConfirm = false;

            if (ctx.TrendDirection == TradeDirection.Long)
            {
                highBreak = ctx.M5.HighPrices[last] > flagHigh + breakoutBuffer;
                closeConfirm = ctx.M5.ClosePrices[last] > flagHigh;
                breakout = highBreak && closeConfirm;
            }
            else if (ctx.TrendDirection == TradeDirection.Short)
            {
                lowBreak = ctx.M5.LowPrices[last] < flagLow - breakoutBuffer;
                closeConfirm = ctx.M5.ClosePrices[last] < flagLow;
                breakout = lowBreak && closeConfirm;
            }

            ctx.FlagHigh = flagHigh;
            ctx.FlagLow = flagLow;
            if (breakout)
            {
                ctx.FlagBreakoutConfirmed = true;
                ctx.BreakoutBarsSince = 0;
            }

            _log?.Invoke($"[FLAG_BREAKOUT][RANGE] high={flagHigh:0.#####} low={flagLow:0.#####} bars={flagBars} lastClosedIndex={last}");
            _log?.Invoke($"[FLAG_BREAKOUT][CHECK] direction={ctx.TrendDirection} highBreak={highBreak} lowBreak={lowBreak} closeConfirm={closeConfirm} breakout={breakout} buffer={breakoutBuffer:0.#####} lastClosedIndex={last}");
        }
    }
}
