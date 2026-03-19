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
        private const int MinScoreThreshold = 64;
        private const int NoM1Penalty = 6;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            var matrix = ctx?.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;
            if (!matrix.AllowPullback)
                return Reject(ctx, "SESSION_DISABLED", MinScoreThreshold);

            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < BarsNotReadyMin)
                return Reject(ctx, "CTX_NOT_READY", MinScoreThreshold);

            var buy = EvaluateSide(TradeDirection.Long, ctx, matrix);
            var sell = EvaluateSide(TradeDirection.Short, ctx, matrix);
            return SelectBest(ctx, buy, sell);
        }

        private EntryEvaluation EvaluateSide(TradeDirection dir, EntryContext ctx, SessionMatrixConfig matrix)
        {
            int score = 60;
            int setupScore = 0;
            var reasons = new List<string>();

            if (XauEntryDecisionPolicy.IsTrendSafetyBlock(ctx, dir, out string hardReason))
                return Build(dir, Math.Max(0, score), false, hardReason, ctx, reasons, MinScoreThreshold);

            bool hasImpulse = dir == TradeDirection.Long ? ctx.HasImpulseLong_M5 : ctx.HasImpulseShort_M5;
            if (hasImpulse)
            {
                score += 8;
                reasons.Add("IMPULSE_OK");
            }
            else
            {
                score -= 18;
                reasons.Add("NO_IMPULSE(-18)");
            }

            int barsSinceImpulse = dir == TradeDirection.Long ? ctx.BarsSinceImpulseLong_M5 : ctx.BarsSinceImpulseShort_M5;
            if (barsSinceImpulse > 6)
            {
                score -= 12;
                reasons.Add("STALE_IMPULSE(-12)");
            }
            else if (barsSinceImpulse == 0)
            {
                score -= 6;
                reasons.Add("IMPULSE_TOO_FRESH(-6)");
            }
            else if (barsSinceImpulse <= 2)
            {
                score += 4;
                reasons.Add("IMPULSE_FRESH(+4)");
            }

            int pbBars = dir == TradeDirection.Long ? ctx.PullbackBarsLong_M5 : ctx.PullbackBarsShort_M5;
            double pbDepth = dir == TradeDirection.Long ? ctx.PullbackDepthRLong_M5 : ctx.PullbackDepthRShort_M5;
            bool noPullback = pbBars == 0;

            bool earlyContinuation =
                pbBars > 0 &&
                hasImpulse &&
                barsSinceImpulse <= 2 &&
                ctx.LastClosedBarInTrendDirection;

            if (noPullback)
            {
                score -= 14;
                reasons.Add("NO_PULLBACK(-14)");
            }
            else if (earlyContinuation)
            {
                score += 8;
                reasons.Add("EARLY_CONTINUATION(+8)");
            }

            if (pbBars > 3)
            {
                score -= 10;
                reasons.Add("PULLBACK_TOO_LONG(-10)");
            }
            else if (pbBars >= 2)
            {
                score += 5;
                reasons.Add("PB_LENGTH_OK(+5)");
            }

            if (pbDepth > 1.4)
            {
                score -= 16;
                reasons.Add("PB_TOO_DEEP(-16)");
            }
            else if (pbDepth > 1.0)
            {
                bool compression =
                    ctx.HasReactionCandle_M5 ||
                    ctx.HasRejectionWick_M5 ||
                    ctx.LastClosedBarInTrendDirection;

                if (compression)
                {
                    score += 4;
                    reasons.Add("DEEP_PB_ALLOWED(+4)");
                }
                else
                {
                    score -= 4;
                    reasons.Add("DEEP_PB_NO_CONFIRM(-4)");
                }
            }
            else if (pbDepth > 0)
            {
                score += 8;
                reasons.Add("PB_OK(+8)");
            }

            bool reaction =
                ctx.HasReactionCandle_M5 ||
                ctx.HasRejectionWick_M5 ||
                ctx.LastClosedBarInTrendDirection;

            if (reaction)
            {
                score += 10;
                reasons.Add("REACTION_OK(+10)");
            }
            else
            {
                score -= 6;
                reasons.Add("NO_REACTION(-6)");
            }

            if (ctx.HasEarlyPullback_M5)
            {
                score += 1;
                reasons.Add("EARLY_PULLBACK(+1)");
            }

            if (ctx.IsTransition_M5)
            {
                score -= 6;
                reasons.Add("TRANSITION_PENALTY(-6)");
            }

            bool m1 = ctx.M1TriggerInTrendDirection || ctx.M1ReversalTrigger;
            if (m1)
            {
                score += 10;
                reasons.Add("M1_OK(+10)");
            }
            else
            {
                score -= NoM1Penalty;
                reasons.Add("NO_M1_TRIGGER(-6)");
            }

            bool against = ctx.MetalHtfAllowedDirection != TradeDirection.None && ctx.MetalHtfAllowedDirection != dir;
            if (against)
            {
                score -= 5;
                reasons.Add("HTF_AGAINST(-5)");
            }

            bool hasFlag = dir == TradeDirection.Long ? ctx.HasFlagLong_M5 : ctx.HasFlagShort_M5;
            bool structuredPB = ctx.IsPullbackDecelerating_M5 && pbBars >= 2;
            bool earlyPB = ctx.HasEarlyPullback_M5;
            bool hasStructure = hasFlag || structuredPB || earlyPB;

            if (!hasStructure)
            {
                setupScore -= 20;
                reasons.Add("WEAK_STRUCTURE(-20)");
            }
            else
            {
                setupScore += 20;
                reasons.Add("STRUCTURE_OK(+20)");
            }

            bool breakoutConfirmed = m1;
            bool earlyBreakout = earlyContinuation;
            bool hasConfirmation = breakoutConfirmed || earlyBreakout;

            if (hasConfirmation)
            {
                setupScore += 20;
                reasons.Add("CONFIRMATION_OK(+20)");
            }
            else
            {
                setupScore -= 10;
                reasons.Add("PARTIAL_CONFIRMATION(-10)");
            }

            score += (int)Math.Round(matrix.EntryScoreModifier);
            score += setupScore;

            XauEntryDecisionPolicy.ApplyLogicBiasScore(ctx, dir, ref score, reasons);
            return Build(dir, score, true, "SCORE_DRIVEN", ctx, reasons, MinScoreThreshold);
        }

        private EntryEvaluation SelectBest(EntryContext ctx, EntryEvaluation buy, EntryEvaluation sell)
        {
            int diff = buy.Score - sell.Score;
            if (Math.Abs(diff) <= ScoreDeadband)
            {
                if (ctx.BarsSinceImpulseLong_M5 < ctx.BarsSinceImpulseShort_M5)
                    return buy;
                if (ctx.BarsSinceImpulseShort_M5 < ctx.BarsSinceImpulseLong_M5)
                    return sell;
            }

            return diff >= 0 ? buy : sell;
        }

        private EntryEvaluation Reject(EntryContext ctx, string reason, int threshold)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = TradeDirection.None,
                Score = 0,
                MinScoreThreshold = threshold,
                IsValid = false,
                Reason = reason
            };
        }

        private EntryEvaluation Build(TradeDirection dir, int score, bool isValid, string state, EntryContext ctx, List<string> reasons, int threshold)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = dir,
                Score = Math.Max(0, score),
                MinScoreThreshold = threshold,
                IsValid = isValid,
                LogicConfidence = ctx?.LogicBiasConfidence ?? 0,
                Reason = $"[XAU_PULLBACK][ENTRY DECISION] score={Math.Max(0, score)} threshold={threshold} valid={isValid} state={state} dir={dir} :: {string.Join(" | ", reasons ?? new List<string>())}"
            };
        }
    }
}
