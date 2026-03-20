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
            ctx?.Print($"[DIR DEBUG] symbol={ctx?.SymbolName} bias={ctx?.LogicBias ?? TradeDirection.None} conf={ctx?.LogicConfidence ?? 0}");
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
            ctx.FlagBreakoutUp = false;
            ctx.FlagBreakoutDown = false;

            // =========================
            // Hard readiness
            // =========================
            var t = ctx.TransitionLong; // vagy kombinált

            bool hasImpulse =
                (ctx.TransitionLong?.HasImpulse ?? false) ||
                (ctx.TransitionShort?.HasImpulse ?? false);

            if (!hasImpulse)
            {
                _log?.Invoke("[FLAG_BREAKOUT][WAIT] no impulse");
                SyncStateBack(ctx, state);
                return;
            }

            if (ctx.M5 == null || ctx.M5.Count < 5 || ctx.AtrM5 <= 0)
            {
                _log?.Invoke("[FLAG_BREAKOUT][SKIP] M5/ATR not ready");
                SyncStateBack(ctx, state);
                return;
            }

            int longFlagBars = ctx.FlagBarsLong_M5;
            int shortFlagBars = ctx.FlagBarsShort_M5;
            bool hasLongFlag = ctx.HasFlagLong_M5;
            bool hasShortFlag = ctx.HasFlagShort_M5;
            int lastClosed = ctx.M5.Count - 2;

            if (!hasLongFlag && !hasShortFlag)
            {
                _log?.Invoke(
                    $"[FLAG_BREAKOUT][WAIT] builder flag missing long={hasLongFlag} short={hasShortFlag} longFlagBars={longFlagBars} shortFlagBars={shortFlagBars}");
                SyncStateBack(ctx, state);
                return;
            }

            double flagHigh = ctx.FlagHigh;
            double flagLow = ctx.FlagLow;
            double flagAtr = ctx.FlagAtr_M5;

            if (flagHigh <= flagLow || flagAtr <= 0)
            {
                _log?.Invoke(
                    $"[FLAG_BREAKOUT][WAIT] builder flag range not ready high={flagHigh:0.#####} low={flagLow:0.#####} flagAtr={flagAtr:0.#####} long={hasLongFlag} short={hasShortFlag}");
                SyncStateBack(ctx, state);
                return;
            }

            // =========================
            // Direction-agnostic breakout checks using builder SSOT
            // =========================
            double breakoutBuffer = flagAtr * 0.10;

            double lastHigh = ctx.M5.HighPrices[lastClosed];
            double lastLow = ctx.M5.LowPrices[lastClosed];
            double lastClose = ctx.M5.ClosePrices[lastClosed];

            bool highBreak = lastHigh > flagHigh + breakoutBuffer;
            bool lowBreak = lastLow < flagLow - breakoutBuffer;

            bool closeUp = lastClose > flagHigh;
            bool closeDown = lastClose < flagLow;

            bool breakoutUp = hasLongFlag && highBreak && closeUp;
            bool breakoutDown = hasShortFlag && lowBreak && closeDown;

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
                $"[FLAG_BREAKOUT][RANGE] high={flagHigh:0.#####} low={flagLow:0.#####} flagAtr={flagAtr:0.#####} builderLong={hasLongFlag} builderShort={hasShortFlag} longFlagBars={longFlagBars} shortFlagBars={shortFlagBars} lastClosedIndex={lastClosed}");

            _log?.Invoke(
                $"[FLAG_BREAKOUT][CHECK] " +
                $"builderLong={hasLongFlag} builderShort={hasShortFlag} " +
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