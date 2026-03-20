using System;
using GeminiV26.Core;
using System.Collections.Generic;
using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes;

namespace GeminiV26.EntryTypes.METAL
{
    public class XAU_ReversalEntry : IEntryType
    {
        public EntryType Type => EntryType.XAU_Reversal;

        private const int MinEvidence = 4;
        private const int MinScore = EntryDecisionPolicy.MinScoreThreshold;
        private const double SlopeEps = 0.0;

        private const int NoM1Penalty = 12;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            ctx?.Print($"[DIR DEBUG] symbol={ctx?.SymbolName} bias={ctx?.LogicBias ?? TradeDirection.None} conf={ctx?.LogicConfidence ?? 0}");
            if (ctx == null || !ctx.IsReady || ctx.M5 == null)
                return Reject(ctx, "CTX_NOT_READY");

            if (ctx.LogicBias == TradeDirection.None)
                return RejectDecision(ctx, TradeDirection.None, 0, "NO_LOGIC_BIAS", null);

            if (ctx.HtfConfidence >= 0.6 && ctx.HtfDirection != ctx.LogicBias)
                return RejectDecision(ctx, TradeDirection.None, 0, "HTF_MISMATCH", null);

            if (ctx.LogicBias == TradeDirection.Long)
            {
                var eval = EvaluateSide(ctx, TradeDirection.Long);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), eval, null, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }
            else if (ctx.LogicBias == TradeDirection.Short)
            {
                var eval = EvaluateSide(ctx, TradeDirection.Short);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), null, eval, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }

            return RejectDecision(ctx, TradeDirection.None, 0, "NO_LOGIC_BIAS", null);
        }
        private EntryEvaluation EvaluateSide(EntryContext ctx, TradeDirection dir)
        {
            var reasons = new List<string>(8);
            int setupScore = 0;

            TradeDirection trendDir = ResolveXauDirection(ctx);
            if (trendDir == TradeDirection.None)
                return Reject(ctx, $"NO_TREND_DIR_{dir}");

            reasons.Add($"Trend={trendDir}");

            if (!ctx.IsRange_M5)
                return RejectDecision(ctx, dir, 0, "NOT_RANGE", reasons);

            reasons.Add("RANGE_OK");

            // extra kontroll XAU-ra – ne kontrázzunk még élő trendet
            if (ctx.Adx_M5 > 22)
                return RejectDecision(ctx, dir, 0, $"RANGE_BUT_ADX_STRONG({ctx.Adx_M5:F1})", reasons);

            // =====================================================
            // 3️⃣ REVERSAL EVIDENCE
            // =====================================================
            int evidence = ctx.ReversalEvidenceScore;
            int score = evidence * 12 + 20;
            if (evidence < MinEvidence)
            {
                score -= 12;
                reasons.Add($"WEAK_EVIDENCE({evidence})");
            }
            reasons.Add($"Evidence={evidence}");

            if (trendDir != dir)
                score += 12;
            else
                score -= 12;

            // =====================================================
            // 4️⃣ M1 REVERSAL TRIGGER
            // =====================================================
            // XAU reversalhez kötelező az M1 trigger
            if (!ctx.M1ReversalTrigger)
            {
                score -= NoM1Penalty;
                reasons.Add("NO_M1_REV");
            }
            else
            {
                score += 15;
                reasons.Add("+M1_REV(15)");
            }

            bool hasFlag =
                dir == TradeDirection.Long
                    ? ctx.HasFlagLong_M5
                    : ctx.HasFlagShort_M5;

            bool structuredPB =
                ctx.IsPullbackDecelerating_M5 &&
                ctx.PullbackBars_M5 >= 2;

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
                ctx.M1ReversalTrigger;

            bool earlyBreakout =
                ctx.LastClosedBarInTrendDirection;

            bool hasConfirmation =
                breakoutConfirmed
                || earlyBreakout;

            if (hasConfirmation)
                setupScore += 20;

            int lastClosed = ctx.M5.Count - 2;
            var bar = ctx.M5[lastClosed];
            bool breakoutDetected = breakoutConfirmed;
            bool strongCandle =
                (dir == TradeDirection.Long && bar.Close > bar.Open) ||
                (dir == TradeDirection.Short && bar.Close < bar.Open);
            bool followThrough = hasConfirmation;

            score = ApplyMandatoryEntryAdjustments(ctx, dir, score, false);
            score = TriggerScoreModel.Apply(ctx, $"XAU_REV_{dir}", score, breakoutDetected, strongCandle, followThrough, "NO_REVERSAL_TRIGGER");
            score += setupScore;

            if (setupScore <= 0)
                score = Math.Min(score, MinScore - 10);

            // =====================================================
            // 5️⃣ MIN SCORE GATE
            // =====================================================
            // =====================================================
            // ACCEPT
            // =====================================================
            string note =
                $"[XAU_REV] {ctx.Symbol} dir={dir} " +
                $"Score={score} Min={MinScore} Decision=ACCEPT | " +
                string.Join(" | ", reasons);

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = score >= MinScore,
                Reason = note
            };
        }

        // =====================================================
        // HELPERS
        // =====================================================
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
                IsValid = false,
                Reason = reason
            };
        }

        private EntryEvaluation RejectDecision(
            EntryContext ctx,
            TradeDirection dir,
            int score,
            string reason,
            List<string> reasons)
        {
            string note =
                $"[XAU_REV] {ctx?.Symbol} dir={dir} " +
                $"Score={score} Decision=REJECT Reason={reason} | " +
                (reasons != null ? string.Join(" | ", reasons) : "");

            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = dir,
                Score = Math.Max(0, score),
                IsValid = false,
                Reason = note
            };
        }

        private static int ApplyMandatoryEntryAdjustments(EntryContext ctx, TradeDirection direction, int score, bool applyTrendRegimePenalty)
        {
            return EntryDirectionQuality.Apply(
                ctx,
                direction,
                score,
                new DirectionQualityRequest
                {
                    TypeTag = "XAU_ReversalEntry",
                    ApplyTrendRegimePenalty = applyTrendRegimePenalty
                });
        }

    }
}
