using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes.METAL
{
    public class XAU_ImpulseEntry : IEntryType
    {
        public EntryType Type => EntryType.XAU_Impulse;

        private const double SlopeEps = 0.0;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            return Invalid(ctx, "XAU_IMPULSE_CONTEXT_ONLY");
        }
/*
        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady || ctx.M5 == null)
                return Invalid(ctx, "CTX_NOT_READY");

            // =====================================================
            // 1️⃣ IRÁNY – XAU saját
            // =====================================================
            TradeDirection dir = ResolveXauDirection(ctx);
            if (dir == TradeDirection.None)
                return Invalid(ctx, "NoTrendDir");

            // =====================================================
            // 2️⃣ ATR EXPANSION
            // =====================================================
            if (!ctx.IsAtrExpanding_M5)
                return Invalid(ctx, "NoATRExpansion");

            // =====================================================
            // 3️⃣ IMPULSE M5
            // =====================================================
            if (!ctx.HasImpulse_M5)
                return Invalid(ctx, "NoImpulse");

            // =====================================================
            // 4️⃣ M1 trigger
            // =====================================================
            if (!ctx.M1TriggerInTrendDirection)
                return Invalid(ctx, "NoM1Trigger");

            int score = 45;
            if (ctx.IsAtrExpanding_M5) score += 5;
            if (ctx.M1TriggerInTrendDirection) score += 5;

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = score >= 35,
                Reason = $"XAU_IMPULSE dir={dir} score={score}"
            };
        }
*/
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
