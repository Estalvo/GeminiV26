using GeminiV26.Core.Entry;
using GeminiV26.Core;
using GeminiV26.Core.Matrix;
using GeminiV26.Instruments.INDEX;
using System;

namespace GeminiV26.EntryTypes.INDEX
{
    public class Index_PullbackEntry : IEntryType
    {
        public EntryType Type => EntryType.Index_Pullback;

        private const int BaseScore = 60;
        private const int MinScore = EntryDecisionPolicy.MinScoreThreshold;

        private const double MaxPullbackDepthAtr = 0.9;
        private const int MaxPullbackBars = 5;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            var matrix = ctx?.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;
            if (!matrix.AllowPullback)
                return Reject(ctx, TradeDirection.None, 0, "SESSION_MATRIX_ALLOWPULLBACK_DISABLED");

            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 10)
                return Reject(ctx, TradeDirection.None, 0, "CTX_NOT_READY");

            var p = IndexInstrumentMatrix.Get(ctx.Symbol);
            if (p == null)
                return Reject(ctx, TradeDirection.None, 0, "NO_INDEX_PROFILE");

            var longEval = EvaluateSide(ctx, p, matrix, TradeDirection.Long);
            var shortEval = EvaluateSide(ctx, p, matrix, TradeDirection.Short);

            if (EntryDecisionPolicy.IsHardInvalid(longEval) && EntryDecisionPolicy.IsHardInvalid(shortEval))
                return Reject(ctx, TradeDirection.None, Math.Max(longEval?.Score ?? 0, shortEval?.Score ?? 0), "IDX_PULLBACK_NO_SIDE");

            return EntryDecisionPolicy.Normalize(EntryDecisionPolicy.SelectBalancedEvaluation(ctx, Type, longEval, shortEval));
        }

        private EntryEvaluation EvaluateSide(
            EntryContext ctx,
            dynamic p,
            SessionMatrixConfig matrix,
            TradeDirection dir)
        {
            // =============================
            // MATRIX DRIVEN THRESHOLDS
            // =============================
            int setupScore = 0;
            double minAdxTrend = p.MinAdxTrend > 0 ? p.MinAdxTrend : 20;
            minAdxTrend = Math.Max(minAdxTrend, matrix.MinAdx);
            int maxBarsSinceImpulse = p.MaxBarsSinceImpulse_M5 > 0 ? p.MaxBarsSinceImpulse_M5 : 4;
            double minAtrPoints = p.MinAtrPoints > 0 ? p.MinAtrPoints : 0;

            double maxPullbackDepthAtr =
                p.PullbackStyle == IndexPullbackStyle.Shallow ? 0.7 :
                p.PullbackStyle == IndexPullbackStyle.Structure ? 1.0 :
                MaxPullbackDepthAtr;

            int maxPullbackBars =
                p.PullbackStyle == IndexPullbackStyle.Shallow ? 3 :
                p.PullbackStyle == IndexPullbackStyle.Structure ? 6 :
                MaxPullbackBars;

            int score = BaseScore;

            var bars = ctx.M5;
            int lastClosed = bars.Count - 2;

            bool hasImpulse =
                dir == TradeDirection.Long ? ctx.HasImpulseLong_M5 :
                dir == TradeDirection.Short ? ctx.HasImpulseShort_M5 :
                false;

            double pullbackDepthAtr =
                dir == TradeDirection.Long ? ctx.PullbackDepthRLong_M5 :
                dir == TradeDirection.Short ? ctx.PullbackDepthRShort_M5 :
                ctx.PullbackDepthAtr_M5;

            bool hasFlag =
                dir == TradeDirection.Long ? ctx.HasFlagLong_M5 :
                dir == TradeDirection.Short ? ctx.HasFlagShort_M5 :
                ctx.IsValidFlagStructure_M5;

            bool lastBarInDir =
                (dir == TradeDirection.Long && bars[lastClosed].Close > bars[lastClosed].Open) ||
                (dir == TradeDirection.Short && bars[lastClosed].Close < bars[lastClosed].Open);

            bool m1TriggerInDir = HasDirectionalM1Trigger(ctx, dir);
            bool continuationSignal = m1TriggerInDir;
            bool breakoutConfirmed = m1TriggerInDir;

            bool hasImpulseSetup = ctx.HasImpulse_M5;

            if (!hasImpulseSetup)
                setupScore -= 40;
            else
                setupScore += 15;

            bool hasStructure =
                (dir == TradeDirection.Long ? ctx.HasPullbackLong_M5 : ctx.HasPullbackShort_M5) ||
                hasFlag;

            if (hasStructure)
                setupScore += 10;

            bool hasContinuation =
                continuationSignal || breakoutConfirmed;

            if (hasContinuation)
                setupScore += 20;

            // =====================================================
            // CHOP SOFT (matrix ADX)
            // =====================================================
            bool chopZone =
                ctx.Adx_M5 < minAdxTrend &&
                Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < 7 &&
                !ctx.IsAtrExpanding_M5;

            if (chopZone)
                score -= 6;

            // =====================================================
            // ATR sanity (matrix driven)
            // =====================================================
            if (minAtrPoints > 0 && ctx.AtrM5 < minAtrPoints)
                score -= 6;

            // =====================================================
            // TREND FATIGUE → SOFT
            // =====================================================
            bool adxExhausted =
                ctx.Adx_M5 > 45 &&
                ctx.AdxSlope_M5 <= 0;

            bool atrContracting =
                ctx.AtrSlope_M5 <= 0;

            bool diConverging =
                Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < 7;

            bool impulseStale =
                !hasImpulse ||
                ctx.BarsSinceImpulse_M5 > maxBarsSinceImpulse;

            int fatigueCount = 0;
            if (adxExhausted) fatigueCount++;
            if (atrContracting) fatigueCount++;
            if (diConverging) fatigueCount++;
            if (impulseStale) fatigueCount++;

            bool trendFatigue = fatigueCount >= 3;

            if (trendFatigue)
                score -= 12;

            // =====================================================
            // PULLBACK STRUCTURAL GATES
            // =====================================================

            if (ctx.IsAtrExpanding_M5 && pullbackDepthAtr > 0.6)
                score -= 6;

            if (pullbackDepthAtr > 0.5)
            {
                int compressionBars = Math.Max(0, Math.Min(ctx.PullbackBars_M5, 10));
                int compressionStart = Math.Max(0, lastClosed - compressionBars + 1);

                double compressionHigh = double.MinValue;
                double compressionLow = double.MaxValue;

                for (int i = compressionStart; i <= lastClosed; i++)
                {
                    compressionHigh = Math.Max(compressionHigh, bars[i].High);
                    compressionLow = Math.Min(compressionLow, bars[i].Low);
                }

                double compressionRange = compressionHigh - compressionLow;
                double atr = Math.Max(0, ctx.AtrM5);

                bool compressionDetected =
                    compressionBars >= 3 &&
                    compressionBars <= 10 &&
                    compressionRange <= atr * 0.6;

                if (!compressionDetected)
                {
                    ctx.Log?.Invoke($"[PB] dir={dir} rejected: deep pullback without compression");
                    score -= 10;
                }

                TradeDirection impulseDirection =
                    ctx.ImpulseDirection != TradeDirection.None ? ctx.ImpulseDirection : dir;

                bool breakoutAligned =
                    (impulseDirection == TradeDirection.Long && bars[lastClosed].Close > compressionHigh) ||
                    (impulseDirection == TradeDirection.Short && bars[lastClosed].Close < compressionLow);

                if (!breakoutAligned)
                {
                    ctx.Log?.Invoke($"[PB] dir={dir} rejected: breakout against impulse");
                    score -= 10;
                }

                ctx.Log?.Invoke($"[PB] dir={dir} DeepPullbackContinuation accepted");
            }

            if (pullbackDepthAtr <= 0 ||
                pullbackDepthAtr > maxPullbackDepthAtr)
                return Reject(ctx, dir, score, "PULLBACK_DEPTH_INVALID");

            if (ctx.PullbackBars_M5 > maxPullbackBars)
                return Reject(ctx, dir, score, "PULLBACK_BARS_TOO_LONG");

            if (!ctx.HasReactionCandle_M5)
                score -= 8;

            if (!lastBarInDir)
                score -= 10;

            double distFromEma =
                Math.Abs(ctx.M5.Last(1).Close - ctx.Ema21_M5);

            if (distFromEma > ctx.AtrM5 * 1.2)
                score -= 8;

            // =====================================================
            // CONTEXT BONUSES
            // =====================================================

            if (m1TriggerInDir)
                score += 10;

            if (ctx.MarketState?.IsTrend == true)
                score += 6;

            if (hasImpulse &&
                ctx.BarsSinceImpulse_M5 <= 2 &&
                pullbackDepthAtr < 0.6)
            {
                score += 8;
            }

            if (ctx.IsPullbackDecelerating_M5)
                score += 5;

            // =====================================================
            // VOL REGIME SOFT
            // =====================================================
            if (ctx.MarketState?.IsLowVol == true)
                score -= 12;

            // =====================================================
            // FLAG PRIORITY
            // =====================================================
            if (hasFlag &&
                m1TriggerInDir)
                score -= 12;

            // =====================================================
            // FINAL SCORE GATE
            // =====================================================
            bool breakoutDetected = breakoutConfirmed || ctx.RangeBreakDirection == dir;
            bool strongCandle = lastBarInDir;
            bool followThrough = continuationSignal || ctx.HasReactionCandle_M5;
            score = ApplyMandatoryEntryAdjustments(ctx, dir, score, true);
            score = TriggerScoreModel.Apply(ctx, $"IDX_PULLBACK_{dir}", score, breakoutDetected, strongCandle, followThrough, "NO_PULLBACK_TRIGGER");
            score += setupScore;

            if (setupScore <= 0)
                score = Math.Min(score, MinScore - 10);

            score += (int)Math.Round(matrix.EntryScoreModifier);

            if (score < MinScore)
            {
                Console.WriteLine(
                    $"[IDX_PULLBACK][REJECT] LOW_SCORE({score}) | " +
                    $"dir={dir} | pbATR={pullbackDepthAtr:F2} | " +
                    $"pbBars={ctx.PullbackBars_M5} | fatigue={trendFatigue} | " +
                    $"ADX={ctx.Adx_M5:F1}"
                );
                return Reject(ctx, dir, score, "LOW_SCORE");
            }

            Console.WriteLine(
                $"[IDX_PULLBACK][PASS] dir={dir} score={score} | " +
                $"pbATR={pullbackDepthAtr:F2} | pbBars={ctx.PullbackBars_M5} | " +
                $"fatigue={trendFatigue} | ADX={ctx.Adx_M5:F1}"
            );

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason =
                    $"IDX_PULLBACK_4.1 dir={dir} score={score} " +
                    $"pbATR={pullbackDepthAtr:F2} pbBars={ctx.PullbackBars_M5} fatigue={trendFatigue}"
            };
        }


        private static EntryEvaluation Reject(EntryContext ctx, TradeDirection dir, int score, string reason)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.Index_Pullback,
                Direction = dir,
                Score = Math.Max(0, score),
                IsValid = false,
                Reason = reason
            };
        }

        private static bool HasDirectionalM1Trigger(EntryContext ctx, TradeDirection dir)
        {
            if (ctx == null || ctx.M1 == null || ctx.M1.Count < 3)
                return false;

            int lastClosed = ctx.M1.Count - 2;
            int prevClosed = ctx.M1.Count - 3;

            var last = ctx.M1[lastClosed];
            var prev = ctx.M1[prevClosed];

            if (dir == TradeDirection.Long)
                return last.Close > last.Open && last.Close > prev.High;

            if (dir == TradeDirection.Short)
                return last.Close < last.Open && last.Close < prev.Low;

            return false;
        }

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
