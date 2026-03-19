using GeminiV26.Core.Entry;
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

            var longEval = EvaluateSide(ctx, TradeDirection.Long);
            var shortEval = EvaluateSide(ctx, TradeDirection.Short);

            return EntryDecisionPolicy.Normalize(EntryDecisionPolicy.SelectBalancedEvaluation(ctx, Type, longEval, shortEval));
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
