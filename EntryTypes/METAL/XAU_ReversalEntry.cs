using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes.METAL
{
    public class XAU_ReversalEntry : IEntryType
    {
        public EntryType Type => EntryType.XAU_Reversal;

        private const int MinEvidence = 4;
        private const int MinScore = 50;
        private const double SlopeEps = 0.0;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady || ctx.M5 == null)
                return Invalid(ctx, "CTX_NOT_READY");

            // =====================================================
            // 1️⃣ TREND IRÁNY (XAU saját)
            // =====================================================
            TradeDirection trendDir = ResolveXauDirection(ctx);
            if (trendDir == TradeDirection.None)
                return Invalid(ctx, "NoTrendDir");

            // Reversal irány = trend ellen (minimalista, de működő)
            TradeDirection dir = trendDir == TradeDirection.Long
                ? TradeDirection.Short
                : TradeDirection.Long;

            // =====================================================
            // 2️⃣ REVERSAL EVIDENCE
            // =====================================================
            if (ctx.ReversalEvidenceScore < MinEvidence)
                return Invalid(ctx, $"WeakEvidence({ctx.ReversalEvidenceScore})");

            // =====================================================
            // 3️⃣ SCORE
            // =====================================================
            int score = ctx.ReversalEvidenceScore * 12 + 20;

            if (ctx.M1ReversalTrigger)
                score += 15;
            else
                score -= 10;

            if (!ctx.IsRange_M5)
                return Invalid(ctx, "NOT_RANGE");

            if (score < MinScore)
                return Invalid(ctx, $"LowScore({score})");

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason = $"XAU_REV dir={dir} score={score}"
            };
        }

        private TradeDirection ResolveXauDirection(EntryContext ctx)
        {
            bool up5 = ctx.Ema21Slope_M5 > SlopeEps;
            bool dn5 = ctx.Ema21Slope_M5 < -SlopeEps;

            bool up15 = ctx.Ema21Slope_M15 > SlopeEps;
            bool dn15 = ctx.Ema21Slope_M15 < -SlopeEps;

            if (up5 && up15) return TradeDirection.Long;
            if (dn5 && dn15) return TradeDirection.Short;

            double a5 = System.Math.Abs(ctx.Ema21Slope_M5);
            double a15 = System.Math.Abs(ctx.Ema21Slope_M15);

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

        private EntryEvaluation Invalid(EntryContext ctx, string reason)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                IsValid = false,
                Reason = reason
            };
        }
    }
}
