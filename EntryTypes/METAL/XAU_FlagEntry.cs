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

            ctx.Log?.Invoke($"[FLAG DEBUG] hi={ctx.FlagHigh} lo={ctx.FlagLow} atr={ctx.AtrM5} range={(ctx.FlagHigh - ctx.FlagLow)}");
            ctx.Log?.Invoke($"[FLAG DEBUG] hasFlagLong={ctx.HasFlagLong_M5} hasFlagShort={ctx.HasFlagShort_M5}");

            ctx.Log?.Invoke($"[FLAG RANGE CHECK] hi={hi} lo={lo} valid={(hi > lo)}");
            
            ctx.Log?.Invoke("[FLAG ERROR] INVALID RANGE → rejecting flag");

            if (hi <= 0 || lo <= 0 || hi <= lo)
            {
                ctx.Log?.Invoke("[FLAG DEBUG] FALLBACK RANGE USED");

                hi = bar.High;
                lo = bar.Low;
            }

            double rangeAtr = (hi - lo) / ctx.AtrM5;

            if (rangeAtr <= 0 || rangeAtr > tuning.MaxFlagAtrMult * 1.3)
                return Invalid(ctx, "FLAG_TOO_WIDE_HARD");
                
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

            if (rangeAtr <= 0.8)
            {
                score += 6;
                reasons.Add("FLAG_TIGHT");
            }
            else if (rangeAtr <= 1.1)
            {
                score += 3;
                reasons.Add("FLAG_OK");
            }
            else
            {
                score -= 6;
                reasons.Add("FLAG_LOOSE");
            }

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

            // ===== IMPULSE QUALITY (v2.22) =====
            if (barsSinceImpulse <= 1)
            {
                score += 6;
                reasons.Add("IMPULSE_FRESH");
            }
            else if (barsSinceImpulse <= 3)
            {
                score += 2;
                reasons.Add("IMPULSE_OK");
            }
            else
            {
                score -= 3;
                reasons.Add("IMPULSE_OLD");
            }

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

            bool earlyBreakout =
                breakoutInstant &&
                breakAtr >= 0.03 &&
                bodyRatio >= 0.45 &&
                ctx.LastClosedBarInTrendDirection;

            bool breakout =
                (breakoutConfirmed && breakAtr >= MinCloseBreakAtr && strongBody) ||
                earlyBreakout;

            if (breakoutInstant && !breakoutConfirmed)
            {
                score += 2;
                reasons.Add("BREAKOUT_FORMING");
            }

            if (breakoutConfirmed && breakAtr >= MinCloseBreakAtr && strongBody)
            {
                score += 10;
                reasons.Add("BREAKOUT_CONFIRMED");
            }
            else if (earlyBreakout)
            {
                score += 6;
                reasons.Add("BREAKOUT_EARLY");
                minScore -= 2;
            }
            else
            {
                score -= (bodyRatio < 0.3) ? 6 : 3;
                reasons.Add("NO_BREAKOUT");
            }

            // ===== HTF =====
            bool isAgainst =
                ctx.MetalHtfAllowedDirection != TradeDirection.None &&
                ctx.MetalHtfAllowedDirection != dir;

            if (isAgainst)
            {
                score -= HtfAgainstPenalty;
                reasons.Add("HTF_AGAINST");

                if (breakAtr < HtfAgainstMinBreakAtr)
                {
                    score -= 3;
                    reasons.Add("HTF_WEAK_BREAK");
                }

                if (bodyRatio < HtfAgainstMinBody)
                {
                    score -= 3;
                    reasons.Add("HTF_WEAK_BODY");
                }

                if (!breakoutConfirmed && !earlyBreakout)
                {
                    score -= 6;
                    reasons.Add("HTF_NO_CONFIRM");
                }
            }

            // ===== STRUCTURE FILTER =====
            if (dir == TradeDirection.Long && IsLowerHigh(ctx.M5, index))
                return InvalidDir(ctx, dir, "LOWER_HIGH", score);

            if (dir == TradeDirection.Short && IsHigherLow(ctx.M5, index))
                return InvalidDir(ctx, dir, "HIGHER_LOW", score);

            int effectiveMinScore = earlyBreakout ? minScore - 2 : minScore;

            bool valid = score >= effectiveMinScore;

            if (!valid)
                return InvalidDir(ctx, dir, "LOW_SCORE", score);

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason = $"ACCEPT {dir} score={score} breakoutConfirmed={breakoutConfirmed} earlyBreakout={earlyBreakout} breakAtr={breakAtr:F2} bodyRatio={bodyRatio:F2} rangeAtr={rangeAtr:F2}"
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