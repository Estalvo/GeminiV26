using GeminiV26.Core.Entry;
using GeminiV26.Instruments.FX;

namespace GeminiV26.EntryTypes.FX
{
    /// <summary>
    /// FX Impulse Continuation Entry
    /// Phase 3.7.x – tightened to avoid late / top impulse entries
    /// </summary>
    public class FX_ImpulseContinuationEntry : IEntryType
    {
        public EntryType Type => EntryType.FX_ImpulseContinuation;

        private const double MinSlope = 0.00015;

        // ===== Patch knobs (FX-safe) =====
        private const int MinScore = 55;                 // was 45
        private const double MinPullbackAtr = 0.15;      // avoid top-tick entries
        private const double MaxPullbackAtr = 1.0;       // already present logic

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            return Invalid(ctx, "FX_RESET_DISABLED_IMPULSE_CONT");
        }
/*
        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (!ctx.IsReady)
                return Invalid(ctx, "CTX_NOT_READY");

            // =====================================================
            // TREND DIRECTION — PULLBACK LOGIKA (SZENT)
            // =====================================================
            bool up =
                ctx.Ema21Slope_M15 > MinSlope &&
                ctx.Ema21Slope_M5 > MinSlope;

            bool down =
                ctx.Ema21Slope_M15 < -MinSlope &&
                ctx.Ema21Slope_M5 < -MinSlope;

            TradeDirection bias = TradeDirection.None;

            if (!up && !down)
            {
                double softSlope = ctx.Ema21Slope_M15 + ctx.Ema21Slope_M5;

                if (softSlope > 0)
                    bias = TradeDirection.Long;
                else if (softSlope < 0)
                    bias = TradeDirection.Short;

                return new EntryEvaluation
                {
                    Symbol = ctx.Symbol,
                    Type = Type,
                    Direction = bias,
                    Score = 0,
                    IsValid = false,
                    Reason = "NoTrend;"
                };
            }

            bias = up ? TradeDirection.Long : TradeDirection.Short;

            // =====================================================
            // IMPULSE CONDITIONS (REAL CONTEXT FIELDS)
            // =====================================================
            if (!ctx.HasImpulse_M5)
                return Invalid(ctx, "NoImpulse");

            // túl mély pullback = nem continuation
            if (ctx.PullbackDepthAtr_M5 > MaxPullbackAtr)
                return Invalid(ctx, "TooDeepPullback");

            // túl kicsi pullback = csúcson belépés
            if (ctx.PullbackDepthAtr_M5 < MinPullbackAtr)
                return Invalid(ctx, "NoMeaningfulPullback");

            // ATR expanding = kifulladó impulse chase FX-en
            if (ctx.IsAtrExpanding_M5)
                return Invalid(ctx, "VolExpanding");

            int score = 30;

            if (ctx.M1TriggerInTrendDirection)
                score += 15;

            if (ctx.IsRange_M5)
                score -= 10;

            // =====================================================
            // SCORE → DIRECTION
            // =====================================================
            TradeDirection dir = TradeDirection.None;

            if (score >= MinScore)
                dir = bias;

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = dir != TradeDirection.None,
                Reason =
                    $"FX_CONT score={score} " +
                    $"pbATR={ctx.PullbackDepthAtr_M5:F2} " +
                    $"m1={ctx.M1TriggerInTrendDirection}"
            };
        }
*/
        private EntryEvaluation Invalid(EntryContext ctx, string reason)
            => new()
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                IsValid = false,
                Reason = reason
            };
    }
}
