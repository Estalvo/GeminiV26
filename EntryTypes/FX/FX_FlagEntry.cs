// =========================================================
// FX_FlagEntry – Phase 3.9 PATCH (Two-direction true search)
// Goal: KEEP EVERYTHING (score system, ATR/ADX gates, penalties, boosts, etc.)
// Change ONLY:
//  1) True two-direction evaluation (Long + Short candidates)
//  2) All direction-dependent logic uses flagDir (candidate dir), NOT ctx.TrendDirection
//  3) Valid()/Invalid() + logs send flagDir toward TR/TC
//  4) TrendDirection remains SOFT bias only (reward/penalty/risk context), not direction of the entry
// =========================================================

using System;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes;
using GeminiV26.Core.Matrix;
using GeminiV26.Instruments.FX;

namespace GeminiV26.EntryTypes.FX
{
    public sealed class FX_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.FX_Flag;

        private const double MinPullbackAtr = 0.15;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 30)
                return Invalid(ctx, TradeDirection.None, "CTX_NOT_READY", 0);

            if (ctx.AtrM5 <= 0)
                return Invalid(ctx, TradeDirection.None, "ATR_NOT_READY", 0);

            var matrix = ctx.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;
            if (!matrix.AllowFlag)
                return Invalid(ctx, TradeDirection.None, "SESSION_MATRIX_FLAG_DISABLED", 0);

            var fx = FxInstrumentMatrix.Get(ctx.Symbol);
            if (fx == null)
                return Invalid(ctx, TradeDirection.None, "NO_FX_PROFILE", 0);

            if (fx.FlagTuning == null || !fx.FlagTuning.TryGetValue(ctx.Session, out var tuning))
                return Invalid(ctx, TradeDirection.None, "NO_FLAG_TUNING", 0);

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
                return Invalid(ctx, TradeDirection.None, "NO_DIRECTIONAL_EDGE", 0);

            EntryEvaluation longEval;
            EntryEvaluation shortEval;

            if (allowLong)
                longEval = EvalForDir(ctx, fx, tuning, TradeDirection.Long);
            else
                longEval = Invalid(ctx, TradeDirection.Long, "DIR_BLOCKED", 0);

            if (allowShort)
                shortEval = EvalForDir(ctx, fx, tuning, TradeDirection.Short);
            else
                shortEval = Invalid(ctx, TradeDirection.Short, "DIR_BLOCKED", 0);

            if (allowLong)
                ApplyScoreModifier(longEval, matrix);

            if (allowShort)
                ApplyScoreModifier(shortEval, matrix);

            bool buyValid = longEval.IsValid;
            bool sellValid = shortEval.IsValid;

            if (!buyValid && !sellValid)
            {
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), longEval, shortEval, TradeDirection.None);
                ctx.Log?.Invoke($"[FLAG][REJECT] No valid direction buyValid={buyValid} sellValid={sellValid}");
                return Invalid(ctx, TradeDirection.None, "FLAG_DIRECTION_INVALID", Math.Max(longEval.Score, shortEval.Score));
            }

            // Prefer VALID; if both valid -> higher score wins
            if (buyValid && sellValid)
            {
                var selected = EntryDecisionPolicy.SelectBalancedEvaluation(ctx, Type, longEval, shortEval);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), longEval, shortEval, selected.Direction);
                return EntryDecisionPolicy.Normalize(selected);
            }

            if (buyValid)
            {
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), longEval, shortEval, longEval.Direction);
                return longEval;
            }

            EntryDirectionQuality.LogDecision(ctx, Type.ToString(), longEval, shortEval, shortEval.Direction);
            return shortEval;
        }

        // =====================================================
        // Directional evaluation (candidate-based flagDir)
        // =====================================================
        private static EntryEvaluation EvalForDir(
            EntryContext ctx,
            dynamic fx,
            dynamic tuning,
            TradeDirection flagDir)
        {
            int score = 0;
            int setupScore = 0;
            int penaltyBudget = 0;
            double triggerScore = 0;

            const int maxPenalty = 15;   // FX-en ennyi össz negatív korrekció lehet max
            int minBoost = 0;

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
                if (r <= 0) return;
                score += r;
            }

            score = tuning.BaseScore + 6;

            // FIX: side-specific impulse
            bool hasImpulse =
                flagDir == TradeDirection.Long ? ctx.HasImpulseLong_M5 :
                flagDir == TradeDirection.Short ? ctx.HasImpulseShort_M5 :
                false;
                
            ctx.Log?.Invoke(
                $"[FX_FLAG START] sym={ctx.Symbol} sess={ctx.Session} candDir={flagDir} " +
                $"trendDir={ctx.TrendDirection} " +
                $"htf={ctx.FxHtfAllowedDirection}/{ctx.FxHtfConfidence01:F2} " +
                $"ema50>200={(ctx.Ema50_M5 > ctx.Ema200_M5)} " +
                $"ema8>21={(ctx.Ema8_M5 > ctx.Ema21_M5)} " +
                $"ema21Slope={ctx.Ema21Slope_M5:F4} " +
                $"impulse={hasImpulse} range={ctx.IsRange_M5}"
            );

            // =====================================================
            // LOW ADX HARD FILTER – ATR AWARE + HYSTERESIS
            // =====================================================
            if (TryGetDouble(ctx, "Adx_M5", out var adxNow))
            {
                double atrPips = ctx.AtrPips_M5;

                double dynamicMinAdx;
                if (atrPips <= 2.5) dynamicMinAdx = 18.0;
                else if (atrPips <= 4.0) dynamicMinAdx = 20.0;
                else dynamicMinAdx = 22.0;

                if (ctx.Session == FxSession.NewYork)
                    dynamicMinAdx += 1.0;

                bool lowEnergy =
                    !ctx.IsAtrExpanding_M5 &&
                    !hasImpulse &&
                    ctx.IsRange_M5;

                if (lowEnergy && adxNow < dynamicMinAdx)
                {
                    ApplyPenalty(6);
                    ctx.Log?.Invoke($"[{ctx.Symbol}][FLAG_SOFT_LOW_ENERGY] candDir={flagDir} adx={adxNow:F1}<{dynamicMinAdx:F1}");
                }

                double hardFloor = dynamicMinAdx - 6.0;

                bool strongContextForAdx =
                    score >= (EntryDecisionPolicy.MinScoreThreshold + 6) &&
                    !ctx.IsRange_M5;

                if (adxNow >= 15.0 && adxNow < hardFloor - 2)
                {
                    if (!strongContextForAdx)
                        return Invalid(ctx, flagDir, $"VERY_LOW_ADX {adxNow:F1}<{hardFloor - 2:F1}", score);

                    ApplyPenalty(4);
                    ctx.Log?.Invoke($"[{ctx.Symbol}][A_ADX_SOFT] candDir={flagDir} adx={adxNow:F1} < {hardFloor:F1} strongContext => penalty=4 score={score}");
                }

                if (adxNow >= dynamicMinAdx - 1.0 && adxNow < dynamicMinAdx)
                    ApplyPenalty(2);
            }

            // --- ADX Climax / Rolling Guard ---
            if (TryGetDouble(ctx, "Adx_M5", out var adxM5))
            {
                double adxSlope = 0;
                bool hasSlope =
                    TryGetDouble(ctx, "AdxSlope_M5", out adxSlope) ||
                    TryGetDouble(ctx, "AdxSlope01_M5", out adxSlope);

                if (adxM5 >= 38.0)
                {
                    if (hasSlope)
                    {
                        if (adxM5 >= 40.0 && adxSlope <= 0.0)
                        {
                            if (ctx.Session == FxSession.NewYork || ctx.Session == FxSession.London)
                            {
                                ApplyPenalty(8);
                                minBoost += 2;
                            }
                            else
                            {
                                ApplyPenalty(6);
                            }
                        }
                        else if (adxM5 >= 38.0 && adxSlope <= 0.0)
                        {
                            ApplyPenalty(5);
                        }
                    }
                    else
                    {
                        if (adxM5 >= 42.0 && (ctx.Session == FxSession.NewYork || ctx.Session == FxSession.London))
                            ApplyPenalty(4);
                    }
                }
            }

            // --- HTF transition hardening ---
            bool htfTransitionZone =
                ctx.FxHtfAllowedDirection == TradeDirection.None &&
                ctx.FxHtfConfidence01 >= 0.50;

            if (htfTransitionZone)
                minBoost += 2;

            // --- NY early bars ---
            int nyBars = int.MaxValue;
            bool hasNyBars =
                TryGetInt(ctx, "BarsSinceSessionOpen_M5", out nyBars) ||
                TryGetInt(ctx, "SessionBarIndex_M5", out nyBars);

            bool nyEarly = ctx.Session == FxSession.NewYork && hasNyBars && nyBars <= 2;
            if (nyEarly)
            {
                ApplyPenalty(3);
                minBoost += 2;
            }

            // =====================================================
            // ASIA continuation precheck (kept, but directionless here)
            // =====================================================
            if (ctx.Session == FxSession.Asia)
            {
                if (ctx.IsRange_M5 == false && ctx.IsAtrExpanding_M5 == false)
                    return Invalid(ctx, flagDir, "ASIA_NO_ATR_EXPANSION", score);

                int asiaBarsSinceBreakPre =
                    flagDir == TradeDirection.Long
                    ? ctx.BarsSinceHighBreak_M5
                    : ctx.BarsSinceLowBreak_M5;

                if (asiaBarsSinceBreakPre > 2)
                {
                    ctx.Log?.Invoke($"[FX_FLAG ASIA_PRECHECK] candDir={flagDir} trendDir={ctx.TrendDirection} barsSinceBreak={asiaBarsSinceBreakPre} (soft only)");
                    ApplyPenalty(1);
                }

                if (ctx.FxHtfAllowedDirection == TradeDirection.None && ctx.FxHtfConfidence01 >= 0.50)
                {
                    ApplyPenalty(2);
                    minBoost += 1;
                    ctx.Log?.Invoke($"[FX_FLAG][HTF][BIAS] candDir={flagDir} state=Transition impact=ScoreOnly penalty=2 conf={ctx.FxHtfConfidence01:F2}");
                }
            }

            // =====================================================
            // COMMON LAST CLOSED BAR
            // =====================================================
            int lastClosedIndex = ctx.M5.Count - 2;
            var lastBar = ctx.M5[lastClosedIndex];
            double lastClose = lastBar.Close;

            bool lastBarInDir =
                (flagDir == TradeDirection.Long && lastBar.Close > lastBar.Open) ||
                (flagDir == TradeDirection.Short && lastBar.Close < lastBar.Open);

            double lastBody = Math.Abs(lastBar.Close - lastBar.Open);
            double lastRange = lastBar.High - lastBar.Low;
            bool lastStrongBody = lastRange > 0 && (lastBody / lastRange) >= 0.55;

            // Keep TrendDirection as soft context only
            bool lastClosesInTrendDir =
                (ctx.TrendDirection == TradeDirection.Long && lastBar.Close > lastBar.Open) ||
                (ctx.TrendDirection == TradeDirection.Short && lastBar.Close < lastBar.Open);

            // ATR slope requirement (kept)
            if (tuning.RequireAtrSlopePositive)
            {
                double atrSlope = 0;
                bool hasAtrSlope =
                    TryGetDouble(ctx, "AtrSlope_M5", out atrSlope) ||
                    TryGetDouble(ctx, "AtrSlope01_M5", out atrSlope);

                bool atrOk = hasAtrSlope ? (atrSlope > 0.0) : ctx.IsAtrExpanding_M5;
                if (!atrOk)
                    return Invalid(ctx, flagDir, "ATR_SLOPE_REQUIRED", score);
            }

            // =====================================================
            // EMA POSITION FILTER (FX-SAFE)
            // =====================================================
            double emaDistAtr = Math.Abs(lastClose - ctx.Ema21_M5) / ctx.AtrM5;

            if (emaDistAtr < 0.10) ApplyPenalty(3);
            if (emaDistAtr < 0.18 && hasImpulse) ApplyPenalty(2);
            if (emaDistAtr > tuning.MaxPullbackAtr * 1.5 && !hasImpulse) ApplyPenalty(6);

            if (emaDistAtr > tuning.MaxPullbackAtr * 1.1 && hasImpulse && !ctx.IsAtrExpanding_M5)
                ApplyPenalty(4);

            // Soft bias (keep)
            if (ctx.TrendDirection == flagDir)
            {
                if (flagDir == TradeDirection.Long && lastClose > ctx.Ema21_M5) ApplyReward(3);
                if (flagDir == TradeDirection.Short && lastClose < ctx.Ema21_M5) ApplyReward(3);
            }

            // =====================================================
            // IMPULSE QUALITY – FX CONTINUATION SAFE
            // =====================================================
            if (hasImpulse)
            {
                double iq = GetImpulseQuality(ctx, 5);

                if (iq > 0.80) ApplyPenalty(5);
                else if (iq > 0.72) ApplyPenalty(3);
                else if (iq < 0.38) ApplyPenalty(4);
            }
            else
            {
                bool compressionValid =
                    !ctx.IsRange_M5 &&
                    lastBarInDir &&
                    emaDistAtr < tuning.MaxPullbackAtr;

                if (compressionValid) ApplyReward(2);
                else ApplyPenalty(2);
            }

            if (hasImpulse && ctx.BarsSinceImpulse_M5 > 4)
            {
                if (!ctx.IsAtrExpanding_M5) ApplyPenalty(6);
                if (ctx.IsRange_M5) ApplyPenalty(4);
            }

            if (!hasImpulse && !ctx.IsAtrExpanding_M5 && ctx.IsRange_M5)
                ApplyPenalty(4);

            if (!hasImpulse && !ctx.IsAtrExpanding_M5 && !ctx.IsRange_M5)
                ApplyPenalty(1);

            if (!hasImpulse)
            {
                bool hasReaction =
                    ctx.HasReactionCandle_M5 ||
                    lastBarInDir;

                if (!hasReaction)
                    ApplyPenalty(1);
            }

            double pullbackDepthR =
                flagDir == TradeDirection.Long
                    ? ctx.PullbackDepthRLong_M5
                    : ctx.PullbackDepthRShort_M5;

            // =====================================================
            // FLAG RANGE (SIMPLE)
            // =====================================================
            double hi = 0;
            double lo = 0;
            double rangeAtr = 0;

            if (!TryComputeSimpleFlag(ctx, tuning.FlagBars, out hi, out lo, out rangeAtr, out bool hasValidRange))
                return Invalid(ctx, flagDir, "FLAG_FAIL", score);

            if (!hasValidRange)
                ctx.Log?.Invoke("[FLAG WARN] No valid range → fallback mode");

            ctx.Log?.Invoke($"[FX_FLAG RANGE] candDir={flagDir} bars={tuning.FlagBars} rangeATR={rangeAtr:F2} hasRange={hasValidRange}");

            double maxFlagAtr = tuning.MaxFlagAtrMult;
            maxFlagAtr += 0.10;
            if (ctx.Session == FxSession.London) maxFlagAtr += 0.10;
            if (ctx.Session == FxSession.NewYork) maxFlagAtr += 0.30;
            if (fx.Volatility == FxVolatilityClass.Low) maxFlagAtr += 0.20;

            ctx.Log?.Invoke($"[FX_FLAG RANGE] candDir={flagDir} maxAllowed={maxFlagAtr:F2}");

            if (rangeAtr > 0)
            {
                if (rangeAtr > maxFlagAtr)
                {
                    ApplyPenalty(4);
                    minBoost += 1;
                    ctx.Log?.Invoke($"[FX_FLAG RANGE] candDir={flagDir} softWide rangeATR={rangeAtr:F2} maxAllowed={maxFlagAtr:F2}");
                }

                if (rangeAtr < 0.6) ApplyReward(2);
                else if (rangeAtr < 0.9) ApplyReward(1);
                else ApplyPenalty(2);
            }
            else
            {
                ApplyPenalty(2);
            }

            // =====================================================
            // BREAKOUT CONFIRMATION (ENTRY TRIGGER) – DIRECTIONAL
            // =====================================================
            bool breakoutConfirmed = false;
            string breakoutReason = "NONE";

            if (ctx.HasBreakout_M1 && ctx.BreakoutDirection == flagDir)
            {
                breakoutConfirmed = true;
                breakoutReason = "M1_BREAKOUT";
            }
            else if (ctx.RangeBreakDirection == flagDir)
            {
                breakoutConfirmed = true;
                breakoutReason = "RANGE_BREAK_DIR";
            }

            ctx.Log?.Invoke(
                $"[FX_FLAG BREAKOUT] candDir={flagDir} confirmed={breakoutConfirmed} reason={breakoutReason} " +
                $"m1Breakout={ctx.HasBreakout_M1}/{ctx.BreakoutDirection} rangeDir={ctx.RangeBreakDirection}"
            );

            if (!breakoutConfirmed)
                ctx.Log?.Invoke($"[TRIGGER WAIT] candDir={flagDir} reason=WAIT_BREAKOUT score={score}");

            string flagDirReason = breakoutReason;

            // =====================================================
            // ASIA late cont check – directional (kept)
            // =====================================================
            if (ctx.Session == FxSession.Asia)
            {
                int asiaBarsSinceBreak2 =
                    flagDir == TradeDirection.Long ? ctx.BarsSinceHighBreak_M5 :
                    flagDir == TradeDirection.Short ? ctx.BarsSinceLowBreak_M5 :
                    int.MaxValue;

                if (asiaBarsSinceBreak2 > 2)
                {
                    ApplyPenalty(6);
                    flagDirReason = $"ASIA_LATE_CONT_DIR({asiaBarsSinceBreak2})";
                }
            }

            // =====================================================
            // FLAG SLOPE VALIDATION (use flagDir)
            // =====================================================
            int firstFlagIndex = lastClosedIndex - tuning.FlagBars + 1;
            if (firstFlagIndex < 0)
                return Invalid(ctx, flagDir, "FLAG_SLOPE_FAIL", score);

            double firstClose = ctx.M5[firstFlagIndex].Close;
            double lastFlagClose = ctx.M5[lastClosedIndex].Close;

            double flagSlopeAtr = (lastFlagClose - firstClose) / ctx.AtrM5;

            double maxDrift =
                ctx.Session == FxSession.London ? 0.35 :
                ctx.Session == FxSession.NewYork ? 0.30 :
                0.25;

            const double MaxSteep = 0.8;

            bool slopeRewarded = false;

            if (hasValidRange && flagDir == TradeDirection.Long)
            {
                if (flagSlopeAtr > maxDrift)
                    ApplyPenalty(4);

                if (flagSlopeAtr < -MaxSteep)
                    ApplyPenalty(4);

                if (flagSlopeAtr >= -0.35 && flagSlopeAtr <= 0.10)
                {
                    ApplyReward(2);
                    slopeRewarded = true;
                }
            }
            else if (hasValidRange && flagDir == TradeDirection.Short)
            {
                if (flagSlopeAtr < -maxDrift)
                    ApplyPenalty(4);

                if (flagSlopeAtr > MaxSteep)
                    ApplyPenalty(4);

                if (flagSlopeAtr >= -0.10 && flagSlopeAtr <= 0.35)
                {
                    ApplyReward(2);
                    slopeRewarded = true;
                }
            }

            if (!slopeRewarded && !ctx.IsRange_M5)
            {
                if (flagDir == TradeDirection.Long && flagSlopeAtr >= -0.25 && flagSlopeAtr <= 0.15)
                    ApplyReward(1);

                if (flagDir == TradeDirection.Short && flagSlopeAtr >= -0.15 && flagSlopeAtr <= 0.25)
                    ApplyReward(1);
            }

            // =====================================================
            // CONTINUATION SIGNAL (breakout from hi/lo)
            // =====================================================
            double buffer = ctx.AtrM5 * tuning.BreakoutAtrBuffer;

            bool brokeUp = hasValidRange && lastClose > hi + buffer;
            bool brokeDown = hasValidRange && lastClose < lo - buffer;

            TradeDirection m5BreakoutDir = TradeDirection.None;
            if (brokeUp && !brokeDown) m5BreakoutDir = TradeDirection.Long;
            else if (brokeDown && !brokeUp) m5BreakoutDir = TradeDirection.Short;

            bool rawBreakout = m5BreakoutDir != TradeDirection.None;

            double body = Math.Abs(lastBar.Close - lastBar.Open);
            double range = lastBar.High - lastBar.Low;
            bool strongBody = range > 0 && body / range >= 0.55;

            bool lastClosesInFlagDir =
                (flagDir == TradeDirection.Long && lastBar.Close > lastBar.Open) ||
                (flagDir == TradeDirection.Short && lastBar.Close < lastBar.Open);

            bool cleanBreakout =
                rawBreakout &&
                (m5BreakoutDir == flagDir) &&
                strongBody &&
                ctx.IsAtrExpanding_M5 &&
                lastClosesInFlagDir;

            bool structuralBreakout =
                rawBreakout &&
                (m5BreakoutDir == flagDir) &&
                lastClosesInFlagDir &&
                (
                    strongBody ||
                    (hasImpulse && ctx.IsAtrExpanding_M5)
                );

            bool breakout = cleanBreakout || structuralBreakout;

            bool hasM1Confirmation =
                HasM1FollowThrough(ctx, flagDir) ||
                HasM1PullbackConfirmDirectional(ctx, flagDir);

            bool hasTrigger = breakout || hasM1Confirmation;
            bool isPreTrigger = !hasTrigger;

            if (tuning.RequireStrongEntryCandle)
            {
                if (!hasTrigger)
                {
                    if (!lastStrongBody || !lastClosesInFlagDir)
                    {
                        ApplyPenalty(8);
                        minBoost += 1;
                    }
                }
            }

            if (tuning.RequireM1Trigger && !breakout && !hasM1Confirmation)
            {
                bool strongContext =
                    score >= EntryDecisionPolicy.MinScoreThreshold + 2 &&
                    !ctx.IsRange_M5;

                if (!strongContext)
                {
                    ApplyPenalty(5);
                    minBoost += 1;
                    ctx.Log?.Invoke($"[{ctx.Symbol}][B_M1_SOFT] candDir={flagDir} reason=M1_TRIGGER_REQUIRED impact=penalty5 score={score}");
                }

                if (strongContext)
                {
                    ApplyPenalty(2);
                    ctx.Log?.Invoke($"[{ctx.Symbol}][B_M1_SOFT] candDir={flagDir} no M1 trigger, strongContext => penalty=2 score={score}");
                }
            }

            int barsSinceBreak =
                flagDir == TradeDirection.Long
                    ? ctx.BarsSinceHighBreak_M5
                    : ctx.BarsSinceLowBreak_M5;

            // =====================================================
            // POST-BREAKOUT COOLDOWN (institutional anti-stop-sweep)
            // =====================================================
            int minBreakoutBars = 0;

            if (breakoutReason == "M1_BREAKOUT")
            {
                minBreakoutBars =
                    ctx.Session == FxSession.Asia ? 1 : 1;
            }
            else if (breakoutReason == "RANGE_BREAK_DIR")
            {
                minBreakoutBars = 1;
            }

            bool breakoutJustHappened =
                breakoutConfirmed &&
                minBreakoutBars > 0 &&
                barsSinceBreak >= 0 &&
                barsSinceBreak < minBreakoutBars;

            if (breakoutJustHappened)
            {
                ctx.Log?.Invoke(
                    $"[FX_FLAG COOLDOWN] candDir={flagDir} breakoutReason={breakoutReason} " +
                    $"barsSinceBreak={barsSinceBreak} minRequired={minBreakoutBars}"
                );
                ApplyPenalty(8);
                flagDirReason = $"POST_BREAKOUT_COOLDOWN({breakoutReason},{barsSinceBreak}<{minBreakoutBars})";
            }

            if (ctx.Session == FxSession.Asia)
            {
                int asiaBarsSinceBreak2 = barsSinceBreak;
                if (asiaBarsSinceBreak2 > 2)
                {
                    ApplyPenalty(6);
                    flagDirReason = $"ASIA_LATE_CONT_DIR({asiaBarsSinceBreak2})";
                }
            }

            if (ctx.Session == FxSession.London && htfTransitionZone && !breakout && !hasM1Confirmation)
            {
                ApplyPenalty(4);
                minBoost += 2;
            }

            // =====================================================
            // GLOBAL ADX EXHAUSTION GUARD – v2 (SOFT & SMART)
            // =====================================================
            if (TryGetDouble(ctx, "Adx_M5", out var adxNow2) &&
                (TryGetDouble(ctx, "AdxSlope_M5", out var adxSlopeNow) ||
                 TryGetDouble(ctx, "AdxSlope01_M5", out adxSlopeNow)))
            {
                if (hasTrigger)
                {
                    bool veryHighAdx = adxNow2 >= 45.0;
                    bool rollingHard = adxSlopeNow <= -0.5;
                    bool noEnergy = !ctx.IsAtrExpanding_M5;
                    bool lateStructure = barsSinceBreak > 3;

                    if (veryHighAdx && rollingHard && noEnergy && lateStructure)
                    {
                        ApplyPenalty(10);
                        flagDirReason = $"ADX_EXHAUSTION_BLOCK adx={adxNow2:F1} slope={adxSlopeNow:F2}";
                    }

                    if (adxNow2 >= 40.0 && adxSlopeNow <= -0.5)
                    {
                        ApplyPenalty(6);
                        minBoost += 1;
                    }
                }

                ctx.Log?.Invoke($"[FX_FLAG ADX] candDir={flagDir} adx={adxNow2:F1}");
                ctx.Log?.Invoke($"[FX_FLAG ADX] candDir={flagDir} slope={adxSlopeNow:F2} atrExp={ctx.IsAtrExpanding_M5}");
            }

            // NY + HTF TRANSITION BIAS
            if (ctx.Session == FxSession.NewYork && htfTransitionZone && !breakout && !hasM1Confirmation)
            {
                ApplyPenalty(3);
                minBoost += 2;
                ctx.Log?.Invoke($"[FX_FLAG][HTF][BIAS] candDir={flagDir} state=Transition impact=ScoreOnly penalty=3 conf={ctx.FxHtfConfidence01:F2}");
            }

            bool softM1 =
                ctx.Session == FxSession.London &&
                score >= EntryDecisionPolicy.MinScoreThreshold + 2 &&
                !ctx.IsRange_M5;

            // EARLY ENTRY RETEST GUARD (use flagDir + hi/lo)
            bool needsRetestGuard =
                hasValidRange &&
                !breakout &&
                !ctx.HasReactionCandle_M5 &&
                !lastClosesInFlagDir
                &&
                (
                    (flagDir == TradeDirection.Long &&
                        lastClose > lo &&
                        lastBar.Low > lo)
                    ||
                    (flagDir == TradeDirection.Short &&
                        lastClose < hi &&
                        lastBar.High < hi)
                );

            if (needsRetestGuard && ctx.Session == FxSession.London) ApplyPenalty(5);
            if (needsRetestGuard && ctx.Session == FxSession.NewYork) ApplyPenalty(6);

            // CONTINUATION CHARACTER FILTER (ANTI LATE FX)
            if (isPreTrigger && !ctx.IsAtrExpanding_M5 && ctx.IsRange_M5)
            {
                bool strongTrendContext =
                    ctx.IsRange_M5 == false &&
                    lastClosesInFlagDir &&
                    TryGetDouble(ctx, "Adx_M5", out var adxNow3) &&
                    adxNow3 >= 28;

                bool meh =
                    ctx.IsRange_M5 ||
                    (!hasImpulse && !ctx.HasReactionCandle_M5 && !lastClosesInFlagDir);

                if (meh)
                {
                    ApplyPenalty(4);
                }
                else if (strongTrendContext)
                {
                    ApplyPenalty(1);
                }
                else
                {
                    ApplyPenalty(3);
                    minBoost += 1;
                }
            }

            if (!breakout && !hasM1Confirmation)
            {
                if (barsSinceBreak > fx.MaxContinuationBarsSinceBreak)
                {
                    ApplyPenalty(8);
                    flagDirReason = $"CONT_TOO_LATE({barsSinceBreak})";
                }

                if (TryGetDouble(ctx, "TotalMoveSinceBreakAtr", out var totalMoveAtr))
                {
                    if (totalMoveAtr > fx.MaxContinuationRatr)
                    {
                        ApplyPenalty(8);
                        flagDirReason = $"CONT_STRETCHED({totalMoveAtr:F2}>{fx.MaxContinuationRatr})";
                    }
                }

                if (htfTransitionZone && !hasTrigger)
                    minBoost += 2;

                if (fx.RequireHtfAlignmentForContinuation &&
                    ctx.FxHtfAllowedDirection != TradeDirection.None &&
                    ctx.FxHtfAllowedDirection != flagDir)
                {
                    double conf = ctx.FxHtfConfidence01;

                    int penalty =
                        conf >= 0.75 ? 6 :
                        conf >= 0.60 ? 4 :
                        conf >= 0.45 ? 3 :
                        2;

                    if (hasTrigger)
                        penalty = Math.Max(0, penalty - 2);

                    if (fx.Volatility == FxVolatilityClass.High)
                        penalty = Math.Max(0, penalty - 1);

                    ApplyPenalty(penalty);
                    minBoost += 1;

                    ctx.Log?.Invoke($"[FX_FLAG HTF_SOFT_ALIGN] candDir={flagDir} conf={conf:F2} penalty={penalty}");
                }
            }

            // STRUCTURE FRESHNESS GUARD
            if (barsSinceBreak > 3 && isPreTrigger)
            {
                ApplyPenalty(3);

                if (barsSinceBreak > 5 && ctx.Session == FxSession.London && !hasM1Confirmation)
                    ApplyPenalty(2);
            }

            // SESSION-AWARE CONTINUATION SCORING
            if (isPreTrigger && !ctx.IsAtrExpanding_M5)
            {
                int basePenalty;
                switch (ctx.Session)
                {
                    case FxSession.NewYork: basePenalty = 5; break;
                    case FxSession.London: basePenalty = 4; break;
                    case FxSession.Asia: basePenalty = 5; break;
                    default: basePenalty = 4; break;
                }

                double volMultiplier =
                    fx.Volatility == FxVolatilityClass.High ? 1.2 :
                    fx.Volatility == FxVolatilityClass.Medium ? 1.0 :
                    fx.Volatility == FxVolatilityClass.Low ? 0.8 :
                    0.7;

                int finalPenalty = (int)Math.Round(basePenalty * volMultiplier);

                ApplyPenalty(finalPenalty);

                if (ctx.FxHtfAllowedDirection != TradeDirection.None &&
                    flagDir != ctx.FxHtfAllowedDirection &&
                    ctx.FxHtfConfidence01 >= 0.55)
                {
                    ApplyPenalty(2);
                }
            }

            ctx.Log?.Invoke($"[FX_FLAG TRIGGER] candDir={flagDir} breakout={breakout} m1={hasM1Confirmation} hasTrigger={hasTrigger}");

            if (softM1 && isPreTrigger) ApplyReward(1);

            if (breakout)
            {
                ApplyReward(3);

                if (hasImpulse && ctx.IsAtrExpanding_M5) ApplyReward(2);
                if (strongBody) ApplyReward(1);
            }

            // HTF CONFLICT – SOFT ONLY
            bool htfHasDir = ctx.FxHtfAllowedDirection != TradeDirection.None;
            bool htfConflict = htfHasDir && flagDir != ctx.FxHtfAllowedDirection;

            if (htfConflict)
            {
                double conf = ctx.FxHtfConfidence01;

                int penalty =
                    conf >= 0.75 ? 6 :
                    conf >= 0.60 ? 4 :
                    conf >= 0.45 ? 2 :
                    1;

                if (hasTrigger)
                    penalty = Math.Max(0, penalty - 2);

                if (fx.Volatility == FxVolatilityClass.High)
                    penalty = Math.Max(0, penalty - 1);

                ApplyPenalty(penalty);
            }

            // STRUCTURAL TREND ALIGNMENT (EMA50 / EMA200) – evaluated vs flagDir
            bool m5Bull = ctx.Ema50_M5 > ctx.Ema200_M5;
            bool m5Bear = ctx.Ema50_M5 < ctx.Ema200_M5;

            bool m15Bull = ctx.Ema50_M15 > ctx.Ema200_M15;
            bool m15Bear = ctx.Ema50_M15 < ctx.Ema200_M15;

            if (flagDir == TradeDirection.Long)
            {
                if (!m5Bull)
                {
                    bool transitionLong =
                        ctx.Ema8_M5 > ctx.Ema21_M5 &&
                        ctx.Ema21Slope_M5 > 0 &&
                        lastClosesInFlagDir &&
                        (hasImpulse || hasM1Confirmation);

                    if (!transitionLong)
                    {
                        ApplyPenalty(8);
                        if (ctx.FxHtfConfidence01 > 0.65) ApplyPenalty(3);
                    }
                    else ApplyReward(4);
                }
                else
                {
                    ApplyReward(2);
                    if (m15Bull) ApplyReward(2);
                }
            }

            if (flagDir == TradeDirection.Short)
            {
                if (!m5Bear)
                {
                    bool transitionShort =
                        ctx.Ema8_M5 < ctx.Ema21_M5 &&
                        ctx.Ema21Slope_M5 < 0 &&
                        lastClosesInFlagDir &&
                        (hasImpulse || hasM1Confirmation);

                    if (!transitionShort)
                    {
                        ApplyPenalty(8);

                        if (ctx.FxHtfConfidence01 > 0.65 &&
                            ctx.FxHtfAllowedDirection == TradeDirection.Long)
                        {
                            ApplyPenalty(3);
                        }
                    }
                    else ApplyReward(4);
                }
                else
                {
                    ApplyReward(2);
                    if (m15Bear) ApplyReward(2);
                }
            }

            // A+ gate: keep
            if (!hasTrigger && !ctx.IsAtrExpanding_M5 && score < EntryDecisionPolicy.MinScoreThreshold + 2)
                ApplyPenalty(3);

            bool continuationSignal = breakoutConfirmed;

            bool hasStructure =
                pullbackDepthR >= MinPullbackAtr;

            if (!hasStructure)
                setupScore -= 35;
            else
                setupScore += 15;

            bool hasContinuation =
                continuationSignal;

            if (hasContinuation)
                setupScore += 20;

            bool missingImpulse =
                string.Equals(ctx.Transition?.Reason, "MissingImpulse", StringComparison.Ordinal);

            if (missingImpulse)
            {
                score = Math.Max(0, score - 6);

                ctx.Log?.Invoke(
                    "[FLAG][PENALTY] Missing impulse detected → score penalty applied " +
                    $"symbol={ctx.Symbol} entry={EntryType.FX_Flag} penalty=6 score={score}");
            }

            // FINAL MIN SCORE
            int min = EntryDecisionPolicy.MinScoreThreshold;

            int sessionStrictness =
                ctx.Session == FxSession.NewYork ? 2 :
                ctx.Session == FxSession.London ? 1 :
                0;

            int effectiveBoost =
                (TryGetDouble(ctx, "Adx_M5", out var adxNow4) && adxNow4 >= 30)
                ? Math.Min(1, minBoost)
                : Math.Min(3, minBoost);

            if (hasTrigger)
            {
                min += sessionStrictness;
                min += effectiveBoost;
            }
            else
            {
                min += Math.Min(1, effectiveBoost);
            }

            if (min < 0) min = 0;

            score = ApplyMandatoryEntryAdjustments(ctx, flagDir, score, true);
            score += setupScore;

            bool breakoutDetected = breakoutConfirmed || breakout;
            bool strongCandle = strongBody || lastStrongBody;
            bool followThrough = hasM1Confirmation || continuationSignal;

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
                score = Math.Min(score, min - 10);

            ctx.Log?.Invoke($"[FX_FLAG FINAL] candDir={flagDir} score={score} min={min}");

            if (score < min)
                return Invalid(ctx, flagDir,
                    $"LOW_SCORE({score}<{min}) htf={ctx.FxHtfAllowedDirection}/{ctx.FxHtfConfidence01:F2} session={ctx.Session} boost={minBoost} flagDir={flagDir}({flagDirReason})",
                    score);

            // ✅ IMPORTANT: NO HARD GATE on ctx.TrendDirection anymore

            return Valid(ctx, flagDir, score, rangeAtr, $"FX_FLAG_V2_{ctx.Session}", flagDirReason, hi, lo, flagSlopeAtr, barsSinceBreak, hasValidRange ? "OK" : "FLAG_RANGE_UNKNOWN");
        }

        // =====================================================
        // HELPERS
        // =====================================================

        private static bool TryComputeSimpleFlag(
            EntryContext ctx,
            int bars,
            out double hi,
            out double lo,
            out double rangeAtr,
            out bool hasValidRange)
        {
            hi = double.MinValue;
            lo = double.MaxValue;
            hasValidRange = false;

            if (ctx.M5 == null || ctx.M5.Count < bars + 3)
            {
                rangeAtr = 0;
                return false;
            }

            int lastClosed = ctx.M5.Count - 2;
            int first = lastClosed - bars + 1;

            if (first < 0)
            {
                rangeAtr = 0;
                return false;
            }

            for (int i = first; i <= lastClosed; i++)
            {
                var bar = ctx.M5[i];
                hi = Math.Max(hi, bar.High);
                lo = Math.Min(lo, bar.Low);
            }

            hasValidRange = hi > lo && hi > 0 && lo > 0;
            rangeAtr = hasValidRange && ctx.AtrM5 > 0
                ? (hi - lo) / ctx.AtrM5
                : 0;
            return true;
        }

        private static double GetImpulseQuality(EntryContext ctx, int lookback)
        {
            if (ctx.M5 == null || ctx.M5.Count < lookback + 3)
                return 0;

            int lastClosed = ctx.M5.Count - 2;

            double range = 0;
            double body = 0;

            for (int k = 0; k < lookback; k++)
            {
                var bar = ctx.M5[lastClosed - k];
                range += bar.High - bar.Low;
                body += Math.Abs(bar.Close - bar.Open);
            }

            return range > 0 ? body / range : 0;
        }

        private static EntryEvaluation Valid(
            EntryContext ctx,
            TradeDirection flagDir,
            int score,
            double rangeAtr,
            string tag,
            string flagDirReason,
            double hi,
            double lo,
            double flagSlopeAtr,
            int barsSinceBreak,
            string rangeState)
            => new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.FX_Flag,
                Direction = flagDir,
                Score = score,
                IsValid = true,
                Reason =
                    $"{tag} score={score} rATR={rangeAtr:F2} " +
                    $"flagDir={flagDir}({flagDirReason}) trendDir={ctx.TrendDirection} " +
                    $"hi={hi:F5} lo={lo:F5} slopeATR={flagSlopeAtr:F2} bSinceBreak={barsSinceBreak} rangeState={rangeState}"
            };

        private static EntryEvaluation Invalid(EntryContext ctx, TradeDirection dir, string reason, int score)
            => new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.FX_Flag,
                Direction = dir,
                Score = score,
                IsValid = false,
                Reason = $"{reason} raw={score}"
            };

        private static bool HasM1FollowThrough(EntryContext ctx, TradeDirection dir)
        {
            if (ctx.M1 == null || ctx.M1.Count < 4)
                return false;

            int lastClosed = ctx.M1.Count - 2;
            int prevClosed = ctx.M1.Count - 3;

            var last = ctx.M1[lastClosed];
            var prev = ctx.M1[prevClosed];

            double body = Math.Abs(last.Close - last.Open);
            double range = last.High - last.Low;

            if (range <= 0) return false;
            if (body / range < 0.55) return false;

            if (dir == TradeDirection.Long)
                return last.Close > prev.High && last.Close > last.Open;

            if (dir == TradeDirection.Short)
                return last.Close < prev.Low && last.Close < last.Open;

            return false;
        }

        // NOTE: ctx.M1TriggerInTrendDirection is trend-based. We guard it so it won't confirm the opposite side.
        private static bool HasM1PullbackConfirmDirectional(EntryContext ctx, TradeDirection dir)
        {
            if (ctx.M1 == null || ctx.M1.Count < 3)
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

        private static bool TryGetDouble(object obj, string propName, out double value)
        {
            value = 0;
            if (obj == null) return false;

            var p = obj.GetType().GetProperty(propName);
            if (p == null) return false;

            var v = p.GetValue(obj, null);
            if (v == null) return false;

            try { value = Convert.ToDouble(v); return true; }
            catch { return false; }
        }

        private static bool TryGetInt(object obj, string propName, out int value)
        {
            value = 0;
            if (obj == null) return false;

            var p = obj.GetType().GetProperty(propName);
            if (p == null) return false;

            var v = p.GetValue(obj, null);
            if (v == null) return false;

            try { value = Convert.ToInt32(v); return true; }
            catch { return false; }
        }

        private static void ApplyScoreModifier(EntryEvaluation eval, SessionMatrixConfig matrix)
        {
            if (eval == null || matrix == null) return;
            eval.Score += (int)System.Math.Round(matrix.EntryScoreModifier);
        }

        private static int ApplyMandatoryEntryAdjustments(EntryContext ctx, TradeDirection direction, int score, bool applyTrendRegimePenalty)
        {
            return EntryDirectionQuality.Apply(
                ctx,
                direction,
                score,
                new DirectionQualityRequest
                {
                    TypeTag = "FX_FlagEntry",
                    ApplyTrendRegimePenalty = applyTrendRegimePenalty
                });
        }

    }
}
