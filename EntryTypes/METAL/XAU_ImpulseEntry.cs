using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes.METAL
{
    /// <summary>
    /// XAU Impulse – CONTEXT ONLY
    /// Nem nyit pozíciót.
    /// Csak információt ad a rendszernek és a lognak.
    /// </summary>
    public class XAU_ImpulseEntry : IEntryType
    {
        public EntryType Type => EntryType.XAU_Impulse;

        private const double SlopeEps = 0.0;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady || ctx.M5 == null)
                return Reject(ctx, "CTX_NOT_READY");

            // =====================================================
            // 1️⃣ IRÁNY MEGHATÁROZÁS (XAU-specifikus)
            // =====================================================
            TradeDirection dir = ResolveXauDirection(ctx);
            if (dir == TradeDirection.None)
                return Context(ctx, "NO_CLEAR_DIRECTION");

            // =====================================================
            // 2️⃣ ATR EXPANSION
            // =====================================================
            bool atrExp = ctx.IsAtrExpanding_M5;

            // =====================================================
            // 3️⃣ IMPULSE M5
            // =====================================================
            bool impulse = ctx.HasImpulse_M5;

            // =====================================================
            // 4️⃣ M1 trigger
            // =====================================================
            bool m1 = ctx.M1TriggerInTrendDirection;

            // =====================================================
            // CONTEXT LOG (mindig Reject, de informatív)
            // =====================================================
            string note =
                $"[XAU_IMPULSE_CTX] {ctx.Symbol} dir={dir} " +
                $"ATR_EXP={atrExp} IMPULSE={impulse} M1={m1} " +
                $"Decision=CONTEXT_ONLY";

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = 0,
                IsValid = false, // SOHA nem enged entryt
                Reason = note
            };
        }

        // =====================================================
        // IRÁNY MEGHATÁROZÁS – változatlan logika
        // =====================================================
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

        private EntryEvaluation Context(EntryContext ctx, string reason)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = TradeDirection.None,
                IsValid = false,
                Reason = $"[XAU_IMPULSE_CTX] {ctx?.Symbol} {reason}"
            };
        }
    }
}
