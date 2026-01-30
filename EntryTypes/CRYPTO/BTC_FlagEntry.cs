using cAlgo.API;
using GeminiV26.Core.Entry;
using System;

namespace GeminiV26.EntryTypes.Crypto
{
    public class BTC_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.Crypto_Flag;

        private const int MaxBarsSinceImpulse = 5;
        private const int FlagBars = 3;
        private const double MaxFlagRangeAtr = 0.85;
        private const double BreakBufferAtr = 0.07;
        private const double MaxDistFromEmaAtr = 1.05;

        private const int BaseScore = 75;
        private const int MinScore = 65;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 20)
                return Invalid(ctx, "CTX_NOT_READY");

            if (!ctx.HasImpulse_M5)
                return Invalid(ctx, "NO_IMPULSE");

            if (ctx.BarsSinceImpulse_M5 > MaxBarsSinceImpulse)
                return Invalid(ctx, "LATE_FLAG");

            if (!ctx.IsValidFlagStructure_M5)
                return Invalid(ctx, "INVALID_FLAG");

            if (ctx.IsRange_M5)
                return Invalid(ctx, "RANGE_NO_FLAG");

            var dir = ctx.TrendDirection;
            if (dir == TradeDirection.None)
                return Invalid(ctx, "NO_TREND_DIR");
                        
            int score = BaseScore;

            if (!ctx.IsVolatilityAcceptable_Crypto)
                score -= 15;

            var bars = ctx.M5;
            int lastClosed = bars.Count - 2;
            int flagEnd = lastClosed - 1;
            int flagStart = flagEnd - FlagBars + 1;

            if (flagStart < 2)
                return Invalid(ctx, "NOT_ENOUGH_FLAG_BARS");

            if (ctx.AtrM5 <= 0)
                return Invalid(ctx, "ATR_ZERO");

            double hi = double.MinValue;
            double lo = double.MaxValue;

            for (int i = flagStart; i <= flagEnd; i++)
            {
                hi = Math.Max(hi, bars[i].High);
                lo = Math.Min(lo, bars[i].Low);
            }

            double range = hi - lo;
            if (range > ctx.AtrM5 * MaxFlagRangeAtr)
                return Invalid(ctx, "FLAG_TOO_WIDE");

            double close = bars[lastClosed].Close;
            double distFromEma = Math.Abs(close - ctx.Ema21_M5);

            if (distFromEma > ctx.AtrM5 * MaxDistFromEmaAtr)
                return Invalid(ctx, "OVEREXTENDED");

            double buf = ctx.AtrM5 * BreakBufferAtr;
            bool bullBreak = close > hi + buf;
            bool bearBreak = close < lo - buf;

            if (dir == TradeDirection.Long && !bullBreak)
                return Invalid(ctx, "NO_BREAKOUT_CLOSE_LONG");

            if (dir == TradeDirection.Short && !bearBreak)
                return Invalid(ctx, "NO_BREAKOUT_CLOSE_SHORT");

            double open = bars[lastClosed].Open;

            if (dir == TradeDirection.Long && close <= open)
                return Invalid(ctx, "NO_BULL_BODY");

            if (dir == TradeDirection.Short && close >= open)
                return Invalid(ctx, "NO_BEAR_BODY");

            if (ctx.M1TriggerInTrendDirection)
                score += 5;

            score += 3;

            if (score < MinScore)
                return Invalid(ctx, $"LOW_SCORE({score})");

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.Crypto_Flag,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason = $"CR_TRUEFLAG dir={dir} score={score} rangeATR={(range / ctx.AtrM5):F2}"
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
