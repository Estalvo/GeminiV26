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
            if (!ctx.IsReady)
                return Invalid(ctx, "CTX_NOT_READY");

            var longEval = EvaluateSide(TradeDirection.Long, ctx);
            var shortEval = EvaluateSide(TradeDirection.Short, ctx);

            if (longEval.IsValid && !shortEval.IsValid)
                return longEval;

            if (!longEval.IsValid && shortEval.IsValid)
                return shortEval;

            if (longEval.IsValid && shortEval.IsValid)
                return longEval.Score >= shortEval.Score ? longEval : shortEval;

            return longEval.Score >= shortEval.Score ? longEval : shortEval;
        }

        private EntryEvaluation EvaluateSide(TradeDirection dir, EntryContext ctx)
        {
            bool hasImpulse =
                dir == TradeDirection.Long ? ctx.HasImpulseLong_M5 : ctx.HasImpulseShort_M5;

            double pullbackDepthAtr =
                dir == TradeDirection.Long ? ctx.PullbackDepthRLong_M5 : ctx.PullbackDepthRShort_M5;

            // =====================================================
            // TREND DIRECTION — PULLBACK LOGIKA (SZENT)
            // =====================================================
            bool up =
                ctx.Ema21Slope_M15 > MinSlope &&
                ctx.Ema21Slope_M5 > MinSlope;

            bool down =
                ctx.Ema21Slope_M15 < -MinSlope &&
                ctx.Ema21Slope_M5 < -MinSlope;

            if (!up && !down)
            {
                return new EntryEvaluation
                {
                    Symbol = ctx.Symbol,
                    Type = Type,
                    Direction = TradeDirection.None,
                    Score = 0,
                    IsValid = false,
                    Reason = "NoTrend;"
                };
            }

            if (dir == TradeDirection.Long && !up)
                return Invalid(ctx, "NoTrend");

            if (dir == TradeDirection.Short && !down)
                return Invalid(ctx, "NoTrend");

            // =====================================================
            // IMPULSE CONDITIONS (REAL CONTEXT FIELDS)
            // =====================================================
            if (ctx.Adx_M5 < 22)
                return Invalid(ctx, "WeakTrend");

            if (!hasImpulse)
                return Invalid(ctx, "NoImpulse");

            // túl mély pullback = nem continuation
            if (pullbackDepthAtr > MaxPullbackAtr)
                return Invalid(ctx, "TooDeepPullback");

            // túl kicsi pullback = csúcson belépés
            if (pullbackDepthAtr < MinPullbackAtr)
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
            TradeDirection finalDir = TradeDirection.None;

            if (score >= MinScore)
                finalDir = dir;

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = finalDir,
                Score = score,
                IsValid = finalDir != TradeDirection.None,
                Reason =
                    $"FX_CONT score={score} " +
                    $"pbATR={pullbackDepthAtr:F2} " +
                    $"m1={ctx.M1TriggerInTrendDirection}"
            };
        }

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
