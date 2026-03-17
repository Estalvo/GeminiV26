using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Matrix;
using GeminiV26.Instruments.FX;

namespace GeminiV26.EntryTypes.METAL
{
    public class XAU_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.XAU_Flag;

        private const int BarsNotReadyMin = 20;
        private const double MinBodyRatio = 0.55;

        private const int HtfAgainstPenalty = 5;

        private const double MinCloseBreakAtr = 0.05;
        private const double HtfAgainstMinBreakAtr = 0.08;
        private const double HtfAgainstMinBody = 0.60;

        private const int ScoreDeadband = 2;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < BarsNotReadyMin)
                return Invalid(ctx, "CTX_NOT_READY");

            var matrix = ctx.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;
            if (!matrix.AllowFlag)
                return Invalid(ctx, "SESSION_DISABLED");

            if (!TryResolveSession(ctx.Session, out var session, out var tag))
                return Invalid(ctx, "NO_SESSION");

            var profile = XAU_InstrumentMatrix.Get(ctx.Symbol);
            if (profile == null)
                return Invalid(ctx, "NO_PROFILE");

            if (!profile.FlagTuning.TryGetValue(session, out var tuning))
                return Invalid(ctx, "NO_TUNING");

            // ===== FLAG RANGE (precomputed only!) =====
            double hi = ctx.FlagHigh;
            double lo = ctx.FlagLow;

            if (hi <= 0 || lo <= 0 || hi <= lo)
                return Invalid(ctx, "FLAG_RANGE_INVALID");

            double rangeAtr = (hi - lo) / ctx.AtrM5;

            var bars = ctx.M5;
            int last = bars.Count - 2;
            var bar = bars[last];

            // ===== spike filter =====
            bool wickBoth = bar.High >= hi && bar.Low <= lo;
            bool closeUp = bar.Close > hi;
            bool closeDn = bar.Close < lo;

            if (wickBoth && !closeUp && !closeDn)
                return Invalid(ctx, "AMBIGUOUS_SPIKE");

            var buy = EvaluateSide(TradeDirection.Long, ctx, tuning, hi, lo, rangeAtr, bar, last);
            var sell = EvaluateSide(TradeDirection.Short, ctx, tuning, hi, lo, rangeAtr, bar, last);

            if (!buy.IsValid && !sell.IsValid)
                return Reject(ctx, tag, session, tuning, buy, sell);

            if (buy.IsValid && !sell.IsValid) return buy;
            if (!buy.IsValid && sell.IsValid) return sell;

            int diff = buy.Score - sell.Score;

            if (Math.Abs(diff) <= ScoreDeadband)
            {
                // no bias → return first breakout that happened
                if (ctx.BreakoutUpBarsSince < ctx.BreakoutDownBarsSince)
                    return buy;

                if (ctx.BreakoutDownBarsSince < ctx.BreakoutUpBarsSince)
                    return sell;
            }

            return diff >= 0 ? buy : sell;
        }

        private EntryEvaluation EvaluateSide(
            TradeDirection dir,
            EntryContext ctx,
            dynamic tuning,
            double hi,
            double lo,
            double rangeAtr,
            Bar bar,
            int index)
        {
            int score = (int)tuning.BaseScore;
            int minScore = (int)tuning.MinScore;

            var reasons = new List<string>();

            // ===== IMPULSE (2-sided) =====
            bool hasImpulse =
                dir == TradeDirection.Long
                    ? ctx.HasImpulseLong_M5
                    : ctx.HasImpulseShort_M5;

            if (!hasImpulse)
                return InvalidDir(ctx, dir, "NO_IMPULSE", score);

            int barsSinceImpulse =
                dir == TradeDirection.Long
                    ? ctx.BarsSinceImpulseLong_M5
                    : ctx.BarsSinceImpulseShort_M5;

            if (barsSinceImpulse > tuning.MaxBarsSinceImpulse)
                return InvalidDir(ctx, dir, "STALE_IMPULSE", score);

            // ===== FLAG STRUCTURE =====
            bool hasFlag =
                dir == TradeDirection.Long
                    ? ctx.HasFlagLong_M5
                    : ctx.HasFlagShort_M5;

            if (!hasFlag)
                return InvalidDir(ctx, dir, "NO_FLAG", score);

            // ===== BREAKOUT (SOURCE OF TRUTH) =====
            bool breakoutConfirmed =
                dir == TradeDirection.Long
                    ? ctx.FlagBreakoutUpConfirmed
                    : ctx.FlagBreakoutDownConfirmed;

            bool breakoutInstant =
                dir == TradeDirection.Long
                    ? ctx.FlagBreakoutUp
                    : ctx.FlagBreakoutDown;

            double breakDist =
                dir == TradeDirection.Long
                    ? Math.Max(0, bar.Close - hi)
                    : Math.Max(0, lo - bar.Close);

            double breakAtr = ctx.AtrM5 > 0 ? breakDist / ctx.AtrM5 : 0;

            double body = Math.Abs(bar.Close - bar.Open);
            double range = bar.High - bar.Low;
            double bodyRatio = range > 0 ? body / range : 0;

            bool strongBody = bodyRatio >= MinBodyRatio;

            bool breakout =
                breakoutConfirmed &&
                breakAtr >= MinCloseBreakAtr &&
                strongBody;

            if (!breakout)
            {
                score -= (bodyRatio < 0.3) ? 6 : 3;
                reasons.Add("NO_BREAKOUT");
            }
            else
            {
                reasons.Add("BREAKOUT_OK");
            }

            // ===== HTF =====
            bool isAgainst =
                ctx.MetalHtfAllowedDirection != TradeDirection.None &&
                ctx.MetalHtfAllowedDirection != dir;

            if (isAgainst)
            {
                if (breakAtr < HtfAgainstMinBreakAtr)
                {
                    score -= HtfAgainstPenalty;
                    reasons.Add("HTF_WEAK_BREAK");
                }

                if (bodyRatio < HtfAgainstMinBody)
                {
                    score -= 3;
                    reasons.Add("HTF_WEAK_BODY");
                }

                if (!breakoutConfirmed && bodyRatio < 0.4)
                    return InvalidDir(ctx, dir, "HTF_NO_QUALITY", score);
            }

            // ===== STRUCTURE FILTER =====
            if (dir == TradeDirection.Long && IsLowerHigh(ctx.M5, index))
                return InvalidDir(ctx, dir, "LOWER_HIGH", score);

            if (dir == TradeDirection.Short && IsHigherLow(ctx.M5, index))
                return InvalidDir(ctx, dir, "HIGHER_LOW", score);

            bool valid = score >= minScore;

            if (!valid)
                return InvalidDir(ctx, dir, "LOW_SCORE", score);

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason = $"ACCEPT {dir} score={score} breakout={breakoutConfirmed}"
            };
        }

        // =========================

        private static bool IsLowerHigh(Bars b, int i)
        {
            if (i < 3) return false;
            return b[i - 1].High < b[i - 2].High && b[i - 2].High < b[i - 3].High;
        }

        private static bool IsHigherLow(Bars b, int i)
        {
            if (i < 3) return false;
            return b[i - 1].Low > b[i - 2].Low && b[i - 2].Low > b[i - 3].Low;
        }

        private static bool TryResolveSession(FxSession raw, out FxSession resolved, out string tag)
        {
            resolved = raw;
            tag = raw switch
            {
                FxSession.Asia => "XAU_FLAG_ASIA",
                FxSession.London => "XAU_FLAG_LONDON",
                FxSession.NewYork => "XAU_FLAG_NEWYORK",
                _ => null
            };
            return tag != null;
        }

        private static EntryEvaluation Reject(
            EntryContext ctx,
            string tag,
            FxSession session,
            dynamic tuning,
            EntryEvaluation buy,
            EntryEvaluation sell)
        {
            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = TradeDirection.None,
                Score = (int)tuning.BaseScore,
                IsValid = false,
                Reason = $"REJECT BOTH buy={buy.Score}/{buy.IsValid} sell={sell.Score}/{sell.IsValid}"
            };
        }

        private static EntryEvaluation InvalidDir(EntryContext ctx, TradeDirection dir, string reason, int score)
        {
            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = dir,
                Score = Math.Max(0, score),
                IsValid = false,
                Reason = reason
            };
        }

        private static EntryEvaluation Invalid(EntryContext ctx, string reason)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = TradeDirection.None,
                IsValid = false,
                Reason = reason
            };
        }
    }
}