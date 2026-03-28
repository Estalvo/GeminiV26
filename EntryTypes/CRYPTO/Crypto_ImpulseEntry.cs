using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes;
using GeminiV26.Core;
using System;

namespace GeminiV26.EntryTypes.Crypto
{
    public class Crypto_ImpulseEntry : IEntryType
    {
        public EntryType Type => EntryType.Crypto_Impulse;
        private const int MinScore = EntryDecisionPolicy.MinScoreThreshold;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            DirectionDebug.LogOnce(ctx);
            if (ctx == null || !ctx.IsReady)
                return Invalid(ctx, "CTX_NOT_READY");

            TradeDirection logicBiasDirection = ctx.LogicBiasDirection;
            if (logicBiasDirection == TradeDirection.None)
                return Invalid(ctx, "NO_LOGIC_BIAS", TradeDirection.None, 0);

            if (!ctx.IsVolatilityAcceptable_Crypto)
                return Invalid(ctx, "CRYPTO_VOL_DISABLED");

            if (ctx.ResolveAssetHtfConfidence01() >= 0.6 && ctx.ResolveAssetHtfAllowedDirection() != TradeDirection.None && ctx.ResolveAssetHtfAllowedDirection() != logicBiasDirection)
                return Invalid(ctx, "HTF_MISMATCH", logicBiasDirection, 0);

            if (logicBiasDirection == TradeDirection.Long)
            {
                var eval = EvaluateSide(ctx, TradeDirection.Long);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), eval, null, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }
            else if (logicBiasDirection == TradeDirection.Short)
            {
                var eval = EvaluateSide(ctx, TradeDirection.Short);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), null, eval, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }

            return Invalid(ctx, "NO_LOGIC_BIAS", TradeDirection.None, 0);
        }
        private EntryEvaluation EvaluateSide(EntryContext ctx, TradeDirection dir)
        {
            int score = 60;
            bool directionalImpulse = ctx.ImpulseDirection == dir;
            bool directionalTrend = ctx.TrendDirection == dir;

            if (!directionalImpulse && !directionalTrend &&
                (ctx.ImpulseDirection != TradeDirection.None || ctx.TrendDirection != TradeDirection.None))
            {
                score -= 18;
            }

            if (directionalImpulse)
                score += 10;

            if (directionalTrend)
                score += 8;

            int lastClosed = ctx.M5.Count - 2;
            var bar = ctx.M5[lastClosed];
            bool breakoutDetected =
                (ctx.HasImpulse_M1 && directionalImpulse) ||
                (ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir);
            bool strongCandle =
                (dir == TradeDirection.Long && bar.Close > bar.Open) ||
                (dir == TradeDirection.Short && bar.Close < bar.Open);
            bool followThrough = breakoutDetected || (ctx.IsAtrExpanding_M5 && (directionalImpulse || directionalTrend));

            score = TriggerScoreModel.Apply(ctx, $"CRYPTO_IMPULSE_{dir}", score, breakoutDetected, strongCandle, followThrough, "NO_IMPULSE_TRIGGER");


            score = ApplyMandatoryEntryAdjustments(ctx, dir, score, true);

            if (score < MinScore)
                return Invalid(ctx, $"LOW_SCORE_{dir}_{score}", dir, score);

            var eval = new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason = $"CRYPTO_IMPULSE dir={dir} score={score}"
            };
            ApplyCryptoSourceTrace(ctx, eval, eval.Direction);
            return eval;
        }

        private EntryEvaluation Invalid(EntryContext ctx, string reason)
            => Invalid(ctx, reason, TradeDirection.None, 0);

        private EntryEvaluation Invalid(EntryContext ctx, string reason, TradeDirection dir, int score)
        {
            var eval = new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = false,
                Reason = reason
            };
            CryptoDirectionFallback.ApplyIfEligible(ctx, eval, reason);
            ApplyCryptoSourceTrace(ctx, eval, eval.Direction);
            return eval;
        }

        private static void ApplyCryptoSourceTrace(EntryContext ctx, EntryEvaluation evaluation, TradeDirection candidateDirection)
        {
            if (evaluation == null)
                return;

            var sourceAllowedDirection = ctx?.CryptoHtfAllowedDirection ?? TradeDirection.None;
            evaluation.HtfTraceSourceStage = "SOURCE";
            evaluation.HtfTraceSourceModule = "CRYPTO_ENTRY";
            evaluation.HtfTraceSourceState = ctx?.CryptoHtfReason ?? "N/A";
            evaluation.HtfTraceSourceAllowedDirection = sourceAllowedDirection;
            evaluation.HtfTraceSourceAlign = sourceAllowedDirection == candidateDirection;
            evaluation.HtfTraceSourceCandidateDirection = candidateDirection;
            evaluation.HtfConfidence01 = ctx?.CryptoHtfConfidence01 ?? 0.0;
        }

        private static int ApplyMandatoryEntryAdjustments(EntryContext ctx, TradeDirection direction, int score, bool applyTrendRegimePenalty)
        {
            return EntryDirectionQuality.Apply(
                ctx,
                direction,
                score,
                new DirectionQualityRequest
                {
                    TypeTag = "Crypto_ImpulseEntry",
                    ApplyTrendRegimePenalty = applyTrendRegimePenalty
                });
        }

    }
}
