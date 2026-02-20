using cAlgo.API;
using GeminiV26.Core.Entry;
using System;

namespace GeminiV26.EntryTypes.Crypto
{
    public class BTC_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.Crypto_Flag;

        private const int MaxBarsSinceImpulse = 8;
        private const int MinFlagBars = 3;
        private const int MaxFlagBars = 7;
        // private const double MaxFlagRangeAtr = 0.85;
        private const double BreakBufferAtr = 0.07;
        private const double MaxDistFromEmaAtr = 1.05;

        //private const int BaseScore = 75;
        private const int MinScore = 20;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 20)
                return Invalid(ctx, "CTX_NOT_READY");

            var profile = CryptoInstrumentMatrix.Get(ctx.Symbol);
            if (profile == null)
                return Invalid(ctx, "NO_CRYPTO_PROFILE");

            // =========================
            // HTF / TREND GATE – FLAG ONLY IF DIRECTIONAL
            // =========================
            // Flag csak akkor futhat, ha a trend egyértelmű
            var dir = ctx.TrendDirection;
            if (dir == TradeDirection.None)
                return Invalid(ctx, "HTF_NEUTRAL_FLAG_DISABLED");

            if (!ctx.HasImpulse_M5 && ctx.BarsSinceImpulse_M5 > 10)
                return Invalid(ctx, "NO_RECENT_IMPULSE");

            if (ctx.BarsSinceImpulse_M5 > MaxBarsSinceImpulse)
                return Invalid(ctx, "LATE_FLAG");

            if (!ctx.IsValidFlagStructure_M5)
                return Invalid(ctx, "INVALID_FLAG");

            if (ctx.IsRange_M5 && !profile.AllowRangeBreakout)
                return Invalid(ctx, "RANGE_NO_FLAG");

            // ===== CONTEXT =====
            int bestFlagStart = -1;
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
                    hiTmp = Math.Max(hiTmp, bars[i].High);
                    loTmp = Math.Min(loTmp, bars[i].Low);
                }

                double r = hiTmp - loTmp;

                if (r < bestRange)
                {
                    bestRange = r;
                    bestFlagStart = start;
                }
            }

if (bestFlagStart < 0)
    return Invalid(ctx, "NO_VALID_FLAG_WINDOW");

int flagStart = bestFlagStart;
double range = bestRange;

            if (flagStart < 2)
                return Invalid(ctx, "NOT_ENOUGH_FLAG_BARS");

            if (ctx.AtrM5 <= 0)
                return Invalid(ctx, "ATR_ZERO");

            // ===== SCORE =====
            int score = 0;

            if (ctx.IsVolatilityAcceptable_Crypto)
                score += 10;
            else
                score -= 10;

            // ===== EMA21 RECLAIM (SHORT SOFT BLOCK) =====
            bool ema21Reclaim =
                bars[lastClosed].Close > ctx.Ema21_M5 &&
                bars[lastClosed - 1].Close <= ctx.Ema21_M5;

            if (dir == TradeDirection.Short && ema21Reclaim)
                score -= 10;

            bool ema21ReclaimLong =
                bars[lastClosed].Close < ctx.Ema21_M5 &&
                bars[lastClosed - 1].Close >= ctx.Ema21_M5;

            if (dir == TradeDirection.Long && ema21ReclaimLong)
                score -= 10;

            double hi = double.MinValue;
            double lo = double.MaxValue;

            for (int i = flagStart; i <= flagEnd; i++)
            {
                hi = Math.Max(hi, bars[i].High);
                lo = Math.Min(lo, bars[i].Low);
            }

            double range = hi - lo;
            if (range > ctx.AtrM5 * profile.MaxFlagAtrMult)
                return Invalid(ctx, "FLAG_TOO_WIDE");

            // =========================
            // FLAG COMPRESSION CHECK
            // =========================
            bool compression =
                ctx.AtrSlope_M5 <= 0.1 &&
                ctx.AdxSlope_M5 <= 0.3;

            if (!compression)
                return Invalid(ctx, "FLAG_NO_COMPRESSION");

            double close = bars[lastClosed].Close;
            double distFromEma = Math.Abs(close - ctx.Ema21_M5);

            if (distFromEma <= ctx.AtrM5 * 0.6)
                score += 8;
            else if (distFromEma > ctx.AtrM5 * MaxDistFromEmaAtr)
                score -= 8;

            double buf = ctx.AtrM5 * BreakBufferAtr;
            bool bullBreak = close > hi + buf;
            bool bearBreak = close < lo - buf;

            // =========================
            // CRYPTO FLAG EARLY BREAKOUT GUARD
            // Az első breakout gyakran fake → várunk megerősítést
            // =========================
            if ((bullBreak || bearBreak) &&
                ctx.BarsSinceImpulse_M5 <= 3 &&
                !ctx.M1TriggerInTrendDirection)
            {
                return Invalid(ctx, "CRYPTO_FLAG_EARLY_BREAKOUT_WAIT");
            }

            if (dir == TradeDirection.Long && !bullBreak)
                return Invalid(ctx, "NO_BREAKOUT_CLOSE_LONG");

            if (dir == TradeDirection.Short && !bearBreak)
                return Invalid(ctx, "NO_BREAKOUT_CLOSE_SHORT");

            double open = bars[lastClosed].Open;

            if (dir == TradeDirection.Long && close > open)
                score += 6;
            else if (dir == TradeDirection.Long)
                score -= 6;

            if (dir == TradeDirection.Short && close < open)
                score += 6;
            else if (dir == TradeDirection.Short)
                score -= 6;

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
