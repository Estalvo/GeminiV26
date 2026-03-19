using GeminiV26.Core.Entry;
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
            if (!ctx.IsReady)
                return Invalid(ctx, "CTX_NOT_READY");

            var longEval = EvaluateSide(TradeDirection.Long, ctx);
            var shortEval = EvaluateSide(TradeDirection.Short, ctx);

            if (EntryDecisionPolicy.IsHardInvalid(longEval) && EntryDecisionPolicy.IsHardInvalid(shortEval))
                return Invalid(ctx, "NO_VALID_SIDE");

            return EntryDecisionPolicy.Normalize(EntryDecisionPolicy.SelectBalancedEvaluation(ctx, Type, longEval, shortEval));
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
            const int htfPenalty = 30;
            const int logicPenalty = 12;
            const int rangePenalty = 25;

            TradeDirection htfDirection = TradeDirection.None;
            double htfConfidence = 0.0;

            switch (SymbolRouting.ResolveInstrumentClass(ctx.Symbol))
            {
                case InstrumentClass.FX:
                    htfDirection = ctx.FxHtfAllowedDirection;
                    htfConfidence = ctx.FxHtfConfidence01;
                    break;
                case InstrumentClass.CRYPTO:
                    htfDirection = ctx.CryptoHtfAllowedDirection;
                    htfConfidence = ctx.CryptoHtfConfidence01;
                    break;
                case InstrumentClass.INDEX:
                    htfDirection = ctx.IndexHtfAllowedDirection;
                    htfConfidence = ctx.IndexHtfConfidence01;
                    break;
                case InstrumentClass.METAL:
                    htfDirection = ctx.MetalHtfAllowedDirection;
                    htfConfidence = ctx.MetalHtfConfidence01;
                    break;
            }

            if (htfDirection != TradeDirection.None && htfConfidence >= 0.70 && direction != htfDirection)
            {
                score -= htfPenalty;
                ctx.Log?.Invoke($"[ENTRY HTF ALIGN] dir={direction} htf={htfDirection} conf={htfConfidence:0.00} penalty={htfPenalty}");
            }

            var logicBias = ctx.LogicBiasDirection;
            var logicConfidence = ctx.LogicBiasConfidence;
            if (logicBias != TradeDirection.None && logicConfidence >= 60 && direction != logicBias)
            {
                score -= logicPenalty;
                ctx.Log?.Invoke($"[ENTRY LOGIC ALIGN] dir={direction} logic={logicBias} conf={logicConfidence} penalty={logicPenalty}");
            }

            if (applyTrendRegimePenalty && ctx.Adx_M5 < 15.0)
            {
                score -= rangePenalty;
                ctx.Log?.Invoke($"[ENTRY REGIME] adx={ctx.Adx_M5:0.0} penalty={rangePenalty}");
            }

            return score;
        }

    }
}
