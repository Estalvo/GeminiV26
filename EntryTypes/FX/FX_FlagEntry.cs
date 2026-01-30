using System;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Instruments.FX;

namespace GeminiV26.EntryTypes.FX
{
    public sealed class FX_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.FX_Flag;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 20)
                return Invalid(ctx, "CTX_NOT_READY");

            var fx = FxInstrumentMatrix.Get(ctx.Symbol);

            if (fx.FlagTuning == null ||
                !fx.FlagTuning.TryGetValue(ctx.Session, out var tuning))
                return Invalid(ctx, "NO_FLAG_TUNING");

            int score = tuning.BaseScore;

            // =====================================================
            // IMPULSE
            // =====================================================
            if (!ctx.HasImpulse_M5)
                return Invalid(ctx, "NO_IMPULSE");

            if (ctx.BarsSinceImpulse_M5 > tuning.MaxBarsSinceImpulse)
                return Invalid(ctx, "STALE_IMPULSE");

            // ATR expansion score logic (as-is)
            if (ctx.IsAtrExpanding_M5)
                score -= tuning.AtrExpandPenalty;

            // HARD BLOCK: ATR must be expanding if matrix requires it
            if (tuning.AtrExpansionHardBlock && !ctx.IsAtrExpanding_M5)
                return Invalid(ctx, "ATR_NOT_EXPANDING");

            // =====================================================
            // FLAG STRUCTURE
            // =====================================================
            if (!TryComputeFlag(ctx, tuning.FlagBars, out var hi, out var lo, out var rangeAtr))
                return Invalid(ctx, "FLAG_BARS_FAIL");

            if (rangeAtr > fx.MaxFlagAtrMult)
                return Invalid(ctx, "FLAG_TOO_WIDE");

            // =====================================================
            // PULLBACK DEPTH
            // =====================================================
            double maxDist = fx.PullbackStyle switch
            {
                FxPullbackStyle.Shallow => tuning.MaxPullbackAtr * 0.8,
                FxPullbackStyle.EMA21 => tuning.MaxPullbackAtr,
                FxPullbackStyle.EMA50 => tuning.MaxPullbackAtr * 1.2,
                FxPullbackStyle.Structure => tuning.MaxPullbackAtr * 1.4,
                _ => tuning.MaxPullbackAtr
            };

            if (IsOverextended(ctx, maxDist))
                return Invalid(ctx, "OVEREXTENDED");

            // =====================================================
            // BREAKOUT
            // =====================================================
            if (!BreakoutClose(ctx, hi, lo, tuning.BreakoutAtrBuffer))
                return Invalid(ctx, "NO_BREAKOUT_CLOSE");

            // =====================================================
            // RECHECK IMPULSE AFTER FLAG (Asia fake-k ellen)
            // =====================================================
            if (ctx.BarsSinceImpulse_M5 > tuning.MaxBarsSinceImpulse)
                return Invalid(ctx, "STALE_IMPULSE_POST_FLAG");

            // =====================================================
            // CANDLE BODY
            // =====================================================
            if (!BodyAligned(ctx))
            {
                // ahol a matrix brutális body-misalignmentet jelez,
                // ott ez legyen HARD TILT
                if (tuning.BodyMisalignPenalty == int.MaxValue)
                    return Invalid(ctx, "WEAK_ENTRY_BODY");

                score -= tuning.BodyMisalignPenalty;
            }

            // =====================================================
            // M1 TRIGGER
            // =====================================================
            if (ctx.M1TriggerInTrendDirection)
            {
                score += tuning.M1TriggerBonus;
            }
            else
            {
                // ha a matrix kéri, legyen kötelező
                if (tuning.RequireM1Trigger)
                    return Invalid(ctx, "M1_TRIGGER_REQUIRED");

                score -= tuning.NoM1Penalty;
            }

            // =====================================================
            // FLAG QUALITY
            // =====================================================
            if (ctx.IsValidFlagStructure_M5)
                score += tuning.FlagQualityBonus;

            // =====================================================
            // SESSION SCORE DELTA (MATRIX)
            // =====================================================
            if (fx.SessionScoreDelta != null &&
                fx.SessionScoreDelta.TryGetValue(ctx.Session, out var sd))
                score += sd;

            // =====================================================
            // HTF PENALTY
            // =====================================================
            score -= HtfPenalty(
                ctx,
                ctx.TrendDirection,
                tuning.HtfBasePenalty,
                tuning.HtfScalePenalty);

            if (score < tuning.MinScore)
                return Invalid(ctx, $"LOW_SCORE({score})");

            return Valid(ctx, score, rangeAtr, $"FX_FLAG_{ctx.Session}");
        }

        // =========================================================
        // HELPERS
        // =========================================================

        private static bool TryComputeFlag(
            EntryContext ctx,
            int bars,
            out double hi,
            out double lo,
            out double rangeAtr)
        {
            hi = double.MinValue;
            lo = double.MaxValue;
            rangeAtr = 999;

            var m5 = ctx.M5;
            int lastClosed = m5.Count - 2;
            int end = lastClosed - 1;
            int start = end - bars + 1;
            if (start < 2) return false;

            for (int i = start; i <= end; i++)
            {
                hi = Math.Max(hi, m5[i].High);
                lo = Math.Min(lo, m5[i].Low);
            }

            if (ctx.AtrM5 <= 0) return false;

            rangeAtr = (hi - lo) / ctx.AtrM5;
            return true;
        }

        private static bool IsOverextended(EntryContext ctx, double maxAtr)
        {
            var bars = ctx.M5;
            int i = bars.Count - 2;
            double dist = Math.Abs(bars[i].Close - ctx.Ema21_M5);
            return dist > ctx.AtrM5 * maxAtr;
        }

        private static bool BreakoutClose(
            EntryContext ctx,
            double hi,
            double lo,
            double bufAtr)
        {
            var bars = ctx.M5;
            int i = bars.Count - 2;
            double buf = ctx.AtrM5 * bufAtr;
            double close = bars[i].Close;

            return ctx.TrendDirection switch
            {
                TradeDirection.Long => close > hi + buf,
                TradeDirection.Short => close < lo - buf,
                _ => false
            };
        }

        private static bool BodyAligned(EntryContext ctx)
        {
            var bars = ctx.M5;
            int i = bars.Count - 2;

            return ctx.TrendDirection switch
            {
                TradeDirection.Long => bars[i].Close > bars[i].Open,
                TradeDirection.Short => bars[i].Close < bars[i].Open,
                _ => false
            };
        }

        private static int HtfPenalty(
            EntryContext ctx,
            TradeDirection dir,
            int basePenalty,
            int scale)
        {
            if (ctx.FxHtfAllowedDirection != TradeDirection.None &&
                ctx.FxHtfConfidence01 > 0 &&
                ctx.FxHtfAllowedDirection != dir)
            {
                return (int)(basePenalty + scale * ctx.FxHtfConfidence01);
            }

            return 0;
        }

        private static EntryEvaluation Valid(
            EntryContext ctx,
            int score,
            double rangeAtr,
            string tag)
            => new()
            {
                Symbol = ctx.Symbol,
                Type = EntryType.FX_Flag,
                Direction = ctx.TrendDirection,
                Score = score,
                IsValid = true,
                Reason = $"{tag} dir={ctx.TrendDirection} score={score} rATR={rangeAtr:F2}"
            };

        private static EntryEvaluation Invalid(EntryContext ctx, string reason)
            => new()
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.FX_Flag,
                Direction = TradeDirection.None,
                IsValid = false,
                Reason = reason
            };
    }
}
