using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes;
using GeminiV26.Core;

namespace GeminiV26.EntryTypes.Crypto
{
    public class BTC_RangeBreakoutEntry : IEntryType
    {
        public EntryType Type => EntryType.Crypto_RangeBreakout;
        private const int MIN_SCORE = EntryDecisionPolicy.MinScoreThreshold;
        private const int MIN_RANGE_BARS = 15;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            DirectionDebug.LogOnce(ctx);
            if (!ctx.IsReady)
                return Invalid(ctx, "CTX_NOT_READY");

            if (ctx.LogicBias == TradeDirection.None)
                return Invalid(ctx, "NO_LOGIC_BIAS", TradeDirection.None, 0);

            var crypto = CryptoInstrumentMatrix.Get(ctx.Symbol);

            if (!crypto.AllowRangeBreakout)
                return Invalid(ctx, "DISABLED");

            if (!ctx.IsRange_M5 || ctx.RangeBarCount_M5 < MIN_RANGE_BARS)
                return Invalid(ctx, "NO_RANGE");

            if (ctx.HtfConfidence >= 0.6 && ctx.HtfDirection != ctx.LogicBias)
                return Invalid(ctx, "HTF_MISMATCH", TradeDirection.None, 0);

            if (ctx.LogicBias == TradeDirection.Long)
            {
                var eval = EvaluateSide(ctx, TradeDirection.Long);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), eval, null, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }
            else if (ctx.LogicBias == TradeDirection.Short)
            {
                var eval = EvaluateSide(ctx, TradeDirection.Short);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), null, eval, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }

            return Invalid(ctx, "NO_LOGIC_BIAS", TradeDirection.None, 0);
        }
        private EntryEvaluation EvaluateSide(EntryContext ctx, TradeDirection dir)
        {
            int score = 25;
            int setupScore = 0;

            if (!ctx.IsVolatilityAcceptable_Crypto)
                score -= 15;

            var eval = NewEval(ctx, dir);

            bool hasVolatility =
                ctx.IsAtrExpanding_M5;

            if (!hasVolatility)
                setupScore -= 30;

            bool hasFlag =
                dir == TradeDirection.Long ? ctx.HasFlagLong_M5 :
                dir == TradeDirection.Short ? ctx.HasFlagShort_M5 :
                ctx.IsValidFlagStructure_M5;

            bool structuredPB =
                ctx.PullbackBars_M5 >= 2 &&
                ctx.IsPullbackDecelerating_M5;

            bool hasStructure =
                hasFlag || structuredPB;

            if (!hasStructure)
                setupScore -= 30;
            else
                setupScore += 15;

            bool continuationSignal =
                ctx.RangeBreakDirection == dir;

            bool hasMomentum =
                continuationSignal;

            if (hasMomentum)
                setupScore += 20;

            // =========================
            // BREAK STRENGTH
            // =========================
            if (ctx.RangeBreakAtrSize_M5 > 1.2)
                return Invalid(ctx, "OVEREXTENDED_BREAK");

            score += 10;

            // =========================
            // FAKEOUT CHECK
            // =========================
            if (ctx.RangeFakeoutBars_M1 <= 1)
                score += 10;
            else
                score -= 15;

            // =========================
            // M1 CONFIRMATION (BONUS ONLY)
            // =========================
            if (ctx.M1TriggerInTrendDirection)
                score += 6;

            // =========================
            // ATR BEHAVIOUR
            // =========================
            if (ctx.IsAtrExpanding_M5)
                score += 10;

            if (ctx.RangeBreakDirection != dir && ctx.RangeBreakDirection != TradeDirection.None)
                score -= 12;

            bool breakoutDetected = ctx.RangeBreakDirection == dir;
            int lastClosed = ctx.M5.Count - 2;
            var bar = ctx.M5[lastClosed];
            bool strongCandle =
                (dir == TradeDirection.Long && bar.Close > bar.Open) ||
                (dir == TradeDirection.Short && bar.Close < bar.Open);
            bool followThrough = ctx.M1TriggerInTrendDirection || (ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir);
            score = TriggerScoreModel.Apply(ctx, $"BTC_RANGE_BREAKOUT_{dir}", score, breakoutDetected, strongCandle, followThrough, "NO_RANGE_BREAK_TRIGGER");

            score = ApplyMandatoryEntryAdjustments(ctx, dir, score, false);
            score += setupScore;

            if (setupScore <= 0)
                score = System.Math.Min(score, MIN_SCORE - 10);

            eval.Score = score;
            eval.IsValid = score >= MIN_SCORE;

            if (!eval.IsValid)
                eval.Reason += $"LowScore({score});";

            return eval;
        }

        private static EntryEvaluation NewEval(EntryContext ctx, TradeDirection dir)
            => new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.Crypto_RangeBreakout,
                Direction = dir
            };

        private static EntryEvaluation Invalid(EntryContext ctx, string reason)
            => new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.Crypto_RangeBreakout,
                Direction = TradeDirection.None,
                IsValid = false,
                Reason = reason + ";"
            };

        private static EntryEvaluation Invalid(EntryContext ctx, string reason, TradeDirection dir, int score)
            => new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.Crypto_RangeBreakout,
                Direction = dir,
                Score = score,
                IsValid = false,
                Reason = reason + ";"
            };

        private static int ApplyMandatoryEntryAdjustments(EntryContext ctx, TradeDirection direction, int score, bool applyTrendRegimePenalty)
        {
            return EntryDirectionQuality.Apply(
                ctx,
                direction,
                score,
                new DirectionQualityRequest
                {
                    TypeTag = "BTC_RangeBreakoutEntry",
                    ApplyTrendRegimePenalty = applyTrendRegimePenalty
                });
        }

    }
}
