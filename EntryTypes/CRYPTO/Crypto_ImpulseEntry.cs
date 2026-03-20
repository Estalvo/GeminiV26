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
            if (ctx == null || !ctx.IsReady)
                return Invalid(ctx, "CTX_NOT_READY");

            if (!ctx.IsVolatilityAcceptable_Crypto)
                return Invalid(ctx, "CRYPTO_VOL_DISABLED");

            bool allowLong = true;
            bool allowShort = true;

            if (ctx.LogicBias != TradeDirection.None && ctx.LogicConfidence >= 60)
            {
                allowLong = ctx.LogicBias == TradeDirection.Long;
                allowShort = ctx.LogicBias == TradeDirection.Short;
            }

            if (ctx.HtfConfidence >= 0.6)
            {
                allowLong = allowLong && ctx.HtfDirection == TradeDirection.Long;
                allowShort = allowShort && ctx.HtfDirection == TradeDirection.Short;
            }

            if (!allowLong && !allowShort)
                return Invalid(ctx, "NO_DIRECTIONAL_EDGE", TradeDirection.None, 0);

            EntryEvaluation longEval;
            EntryEvaluation shortEval;

            if (allowLong)
                longEval = EvaluateSide(ctx, TradeDirection.Long);
            else
                longEval = Invalid(ctx, "DIR_BLOCKED", TradeDirection.Long, 0);

            if (allowShort)
                shortEval = EvaluateSide(ctx, TradeDirection.Short);
            else
                shortEval = Invalid(ctx, "DIR_BLOCKED", TradeDirection.Short, 0);

            var selected = EntryDecisionPolicy.SelectBalancedEvaluation(ctx, Type, longEval, shortEval);
            EntryDirectionQuality.LogDecision(ctx, Type.ToString(), longEval, shortEval, selected.Direction);
            return EntryDecisionPolicy.Normalize(selected);
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

            score = ApplyMandatoryEntryAdjustments(ctx, dir, score, true);
            score = TriggerScoreModel.Apply(ctx, $"CRYPTO_IMPULSE_{dir}", score, breakoutDetected, strongCandle, followThrough, "NO_IMPULSE_TRIGGER");

            if (score < MinScore)
                return Invalid(ctx, $"LOW_SCORE_{dir}_{score}", dir, score);

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason = $"CRYPTO_IMPULSE dir={dir} score={score}"
            };
        }

        private EntryEvaluation Invalid(EntryContext ctx, string reason)
            => Invalid(ctx, reason, TradeDirection.None, 0);

        private EntryEvaluation Invalid(EntryContext ctx, string reason, TradeDirection dir, int score)
            => new EntryEvaluation
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
                    TypeTag = "Crypto_ImpulseEntry",
                    ApplyTrendRegimePenalty = applyTrendRegimePenalty
                });
        }

    }
}
