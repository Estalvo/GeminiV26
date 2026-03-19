using System;
using System.Collections.Generic;
using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes.METAL
{
    public class XAU_ReversalEntry : IEntryType
    {
        public EntryType Type => EntryType.XAU_Reversal;

        private const int MinEvidence = 4;
        private const int MinScoreThreshold = 50;
        private const double SlopeEps = 0.0;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady || ctx.M5 == null)
                return Reject(ctx, "CTX_NOT_READY");

            TradeDirection trendDir = ResolveXauDirection(ctx);
            if (trendDir == TradeDirection.None)
                return Reject(ctx, "NO_TREND_CONTEXT");

            TradeDirection dir = trendDir == TradeDirection.Long ? TradeDirection.Short : TradeDirection.Long;
            var reasons = new List<string> { $"Trend={trendDir}" };
            int score = 20;
            int setupScore = 0;

            if (XauEntryDecisionPolicy.IsReversalSafetyBlock(ctx, dir, out string hardReason))
                return Build(ctx, dir, score, false, hardReason, reasons);

            reasons.Add("RANGE_OK");

            int evidence = ctx.ReversalEvidenceScore;
            if (evidence < MinEvidence)
            {
                score -= 12;
                reasons.Add($"WEAK_EVIDENCE(-12:{evidence})");
            }
            else
            {
                score += evidence * 12;
                reasons.Add($"EVIDENCE_OK(+{evidence * 12})");
            }

            if (!ctx.M1ReversalTrigger)
            {
                score -= 12;
                reasons.Add("NO_M1_TRIGGER(-12)");
            }
            else
            {
                score += 15;
                reasons.Add("M1_REV_OK(+15)");
            }

            bool hasFlag = dir == TradeDirection.Long ? ctx.HasFlagLong_M5 : ctx.HasFlagShort_M5;
            bool structuredPB = ctx.IsPullbackDecelerating_M5 && ctx.PullbackBars_M5 >= 2;
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

            bool breakoutConfirmed = ctx.M1ReversalTrigger;
            bool earlyBreakout = ctx.LastClosedBarInTrendDirection;
            if (breakoutConfirmed || earlyBreakout)
            {
                setupScore += 20;
                reasons.Add("CONFIRMATION_OK(+20)");
            }
            else
            {
                setupScore -= 10;
                reasons.Add("PARTIAL_CONFIRMATION(-10)");
            }

            if (ctx.IsTransition_M5)
            {
                score -= 5;
                reasons.Add("TRANSITION_PENALTY(-5)");
            }

            if (ctx.MetalHtfAllowedDirection != TradeDirection.None && ctx.MetalHtfAllowedDirection != dir)
            {
                score -= 5;
                reasons.Add("HTF_AGAINST(-5)");
            }

            score += setupScore;
            XauEntryDecisionPolicy.ApplyLogicBiasScore(ctx, dir, ref score, reasons);
            return Build(ctx, dir, score, true, "SCORE_DRIVEN", reasons);
        }

        private TradeDirection ResolveXauDirection(EntryContext ctx)
        {
            bool up5 = ctx.Ema21Slope_M5 > SlopeEps;
            bool dn5 = ctx.Ema21Slope_M5 < -SlopeEps;
            bool up15 = ctx.Ema21Slope_M15 > SlopeEps;
            bool dn15 = ctx.Ema21Slope_M15 < -SlopeEps;

            if (up5 && up15) return TradeDirection.Long;
            if (dn5 && dn15) return TradeDirection.Short;

            double a5 = Math.Abs(ctx.Ema21Slope_M5);
            double a15 = Math.Abs(ctx.Ema21Slope_M15);
            if (a5 <= 0 && a15 <= 0) return TradeDirection.None;

            if (a15 >= a5 * 0.9)
            {
                if (up15) return TradeDirection.Long;
                if (dn15) return TradeDirection.Short;
            }

            if (up5) return TradeDirection.Long;
            if (dn5) return TradeDirection.Short;
            return TradeDirection.None;
        }

        private EntryEvaluation Reject(EntryContext ctx, string reason)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = TradeDirection.None,
                Score = 0,
                MinScoreThreshold = MinScoreThreshold,
                IsValid = false,
                Reason = $"[XAU_REV][ENTRY DECISION] score=0 threshold={MinScoreThreshold} valid=false state={reason}"
            };
        }

        private EntryEvaluation Build(EntryContext ctx, TradeDirection dir, int score, bool isValid, string state, List<string> reasons)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = dir,
                Score = Math.Max(0, score),
                MinScoreThreshold = MinScoreThreshold,
                IsValid = isValid,
                LogicConfidence = ctx?.LogicBiasConfidence ?? 0,
                Reason = $"[XAU_REV][ENTRY DECISION] score={Math.Max(0, score)} threshold={MinScoreThreshold} valid={isValid} state={state} dir={dir} :: {string.Join(" | ", reasons ?? new List<string>())}"
            };
        }
    }
}
