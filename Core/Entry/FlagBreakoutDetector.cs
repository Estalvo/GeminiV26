using System;
using System.Collections.Generic;

namespace GeminiV26.Core.Entry
{
    /// <summary>
    /// Direction-agnostic flag breakout detector.
    ///
    /// FONTOS:
    /// - a detector NEM dönt irányról
    /// - a detector NEM használ TrendDirection-t
    /// - csak tényeket ír a contextbe:
    ///     - breakout felfelé?
    ///     - breakout lefelé?
    ///     - mennyi bar telt el az utolsó breakout óta?
    ///
    /// Az ENTRY layer dönti el, hogy ezekből lesz-e valid long / short setup.
    /// </summary>
    public sealed class FlagBreakoutDetector
    {
        private readonly Action<string> _log;
        private readonly Dictionary<string, BreakoutRuntimeState> _stateBySymbol =
            new(StringComparer.OrdinalIgnoreCase);

        public FlagBreakoutDetector(Action<string> log)
        {
            _log = log;
        }

        public void Evaluate(EntryContext ctx)
        {
            if (ctx == null)
                return;

            string symbol = string.IsNullOrWhiteSpace(ctx.Symbol) ? "__DEFAULT__" : ctx.Symbol;

            if (!_stateBySymbol.TryGetValue(symbol, out var state))
            {
                state = new BreakoutRuntimeState();
                _stateBySymbol[symbol] = state;
            }

            // =========================
            // Rehydrate runtime state into context
            // =========================
            ctx.FlagBreakoutUpConfirmed = state.FlagBreakoutUpConfirmed;
            ctx.FlagBreakoutDownConfirmed = state.FlagBreakoutDownConfirmed;
            ctx.BreakoutUpBarsSince = state.BreakoutUpBarsSince;
            ctx.BreakoutDownBarsSince = state.BreakoutDownBarsSince;

            // Backward compatibility
            ctx.FlagBreakoutConfirmed = ctx.FlagBreakoutUpConfirmed || ctx.FlagBreakoutDownConfirmed;
            ctx.BreakoutBarsSince = Math.Min(ctx.BreakoutUpBarsSince, ctx.BreakoutDownBarsSince);

            // =========================
            // Expire old breakout states
            // =========================
            ExpireState(ctx, state);

            // defaults
            ctx.FlagHigh = 0.0;
            ctx.FlagLow = 0.0;
            ctx.FlagBreakoutUp = false;
            ctx.FlagBreakoutDown = false;

            // =========================
            // Hard readiness
            // =========================
            if (!ctx.TransitionValid || ctx.Transition == null)
            {
                _log?.Invoke("[FLAG_BREAKOUT][SKIP] transition invalid");
                SyncStateBack(ctx, state);
                return;
            }

            if (ctx.M5 == null || ctx.M5.Count < 5 || ctx.AtrM5 <= 0)
            {
                _log?.Invoke("[FLAG_BREAKOUT][SKIP] M5/ATR not ready");
                SyncStateBack(ctx, state);
                return;
            }

            int flagBars = Math.Max(0, ctx.Transition.FlagBars);
            int lastClosed = ctx.M5.Count - 2;
            if (flagBars <= 0 || lastClosed <= 0)
            {
                _log?.Invoke($"[FLAG_BREAKOUT][SKIP] invalid flagBars={flagBars} lastClosed={lastClosed}");
                SyncStateBack(ctx, state);
                return;
            }

            // =========================
            // Compute flag range
            // =========================
            int flagEnd = lastClosed - 1;
            int start = Math.Max(0, flagEnd - flagBars + 1);
            if (start > flagEnd)
            {
                _log?.Invoke($"[FLAG_BREAKOUT][SKIP] invalid range start={start} end={flagEnd}");
                SyncStateBack(ctx, state);
                return;
            }

            double flagHigh = double.MinValue;
            double flagLow = double.MaxValue;

            for (int i = start; i <= flagEnd; i++)
            {
                flagHigh = Math.Max(flagHigh, ctx.M5.HighPrices[i]);
                flagLow = Math.Min(flagLow, ctx.M5.LowPrices[i]);
            }

            ctx.FlagHigh = flagHigh;
            ctx.FlagLow = flagLow;

            // =========================
            // Direction-agnostic breakout checks
            // =========================
            double breakoutBuffer = ctx.AtrM5 * 0.10;

            double lastHigh = ctx.M5.HighPrices[lastClosed];
            double lastLow = ctx.M5.LowPrices[lastClosed];
            double lastClose = ctx.M5.ClosePrices[lastClosed];

            bool highBreak = lastHigh > flagHigh + breakoutBuffer;
            bool lowBreak = lastLow < flagLow - breakoutBuffer;

            bool closeUp = lastClose > flagHigh;
            bool closeDown = lastClose < flagLow;

            bool breakoutUp = highBreak && closeUp;
            bool breakoutDown = lowBreak && closeDown;

            ctx.FlagBreakoutUp = breakoutUp;
            ctx.FlagBreakoutDown = breakoutDown;

            // =========================
            // Update runtime state
            // =========================
            if (breakoutUp)
            {
                ctx.FlagBreakoutUpConfirmed = true;
                ctx.BreakoutUpBarsSince = 0;
            }

            if (breakoutDown)
            {
                ctx.FlagBreakoutDownConfirmed = true;
                ctx.BreakoutDownBarsSince = 0;
            }

            // Backward compatibility
            ctx.FlagBreakoutConfirmed = ctx.FlagBreakoutUpConfirmed || ctx.FlagBreakoutDownConfirmed;
            ctx.BreakoutBarsSince = Math.Min(ctx.BreakoutUpBarsSince, ctx.BreakoutDownBarsSince);

            SyncStateBack(ctx, state);

            // =========================
            // Logs
            // =========================
            _log?.Invoke(
                $"[FLAG_BREAKOUT][RANGE] high={flagHigh:0.#####} low={flagLow:0.#####} bars={flagBars} lastClosedIndex={lastClosed}");

            _log?.Invoke(
                $"[FLAG_BREAKOUT][CHECK] " +
                $"highBreak={highBreak} lowBreak={lowBreak} " +
                $"closeUp={closeUp} closeDown={closeDown} " +
                $"breakoutUp={breakoutUp} breakoutDown={breakoutDown} " +
                $"buffer={breakoutBuffer:0.#####} lastClosedIndex={lastClosed}");

            _log?.Invoke(
                $"[TRACE][FLAG_BREAKOUT_STATE] symbol={ctx.Symbol} " +
                $"upConfirmed={ctx.FlagBreakoutUpConfirmed} upBarsSince={ctx.BreakoutUpBarsSince} " +
                $"downConfirmed={ctx.FlagBreakoutDownConfirmed} downBarsSince={ctx.BreakoutDownBarsSince}");
        }

        private void ExpireState(EntryContext ctx, BreakoutRuntimeState state)
        {
            // UP expiry
            if (ctx.FlagBreakoutUpConfirmed)
            {
                ctx.BreakoutUpBarsSince++;
                if (ctx.BreakoutUpBarsSince > 3)
                {
                    ctx.FlagBreakoutUpConfirmed = false;
                    _log?.Invoke(
                        $"[FLAG_BREAKOUT][EXPIRE_UP] barsSince={ctx.BreakoutUpBarsSince} breakoutExpired=true");
                }
            }

            // DOWN expiry
            if (ctx.FlagBreakoutDownConfirmed)
            {
                ctx.BreakoutDownBarsSince++;
                if (ctx.BreakoutDownBarsSince > 3)
                {
                    ctx.FlagBreakoutDownConfirmed = false;
                    _log?.Invoke(
                        $"[FLAG_BREAKOUT][EXPIRE_DOWN] barsSince={ctx.BreakoutDownBarsSince} breakoutExpired=true");
                }
            }

            // Backward compatibility after expiry
            ctx.FlagBreakoutConfirmed = ctx.FlagBreakoutUpConfirmed || ctx.FlagBreakoutDownConfirmed;
            ctx.BreakoutBarsSince = Math.Min(ctx.BreakoutUpBarsSince, ctx.BreakoutDownBarsSince);
        }

        private static void SyncStateBack(EntryContext ctx, BreakoutRuntimeState state)
        {
            state.FlagBreakoutUpConfirmed = ctx.FlagBreakoutUpConfirmed;
            state.FlagBreakoutDownConfirmed = ctx.FlagBreakoutDownConfirmed;
            state.BreakoutUpBarsSince = ClampBarsSince(ctx.BreakoutUpBarsSince);
            state.BreakoutDownBarsSince = ClampBarsSince(ctx.BreakoutDownBarsSince);
        }

        private static int ClampBarsSince(int value)
        {
            if (value < 0)
                return 0;

            if (value > 999)
                return 999;

            return value;
        }

        private sealed class BreakoutRuntimeState
        {
            public bool FlagBreakoutUpConfirmed { get; set; }
            public bool FlagBreakoutDownConfirmed { get; set; }

            public int BreakoutUpBarsSince { get; set; } = 999;
            public int BreakoutDownBarsSince { get; set; } = 999;
        }
    }
}