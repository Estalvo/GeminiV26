using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes;
using GeminiV26.Core.Matrix;
using GeminiV26.Instruments.INDEX;
using System;
using System.Collections.Generic;

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
        private const int MinScore = EntryDecisionPolicy.MinScoreThreshold;

        private static readonly Dictionary<string, int> _traceInvocationCountByBarDir = new Dictionary<string, int>();

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            DirectionDebug.LogOnce(ctx);
            var matrix = ctx?.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;
            if (!matrix.AllowFlag)
                return Reject(ctx, "SESSION_MATRIX_ALLOWFLAG_DISABLED", 0, TradeDirection.None);

            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 40)
                return Reject(ctx, "CTX_NOT_READY", 0, TradeDirection.None);

            if (ctx.LogicBias == TradeDirection.None)
                return Reject(ctx, "NO_LOGIC_BIAS", 0, TradeDirection.None);

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

            bool hasHtfMismatch = ctx.ResolveAssetHtfConfidence01() >= 0.6 && ctx.ResolveAssetHtfAllowedDirection() != TradeDirection.None && ctx.ResolveAssetHtfAllowedDirection() != ctx.LogicBias;

            if (ctx.LogicBias == TradeDirection.Long)
            {
                var eval = EvaluateDir(
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
                    scoreMultiplier,
                    hasHtfMismatch);
                eval.Score += (int)Math.Round(matrix.EntryScoreModifier);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), eval, null, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }
            else if (ctx.LogicBias == TradeDirection.Short)
            {
                var eval = EvaluateDir(
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
                    scoreMultiplier,
                    hasHtfMismatch);
                eval.Score += (int)Math.Round(matrix.EntryScoreModifier);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), null, eval, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }

            return Reject(ctx, "NO_LOGIC_BIAS", 0, TradeDirection.None);
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
            double scoreMultiplier,
            bool hasHtfMismatch)
        {
            int score = BaseScore;
            int setupScore = 0;
            int penaltyBudget = 0;
            double triggerScore = 0;
            const int maxPenalty = 22;
            bool continuationAuthority = HasContinuationAuthority(ctx, dir);
            bool sourceHtfAlign = IsAlignedWithAllowedDirection(ctx?.IndexHtfAllowedDirection ?? TradeDirection.None, dir);
            string sourceHtfState = ctx?.IndexHtfReason ?? "N/A";

            ctx.Log?.Invoke(
                $"[AUDIT][HTF TRACE][SOURCE] symbol={ctx?.Symbol} entryType={Type} candidateDirection={dir} " +
                $"rawHtfState={sourceHtfState} allowedDirection={ctx?.IndexHtfAllowedDirection ?? TradeDirection.None} " +
                $"htfAlign={sourceHtfAlign} sourceModule=IndexHtfBiasEngine");

            if (hasHtfMismatch)
            {
                if (continuationAuthority)
                {
                    ApplyPenalty(8);
                    ctx.Log?.Invoke($"[IDX_FLAG][SOFT_PENALTY] reason=HTF_MISMATCH penalty=8 dir={dir}");
                }
                else
                {
                    return Reject(ctx, "HTF_MISMATCH", score, dir);
                }
            }

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

            bool hasImpulse =
                dir == TradeDirection.Long ? ctx.HasImpulseLong_M5 :
                dir == TradeDirection.Short ? ctx.HasImpulseShort_M5 :
                false;

            bool hasPullback =
                dir == TradeDirection.Long ? ctx.HasPullbackLong_M5 :
                dir == TradeDirection.Short ? ctx.HasPullbackShort_M5 :
                false;

            DateTime traceBarTime = bars.OpenTimes[lastClosed];
            string traceKey = $"{ctx.Symbol}|{traceBarTime:O}|{dir}";
            int traceCount;
            lock (_traceInvocationCountByBarDir)
            {
                _traceInvocationCountByBarDir.TryGetValue(traceKey, out traceCount);
                traceCount++;
                _traceInvocationCountByBarDir[traceKey] = traceCount;
            }

            ctx.Log?.Invoke(
                $"[IDX_FLAG][TRACE] evaluator invoked sym={ctx.Symbol} dir={dir} bar={traceBarTime:O} count={traceCount}"
            );

            ctx.Log?.Invoke(
                $"[IDX_FLAG][START] sym={ctx.Symbol} dir={dir} " +
                $"adx={ctx.Adx_M5:F1} atr={ctx.AtrM5:F1} trend={ctx.MarketState?.IsTrend} lowVol={ctx.MarketState?.IsLowVol} " +
                $"impulse={hasImpulse} bsi={ctx.BarsSinceImpulse_M5}"
            );

            // =====================================================
            // HARD CONTEXT GATES
            // =====================================================
            if (ctx.MarketState?.IsLowVol == true)
                return Reject(ctx, "LOW_VOL_ENV", score, dir);

            if (ctx.Adx_M5 >= 15 && ctx.Adx_M5 < minAdxTrend)
                return Reject(ctx, $"ADX_TOO_LOW({ctx.Adx_M5:F1}<{minAdxTrend:F1})", score, dir);

            bool chopZone =
                ctx.Adx_M5 >= 15 && ctx.Adx_M5 < chopAdxThreshold &&
                Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < chopDiDiff &&
                !ctx.IsAtrExpanding_M5;
                
            if (chopZone)
                return Reject(ctx, "CHOP_ZONE", score, dir);

            if (!hasImpulse)
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
            {
                if (continuationAuthority)
                {
                    ApplyPenalty(10);
                    ctx.Log?.Invoke(
                        $"[IDX_FLAG][SOFT_PENALTY] reason=IDX_TREND_FATIGUE({fatigueCount}/{fatigueThreshold}) penalty=10 dir={dir}");
                }
                else
                {
                    return Reject(ctx, $"IDX_TREND_FATIGUE({fatigueCount}/{fatigueThreshold})", score, dir);
                }
            }

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

            bool hasValidRange = hi > lo && hi > 0 && lo > 0;

            if (!hasValidRange)
                ctx.Log?.Invoke("[FLAG WARN] No valid range → fallback mode");

            double flagRange = hasValidRange ? hi - lo : 0;
            double flagAtr = hasValidRange && ctx.AtrM5 > 0
                ? flagRange / ctx.AtrM5
                : 0;

            ctx.Log?.Invoke(
                $"[IDX_FLAG][RANGE] dir={dir} flagBars={flagBars} flagATR={flagAtr:F2} maxAllowed={maxFlagRangeAtr:F2} hasRange={hasValidRange}"
            );

            if (flagAtr > 0)
            {
                if (flagAtr > maxFlagRangeAtr)
                    ApplyPenalty(4);

                if (flagAtr < 0.45)
                    ApplyReward(2);
                else if (flagAtr > 1.0)
                    ApplyPenalty(3);
            }
            else
            {
                ApplyPenalty(2);
            }

            // =====================================================
            // FLAG SLOPE
            // =====================================================
            double firstOpen = bars[flagStart].Open;
            double lastFlagClose = bars[flagEnd].Close;
            double flagSlopeAtr = ctx.AtrM5 > 0
                ? (lastFlagClose - firstOpen) / ctx.AtrM5
                : 0;

            if (hasValidRange)
            {
                if (dir == TradeDirection.Long)
                {
                    if (flagSlopeAtr > MaxSameDirSlopeAtr)
                        ApplyPenalty(4);

                    if (flagSlopeAtr < -MaxOpposingSlopeAtr)
                        ApplyPenalty(4);

                    if (flagSlopeAtr >= -0.25 && flagSlopeAtr <= 0.05)
                        ApplyReward(3);
                }
                else
                {
                    if (flagSlopeAtr < -MaxSameDirSlopeAtr)
                        ApplyPenalty(4);

                    if (flagSlopeAtr > MaxOpposingSlopeAtr)
                        ApplyPenalty(4);

                    if (flagSlopeAtr >= -0.05 && flagSlopeAtr <= 0.25)
                        ApplyReward(3);
                }
            }

            // =====================================================
            // EMA BIAS + OVEREXTENSION
            // =====================================================
            double distFromEmaAtr = Math.Abs(close - ctx.Ema21_M5) / ctx.AtrM5;

            if (distFromEmaAtr > maxDistFromEmaAtr)
            {
                if (continuationAuthority)
                {
                    ApplyPenalty(10);
                    ctx.Log?.Invoke(
                        $"[IDX_FLAG][SOFT_PENALTY] reason=OVEREXTENDED_EMA({distFromEmaAtr:F2}>{maxDistFromEmaAtr:F2}) penalty=10 dir={dir}");
                }
                else
                {
                    return Reject(ctx,
                        $"OVEREXTENDED_EMA({distFromEmaAtr:F2}>{maxDistFromEmaAtr:F2})",
                        score,
                        dir);
                }
            }

            if (dir == TradeDirection.Long && close < ctx.Ema21_M5)
                return Reject(ctx, "EMA_BIAS_MISMATCH_LONG", score, dir);

            if (dir == TradeDirection.Short && close > ctx.Ema21_M5)
                return Reject(ctx, "EMA_BIAS_MISMATCH_SHORT", score, dir);

            // =====================================================
            // STRUCTURE
            // =====================================================
            bool structureOk;

            bool hasFlag =
                dir == TradeDirection.Long ? ctx.HasFlagLong_M5 :
                dir == TradeDirection.Short ? ctx.HasFlagShort_M5 :
                ctx.IsValidFlagStructure_M5;

            if (dir == TradeDirection.Long)
            {
                structureOk =
                    ctx.BrokeLastSwingHigh_M5 ||
                    (!requireStructure && close > ctx.Ema21_M5);
            }
            else
            {
                structureOk =
                    ctx.BrokeLastSwingLow_M5 ||
                    (!requireStructure && close < ctx.Ema21_M5);
            }

            if (!structureOk)
                ApplyPenalty(4);

            if (!hasFlag)
                ApplyPenalty(2);
            else
                ApplyReward(3);

            // =====================================================
            // BREAKOUT
            // =====================================================
            double buffer = ctx.AtrM5 * breakoutBufferAtr;
            bool bullBreak = hasValidRange && close > hi + buffer;
            bool bearBreak = hasValidRange && close < lo - buffer;

            bool breakoutSignal =
                (ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir) ||
                ctx.RangeBreakDirection == dir ||
                (dir == TradeDirection.Long
                    ? (ctx.FlagBreakoutUp || ctx.FlagBreakoutUpConfirmed)
                    : (ctx.FlagBreakoutDown || ctx.FlagBreakoutDownConfirmed));

            bool breakoutConfirmed =
                (dir == TradeDirection.Long && (bullBreak || breakoutSignal)) ||
                (dir == TradeDirection.Short && (bearBreak || breakoutSignal));

            bool continuationSignal = breakoutSignal;

            bool hasImpulseSetup =
                hasImpulse;

            if (!hasImpulseSetup)
                setupScore -= 40;
            else
                setupScore += 15;

            bool hasStructure =
                hasPullback || hasFlag;

            if (hasStructure)
                setupScore += 10;

            bool hasContinuation =
                continuationSignal || breakoutConfirmed;

            if (hasContinuation)
                setupScore += 20;

            double breakDist = 0;

            if (hasValidRange)
            {
                breakDist =
                    dir == TradeDirection.Long
                        ? Math.Max(0, close - hi)
                        : Math.Max(0, lo - close);
            }

            double follow = ctx.AtrM5 * 0.12;
            bool followThrough = true;

            if (hasValidRange)
            {
                if (dir == TradeDirection.Long && close < hi + follow)
                    followThrough = false;

                if (dir == TradeDirection.Short && close > lo - follow)
                    followThrough = false;
            }

            if (!breakoutConfirmed)
                ApplyPenalty(8);

            if (!followThrough)
                ApplyPenalty(6);

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
                ApplyPenalty(6);

            if (breakoutBarAtr < MinBreakoutBarAtr)
                ApplyPenalty(6);

            if (dir == TradeDirection.Long && close <= open)
                ApplyPenalty(8);

            if (dir == TradeDirection.Short && close >= open)
                ApplyPenalty(8);

            // =====================================================
            // M1 CONFIRMATION
            // =====================================================
            bool m1Ok =
                HasDirectionalM1Trigger(ctx, dir) ||
                HasDirectionalM1FollowThrough(ctx, dir);

            if (!m1Ok)
                ApplyPenalty(6);
            else
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

            bool missingImpulse =
                string.Equals(ctx.Transition?.Reason, "MissingImpulse", StringComparison.Ordinal);

            if (missingImpulse)
            {
                score = Math.Max(0, score - 6);

                ctx.Log?.Invoke(
                    "[FLAG][PENALTY] Missing impulse detected → score penalty applied " +
                    $"symbol={ctx.Symbol} entry={Type} penalty=6 score={score}");
            }

            score = ApplyMandatoryEntryAdjustments(ctx, dir, score, true);
            score += setupScore;

            bool breakoutDetected = breakoutConfirmed;
            bool strongCandle =
                bodyRatio >= MinBreakoutBodyRatio &&
                ((dir == TradeDirection.Long && close > open) || (dir == TradeDirection.Short && close < open));
            followThrough = followThrough && m1Ok;

            if (breakoutDetected)
                triggerScore += 1;

            if (strongCandle)
                triggerScore += 1;

            if (followThrough)
                triggerScore += 2;

            score += (int)Math.Round(triggerScore * 5);

            if (triggerScore == 0)
                score -= 15;

            bool minimalTrigger = breakoutDetected || strongCandle;
            if (!minimalTrigger)
                score -= 10;

            ctx.Log?.Invoke(
                $"[TRIGGER SCORE] breakout={(breakoutDetected ? 1 : 0)} strong={(strongCandle ? 1 : 0)} follow={(followThrough ? 1 : 0)} total={triggerScore:F0} finalScore={score}");

            if (setupScore <= 0)
                score = Math.Min(score, MinScore - 10);

            ctx.Log?.Invoke(
                $"[IDX_FLAG][FINAL] dir={dir} score={score} flagATR={flagAtr:F2} slopeATR={flagSlopeAtr:F2} " +
                $"emaDistATR={distFromEmaAtr:F2} fatigue={fatigueCount}/{fatigueThreshold} hasRange={hasValidRange}"
            );


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
                    $"emaATR={distFromEmaAtr:F2} rangeState={(hasValidRange ? "OK" : "FLAG_RANGE_UNKNOWN")}",
                HtfTraceSourceStage = "Index_FlagEntry.EvaluateDir",
                HtfTraceSourceModule = "IndexHtfBiasEngine",
                HtfTraceSourceState = sourceHtfState,
                HtfTraceSourceAllowedDirection = ctx?.IndexHtfAllowedDirection ?? TradeDirection.None,
                HtfTraceSourceAlign = sourceHtfAlign,
                HtfTraceSourceCandidateDirection = dir
            };
        }

        private static bool HasDirectionalM1Trigger(EntryContext ctx, TradeDirection dir)
        {            
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
            bool hasImpulse =
                dir == TradeDirection.Long ? ctx?.HasImpulseLong_M5 == true :
                dir == TradeDirection.Short ? ctx?.HasImpulseShort_M5 == true :
                false;

            ctx.Log?.Invoke(
                $"[IDX_FLAG][REJECT] {reason} | score={Math.Max(0, score)} | dir={dir} | " +
                $"ADX={ctx?.Adx_M5:F1} Impulse={hasImpulse} ATR={ctx?.AtrM5:F1}"
            );

            bool sourceHtfAlign = IsAlignedWithAllowedDirection(ctx?.IndexHtfAllowedDirection ?? TradeDirection.None, dir);
            string sourceHtfState = ctx?.IndexHtfReason ?? "N/A";

            if (!string.IsNullOrWhiteSpace(reason) && reason.Contains("HTF_MISMATCH"))
            {
                ctx?.Log?.Invoke(
                    $"[AUDIT][HTF TRACE][REJECT] symbol={ctx?.Symbol} entryType={EntryType.Index_Flag} candidateDirection={dir} " +
                    $"rejectReason={reason} currentHtfState={sourceHtfState} " +
                    $"currentAllowedDirection={ctx?.IndexHtfAllowedDirection ?? TradeDirection.None} " +
                    $"currentHtfAlign={sourceHtfAlign} module=Index_FlagEntry");
            }

            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.Index_Flag,
                Direction = dir,
                IsValid = false,
                Score = Math.Max(0, score),
                Reason = reason,
                HtfTraceSourceStage = "Index_FlagEntry.EvaluateDir",
                HtfTraceSourceModule = "IndexHtfBiasEngine",
                HtfTraceSourceState = sourceHtfState,
                HtfTraceSourceAllowedDirection = ctx?.IndexHtfAllowedDirection ?? TradeDirection.None,
                HtfTraceSourceAlign = sourceHtfAlign,
                HtfTraceSourceCandidateDirection = dir
            };
        }

        private static bool IsAlignedWithAllowedDirection(TradeDirection allowedDirection, TradeDirection candidateDirection)
        {
            if (candidateDirection == TradeDirection.None)
                return false;

            return allowedDirection == TradeDirection.None || allowedDirection == candidateDirection;
        }

        private static int ApplyMandatoryEntryAdjustments(EntryContext ctx, TradeDirection direction, int score, bool applyTrendRegimePenalty)
        {
            return EntryDirectionQuality.Apply(
                ctx,
                direction,
                score,
                new DirectionQualityRequest
                {
                    TypeTag = "Index_FlagEntry",
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
