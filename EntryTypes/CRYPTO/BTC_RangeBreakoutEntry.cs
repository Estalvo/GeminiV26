using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes;
using GeminiV26.Core;

namespace GeminiV26.EntryTypes.Crypto
{
    public class BTC_RangeBreakoutEntry : IEntryType
    {
        public EntryType Type => EntryType.Crypto_RangeBreakout;
        private const int MIN_SCORE = EntryDecisionPolicy.MinScoreThreshold;
        private const int MIN_RANGE_BARS = 10;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            DirectionDebug.LogOnce(ctx);
            if (!ctx.IsReady)
                return Invalid(ctx, "CTX_NOT_READY");

            TradeDirection logicBiasDirection = ctx.LogicBiasDirection;
            if (logicBiasDirection == TradeDirection.None)
                return Invalid(ctx, "NO_LOGIC_BIAS", TradeDirection.None, 0);

            var crypto = CryptoInstrumentMatrix.Get(ctx.Symbol);

            if (!crypto.AllowRangeBreakout)
                return Invalid(ctx, "DISABLED");

            if (!ctx.IsRange_M5 || ctx.RangeBarCount_M5 < MIN_RANGE_BARS)
            {
                ctx.Log?.Invoke(
                    "[CRYPTO][STRUCT_FILTER] entryType=Breakout reason=NO_RANGE blocked=true");
                return Invalid(ctx, "NO_RANGE");
            }

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
            int score = 25;
            int setupScore = 0;
            int baseScore;
            int scoreAfterRegime;
            int scoreAfterHtf;

            TradeDirection impulseDirection = ctx.ImpulseDirection;
            if (impulseDirection == TradeDirection.None)
                impulseDirection = dir;
            int barsSinceImpulse = System.Math.Max(0, ctx.BarsSinceImpulse_M5);
            bool isSameDirection = (dir == impulseDirection);
            bool shouldBlock = (barsSinceImpulse < 1) && !isSameDirection;
            ctx.Log?.Invoke(
                $"[CRYPTO][IMPULSE_GATE] entryType=Breakout barsSinceImpulse={barsSinceImpulse} impulseDir={impulseDirection} entryDir={dir} sameDir={isSameDirection.ToString().ToLowerInvariant()} blocked={shouldBlock.ToString().ToLowerInvariant()}");
            if (shouldBlock)
                return Invalid(ctx, "IMPULSE_LOCK_IMMEDIATE_COUNTER", dir, score);

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
                ctx.PullbackBars_M5 >= 1 &&
                ctx.IsPullbackDecelerating_M5;

            bool hasStructure =
                hasFlag || structuredPB;

            if (!hasStructure)
            {
                setupScore -= 30;
                ctx.Log?.Invoke(
                    "[CRYPTO][STRUCT_FILTER] entryType=Breakout reason=NO_FLAG_OR_STRUCTURED_PULLBACK blocked=false");
            }
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
            if (ctx.RangeBreakAtrSize_M5 > 1.4)
                return Invalid(ctx, "OVEREXTENDED_BREAK", dir, 0);

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

            score += setupScore;
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
                $"[CRYPTO][REGIME_ADJUST] entryType=Breakout regime={regime} delta={regimeDelta} scoreAfter={scoreAfterRegime}");

            var htfDirection = ctx.ActiveHtfDirection;
            int htfDelta = htfDirection == dir ? +8 : -10;
            score += htfDelta;
            scoreAfterHtf = score;
            ctx.Log?.Invoke(
                $"[CRYPTO][HTF_SCORE] entryType=Breakout entryDir={dir} htfDir={htfDirection} delta={htfDelta} scoreAfter={scoreAfterHtf}");

            ctx.Log?.Invoke(
                $"[CRYPTO][FINAL_SCORE] entryType=Breakout baseScore={baseScore} afterRegime={scoreAfterRegime} afterHtf={scoreAfterHtf} finalScore={score}");

            if (setupScore <= 0)
                score = System.Math.Min(score, MIN_SCORE - 10);

            eval.Score = score;
            eval.IsValid = score >= MIN_SCORE;

            if (!eval.IsValid)
                eval.Reason += $"LowScore({score});";
            else
            {
                double htfConf = ctx.ResolveAssetHtfConfidence01();
                var htfDir = ctx.ResolveAssetHtfAllowedDirection();
                bool htfMismatch =
                    htfConf >= 0.6 &&
                    htfDir != TradeDirection.None &&
                    dir != TradeDirection.None &&
                    htfDir != dir;
                ctx.Log?.Invoke(
                    $"[CRYPTO][ENTRY_FINAL] dir={dir} score={score} htfMismatch={htfMismatch}");
            }

            return eval;
        }

        private static EntryEvaluation NewEval(EntryContext ctx, TradeDirection dir)
        {
            var eval = new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.Crypto_RangeBreakout,
                Direction = dir
            };
            ApplyCryptoSourceTrace(ctx, eval, dir);
            return eval;
        }

        private static EntryEvaluation Invalid(EntryContext ctx, string reason)
            => Invalid(ctx, reason, TradeDirection.None, 0);

        private static EntryEvaluation Invalid(EntryContext ctx, string reason, TradeDirection dir, int score)
        {
            var eval = new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.Crypto_RangeBreakout,
                Direction = dir,
                Score = score,
                IsValid = false,
                Reason = reason + ";"
            };
            CryptoDirectionFallback.ApplyIfEligible(ctx, eval, reason);
            ApplyCryptoSourceTrace(ctx, eval, eval.Direction);
            return eval;
        }

        private static void ApplyCryptoSourceTrace(EntryContext ctx, EntryEvaluation evaluation, TradeDirection candidateDirection)
        {
            if (evaluation == null)
                return;

            var sourceAllowedDirection = ctx?.ActiveHtfDirection ?? TradeDirection.None;
            evaluation.HtfTraceSourceStage = "SOURCE";
            evaluation.HtfTraceSourceModule = "CRYPTO_ENTRY";
            evaluation.HtfTraceSourceState = "N/A";
            evaluation.HtfTraceSourceAllowedDirection = sourceAllowedDirection;
            evaluation.HtfTraceSourceAlign = sourceAllowedDirection == candidateDirection;
            evaluation.HtfTraceSourceCandidateDirection = candidateDirection;
            evaluation.HtfConfidence01 = ctx?.ActiveHtfConfidence ?? 0.0;
        }

    }
}
