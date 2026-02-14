using System;
using System.Collections.Generic;
using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes.METAL
{
    public class XAU_ReversalEntry : IEntryType
    {
        public EntryType Type => EntryType.XAU_Reversal;

        private const int MinEvidence = 4;
        private const int MinScore = 50;
        private const double SlopeEps = 0.0;

        private const int NoM1Penalty = 12;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady || ctx.M5 == null)
                return Reject(ctx, "CTX_NOT_READY");

            var reasons = new List<string>(8);

            // =====================================================
            // 1️⃣ TREND IRÁNY (XAU saját)
            // =====================================================
            TradeDirection trendDir = ResolveXauDirection(ctx);
            if (trendDir == TradeDirection.None)
                return Reject(ctx, "NO_TREND_DIR");

            // Reversal irány = trend ellen
            TradeDirection dir = trendDir == TradeDirection.Long
                ? TradeDirection.Short
                : TradeDirection.Long;

            reasons.Add($"Trend={trendDir}");

            // =====================================================
            // 2️⃣ RANGE KÖTELEZŐ
            // =====================================================
            if (!ctx.IsRange_M5)
                return RejectDecision(ctx, dir, 0, "NOT_RANGE", reasons);

            reasons.Add("RANGE_OK");

            // =====================================================
            // 3️⃣ REVERSAL EVIDENCE
            // =====================================================
            int evidence = ctx.ReversalEvidenceScore;
            if (evidence < MinEvidence)
                return RejectDecision(ctx, dir, 0, $"WEAK_EVIDENCE({evidence})", reasons);

            int score = evidence * 12 + 20;
            reasons.Add($"Evidence={evidence}");

            // =====================================================
            // 4️⃣ M1 REVERSAL TRIGGER
            // =====================================================
            if (ctx.M1ReversalTrigger)
            {
                score += 15;
                reasons.Add("+M1_REV(15)");
            }
            else
            {
                score -= NoM1Penalty;
                reasons.Add($"NO_M1_REV(-{NoM1Penalty})");
            }

            // =====================================================
            // 5️⃣ MIN SCORE GATE
            // =====================================================
            if (score < MinScore)
                return RejectDecision(ctx, dir, score, $"LOW_SCORE({score})", reasons);

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
                IsValid = true,
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
    }
}
