using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes.METAL
{
    /// <summary>
    /// XAU Impulse Continuation Entry – LIVE VERSION
    /// Phase 3.9 – Momentum continuation for metals
    ///
    /// Belép, ha:
    /// - M5 ATR expanzió
    /// - M5 impulse
    /// - ADX minimum szint
    /// - EMA alignment irányba
    /// - M1 trigger megerősítés
    /// </summary>
    public class XAU_ImpulseEntry : IEntryType
    {
        public EntryType Type => EntryType.XAU_Impulse;

        private const double SlopeEps = 0.0;
        private const int MinScore = 65;
        private const double MinAdx = 18.0;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady || ctx.M5 == null)
                return Reject(ctx, "CTX_NOT_READY");

            int score = 60; // base impulse score

            // =====================================================
            // 1️⃣ IRÁNY
            // =====================================================
            TradeDirection dir = ResolveXauDirection(ctx);
            if (dir == TradeDirection.None)
                return Reject(ctx, "NO_CLEAR_DIRECTION");

            // =====================================================
            // 2️⃣ ATR EXPANSION
            // =====================================================
            if (!ctx.IsAtrExpanding_M5)
                return Reject(ctx, "ATR_NOT_EXPANDING");

            score += 5;

            // =====================================================
            // 3️⃣ IMPULSE M5
            // =====================================================
            if (!ctx.HasImpulse_M5)
                return Reject(ctx, "NO_M5_IMPULSE");

            score += 5;

            // =====================================================
            // 4️⃣ ADX FILTER (XAU stricter momentum control)
            // =====================================================

            double minAdxRequired = 18.0;

            // XAU impulse continuationhez erősebb trend kell
            if (ctx.Symbol != null && ctx.Symbol.ToUpper().Contains("XAU"))
                minAdxRequired = 28.0;

            if (ctx.Adx_M5 < minAdxRequired)
                return Reject(ctx, $"ADX_TOO_LOW({ctx.Adx_M5:F1})");

            if (ctx.Adx_M5 >= 30)
                score += 5;

            // =====================================================
            // 5️⃣ EMA ALIGNMENT
            // =====================================================
            if (dir == TradeDirection.Long)
            {
                if (ctx.Ema8_M5 > ctx.Ema21_M5)
                    score += 5;
            }
            else
            {
                if (ctx.Ema8_M5 < ctx.Ema21_M5)
                    score += 5;
            }

            // =====================================================
            // 6️⃣ M1 TRIGGER (Continuation confirmation)
            // =====================================================
            if (!ctx.M1TriggerInTrendDirection)
                return Reject(ctx, "NO_M1_TRIGGER");

            score += 5;

            // =====================================================
            // FINAL DECISION
            // =====================================================
            if (score < MinScore)
                return Reject(ctx, $"LOW_SCORE({score})");

            string note =
                $"[XAU_IMPULSE_CONT] {ctx.Symbol} dir={dir} " +
                $"Score={score} ADX={ctx.Adx_M5:F1} ATR_EXP=True IMPULSE=True";

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
        // IRÁNY MEGHATÁROZÁS – változatlan
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
                Reason = $"[XAU_IMPULSE_REJECT] {reason}"
            };
        }
    }
}