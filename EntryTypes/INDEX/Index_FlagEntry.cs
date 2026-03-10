using cAlgo.API;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Matrix;
using GeminiV26.Instruments.INDEX;
using System;

namespace GeminiV26.EntryTypes.INDEX
{
    public sealed class Index_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.Index_Flag;

        private const int DefaultMaxBarsSinceImpulse = 3;
        private const int DefaultFlagBars = 3;

        private const double DefaultMaxFlagRangeAtr = 1.20;
        private const double DefaultBreakBufferAtr = 0.08;
        private const double DefaultMaxDistFromEmaAtr = 0.65;

        private const double MinBreakoutBodyRatio = 0.55;
        private const double MinBreakoutBarAtr = 0.35;

        private const double MaxOpposingSlopeAtr = 0.45;
        private const double MaxSameDirSlopeAtr = 0.20;

        private const int BaseScore = 84;
        private const int MinScore = 72;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            var matrix = ctx?.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;
            if (!matrix.AllowFlag)
                return Reject(ctx, "SESSION_MATRIX_ALLOWFLAG_DISABLED", 0, TradeDirection.None);

            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 40)
                return Reject(ctx, "CTX_NOT_READY", 0, TradeDirection.None);

            if (ctx.AtrM5 <= 0)
                return Reject(ctx, "ATR_NOT_READY", 0, TradeDirection.None);

            var p = IndexInstrumentMatrix.Get(ctx.Symbol);
            if (p == null)
                return Reject(ctx, "NO_INDEX_PROFILE", 0, TradeDirection.None);

            int maxBarsSinceImpulse = p.MaxBarsSinceImpulse_M5 > 0 ? p.MaxBarsSinceImpulse_M5 : DefaultMaxBarsSinceImpulse;
            int flagBars = p.FlagBars > 0 ? p.FlagBars : DefaultFlagBars;
            double maxFlagRangeAtr = p.MaxFlagAtrMult > 0 ? p.MaxFlagAtrMult : DefaultMaxFlagRangeAtr;
            double breakoutBufferAtr = p.BreakoutBufferAtr > 0 ? p.BreakoutBufferAtr : DefaultBreakBufferAtr;
            double maxDistFromEmaAtr = p.MaxEmaDistanceAtr > 0 ? p.MaxEmaDistanceAtr : DefaultMaxDistFromEmaAtr;

            double minAdxTrend = p.MinAdxTrend > 0 ? p.MinAdxTrend : 18.0;
            minAdxTrend = Math.Max(minAdxTrend, matrix.MinAdx);
            double chopAdxThreshold = p.ChopAdxThreshold > 0 ? p.ChopAdxThreshold : 17.0;
            double chopDiDiff = p.ChopDiDiffThreshold > 0 ? p.ChopDiDiffThreshold : 7.0;

            int fatigueThreshold = p.FatigueThreshold > 0 ? p.FatigueThreshold : 3;
            double fatigueAdxLevel = p.FatigueAdxLevel > 0 ? p.FatigueAdxLevel : 38.0;

            double scoreMultiplier = p.ScoreWeightMultiplier > 0 ? p.ScoreWeightMultiplier : 1.0;
            bool requireStructure = p.PullbackStyle == IndexPullbackStyle.Structure;

            ctx.Log?.Invoke(
                $"[IDX_FLAG][PROFILE] sym={ctx.Symbol} norm={p.Symbol} " +
                $"minAdx={minAdxTrend:F1} chopAdx={chopAdxThreshold:F1} fatigueTh={fatigueThreshold} scoreMult={scoreMultiplier:F2}"
            );

            var longEval = EvaluateDir(
                ctx,
                TradeDirection.Long,
                flagBars,
                maxBarsSinceImpulse,
                maxFlagRangeAtr,
                breakoutBufferAtr,
                maxDistFromEmaAtr,
                requireStructure,
                minAdxTrend,
                chopAdxThreshold,
                chopDiDiff,
                fatigueThreshold,
                fatigueAdxLevel,
                scoreMultiplier);

            var shortEval = EvaluateDir(
                ctx,
                TradeDirection.Short,
                flagBars,
                maxBarsSinceImpulse,
                maxFlagRangeAtr,
                breakoutBufferAtr,
                maxDistFromEmaAtr,
                requireStructure,
                minAdxTrend,
                chopAdxThreshold,
                chopDiDiff,
                fatigueThreshold,
                fatigueAdxLevel,
                scoreMultiplier);

            longEval.Score += (int)Math.Round(matrix.EntryScoreModifier);
            shortEval.Score += (int)Math.Round(matrix.EntryScoreModifier);

            if (longEval.IsValid && shortEval.IsValid)
                return longEval.Score >= shortEval.Score ? longEval : shortEval;

            if (longEval.IsValid) return longEval;
            if (shortEval.IsValid) return shortEval;

            return longEval.Score >= shortEval.Score ? longEval : shortEval;
        }

        private EntryEvaluation EvaluateDir(
            EntryContext ctx,
            TradeDirection dir,
            int flagBars,
            int maxBarsSinceImpulse,
            double maxFlagRangeAtr,
            double breakoutBufferAtr,
            double maxDistFromEmaAtr,
            bool requireStructure,
            double minAdxTrend,
            double chopAdxThreshold,
            double chopDiDiff,
            int fatigueThreshold,
            double fatigueAdxLevel,
            double scoreMultiplier)
        {
            int score = BaseScore;
            int penaltyBudget = 0;
            const int maxPenalty = 22;

            void ApplyPenalty(int p)
            {
                if (p <= 0) return;
                int room = maxPenalty - penaltyBudget;
                int use = Math.Min(room, p);
                if (use <= 0) return;
                score -= use;
                penaltyBudget += use;
            }

            void ApplyReward(int r)
            {
                if (r > 0) score += r;
            }

            var bars = ctx.M5;
            int lastClosed = bars.Count - 2;
            int breakoutBarIndex = lastClosed;
            int flagEnd = breakoutBarIndex - 1;
            int flagStart = flagEnd - flagBars + 1;

            if (flagStart < 3)
                return Reject(ctx, "NOT_ENOUGH_FLAG_BARS", score, dir);

            var breakoutBar = bars[breakoutBarIndex];
            double close = breakoutBar.Close;
            double open = breakoutBar.Open;
            double high = breakoutBar.High;
            double low = breakoutBar.Low;

            ctx.Log?.Invoke(
                $"[IDX_FLAG][START] sym={ctx.Symbol} dir={dir} " +
                $"adx={ctx.Adx_M5:F1} atr={ctx.AtrM5:F1} trend={ctx.MarketState?.IsTrend} lowVol={ctx.MarketState?.IsLowVol} " +
                $"impulse={ctx.HasImpulse_M5} bsi={ctx.BarsSinceImpulse_M5}"
            );

            // =====================================================
            // HARD CONTEXT GATES
            // =====================================================
            if (ctx.MarketState?.IsLowVol == true)
                return Reject(ctx, "LOW_VOL_ENV", score, dir);

            if (ctx.Adx_M5 < minAdxTrend)
                return Reject(ctx, $"ADX_TOO_LOW({ctx.Adx_M5:F1}<{minAdxTrend:F1})", score, dir);

            bool chopZone =
                ctx.Adx_M5 < chopAdxThreshold &&
                Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < chopDiDiff &&
                !ctx.IsAtrExpanding_M5;

            if (chopZone)
                return Reject(ctx, "CHOP_ZONE", score, dir);

            if (!ctx.HasImpulse_M5)
                return Reject(ctx, "NO_IMPULSE", score, dir);

            if (ctx.BarsSinceImpulse_M5 > maxBarsSinceImpulse)
                return Reject(ctx, $"STALE_IMPULSE({ctx.BarsSinceImpulse_M5}>{maxBarsSinceImpulse})", score, dir);

            // =====================================================
            // FATIGUE
            // =====================================================
            bool adxExhausted = ctx.Adx_M5 >= fatigueAdxLevel && ctx.AdxSlope_M5 <= 0;
            bool atrContracting = ctx.AtrSlope_M5 <= 0;
            bool diConverging = Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < chopDiDiff;
            bool lateImpulse = ctx.BarsSinceImpulse_M5 >= Math.Max(2, maxBarsSinceImpulse);

            int fatigueCount = 0;
            if (adxExhausted) fatigueCount++;
            if (atrContracting) fatigueCount++;
            if (diConverging) fatigueCount++;
            if (lateImpulse) fatigueCount++;

            if (fatigueCount >= fatigueThreshold)
                return Reject(ctx, $"IDX_TREND_FATIGUE({fatigueCount}/{fatigueThreshold})", score, dir);

            // =====================================================
            // FLAG RANGE
            // =====================================================
            double hi = double.MinValue;
            double lo = double.MaxValue;

            for (int i = flagStart; i <= flagEnd; i++)
            {
                hi = Math.Max(hi, bars[i].High);
                lo = Math.Min(lo, bars[i].Low);
            }

            double flagRange = hi - lo;
            double flagAtr = flagRange / ctx.AtrM5;

            ctx.Log?.Invoke(
                $"[IDX_FLAG][RANGE] dir={dir} flagBars={flagBars} flagATR={flagAtr:F2} maxAllowed={maxFlagRangeAtr:F2}"
            );

            if (flagAtr > maxFlagRangeAtr)
                return Reject(ctx, $"FLAG_TOO_WIDE({flagAtr:F2}>{maxFlagRangeAtr:F2})", score, dir);

            if (flagAtr < 0.45)
                ApplyReward(2);
            else if (flagAtr > 1.0)
                ApplyPenalty(3);

            // =====================================================
            // FLAG SLOPE
            // =====================================================
            double firstOpen = bars[flagStart].Open;
            double lastFlagClose = bars[flagEnd].Close;
            double flagSlopeAtr = (lastFlagClose - firstOpen) / ctx.AtrM5;

            if (dir == TradeDirection.Long)
            {
                if (flagSlopeAtr > MaxSameDirSlopeAtr)
                    return Reject(ctx, $"FLAG_SLOPE_WRONG_LONG({flagSlopeAtr:F2})", score, dir);

                if (flagSlopeAtr < -MaxOpposingSlopeAtr)
                    return Reject(ctx, $"FLAG_TOO_STEEP_LONG({flagSlopeAtr:F2})", score, dir);

                if (flagSlopeAtr >= -0.25 && flagSlopeAtr <= 0.05)
                    ApplyReward(3);
            }
            else
            {
                if (flagSlopeAtr < -MaxSameDirSlopeAtr)
                    return Reject(ctx, $"FLAG_SLOPE_WRONG_SHORT({flagSlopeAtr:F2})", score, dir);

                if (flagSlopeAtr > MaxOpposingSlopeAtr)
                    return Reject(ctx, $"FLAG_TOO_STEEP_SHORT({flagSlopeAtr:F2})", score, dir);

                if (flagSlopeAtr >= -0.05 && flagSlopeAtr <= 0.25)
                    ApplyReward(3);
            }

            // =====================================================
            // EMA BIAS + OVEREXTENSION
            // =====================================================
            double distFromEmaAtr = Math.Abs(close - ctx.Ema21_M5) / ctx.AtrM5;

            if (distFromEmaAtr > maxDistFromEmaAtr)
                return Reject(ctx,
                    $"OVEREXTENDED_EMA({distFromEmaAtr:F2}>{maxDistFromEmaAtr:F2})",
                    score,
                    dir);

            if (dir == TradeDirection.Long && close < ctx.Ema21_M5)
                return Reject(ctx, "EMA_BIAS_MISMATCH_LONG", score, dir);

            if (dir == TradeDirection.Short && close > ctx.Ema21_M5)
                return Reject(ctx, "EMA_BIAS_MISMATCH_SHORT", score, dir);

            // =====================================================
            // STRUCTURE
            // =====================================================
            bool structureOk;

            if (dir == TradeDirection.Long)
            {
                structureOk =
                    ctx.BrokeLastSwingHigh_M5 ||
                    (!requireStructure && close > ctx.Ema21_M5 && ctx.IsValidFlagStructure_M5);
            }
            else
            {
                structureOk =
                    ctx.BrokeLastSwingLow_M5 ||
                    (!requireStructure && close < ctx.Ema21_M5 && ctx.IsValidFlagStructure_M5);
            }

            if (!structureOk)
                return Reject(ctx, "NO_CONTINUATION_STRUCTURE", score, dir);

            if (!ctx.IsValidFlagStructure_M5)
                ApplyPenalty(6);
            else
                ApplyReward(3);

            // =====================================================
            // BREAKOUT
            // =====================================================
            double buffer = ctx.AtrM5 * breakoutBufferAtr;
            bool bullBreak = close > hi + buffer;
            bool bearBreak = close < lo - buffer;

            bool breakoutConfirmed =
                (dir == TradeDirection.Long && bullBreak) ||
                (dir == TradeDirection.Short && bearBreak);

            if (!breakoutConfirmed)
                return Reject(ctx, "NO_FLAG_BREAKOUT", score, dir);

            double follow = ctx.AtrM5 * 0.12;

            if (dir == TradeDirection.Long && close < hi + follow)
                return Reject(ctx, "WEAK_BREAKOUT_NO_FOLLOW", score, dir);

            if (dir == TradeDirection.Short && close > lo - follow)
                return Reject(ctx, "WEAK_BREAKOUT_NO_FOLLOW", score, dir);

            // =====================================================
            // BREAKOUT BAR QUALITY
            // =====================================================
            double barRange = high - low;
            if (barRange <= 0)
                return Reject(ctx, "BAD_BAR_RANGE", score, dir);

            double body = Math.Abs(close - open);
            double bodyRatio = body / barRange;
            double breakoutBarAtr = barRange / ctx.AtrM5;

            if (bodyRatio < MinBreakoutBodyRatio)
                return Reject(ctx, $"WEAK_BODY({bodyRatio:F2})", score, dir);

            if (breakoutBarAtr < MinBreakoutBarAtr)
                return Reject(ctx, $"BREAKOUT_TOO_SMALL({breakoutBarAtr:F2})", score, dir);

            if (dir == TradeDirection.Long && close <= open)
                return Reject(ctx, "BREAKOUT_BAR_NOT_BULLISH", score, dir);

            if (dir == TradeDirection.Short && close >= open)
                return Reject(ctx, "BREAKOUT_BAR_NOT_BEARISH", score, dir);

            // =====================================================
            // M1 CONFIRMATION
            // =====================================================
            bool m1Ok =
                HasDirectionalM1Trigger(ctx, dir) ||
                HasDirectionalM1FollowThrough(ctx, dir);

            if (!m1Ok)
                return Reject(ctx, "NO_M1_CONFIRMATION", score, dir);

            ApplyReward(5);

            // =====================================================
            // EXTRA QUALITY
            // =====================================================
            if (ctx.IsAtrExpanding_M5)
                ApplyReward(2);
            else
                ApplyPenalty(2);

            if (ctx.MarketState?.IsTrend == true)
                ApplyReward(2);
            else
                ApplyPenalty(3);

            if (dir == TradeDirection.Long)
            {
                if (ctx.Ema50_M5 > ctx.Ema200_M5) ApplyReward(2);
                else ApplyPenalty(4);
            }
            else
            {
                if (ctx.Ema50_M5 < ctx.Ema200_M5) ApplyReward(2);
                else ApplyPenalty(4);
            }

            // HTF = soft only
            if (ctx.IndexHtfAllowedDirection != TradeDirection.None)
            {
                if (ctx.IndexHtfAllowedDirection == dir)
                    ApplyReward(3);
                else
                    ApplyPenalty(ctx.IndexHtfConfidence01 >= 0.70 ? 5 : 3);
            }

            score = (int)Math.Round(score * scoreMultiplier);

            ctx.Log?.Invoke(
                $"[IDX_FLAG][FINAL] dir={dir} score={score} flagATR={flagAtr:F2} slopeATR={flagSlopeAtr:F2} " +
                $"emaDistATR={distFromEmaAtr:F2} fatigue={fatigueCount}/{fatigueThreshold}"
            );

            score += (int)Math.Round(matrix.EntryScoreModifier);

            if (score < MinScore)
                return Reject(ctx, $"LOW_SCORE({score}<{MinScore})", score, dir);

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason =
                    $"IDX_FLAG_PRO dir={dir} score={score} mult={scoreMultiplier:F2} " +
                    $"fatigue={fatigueCount}/{fatigueThreshold} flagATR={flagAtr:F2} slopeATR={flagSlopeAtr:F2} " +
                    $"emaATR={distFromEmaAtr:F2}"
            };
        }

        private static bool HasDirectionalM1Trigger(EntryContext ctx, TradeDirection dir)
        {
            if (!ctx.M1TriggerInTrendDirection)
                return false;

            if (ctx.M1 == null || ctx.M1.Count < 3)
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

        private static bool HasDirectionalM1FollowThrough(EntryContext ctx, TradeDirection dir)
        {
            if (ctx.M1 == null || ctx.M1.Count < 4)
                return false;

            int lastClosed = ctx.M1.Count - 2;
            int prevClosed = ctx.M1.Count - 3;

            var last = ctx.M1[lastClosed];
            var prev = ctx.M1[prevClosed];

            double range = last.High - last.Low;
            if (range <= 0) return false;

            double body = Math.Abs(last.Close - last.Open);
            double ratio = body / range;
            if (ratio < 0.50) return false;

            if (dir == TradeDirection.Long)
                return last.Close > prev.High && last.Close > last.Open;

            if (dir == TradeDirection.Short)
                return last.Close < prev.Low && last.Close < last.Open;

            return false;
        }

        private static EntryEvaluation Reject(
            EntryContext ctx,
            string reason,
            int score,
            TradeDirection dir)
        {
            ctx.Log?.Invoke(
                $"[IDX_FLAG][REJECT] {reason} | score={Math.Max(0, score)} | dir={dir} | " +
                $"ADX={ctx?.Adx_M5:F1} Impulse={ctx?.HasImpulse_M5} ATR={ctx?.AtrM5:F1}"
            );

            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.Index_Flag,
                Direction = dir,
                IsValid = false,
                Score = Math.Max(0, score),
                Reason = reason
            };
        }
    }
}