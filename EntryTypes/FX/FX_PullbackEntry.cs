using System;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Matrix;
using GeminiV26.Instruments.FX;

namespace GeminiV26.EntryTypes.FX
{
    /// <summary>
    /// FX Pullback entry – tightened validity gates + FlagEntry-style regulation.
    /// Goal: stop "forcing" low-quality losers by hard-blocking weak market states,
    /// while still letting the router rank between valid candidates.
    /// </summary>
    public class FX_PullbackEntry : IEntryType
    {
        public EntryType Type => EntryType.FX_Pullback;

        private const int MIN_SCORE = 35;
        private const int ATR_REL_LOOKBACK = 20;
        private const double ATR_REL_EXPANSION_FACTOR = 0.85;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady)
                return Block(ctx, TradeDirection.None, "CTX_NOT_READY", 0);

            var fx = FxInstrumentMatrix.Get(ctx.Symbol);
            if (fx == null)
                return Block(ctx, TradeDirection.None, "NO_FX_PROFILE", 0);

            if (!fx.FlagTuning[FxSession.Asia].AtrExpansionHardBlock)
                ctx?.Log?.Invoke("[MATRIX POLICY] ATR expansion hard block disabled");

            if (!fx.FlagTuning[FxSession.Asia].RequireStrongEntryCandle)
                ctx?.Log?.Invoke("[MATRIX POLICY] strong entry candle requirement disabled");

            var matrix = ctx.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;
            if (!matrix.AllowPullback)
                return Block(ctx, TradeDirection.None, "SESSION_MATRIX_PULLBACK_DISABLED", 0);

            var longEval = EvaluateSide(ctx, fx, matrix, TradeDirection.Long);
            var shortEval = EvaluateSide(ctx, fx, matrix, TradeDirection.Short);

            bool longValid = longEval.IsValid;
            bool shortValid = shortEval.IsValid;

            // FIX #1:
            // Ha mindkét oldal invalid, NEM gyártunk fallbackből tradelhető setupot.
            // Diagnosztikai score marad, de no direction + invalid.
            if (!longValid && !shortValid)
            {
                int bestScore = Math.Max(longEval.Score, shortEval.Score);
                ctx?.Log?.Invoke($"[FX_PullbackEntry] BOTH_INVALID long={longEval.Score} short={shortEval.Score}");

                return new EntryEvaluation
                {
                    Symbol = ctx?.Symbol,
                    Type = EntryType.FX_Pullback,
                    Direction = TradeDirection.None,
                    Score = bestScore,
                    IsValid = false,
                    Reason = "PB_BOTH_INVALID"
                };
            }

            if (longValid && shortValid)
            {
                if (ctx.FxHtfAllowedDirection == TradeDirection.Long)
                    longEval.Score += 3;

                if (ctx.FxHtfAllowedDirection == TradeDirection.Short)
                    shortEval.Score += 3;

                var winner = longEval.Score >= shortEval.Score ? longEval : shortEval;
                return winner;
            }

            return longValid ? longEval : shortEval;
        }

        private EntryEvaluation EvaluateSide(
            EntryContext ctx,
            dynamic fx,
            SessionMatrixConfig matrix,
            TradeDirection dir)
        {
            int score = 60;
            int penalty = 0;
            int penaltyBudget = 14;

            if (ctx == null || !ctx.IsReady)
                return Block(ctx, dir, "CTX_NOT_READY", score);

            double atrAvg20 = ComputeAtrAverage(ctx, ATR_REL_LOOKBACK);
            if (atrAvg20 <= 0)
                return Block(ctx, dir, "SESSION_MATRIX_ATR_AVG_UNAVAILABLE", score);

            double atrRelativeThreshold = atrAvg20 * ATR_REL_EXPANSION_FACTOR;
            bool atrRelativePass = ctx.AtrM5 >= atrRelativeThreshold;
            ctx?.Log?.Invoke(
                $"[FX_PB ATR] dir={dir} atr={ctx.AtrM5:G6} avg20={atrAvg20:G6} thr={atrRelativeThreshold:G6} pass={atrRelativePass}");

            if (!atrRelativePass)
            {
                ApplyPenalty(ref score, ref penalty, 6, penaltyBudget, ctx, "ATR_CONTRACTION");
                ctx?.Log?.Invoke(
                    $"[PB FILTER] dir={dir} ATR contraction penalty | ATR={ctx.AtrM5:F2} threshold={atrRelativeThreshold:F2}");
            }

            if (matrix.MinEmaDistance > 0 && System.Math.Abs(ctx.Ema8_M5 - ctx.Ema21_M5) < matrix.MinEmaDistance)
                return Block(ctx, dir, "SESSION_MATRIX_EMA_DISTANCE_TOO_LOW", score);

            bool hasImpulse =
                dir == TradeDirection.Long ? ctx.HasImpulseLong_M5 :
                dir == TradeDirection.Short ? ctx.HasImpulseShort_M5 :
                false;

            double pullbackDepth =
                dir == TradeDirection.Long ? ctx.PullbackDepthRLong_M5 :
                dir == TradeDirection.Short ? ctx.PullbackDepthRShort_M5 :
                ctx.PullbackDepthAtr_M5;

            bool hasFlag =
                dir == TradeDirection.Long ? ctx.HasFlagLong_M5 :
                dir == TradeDirection.Short ? ctx.HasFlagShort_M5 :
                ctx.IsValidFlagStructure_M5;

            bool hasPullback =
                dir == TradeDirection.Long ? ctx.HasPullbackLong_M5 :
                dir == TradeDirection.Short ? ctx.HasPullbackShort_M5 :
                false;

            int barsSinceImpulse =
                dir == TradeDirection.Long ? ctx.BarsSinceImpulseLong_M5 :
                ctx.BarsSinceImpulseShort_M5;

            if (!hasPullback)
            {
                return Block(ctx, dir, "NO_PULLBACK_STRUCTURE", score);
            }

            double dynamicMinAdx = (ctx.Session == FxSession.Asia) ? 18.0 : 20.0;
            dynamicMinAdx = System.Math.Max(dynamicMinAdx, matrix.MinAdx);

            if (ctx.Adx_M5 < dynamicMinAdx)
                return Block(ctx, dir, $"ADX_TOO_LOW_{ctx.Adx_M5:0.0}", score);

            if (ctx.Adx_M5 < 23.0)
            {
                ApplyPenalty(ref score, ref penalty, 6, penaltyBudget, ctx, "ADX_SOFT_LOW");
            }
            else if (ctx.Adx_M5 >= 40.0 && ctx.AdxSlope_M5 <= 0)
            {
                ApplyPenalty(ref score, ref penalty, 5, penaltyBudget, ctx, "ADX_EXHAUST_SOFT");
            }

            if (!hasImpulse)
            {
                if (ctx.Session == FxSession.Asia)
                {
                    ApplyPenalty(ref score, ref penalty, 5, penaltyBudget, ctx, "ASIA_NO_IMPULSE");
                    ctx?.Log?.Invoke($"[PB FILTER] dir={dir} Asia impulse missing → penalty applied");
                }

                ApplyPenalty(ref score, ref penalty, 8, penaltyBudget, ctx, "NO_IMPULSE");
            }
            else
            {
                if (barsSinceImpulse <= 2)
                    score += 6;
                else if (barsSinceImpulse <= 5)
                    score += 2;
                else
                    ApplyPenalty(ref score, ref penalty, 6, penaltyBudget, ctx, "IMPULSE_TOO_OLD");
            }

            int weakCount = 0;

            if (!ctx.PullbackTouchedEma21_M5) weakCount++;
            if (!ctx.IsPullbackDecelerating_M5) weakCount++;
            if (!ctx.HasReactionCandle_M5) weakCount++;

            int lastClosed = ctx.M5.Count - 2;
            var lastBar = ctx.M5[lastClosed];
            bool lastBarInDir =
                (dir == TradeDirection.Long && lastBar.Close > lastBar.Open) ||
                (dir == TradeDirection.Short && lastBar.Close < lastBar.Open);

            if (!lastBarInDir) weakCount++;

            bool hasDirectionalM1Trigger = HasM1TriggerDirectional(ctx, dir);

            if (weakCount >= 3 && !hasDirectionalM1Trigger)
            {
                ctx?.Log?.Invoke($"[PB FILTER] dir={dir} weak structure blocked | weakCount={weakCount}");
                return Block(ctx, dir, "PB_WEAK_STRUCTURE", score);
            }

            if (!ctx.PullbackTouchedEma21_M5)
                ApplyPenalty(ref score, ref penalty, 6, penaltyBudget, ctx, "PB_NO_EMA21_TOUCH");

            if (!ctx.IsPullbackDecelerating_M5)
                ApplyPenalty(ref score, ref penalty, 6, penaltyBudget, ctx, "PB_NOT_DECEL");

            if (!ctx.HasReactionCandle_M5)
                ApplyPenalty(ref score, ref penalty, 4, penaltyBudget, ctx, "PB_NO_REACTION");

            if (!lastBarInDir)
                ApplyPenalty(ref score, ref penalty, 6, penaltyBudget, ctx, "PB_LASTBAR_NOT_TREND_DIR");

            if (pullbackDepth > 0.75)
            {
                var bars = ctx.M5;
                int lastClosedBar = bars.Count - 2;

                int compressionBars = Math.Max(0, Math.Min(ctx.PullbackBars_M5, 10));
                int compressionStart = Math.Max(0, lastClosedBar - compressionBars + 1);

                double compressionHigh = double.MinValue;
                double compressionLow = double.MaxValue;

                for (int i = compressionStart; i <= lastClosedBar; i++)
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
                    return Block(ctx, dir, "PB_TOO_DEEP", score);
                }

                TradeDirection impulseDirection =
                    ctx.ImpulseDirection != TradeDirection.None ? ctx.ImpulseDirection : dir;

                bool breakoutAligned =
                    (impulseDirection == TradeDirection.Long && bars[lastClosedBar].Close > compressionHigh) ||
                    (impulseDirection == TradeDirection.Short && bars[lastClosedBar].Close < compressionLow);

                if (!breakoutAligned)
                {
                    ctx.Log?.Invoke($"[PB] dir={dir} rejected: breakout against impulse");
                    return Block(ctx, dir, "PB_TOO_DEEP", score);
                }

                ctx.Log?.Invoke($"[PB] dir={dir} DeepPullbackContinuation accepted");
            }

            if (pullbackDepth > 1.6)
                return Block(ctx, dir, "PB_TOO_DEEP_EXTREME", score);

            if (pullbackDepth > 1.0)
                ApplyPenalty(ref score, ref penalty, 6, penaltyBudget, ctx, "PB_TOO_DEEP");

            int fuel = 0;

            if (ctx.Adx_M5 >= 25) fuel += 4;
            else fuel -= 6;

            if (ctx.AdxSlope_M5 > 0) fuel += 4;
            else fuel -= 4;

            if (ctx.IsAtrExpanding_M5) fuel += 3;
            else fuel -= 3;

            if (barsSinceImpulse <= 2) fuel += 4;

            score += fuel;

            if (ctx.Adx_M5 > 45 &&
                ctx.AdxSlope_M5 <= 0 &&
                !ctx.IsAtrExpanding_M5)
            {
                return Block(ctx, dir, "TREND_EXHAUSTION", score);
            }

            if (ctx.Session == FxSession.Asia)
            {
                ApplyPenalty(ref score, ref penalty, 6, penaltyBudget, ctx, "ASIA_SOFT");
            }

            if (ctx.Session == FxSession.NewYork)
            {
                if (!hasDirectionalM1Trigger)
                    ApplyPenalty(ref score, ref penalty, 6, penaltyBudget, ctx, "NY_NO_M1_TRIGGER");
            }

            if (hasFlag)
            {
                int flagPenalty = hasDirectionalM1Trigger ? 6 : 10;

                ApplyPenalty(
                    ref score,
                    ref penalty,
                    flagPenalty,
                    penaltyBudget,
                    ctx,
                    hasDirectionalM1Trigger ? "FLAG_ACTIVE_WITH_M1" : "FLAG_ACTIVE_NO_M1"
                );
            }

            bool htfMismatch =
                ctx.FxHtfAllowedDirection != TradeDirection.None &&
                ctx.FxHtfAllowedDirection != dir;

            if (htfMismatch)
            {
                double conf = ctx.FxHtfConfidence01;
                int htfPenalty = (int)(conf * 10);

                ApplyPenalty(ref score, ref penalty, htfPenalty, penaltyBudget, ctx, "HTF_MISMATCH");

                if (conf >= 0.75 && fuel < 3)
                {
                    ApplyPenalty(ref score, ref penalty, 3, penaltyBudget, ctx, "HTF_STRONG_WEAK_LOCAL");
                }
            }

            if (penaltyBudget > 0 && penalty > penaltyBudget)
            {
                int overflow = penalty - penaltyBudget;
                score -= overflow;
                ctx?.Log?.Invoke($"[FX_PullbackEntry] SOFT_BUDGET_OVERFLOW -{overflow} | score={score}");
            }

            score += (int)System.Math.Round(matrix.EntryScoreModifier);

            if (score < MIN_SCORE)
                return Block(ctx, dir, $"LOW_SCORE_{score}", score);

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.FX_Pullback,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason = $"FX_PULLBACK_V3 dir={dir} score={score} pen={penalty}/{penaltyBudget}"
            };
        }

        private static bool HasM1TriggerDirectional(EntryContext ctx, TradeDirection dir)
        {
            if (ctx?.M1 == null || ctx.M1.Count < 3)
                return false;

            int lastClosed = ctx.M1.Count - 2;
            int prevClosed = ctx.M1.Count - 3;

            var last = ctx.M1[lastClosed];
            var prev = ctx.M1[prevClosed];

            if (dir == TradeDirection.Long)
                return last.Close > last.Open && last.Low > prev.Low;

            if (dir == TradeDirection.Short)
                return last.Close < last.Open && last.High < prev.High;

            return false;
        }

        private static void ApplyPenalty(
            ref int score,
            ref int penalty,
            int amount,
            int budget,
            EntryContext ctx,
            string tag)
        {
            if (amount <= 0) return;

            penalty += amount;
            score -= amount;

            ctx?.Log?.Invoke($"[FX_PullbackEntry] PEN {tag} -{amount} | score={score} pen={penalty}/{budget}");
        }

        private EntryEvaluation Block(EntryContext ctx, TradeDirection dir, string reason, int score)
        {
            ctx?.Log?.Invoke($"[FX_PullbackEntry] BLOCK {reason} dir={dir} | score={score}");

            // FIX #2:
            // invalid eval soha ne hordozzon végrehajtható irányt
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.FX_Pullback,
                Direction = TradeDirection.None,
                Score = score,
                IsValid = false,
                Reason = reason
            };
        }

        private static double ComputeAtrAverage(EntryContext ctx, int lookback)
        {
            if (ctx?.M5 == null || lookback <= 0 || ctx.M5.Count <= lookback + 1)
                return 0;

            double trSum = 0;
            for (int i = 1; i <= lookback; i++)
            {
                double high = ctx.M5.HighPrices.Last(i);
                double low = ctx.M5.LowPrices.Last(i);
                double prevClose = ctx.M5.ClosePrices.Last(i + 1);

                double trHighLow = high - low;
                double trHighPrevClose = System.Math.Abs(high - prevClose);
                double trLowPrevClose = System.Math.Abs(low - prevClose);

                trSum += System.Math.Max(trHighLow, System.Math.Max(trHighPrevClose, trLowPrevClose));
            }

            return trSum / lookback;
        }
    }
}