using System;
using System.Collections.Generic;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Matrix;

namespace GeminiV26.EntryTypes.METAL
{
    public class XAU_PullbackEntry : IEntryType
    {
        public EntryType Type => EntryType.XAU_Pullback;

        private const int BarsNotReadyMin = 20;
        private const int ScoreDeadband = 2;

        private const int FreshImpulsePenalty = 6;
        private const int NoM1Penalty = 6;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            var matrix = ctx?.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;
            if (!matrix.AllowPullback)
                return Reject(ctx, "SESSION_DISABLED");

            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < BarsNotReadyMin)
                return Reject(ctx, "CTX_NOT_READY");

            if (ctx.MarketState?.IsTrend != true)
                return Reject(ctx, "NO_TREND_STATE");

            var buy = EvaluateSide(TradeDirection.Long, ctx, matrix);
            var sell = EvaluateSide(TradeDirection.Short, ctx, matrix);

            if (!buy.IsValid && !sell.IsValid)
                return RejectBoth(ctx, buy, sell);

            if (buy.IsValid && !sell.IsValid) return buy;
            if (!buy.IsValid && sell.IsValid) return sell;

            int diff = buy.Score - sell.Score;

            if (Math.Abs(diff) <= ScoreDeadband)
            {
                // döntés: melyik oldal reagált előbb
                if (ctx.BarsSinceImpulseLong_M5 < ctx.BarsSinceImpulseShort_M5)
                    return buy;

                if (ctx.BarsSinceImpulseShort_M5 < ctx.BarsSinceImpulseLong_M5)
                    return sell;
            }

            return diff >= 0 ? buy : sell;
        }

        private EntryEvaluation EvaluateSide(
            TradeDirection dir,
            EntryContext ctx,
            SessionMatrixConfig matrix)
        {
            int score = 60;
            int minScore = 64;
            int setupScore = 0;

            var reasons = new List<string>();

            // =========================
            // IMPULSE (2-sided)
            // =========================
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

            if (barsSinceImpulse > 6)
                return InvalidDir(ctx, dir, "STALE_IMPULSE", score);

            if (barsSinceImpulse == 0)
            {
                return InvalidDir(ctx, dir, "IMPULSE_TOO_FRESH", score);
            }

            // =========================
            // PULLBACK
            // =========================
            int pbBars =
                dir == TradeDirection.Long
                    ? ctx.PullbackBarsLong_M5
                    : ctx.PullbackBarsShort_M5;
            
            double pbDepth =
                dir == TradeDirection.Long
                    ? ctx.PullbackDepthRLong_M5
                    : ctx.PullbackDepthRShort_M5;

           bool noPullback = pbBars == 0;

            // EARLY CONTINUATION DETECTION
            bool earlyContinuation =
                pbBars > 0 &&          // ← EZ A KULCS FIX
                hasImpulse &&
                barsSinceImpulse <= 2 &&
                ctx.LastClosedBarInTrendDirection;

            // ha early continuation → NEM büntetjük
            if (noPullback)
            {
            return InvalidDir(ctx, dir, "NO_PULLBACK", score);
            }
            else if (earlyContinuation)
            {
                score += 8;
                reasons.Add("EARLY_CONTINUATION");
            }

            if (pbBars > 3)
                return InvalidDir(ctx, dir, "PULLBACK_TOO_LONG", score);

            double max = 1.0;
            double deepLimit = 1.4;

            // HARD REJECT csak extrém eset
            if (pbDepth > deepLimit)
            {
                return InvalidDir(ctx, dir, "PB_TOO_DEEP_HARD", score);
            }

            // DEEP PULLBACK (nem automatikus halál!)
            if (pbDepth > max)
            {
                bool compression =
                    ctx.HasReactionCandle_M5 ||
                    ctx.HasRejectionWick_M5 ||
                    ctx.LastClosedBarInTrendDirection;

                if (compression)
                {
                    score += 4;
                    reasons.Add("DEEP_PB_ALLOWED");
                }
                else
                {
                    score -= 4;
                    reasons.Add("DEEP_PB_NO_CONFIRM");
                }
            }
            else
            {
                score += 8;
                reasons.Add("PB_OK");
            }

            // =========================
            // REACTION (core edge)
            // =========================
            bool reaction =
                ctx.HasReactionCandle_M5 ||
                ctx.HasRejectionWick_M5 ||
                ctx.LastClosedBarInTrendDirection;

            if (reaction)
            {
                score += 10;
                reasons.Add("REACTION_OK");
            }
            else
            {
                score -= 6;
                reasons.Add("NO_REACTION");
            }

            if (ctx.HasEarlyPullback_M5)
            {
                score += 1;
                reasons.Add("EARLY_PULLBACK");
            }

            if (ctx.IsTransition_M5)
            {
                score -= 6;
                reasons.Add("TRANSITION_PENALTY");
            }

            // =========================
            // M1 trigger
            // =========================
            bool m1 =
                ctx.M1TriggerInTrendDirection ||
                ctx.M1ReversalTrigger;

            if (m1)
            {
                score += 10;
                reasons.Add("M1_OK");
            }
            else
            {
                score -= NoM1Penalty;
                reasons.Add("NO_M1");
            }

            // =========================
            // HTF (soft only)
            // =========================
            bool against =
                ctx.MetalHtfAllowedDirection != TradeDirection.None &&
                ctx.MetalHtfAllowedDirection != dir;

            if (against)
            {
                score -= 5;
                reasons.Add("HTF_AGAINST");
            }

            bool hasFlag =
                dir == TradeDirection.Long
                    ? ctx.HasFlagLong_M5
                    : ctx.HasFlagShort_M5;

            bool structuredPB =
                ctx.IsPullbackDecelerating_M5 &&
                pbBars >= 2;

            bool earlyPB =
                ctx.HasEarlyPullback_M5;

            bool hasStructure =
                hasFlag
                || structuredPB
                || earlyPB;

            if (!hasStructure)
                setupScore -= 40;
            else
                setupScore += 20;

            bool breakoutConfirmed =
                m1;

            bool earlyBreakout =
                earlyContinuation;

            bool hasConfirmation =
                breakoutConfirmed
                || earlyBreakout;

            if (hasConfirmation)
                setupScore += 20;

            // =========================
            // FINAL
            // =========================
            score += (int)Math.Round(matrix.EntryScoreModifier);
            score += setupScore;

            if (setupScore <= 0)
                score = Math.Min(score, minScore - 10);

            bool valid = score >= minScore;

            if (!valid)
                return InvalidDir(ctx, dir, "LOW_SCORE", score);

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason = $"ACCEPT {dir} score={score} pbBars={pbBars} depth={pbDepth:F2} early={earlyContinuation}"
            };
        }

        // =========================

        private EntryEvaluation Reject(EntryContext ctx, string reason)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = TradeDirection.None,
                IsValid = false,
                Reason = reason
            };
        }

        private EntryEvaluation RejectBoth(
            EntryContext ctx,
            EntryEvaluation buy,
            EntryEvaluation sell)
        {
            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = TradeDirection.None,
                IsValid = false,
                Score = Math.Max(buy.Score, sell.Score),
                Reason = $"REJECT BOTH buy={buy.Score}/{buy.IsValid} sell={sell.Score}/{sell.IsValid}"
            };
        }

        private EntryEvaluation InvalidDir(
            EntryContext ctx,
            TradeDirection dir,
            string reason,
            int score)
        {
            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = Math.Max(0, score),
                IsValid = false,
                Reason = reason
            };
        }
    }
}