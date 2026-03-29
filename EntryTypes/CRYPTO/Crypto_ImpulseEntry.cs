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

            double htfConf = ctx.ResolveAssetHtfConfidence01();
            var htfDir = ctx.ResolveAssetHtfAllowedDirection();
            bool htfMismatch =
                htfConf >= 0.6 &&
                htfDir != TradeDirection.None &&
                logicBiasDirection != TradeDirection.None &&
                htfDir != logicBiasDirection;

            if (htfMismatch)
            {
                ctx.Log?.Invoke(
                    $"[CRYPTO][HTF_SOFT] mismatch allowed | dir={logicBiasDirection} htf={htfDir} conf={htfConf:0.00}");
            }

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
            int baseScore;
            int scoreAfterRegime;
            int scoreAfterHtf;

            TradeDirection impulseDirection = ctx.ImpulseDirection;
            if (impulseDirection == TradeDirection.None)
                impulseDirection = dir;
            int barsSinceImpulse = Math.Max(0, ctx.BarsSinceImpulse_M5);
            bool isSameDirection = (dir == impulseDirection);
            bool shouldBlock = (barsSinceImpulse < 1) && !isSameDirection;
            ctx.Log?.Invoke(
                $"[CRYPTO][IMPULSE_GATE] entryType=Impulse barsSinceImpulse={barsSinceImpulse} impulseDir={impulseDirection} entryDir={dir} sameDir={isSameDirection.ToString().ToLowerInvariant()} blocked={shouldBlock.ToString().ToLowerInvariant()}");
            if (shouldBlock)
                return Invalid(ctx, "IMPULSE_LOCK_IMMEDIATE_COUNTER", dir, score);

            if (!ctx.IsVolatilityAcceptable_Crypto)
            {
                score -= 8;
                ctx.Log?.Invoke(
                    "[CRYPTO][STRUCT_FILTER] entryType=Impulse reason=LOW_CRYPTO_VOL blocked=false");
            }

            bool directionalImpulse = ctx.ImpulseDirection == dir;
            bool directionalTrend = ctx.TrendDirection == dir;

            if (!directionalImpulse && !directionalTrend &&
                (ctx.ImpulseDirection != TradeDirection.None || ctx.TrendDirection != TradeDirection.None))
            {
                score -= 10;
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
            baseScore = score;

            bool trendRegime =
                ctx.MarketState?.IsTrend == true ||
                (!ctx.IsRange_M5 && ctx.Adx_M5 >= 18.0);
            string regime = trendRegime ? "Trend" : "NonTrend";
            bool regimeMismatch = !trendRegime;
            int regimeDelta = regimeMismatch ? -25 : +5;
            score += regimeDelta;
            scoreAfterRegime = score;
            ctx.Log?.Invoke(
                $"[CRYPTO][REGIME_ADJUST] entryType=Impulse regime={regime} delta={regimeDelta} scoreAfter={scoreAfterRegime}");

            var htfDirection = ctx.CryptoHtfAllowedDirection;
            int htfDelta = htfDirection == dir ? +8 : -10;
            score += htfDelta;
            scoreAfterHtf = score;
            ctx.Log?.Invoke(
                $"[CRYPTO][HTF_SCORE] entryType=Impulse entryDir={dir} htfDir={htfDirection} delta={htfDelta} scoreAfter={scoreAfterHtf}");

            ctx.Log?.Invoke(
                $"[CRYPTO][FINAL_SCORE] entryType=Impulse baseScore={baseScore} afterRegime={scoreAfterRegime} afterHtf={scoreAfterHtf} finalScore={score}");

            if (score < MinScore)
                return Invalid(ctx, $"LOW_SCORE_{dir}_{score}", dir, score);

            double htfConf = ctx.ResolveAssetHtfConfidence01();
            var htfDir = ctx.ResolveAssetHtfAllowedDirection();
            bool htfMismatch =
                htfConf >= 0.6 &&
                htfDir != TradeDirection.None &&
                dir != TradeDirection.None &&
                htfDir != dir;
            ctx.Log?.Invoke(
                $"[CRYPTO][ENTRY_FINAL] dir={dir} score={score} htfMismatch={htfMismatch}");

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

    }
}
