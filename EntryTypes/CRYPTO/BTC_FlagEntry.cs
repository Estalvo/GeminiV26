using cAlgo.API;
using GeminiV26.Core.Entry;
using System;

namespace GeminiV26.EntryTypes.Crypto
{
    public class BTC_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.Crypto_Flag;

        private const int MaxBarsSinceImpulse = 14;
        private const int MinFlagBars = 3;
        private const int MaxFlagBars = 7;

        private const double BreakBufferAtr = 0.03;
        private const double MaxDistFromEmaAtr = 1.2;

        private const int MinScore = 16;
        private const int HtfAgainstPenalty = 8;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 20)
                return Invalid(ctx, "CTX_NOT_READY");

            if (ctx.AtrM5 <= 0)
                return Invalid(ctx, "ATR_ZERO");

            if (!ctx.HasImpulse_M5 && ctx.BarsSinceImpulse_M5 > 12)
                return Invalid(ctx, "NO_RECENT_IMPULSE");

            if (ctx.BarsSinceImpulse_M5 > MaxBarsSinceImpulse)
                return Invalid(ctx, "LATE_FLAG");

            var bars = ctx.M5;
            int lastClosed = bars.Count - 2;
            int flagEnd = lastClosed - 1;

            int bestStart = -1;
            double bestRange = double.MaxValue;

            for (int len = MinFlagBars; len <= MaxFlagBars; len++)
            {
                int start = flagEnd - len + 1;
                if (start < 2)
                    continue;

                double hiTmp = double.MinValue;
                double loTmp = double.MaxValue;

                for (int i = start; i <= flagEnd; i++)
                {
                    double bodyHigh = Math.Max(bars[i].Open, bars[i].Close);
                    double bodyLow = Math.Min(bars[i].Open, bars[i].Close);

                    hiTmp = Math.Max(hiTmp, bodyHigh);
                    loTmp = Math.Min(loTmp, bodyLow);
                }

                double r = hiTmp - loTmp;

                if (r < bestRange)
                {
                    bestRange = r;
                    bestStart = start;
                }
            }

            if (bestStart < 2)
                return Invalid(ctx, "NO_FLAG_WINDOW");

            int flagStart = bestStart;

            double hi = double.MinValue;
            double lo = double.MaxValue;

            for (int i = flagStart; i <= flagEnd; i++)
            {
                hi = Math.Max(hi, bars[i].High);
                lo = Math.Min(lo, bars[i].Low);
            }

            bool hasValidRange = hi > lo && hi > 0 && lo > 0;
            if (!hasValidRange)
                ctx.Log?.Invoke("[FLAG WARN] No valid range → fallback mode");

            double rangeAtr = hasValidRange && ctx.AtrM5 > 0
                ? (hi - lo) / ctx.AtrM5
                : 0;

            var profile = CryptoInstrumentMatrix.Get(ctx.Symbol);

            var longEval = EvaluateSide(ctx, TradeDirection.Long, hi, lo, hasValidRange, rangeAtr, lastClosed, profile?.MaxFlagAtrMult ?? 0);
            var shortEval = EvaluateSide(ctx, TradeDirection.Short, hi, lo, hasValidRange, rangeAtr, lastClosed, profile?.MaxFlagAtrMult ?? 0);

            bool buyValid = longEval.IsValid;
            bool sellValid = shortEval.IsValid;

            if (!buyValid && !sellValid)
            {
                ctx.Log?.Invoke($"[FLAG][REJECT] No valid direction buyValid={buyValid} sellValid={sellValid}");
                return Invalid(ctx, "FLAG_DIRECTION_INVALID");
            }

            if (buyValid && !sellValid) return longEval;
            if (!buyValid && sellValid) return shortEval;

            return longEval.Score >= shortEval.Score ? longEval : shortEval;
        }

        private EntryEvaluation EvaluateSide(
            EntryContext ctx,
            TradeDirection dir,
            double hi,
            double lo,
            bool hasValidRange,
            double rangeAtr,
            int lastIndex,
            double maxFlagAtr)
        {
            var bars = ctx.M5;
            var bar = bars[lastIndex];

            int score = 0;

            if (!ctx.HasImpulse_M5)
            {
                score -= 6;
            }
            else if (ctx.BarsSinceImpulse_M5 > 6)
            {
                score -= 4;
            }

            bool compression =
                ctx.AtrSlope_M5 <= 0.30 &&
                ctx.AdxSlope_M5 <= 1.5;

            if (!compression)
                score -= 6;

            if (rangeAtr > 0)
            {
                if (rangeAtr <= 0.8)
                {
                    score += 3;
                }
                else if (rangeAtr > 1.2)
                {
                    score -= 3;
                }

                if (rangeAtr < 0.15)
                    score -= 3;

                if (maxFlagAtr > 0 && rangeAtr > maxFlagAtr)
                    score -= 4;
            }
            else
            {
                score -= 2;
            }

            bool hasFlag =
                dir == TradeDirection.Long ? ctx.HasFlagLong_M5 :
                dir == TradeDirection.Short ? ctx.HasFlagShort_M5 :
                ctx.IsValidFlagStructure_M5;

            string flagState = hasFlag ? "OK" : "FLAG_WEAK_OR_FORMING";

            if (!hasFlag)
                score -= 2;

            if (ctx.IsVolatilityAcceptable_Crypto)
                score += 10;
            else
                score -= 10;

            double close = bar.Close;
            double open = bar.Open;

            double distFromEma = Math.Abs(close - ctx.Ema21_M5);

            if (distFromEma <= ctx.AtrM5 * 0.6)
                score += 8;
            else if (distFromEma > ctx.AtrM5 * MaxDistFromEmaAtr)
                score -= 8;

            double buf = ctx.AtrM5 * BreakBufferAtr;

            bool bullBreak =
                hasValidRange &&
                close > hi + buf &&
                ctx.HasImpulse_M5 &&
                ctx.BarsSinceImpulse_M5 <= 6;

            bool bearBreak =
                hasValidRange &&
                close < lo - buf &&
                ctx.HasImpulse_M5 &&
                ctx.BarsSinceImpulse_M5 <= 6;

            bool bullReclaim =
                hasValidRange &&
                close > hi &&
                ctx.LastClosedBarInTrendDirection &&
                ctx.HasReactionCandle_M5 &&
                ctx.HasImpulse_M5 &&
                ctx.BarsSinceImpulse_M5 <= 6;

            bool bearReclaim =
                hasValidRange &&
                close < lo &&
                ctx.LastClosedBarInTrendDirection &&
                ctx.HasReactionCandle_M5 &&
                ctx.HasImpulse_M5 &&
                ctx.BarsSinceImpulse_M5 <= 6;

            bool breakoutSignal =
                (ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir) ||
                ctx.RangeBreakDirection == dir ||
                (dir == TradeDirection.Long
                    ? (ctx.FlagBreakoutUp || ctx.FlagBreakoutUpConfirmed)
                    : (ctx.FlagBreakoutDown || ctx.FlagBreakoutDownConfirmed));

            bool longValid = bullBreak || bullReclaim || (dir == TradeDirection.Long && breakoutSignal);
            bool shortValid = bearBreak || bearReclaim || (dir == TradeDirection.Short && breakoutSignal);

            if (dir == TradeDirection.Long && !longValid)
                return Invalid(ctx, "NO_BREAK_LONG");

            if (dir == TradeDirection.Short && !shortValid)
                return Invalid(ctx, "NO_BREAK_SHORT");

            if (dir == TradeDirection.Long && close > open) score += 6;
            if (dir == TradeDirection.Short && close < open) score += 6;

            if (ctx.TrendDirection != TradeDirection.None &&
                dir != ctx.TrendDirection)
            {
                score -= HtfAgainstPenalty;
            }

            bool missingImpulse =
                string.Equals(ctx.Transition?.Reason, "MissingImpulse", StringComparison.Ordinal);

            if (missingImpulse)
            {
                ctx.Log?.Invoke(
                    "[FLAG] Missing impulse context" +
                    $"symbol={ctx.Symbol} entry={EntryType.Crypto_Flag} penalty=6 score={score}");
            }

            if (score < MinScore)
                return Invalid(ctx, $"LOW_SCORE({score})");

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.Crypto_Flag,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason = $"CR_FLAG_V2 dir={dir} score={score} rangeATR={rangeAtr:F2} rangeState={(hasValidRange ? "OK" : "FLAG_RANGE_UNKNOWN")} flagState={flagState}"
            };
        }

        private static EntryEvaluation Invalid(EntryContext ctx, string reason) =>
            new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.Crypto_Flag,
                Direction = TradeDirection.None,
                IsValid = false,
                Reason = reason
            };
    }
}
