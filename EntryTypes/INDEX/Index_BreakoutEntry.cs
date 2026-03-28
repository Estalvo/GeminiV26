using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes;
using GeminiV26.Core.Matrix;
using GeminiV26.Instruments.INDEX;
using System;

namespace GeminiV26.EntryTypes.INDEX
{
    public class Index_BreakoutEntry : IEntryType
    {
        public EntryType Type => EntryType.Index_Breakout;

        private const int BaseScore = 50;
        private const int MinScore = EntryDecisionPolicy.MinScoreThreshold;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            DirectionDebug.LogOnce(ctx);
            var matrix = ctx?.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;

            if (!matrix.AllowBreakout)
                return Reject(ctx, "SESSION_MATRIX_ALLOWBREAKOUT_DISABLED", 0, TradeDirection.None);

            if (ctx == null || !ctx.IsReady)
                return Reject(ctx, "CTX_NOT_READY", 0, TradeDirection.None);

            if (ctx.LogicBias == TradeDirection.None)
                return Reject(ctx, "NO_LOGIC_BIAS", 0, TradeDirection.None);

            var p = IndexInstrumentMatrix.Get(ctx.Symbol);

            if (ctx.LogicBias == TradeDirection.Long)
            {
                var eval = EvaluateSide(ctx, p, matrix, TradeDirection.Long);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), eval, null, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }
            else if (ctx.LogicBias == TradeDirection.Short)
            {
                var eval = EvaluateSide(ctx, p, matrix, TradeDirection.Short);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), null, eval, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }

            return Reject(ctx, "NO_LOGIC_BIAS", 0, TradeDirection.None);
        }

        private EntryEvaluation EvaluateSide(
            EntryContext ctx,
            dynamic p,
            SessionMatrixConfig matrix,
            TradeDirection dir)
        {
            int score = BaseScore;
            int setupScore = 0;

            // =============================
            // MATRIX THRESHOLDS
            // =============================
            double minAdxTrend = p.MinAdxTrend > 0 ? p.MinAdxTrend : 20;
            minAdxTrend = Math.Max(minAdxTrend, matrix.MinAdx);
            int maxBarsSinceImpulse = p.MaxBarsSinceImpulse_M5 > 0 ? p.MaxBarsSinceImpulse_M5 : 3;
            double minAtrPoints = p.MinAtrPoints > 0 ? p.MinAtrPoints : 0;

            // =============================
            // HTF SOFT HANDLING
            // =============================
            bool htfStrongMismatch =
                ctx.ResolveAssetHtfConfidence01() >= 0.6 &&
                ctx.ResolveAssetHtfAllowedDirection() != TradeDirection.None &&
                ctx.ResolveAssetHtfAllowedDirection() != dir;

            bool htfWeakMismatch =
                ctx.ResolveAssetHtfConfidence01() >= 0.35 &&
                ctx.ResolveAssetHtfConfidence01() < 0.6 &&
                ctx.ResolveAssetHtfAllowedDirection() != TradeDirection.None &&
                ctx.ResolveAssetHtfAllowedDirection() != dir;

            bool htfAligned =
                ctx.ResolveAssetHtfAllowedDirection() == dir &&
                ctx.ResolveAssetHtfAllowedDirection() != TradeDirection.None;

            if (htfStrongMismatch)
                score -= 18;
            else if (htfWeakMismatch)
                score -= 8;
            else if (htfAligned && ctx.ResolveAssetHtfConfidence01() >= 0.6)
                score += 6;
            else if (htfAligned && ctx.ResolveAssetHtfConfidence01() >= 0.35)
                score += 3;

            // =============================
            // CHOP
            // =============================
            bool chopZone =
                ctx.Adx_M5 < minAdxTrend &&
                Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < 8 &&
                !ctx.IsAtrExpanding_M5;

            if (chopZone)
                score -= 8;

            // =============================
            // ATR GUARD
            // =============================
            if (minAtrPoints > 0 && ctx.AtrM5 < minAtrPoints)
                score -= 6;

            // =============================
            // BREAKOUT CONTEXT FLAGS
            // =============================
            bool earlyBreakout =
                (ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir) ||
                (ctx.RangeBreakDirection == dir);

            bool strongBreakoutNow =
                ctx.HasBreakout_M1 &&
                ctx.BreakoutDirection == dir &&
                ctx.IsAtrExpanding_M5;

            // =============================
            // TREND FATIGUE
            // =============================
            bool adxExhausted = ctx.Adx_M5 > 40 && ctx.AdxSlope_M5 <= 0;
            bool atrContracting = ctx.AtrSlope_M5 <= 0;
            bool diConverging = Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < 7;
            bool impulseStale =
                !ctx.HasImpulse_M5 ||
                ctx.BarsSinceImpulse_M5 > maxBarsSinceImpulse;

            int fatigueCount = 0;
            if (adxExhausted) fatigueCount++;
            if (atrContracting) fatigueCount++;
            if (diConverging) fatigueCount++;
            if (impulseStale) fatigueCount++;

            bool continuationAuthority = HasContinuationAuthority(ctx, dir);
            if (fatigueCount >= 3 && !strongBreakoutNow)
            {
                if (continuationAuthority)
                {
                    score -= 12;
                    ctx.Log?.Invoke(
                        $"[IDX_BREAKOUT][SOFT_PENALTY] reason=IDX_TREND_FATIGUE_ULTRASOUND penalty=12 dir={dir} score={score}");
                }
                else
                {
                    return Reject(ctx, "IDX_TREND_FATIGUE_ULTRASOUND", score, dir);
                }
            }

            if (fatigueCount >= 3 && strongBreakoutNow)
                score -= 6;

            // =============================
            // IMPULSE QUALITY
            // =============================
            bool freshImpulse =
                ctx.HasImpulse_M5 &&
                ctx.BarsSinceImpulse_M5 <= maxBarsSinceImpulse;

            if (!freshImpulse)
                score -= 12;

            if (!ctx.IsAtrExpanding_M5)
                score -= 10;

            if (!ctx.HasImpulse_M5 && !strongBreakoutNow)
                setupScore -= 40;
            else
                setupScore += 15;

            bool hasStructure =
                (dir == TradeDirection.Long ? ctx.HasPullbackLong_M5 : ctx.HasPullbackShort_M5);

            if (hasStructure)
                setupScore += 10;

            // =============================
            // BREAKOUT LOGIC
            // =============================
            bool continuationSignal =
                ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir;

            bool breakoutConfirmed =
                continuationSignal ||
                ctx.RangeBreakDirection == dir;

            bool hasContinuation = breakoutConfirmed;

            if (hasContinuation)
                setupScore += 20;

            if (ctx.BreakoutDirection == dir)
                score += 8;
            else if (ctx.BreakoutDirection != TradeDirection.None)
                score -= 8;

            if (ctx.RangeBreakDirection == dir)
                score += 6;
            else if (ctx.RangeBreakDirection != TradeDirection.None)
                score -= 6;

            // =============================
            // CORE TREND
            // =============================
            if (ctx.Adx_M5 < minAdxTrend)
                score -= 8;

            if (ctx.TrendDirection == dir)
                score += 5;

            if (ctx.HasImpulse_M5)
                score += 5;

            if (ctx.IsAtrExpanding_M5)
                score += 3;

            if (ctx.MarketState?.IsTrend == true)
                score += 5;

            if (ctx.MarketState?.IsLowVol == true)
                score -= 8;

            if (ctx.TrendDirection == dir &&
                ctx.HasImpulse_M5 &&
                ctx.IsAtrExpanding_M5)
            {
                score += 3;
            }

            // =============================
            // PROFILE
            // =============================
            if (p.SessionBias == IndexSessionBias.NewYork)
                score += 1;

            if (p.Volatility == IndexVolatilityClass.Extreme)
                score += 1;

            if (p.PullbackStyle == IndexPullbackStyle.Structure)
                score -= 6;
            else
                score -= 4;

            int lastClosed = ctx.M5.Count - 2;
            if (lastClosed < 0)
                return Reject(ctx, "NO_LAST_BAR", score, dir);

            var bar = ctx.M5[lastClosed];

            // =============================
            // MOMENTUM FILTER
            // =============================
            const double strongBodyRatioThreshold = 0.60;
            const double closeNearExtremeThreshold = 0.20;
            const double velocityAtrThreshold = 0.25;

            double barRange = Math.Max(0, bar.High - bar.Low);
            double barBody = Math.Abs(bar.Close - bar.Open);
            double bodyRatio = barRange > 0 ? barBody / barRange : 0;

            bool closeNearExtreme =
                dir == TradeDirection.Long
                    ? (barRange > 0 && (bar.High - bar.Close) <= barRange * closeNearExtremeThreshold)
                    : (barRange > 0 && (bar.Close - bar.Low) <= barRange * closeNearExtremeThreshold);

            bool breakoutBodyDirectionOk =
                (dir == TradeDirection.Long && bar.Close > bar.Open) ||
                (dir == TradeDirection.Short && bar.Close < bar.Open);

            bool strongBreakoutCandle =
                breakoutBodyDirectionOk &&
                bodyRatio >= strongBodyRatioThreshold &&
                closeNearExtreme;

            bool rangeExpansion = false;
            if (lastClosed >= 3)
            {
                double prevRange1 = Math.Max(0, ctx.M5[lastClosed - 1].High - ctx.M5[lastClosed - 1].Low);
                double prevRange2 = Math.Max(0, ctx.M5[lastClosed - 2].High - ctx.M5[lastClosed - 2].Low);
                double prevRange3 = Math.Max(0, ctx.M5[lastClosed - 3].High - ctx.M5[lastClosed - 3].Low);
                double avgPrevRange = (prevRange1 + prevRange2 + prevRange3) / 3.0;
                rangeExpansion = barRange > avgPrevRange;
            }

            bool immediateVelocity = false;
            if (ctx.AtrM5 > 0 && lastClosed >= 2)
            {
                double velocityNow =
                    dir == TradeDirection.Long
                        ? (ctx.M5[lastClosed].Close - ctx.M5[lastClosed - 1].Close)
                        : (ctx.M5[lastClosed - 1].Close - ctx.M5[lastClosed].Close);

                double velocityPrev =
                    dir == TradeDirection.Long
                        ? (ctx.M5[lastClosed - 1].Close - ctx.M5[lastClosed - 2].Close)
                        : (ctx.M5[lastClosed - 2].Close - ctx.M5[lastClosed - 1].Close);

                double bestVelocity = Math.Max(velocityNow, velocityPrev);
                immediateVelocity = bestVelocity >= (ctx.AtrM5 * velocityAtrThreshold);
            }

            string momentumType = null;
            if (strongBreakoutCandle) momentumType = "body";
            else if (rangeExpansion) momentumType = "range";
            else if (immediateVelocity) momentumType = "velocity";

            if (momentumType == null)
                return Reject(ctx, "IDX_NO_MOMENTUM_DEAD_BREAKOUT", score, dir);

            if (strongBreakoutCandle && ctx.IsAtrExpanding_M5)
                score += 12;

            bool strongCandle =
                (dir == TradeDirection.Long && bar.Close > bar.Open) ||
                (dir == TradeDirection.Short && bar.Close < bar.Open);

            bool followThrough =
                continuationSignal ||
                (ctx.IsAtrExpanding_M5 && freshImpulse);

            score = TriggerScoreModel.Apply(
                ctx,
                $"IDX_BREAKOUT_{dir}",
                score,
                breakoutConfirmed,
                strongCandle,
                followThrough,
                "NO_BREAKOUT_M1");

            score = ApplyMandatoryEntryAdjustments(ctx, dir, score, false);

            score += (int)Math.Round(matrix.EntryScoreModifier);
            score += setupScore;

            if (setupScore <= 0 && !earlyBreakout && !strongBreakoutNow)
                score = Math.Min(score, MinScore - 10);

            if (score < MinScore)
                return Reject(ctx, $"LOW_SCORE({score})", score, dir);

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason =
                    $"IDX_BREAKOUT dir={dir} score={score} " +
                    $"trendAlign={(ctx.TrendDirection == dir)} " +
                    $"impulse={ctx.HasImpulse_M5} " +
                    $"atrExp={ctx.IsAtrExpanding_M5} " +
                    $"adx={ctx.Adx_M5:F1}"
            };
        }

        private static EntryEvaluation Reject(
            EntryContext ctx,
            string reason,
            int score,
            TradeDirection dir)
        {
            Console.WriteLine(
                $"[IDX_BREAKOUT][REJECT] {reason} | score={score} | dir={dir}");

            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.Index_Breakout,
                Direction = dir,
                IsValid = false,
                Score = Math.Max(0, score),
                Reason = reason
            };
        }

        private static int ApplyMandatoryEntryAdjustments(
            EntryContext ctx,
            TradeDirection direction,
            int score,
            bool applyTrendRegimePenalty)
        {
            return EntryDirectionQuality.Apply(
                ctx,
                direction,
                score,
                new DirectionQualityRequest
                {
                    TypeTag = "Index_BreakoutEntry",
                    ApplyTrendRegimePenalty = applyTrendRegimePenalty
                });
        }

        private static bool HasContinuationAuthority(EntryContext ctx, TradeDirection dir)
        {
            if (ctx == null || dir == TradeDirection.None)
                return false;

            return
                ctx.TrendDirection == dir &&
                ctx.HasImpulse_M5 &&
                ctx.IsAtrExpanding_M5 &&
                ctx.MarketState?.IsTrend == true;
        }
    }
}
