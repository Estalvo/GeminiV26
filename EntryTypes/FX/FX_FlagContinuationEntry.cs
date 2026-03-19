using GeminiV26.Core.Entry;
using GeminiV26.Core;
using GeminiV26.Instruments.FX;

namespace GeminiV26.EntryTypes.FX
{
    public sealed class FX_FlagContinuationEntry : IEntryType
    {
        public EntryType Type => EntryType.FX_FlagContinuation;

        private const int MinScore = EntryDecisionPolicy.MinScoreThreshold;
        private const double MinPullbackAtr = 0.15;
        private const double MaxPullbackAtr = 0.60;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady)
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

            double pullbackDepthAtr =
                dir == TradeDirection.Long ? ctx.PullbackDepthRLong_M5 : ctx.PullbackDepthRShort_M5;

            bool isValidFlagStructure =
                dir == TradeDirection.Long ? ctx.HasFlagLong_M5 : ctx.HasFlagShort_M5;

            bool hasValidImpulse =
                hasImpulse ||
                (ctx.IsAtrExpanding_M5 && !ctx.IsRange_M5);

            if (!hasValidImpulse)
                return Invalid(ctx, dir, "NO_IMPULSE", 49);

            if (!isValidFlagStructure)
                return Invalid(ctx, dir, "INVALID_FLAG", 50);

            if (pullbackDepthAtr < MinPullbackAtr)
                return Invalid(ctx, dir, "PB_TOO_SHALLOW", 50);

            // =========================
            // SESSION-AWARE PULLBACK DEPTH
            // =========================
            double maxPb = MaxPullbackAtr;

            if (ctx.Session.ToString() == "NewYork")
                maxPb = 0.75;

            int score = 48;

            if (pullbackDepthAtr > maxPb)
                return Invalid(ctx, dir, "PB_TOO_DEEP", 49);

            if (!ctx.IsPullbackDecelerating_M5)
                return Invalid(ctx, dir, "NO_DECELERATION", 51);

            if (!ctx.HasReactionCandle_M5)
                return Invalid(ctx, dir, "NO_REACTION", 50);

            // ✅ FIX: side-aware M1 confirmation
            bool m1Confirm =
                ctx.M1FlagBreakTrigger ||
                (ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir);

            bool continuationSignal = m1Confirm;

            bool hasStructure =
                pullbackDepthAtr >= MinPullbackAtr;

            if (!hasStructure)
                setupScore -= 35;
            else
                setupScore += 15;

            bool hasContinuation =
                continuationSignal;

            if (hasContinuation)
                setupScore += 20;

            // ✅ FIX: side-aware score boost
            if (ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir)
                score += 10;

            int lastClosed = ctx.M5.Count - 2;
            var bar = ctx.M5[lastClosed];
            bool breakoutDetected = m1Confirm || ctx.RangeBreakDirection == dir;
            bool strongCandle =
                (dir == TradeDirection.Long && bar.Close > bar.Open) ||
                (dir == TradeDirection.Short && bar.Close < bar.Open);
            bool followThrough = continuationSignal || ctx.LastClosedBarInTrendDirection;

            if (ctx.IsRange_M5)
                score -= 10;

            score = ApplyMandatoryEntryAdjustments(ctx, dir, score, true);
            score = TriggerScoreModel.Apply(ctx, $"FX_FLAG_CONT_{dir}", score, breakoutDetected, strongCandle, followThrough, "NO_M1_CONFIRM");
            score += setupScore;

            if (setupScore <= 0)
                score = System.Math.Min(score, MinScore - 10);

            if (score < MinScore)
                return Invalid(ctx, dir, "LOW_SCORE", score);

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason = $"FX_FLAG_CONT score={score} pbATR={pullbackDepthAtr:F2}"
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
