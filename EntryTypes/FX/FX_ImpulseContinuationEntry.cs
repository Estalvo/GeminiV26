using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes;
using GeminiV26.Core;
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
        private const int MinScore = EntryDecisionPolicy.MinScoreThreshold;                 // was 45
        private const double MinPullbackAtr = 0.15;      // avoid top-tick entries
        private const double MaxPullbackAtr = 1.0;       // already present logic

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            FxDirectionValidation.LogDirectionDebug(ctx);
            if (!ctx.IsReady)
                return Invalid(ctx, "CTX_NOT_READY");

            if (ctx.LogicBias == TradeDirection.None)
                return Invalid(ctx, TradeDirection.None, "NO_LOGIC_BIAS", 0);

            if (FxDirectionValidation.ShouldBlockHtfMismatch(ctx))
                return Invalid(ctx, TradeDirection.None, "HTF_STRONG_OPPOSITE_LTF_WEAK", 0);

            if (ctx.LogicBias == TradeDirection.Long)
            {
                var eval = EvaluateSide(TradeDirection.Long, ctx);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), eval, null, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }
            else if (ctx.LogicBias == TradeDirection.Short)
            {
                var eval = EvaluateSide(TradeDirection.Short, ctx);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), null, eval, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }

            return Invalid(ctx, TradeDirection.None, "NO_LOGIC_BIAS", 0);
        }

        private EntryEvaluation EvaluateSide(TradeDirection dir, EntryContext ctx)
        {
            int setupScore = 0;

            bool hasImpulse =
                dir == TradeDirection.Long ? ctx.HasImpulseLong_M5 : ctx.HasImpulseShort_M5;

            double pullbackDepthR =
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
                return Invalid(ctx, dir, "NoTrend", 0);

            if (dir == TradeDirection.Short && !down)
                return Invalid(ctx, dir, "NoTrend", 0);

            // =====================================================
            // IMPULSE CONDITIONS (REAL CONTEXT FIELDS)
            // =====================================================
            if (ctx.Adx_M5 >= 15 && ctx.Adx_M5 < 22)
                return Invalid(ctx, dir, "WeakTrend", 50);

            if (!hasImpulse)
                return Invalid(ctx, dir, "NoImpulse", 50);

            if (pullbackDepthR > MaxPullbackAtr)
                return Invalid(ctx, dir, "TooDeepPullback", 49);

            if (pullbackDepthR < MinPullbackAtr)
                return Invalid(ctx, dir, "NoMeaningfulPullback", 50);

            // ATR expanding = kifulladó impulse chase FX-en
            if (ctx.IsAtrExpanding_M5)
                return Invalid(ctx, dir, "VolExpanding", 51);

            bool continuationSignal =
                ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir;

            bool hasStructure =
                pullbackDepthR >= MinPullbackAtr;

            if (!hasStructure)
                setupScore -= 35;
            else
                setupScore += 15;

            bool hasContinuation =
                continuationSignal;

            if (hasContinuation)
                setupScore += 20;

            int score = 30;

            if (continuationSignal)
                score += 15;

            if (ctx.IsRange_M5)
                score -= 10;

            int lastClosed = ctx.M5.Count - 2;
            var bar = ctx.M5[lastClosed];
            bool breakoutDetected = continuationSignal || ctx.RangeBreakDirection == dir;
            bool strongCandle =
                (dir == TradeDirection.Long && bar.Close > bar.Open) ||
                (dir == TradeDirection.Short && bar.Close < bar.Open);
            bool followThrough = continuationSignal || (ctx.LastClosedBarInTrendDirection && ctx.HasReactionCandle_M5);

            score = ApplyMandatoryEntryAdjustments(ctx, dir, score, true);
            score = TriggerScoreModel.Apply(ctx, $"FX_IMPULSE_CONT_{dir}", score, breakoutDetected, strongCandle, followThrough, "NO_CONTINUATION_SIGNAL");
            score += setupScore;

            if (setupScore <= 0)
                score = System.Math.Min(score, MinScore - 10);

            // =====================================================
            // SCORE → DIRECTION
            // =====================================================
            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason =
                    $"FX_CONT score={score} " +
                    $"pbR={pullbackDepthR:F2} " +
                    $"m1={(ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir)}"
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

        private EntryEvaluation Invalid(EntryContext ctx, TradeDirection dir, string reason, int score)
            => new()
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = false,
                Reason = reason
            };

        private static int ApplyMandatoryEntryAdjustments(EntryContext ctx, TradeDirection direction, int score, bool applyTrendRegimePenalty)
        {
            return EntryDirectionQuality.Apply(
                ctx,
                direction,
                score,
                new DirectionQualityRequest
                {
                    TypeTag = "FX_ImpulseContinuationEntry",
                    ApplyTrendRegimePenalty = applyTrendRegimePenalty
                });
        }

    }
}
