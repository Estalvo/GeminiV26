// =========================================================
// FX_FlagEntry – Phase 3.9 PATCH
// Goal: KEEP EVERYTHING (score system, ATR/ADX gates, penalties, boosts, etc.)
// Change ONLY:
//  1) Introduce flagDir (pattern-driven direction)
//  2) Replace direction-dependent logic that uses ctx.TrendDirection with flagDir
//  3) Ensure Valid()/Invalid() + logs send flagDir toward TR/TC
//  4) TrendDirection remains SOFT bias only (reward/penalty/risk context), not direction of the entry
// =========================================================

using System;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Instruments.FX;

namespace GeminiV26.EntryTypes.FX
{
    public sealed class FX_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.FX_Flag;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            int score = 0;
            int penaltyBudget = 0;

            const int maxPenalty = 15;   // FX-en ennyi össz negatív korrekció lehet max

            // === MIN SCORE DYNAMIC BOOST (no simplification, just additive) ===
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

            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 30)
                return Invalid(ctx, TradeDirection.None, "CTX_NOT_READY", score);

            // 🔒 ATR SAFETY GUARD – IDE
            if (ctx.AtrM5 <= 0)
                return Invalid(ctx, TradeDirection.None, "ATR_NOT_READY", score);

            var fx = FxInstrumentMatrix.Get(ctx.Symbol);
            if (fx == null)
                return Invalid(ctx, TradeDirection.None, "NO_FX_PROFILE", score);

            if (fx.FlagTuning == null || !fx.FlagTuning.TryGetValue(ctx.Session, out var tuning))
                return Invalid(ctx, TradeDirection.None, "NO_FLAG_TUNING", score);

            score = tuning.BaseScore + 6;

            ctx.Log?.Invoke(
                $"[FX_FLAG START] sym={ctx.Symbol} sess={ctx.Session} " +
                $"trendDir={ctx.TrendDirection} " +
                $"htf={ctx.FxHtfAllowedDirection}/{ctx.FxHtfConfidence01:F2} " +
                $"ema50>200={(ctx.Ema50_M5 > ctx.Ema200_M5)} " +
                $"ema8>21={(ctx.Ema8_M5 > ctx.Ema21_M5)} " +
                $"ema21Slope={ctx.Ema21Slope_M5:F4} " +
                $"impulse={ctx.HasImpulse_M5} range={ctx.IsRange_M5}"
            );

            // =====================================================
            // LOW ADX HARD FILTER – ATR AWARE + HYSTERESIS
            // =====================================================

            if (TryGetDouble(ctx, "Adx_M5", out var adxNow))
            {
                double atrPips = ctx.AtrPips_M5;

                double dynamicMinAdx;

                // Volatility based baseline
                if (atrPips <= 2.5)
                    dynamicMinAdx = 18.0;
                else if (atrPips <= 4.0)
                    dynamicMinAdx = 20.0;
                else
                    dynamicMinAdx = 22.0;

                // Session tightening (NY only)
                if (ctx.Session == FxSession.NewYork)
                    dynamicMinAdx += 1.0;

                // -------------------------------------------------
                // LOW ENERGY CONTEXT CHECK
                // -------------------------------------------------
                bool lowEnergy =
                    !ctx.IsAtrExpanding_M5 &&
                    !ctx.HasImpulse_M5 &&
                    ctx.IsRange_M5;

                if (lowEnergy && adxNow < dynamicMinAdx)
                    return Invalid(ctx, TradeDirection.None,
                        $"LOW_ENERGY_NO_TREND {adxNow:F1}<{dynamicMinAdx:F1}",
                        score);

                // -------------------------------------------------
                // HARD FLOOR (extreme weak trend only) - SOFTENED
                // -------------------------------------------------
                double hardFloor = dynamicMinAdx - 4.0;

                bool strongContextForAdx =
                    score >= (tuning.MinScore + 6) &&     // kell, hogy tényleg jó legyen
                    !ctx.IsRange_M5;                      // range-ben ne engedjünk

                if (adxNow < hardFloor)
                {
                    if (!strongContextForAdx)
                        return Invalid(ctx, TradeDirection.None, $"VERY_LOW_ADX {adxNow:F1}<{hardFloor:F1}", score);

                    // strong context: ne öljük meg, csak fájjon
                    ApplyPenalty(4);
                    Console.WriteLine($"[{ctx.Symbol}][A_ADX_SOFT] adx={adxNow:F1} < {hardFloor:F1} but strongContext => penalty=4 score={score}");
                }

                // -------------------------------------------------
                // HYSTERESIS BAND (knife-edge smoothing)
                // -------------------------------------------------
                if (adxNow >= dynamicMinAdx - 1.0 &&
                    adxNow < dynamicMinAdx)
                {
                    ApplyPenalty(2);   // ne block, csak szigoríts
                }
            }

            // --- ADX Climax / Rolling Guard (reflection-safe) ---
            // Uses (if present): Adx_M5 (double), AdxSlope_M5 (double) OR AdxSlope01_M5 (double)
            if (TryGetDouble(ctx, "Adx_M5", out var adxM5))
            {
                double adxSlope = 0;
                bool hasSlope =
                    TryGetDouble(ctx, "AdxSlope_M5", out adxSlope) ||
                    TryGetDouble(ctx, "AdxSlope01_M5", out adxSlope);

                // fallback: if slope not available, treat "climax" as soft-penalty only
                if (adxM5 >= 38.0)
                {
                    if (hasSlope)
                    {
                        // CLIMAX + rolling/flat = classic stop-sweep zone
                        if (adxM5 >= 40.0 && adxSlope <= 0.0)
                        {
                            if (ctx.Session == FxSession.NewYork || ctx.Session == FxSession.London)
                            {
                                ApplyPenalty(8);
                                minBoost += 2;   // kicsit magasabb léc
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
                        // slope unknown -> soft penalty only
                        if (adxM5 >= 42.0 &&
                            (ctx.Session == FxSession.NewYork || ctx.Session == FxSession.London))
                        {
                            ApplyPenalty(4);
                        }
                    }
                }
            }

            // --- HTF TRANSITION hardening (allow=None / transition zone) ---
            bool htfTransitionZone =
                ctx.FxHtfAllowedDirection == TradeDirection.None &&
                ctx.FxHtfConfidence01 >= 0.50;

            if (htfTransitionZone)
            {
                minBoost += 2;     // raises the bar without hard-blocking
            }

            // --- NY Session Impulse Delay (first bars after NY open if available) ---
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
            // ASIA CONTINUATION HARD FILTER (ANTI LATE GRIND)
            // =====================================================
            // NOTE: in Phase 3.9 patch, Asia logic must use flagDir once we have it,
            // BUT we calculate flagDir AFTER we compute hi/lo (pattern). So keep this block
            // as-is for now, but we will re-run the Asia barsSinceBreak check after flagDir is known.
            // (No delete, we ADD a second Asia check later.)
            if (ctx.Session == FxSession.Asia)
            {
                if (ctx.IsRange_M5 == false && ctx.IsAtrExpanding_M5 == false)
                    return Invalid(ctx, TradeDirection.None, "ASIA_NO_ATR_EXPANSION", score);

                int asiaBarsSinceBreak =
                    ctx.TrendDirection == TradeDirection.Long
                    ? ctx.BarsSinceHighBreak_M5
                    : ctx.BarsSinceLowBreak_M5;

                if (asiaBarsSinceBreak > 2)
                {
                    ctx.Log?.Invoke(
                        $"[FX_FLAG ASIA_PRECHECK] lateCont trendDir={ctx.TrendDirection} barsSinceBreak={asiaBarsSinceBreak} (soft only)"
                    );

                    // opcionális: nagyon enyhe büntetés, hogy ne teljesen ignoráljuk
                    ApplyPenalty(1);

                    // NINCS return itt – a valódi irányalapú Asia check lent történik flagDir-rel
                }

                if (ctx.FxHtfAllowedDirection == TradeDirection.None &&
                    ctx.FxHtfConfidence01 >= 0.50)
                {
                    return Invalid(ctx, TradeDirection.None,
                        $"ASIA_HTF_TRANSITION_BLOCK conf={ctx.FxHtfConfidence01:F2}",
                        score);
                }
            }

            // =====================================================
            // COMMON LAST CLOSED BAR (ONE SOURCE OF TRUTH)
            // =====================================================
            int lastClosedIndex = ctx.M5.Count - 2;     // utolsó LEZÁRT bar
            var lastBar = ctx.M5[lastClosedIndex];
            double lastClose = lastBar.Close;

            // =====================================================
            // MATRIX-DRIVEN HARDENING (single place, no spaghetti)
            // =====================================================

            // 1) Strong entry candle requirement (session tuning)
            double lastBody = Math.Abs(lastBar.Close - lastBar.Open);
            double lastRange = lastBar.High - lastBar.Low;
            bool lastStrongBody = lastRange > 0 && (lastBody / lastRange) >= 0.55;

            // ---- Phase 3.9: keep TrendDirection here only as SOFT context for pre-trigger quality ----
            bool lastClosesInTrendDir =
                (ctx.TrendDirection == TradeDirection.Long && lastBar.Close > lastBar.Open) ||
                (ctx.TrendDirection == TradeDirection.Short && lastBar.Close < lastBar.Open);

            // 2) ATR slope requirement (if available -> use it, else fallback to IsAtrExpanding_M5)
            if (tuning.RequireAtrSlopePositive)
            {
                double atrSlope = 0;
                bool hasAtrSlope =
                    TryGetDouble(ctx, "AtrSlope_M5", out atrSlope) ||
                    TryGetDouble(ctx, "AtrSlope01_M5", out atrSlope);

                bool atrOk =
                    hasAtrSlope ? (atrSlope > 0.0) : ctx.IsAtrExpanding_M5;

                if (!atrOk)
                    return Invalid(ctx, TradeDirection.None, "ATR_SLOPE_REQUIRED", score);
            }

            // =====================================================
            // 1. EMA POSITION FILTER (FX-SAFE)
            // =====================================================
            int lastClosed = ctx.M5.Count - 2;

            double emaDistAtr = Math.Abs(lastClose - ctx.Ema21_M5) / ctx.AtrM5;

            if (emaDistAtr < 0.10)
                ApplyPenalty(3);

            if (emaDistAtr < 0.18 && ctx.HasImpulse_M5)
                ApplyPenalty(2);

            if (emaDistAtr > tuning.MaxPullbackAtr * 1.5 && !ctx.HasImpulse_M5)
                ApplyPenalty(6);

            if (emaDistAtr > tuning.MaxPullbackAtr * 1.1 &&
                ctx.HasImpulse_M5 &&
                !ctx.IsAtrExpanding_M5)
            {
                ApplyPenalty(4);
            }

            // ✅ KEEP: TrendDirection remains soft bias reward
            if (ctx.TrendDirection == TradeDirection.Short && lastClose < ctx.Ema21_M5)
                ApplyReward(3);
            else if (ctx.TrendDirection == TradeDirection.Long && lastClose > ctx.Ema21_M5)
                ApplyReward(3);

            // =====================================================
            // IMPULSE QUALITY – FX CONTINUATION SAFE
            // =====================================================

            if (ctx.HasImpulse_M5)
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
                    ctx.LastClosedBarInTrendDirection &&
                    emaDistAtr < tuning.MaxPullbackAtr;

                if (compressionValid) ApplyReward(2);
                else ApplyPenalty(2);
            }

            // =====================================================
            // IMPULSE EXHAUSTION FILTER
            // =====================================================

            if (ctx.HasImpulse_M5 && ctx.BarsSinceImpulse_M5 > 4)
            {
                if (!ctx.IsAtrExpanding_M5) ApplyPenalty(6);
                if (ctx.IsRange_M5) ApplyPenalty(4);
            }

            // =====================================================
            // 2B. NO-IMPULSE PENALTY (ANTI CHOP)
            // =====================================================
            if (!ctx.HasImpulse_M5 &&
                !ctx.IsAtrExpanding_M5 &&
                ctx.IsRange_M5)
            {
                ApplyPenalty(4);
            }

            // =====================================================
            // 2C. LOW ENERGY COMPRESSION PENALTY
            // =====================================================
            if (!ctx.HasImpulse_M5 && !ctx.IsAtrExpanding_M5 && !ctx.IsRange_M5)
            {
                ApplyPenalty(1);
            }

            // =====================================================
            // 2D. NO-IMPULSE REQUIRES REACTION
            // =====================================================
            if (!ctx.HasImpulse_M5)
            {
                bool hasReaction =
                    ctx.HasReactionCandle_M5 ||
                    ctx.LastClosedBarInTrendDirection;

                if (!hasReaction)
                    ApplyPenalty(1);
            }

            // =====================================================
            // 3. FLAG RANGE (SIMPLE)
            // =====================================================
            if (!TryComputeSimpleFlag(ctx, tuning.FlagBars, out var hi, out var lo, out var rangeAtr))
                return Invalid(ctx, TradeDirection.None, "FLAG_FAIL", score);

            ctx.Log?.Invoke($"[FX_FLAG RANGE] bars={tuning.FlagBars} rangeATR={rangeAtr:F2}");

            double maxFlagAtr = tuning.MaxFlagAtrMult;

            if (ctx.Session == FxSession.London) maxFlagAtr += 0.10;
            if (ctx.Session == FxSession.NewYork) maxFlagAtr += 0.30;
            if (fx.Volatility == FxVolatilityClass.Low) maxFlagAtr += 0.20;

            ctx.Log?.Invoke($"[FX_FLAG RANGE] maxAllowed={maxFlagAtr:F2}");

            if (rangeAtr > maxFlagAtr)
                return Invalid(ctx, TradeDirection.None, "FLAG_TOO_WIDE", score);

            if (rangeAtr < 0.6) ApplyReward(2);
            else if (rangeAtr < 0.9) ApplyReward(1);
            else ApplyPenalty(2);

            // =====================================================
            // Phase 3.9: FLAG DIRECTION (pattern-driven, not ctx.TrendDirection)
            // Priority:
            //  1) M1 breakout direction
            //  2) M1 impulse direction
            //  3) RangeBreakDirection
            //  4) fallback from M5 flag boundaries (if price is breaking one side)
            // =====================================================
            TradeDirection flagDir = TradeDirection.None;
            string flagDirReason = "NONE";

            if (ctx.HasBreakout_M1 && ctx.BreakoutDirection != TradeDirection.None)
            {
                flagDir = ctx.BreakoutDirection;
                flagDirReason = "M1_BREAKOUT";
            }
            else if (ctx.HasImpulse_M1 && ctx.ImpulseDirection != TradeDirection.None)
            {
                flagDir = ctx.ImpulseDirection;
                flagDirReason = "M1_IMPULSE";
            }
            else if (ctx.RangeBreakDirection != TradeDirection.None)
            {
                flagDir = ctx.RangeBreakDirection;
                flagDirReason = "RANGE_BREAK_DIR";
            }

            ctx.Log?.Invoke(
                $"[FX_FLAG DIR] trendDir={ctx.TrendDirection} flagDir={flagDir} reason={flagDirReason} " +
                $"m1Breakout={ctx.HasBreakout_M1}/{ctx.BreakoutDirection} m1Impulse={ctx.HasImpulse_M1}/{ctx.ImpulseDirection} rangeDir={ctx.RangeBreakDirection}"
            );

            // HARD SAFETY: if we cannot determine pattern direction, it's NOT a valid flag entry
            if (flagDir == TradeDirection.None)
                return Invalid(ctx, TradeDirection.None, "NO_FLAG_DIR", score);

            if (ctx.Session == FxSession.Asia)
            {
                int asiaBarsSinceBreak2 =
                    flagDir == TradeDirection.Long ? ctx.BarsSinceHighBreak_M5 :
                    flagDir == TradeDirection.Short ? ctx.BarsSinceLowBreak_M5 :
                    int.MaxValue;

                if (asiaBarsSinceBreak2 > 2)
                    return Invalid(ctx, flagDir, $"ASIA_LATE_CONT_DIR({asiaBarsSinceBreak2})", score);
            }
            
            // =====================================================
            // 3B. FLAG SLOPE VALIDATION (use flagDir)
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

            const double MaxOppositeSlope = 0.8;
            const double RewardZoneLow = -0.1;
            const double RewardZoneHigh = 0.15;

            bool slopeRewarded = false;

            if (flagDir == TradeDirection.Short)
            {
                if (flagSlopeAtr > maxDrift)
                    return Invalid(ctx, flagDir, "FLAG_TOO_UPWARD_SHORT", score);

                if (flagSlopeAtr < -MaxOppositeSlope)
                    return Invalid(ctx, flagDir, "FLAG_TOO_STEEP_SHORT", score);

                if (flagSlopeAtr >= RewardZoneLow && flagSlopeAtr <= RewardZoneHigh)
                {
                    ApplyReward(2);
                    slopeRewarded = true;
                }
            }

            if (flagDir == TradeDirection.Long)
            {
                if (flagSlopeAtr > maxDrift)
                    return Invalid(ctx, flagDir, "FLAG_TOO_UPWARD_LONG", score);

                if (flagSlopeAtr < -MaxOppositeSlope)
                    return Invalid(ctx, flagDir, "FLAG_TOO_STEEP_LONG", score);

                if (flagSlopeAtr >= RewardZoneLow && flagSlopeAtr <= RewardZoneHigh)
                {
                    ApplyReward(2);
                    slopeRewarded = true;
                }
            }

            if (flagDir == TradeDirection.Long &&
                !slopeRewarded &&
                flagSlopeAtr >= -0.1 &&
                flagSlopeAtr <= 0.25 &&
                !ctx.IsRange_M5)
            {
                ApplyReward(2);
            }

            // =====================================================
            // 4. CONTINUATION SIGNAL (breakout computed from hi/lo, then mapped to direction)
            // =====================================================
            double buffer = ctx.AtrM5 * tuning.BreakoutAtrBuffer;

            bool brokeUp = lastClose > hi + buffer;
            bool brokeDown = lastClose < lo - buffer;

            TradeDirection m5BreakoutDir = TradeDirection.None;
            if (brokeUp && !brokeDown) m5BreakoutDir = TradeDirection.Long;
            else if (brokeDown && !brokeUp) m5BreakoutDir = TradeDirection.Short;

            bool rawBreakout = m5BreakoutDir != TradeDirection.None;

            double body = Math.Abs(lastBar.Close - lastBar.Open);
            double range = lastBar.High - lastBar.Low;
            bool strongBody = range > 0 && body / range >= 0.55;

            // Phase 3.9: direction-aware "closes in dir" for breakout quality
            bool lastClosesInFlagDir =
                (flagDir == TradeDirection.Long && lastBar.Close > lastBar.Open) ||
                (flagDir == TradeDirection.Short && lastBar.Close < lastBar.Open);

            // Level 1: Clean momentum breakout
            bool cleanBreakout =
                rawBreakout &&
                (m5BreakoutDir == flagDir) &&
                strongBody &&
                ctx.IsAtrExpanding_M5 &&
                lastClosesInFlagDir;

            // Level 2: Structural breakout
            bool structuralBreakout =
                rawBreakout &&
                (m5BreakoutDir == flagDir) &&
                lastClosesInFlagDir &&
                (
                    strongBody ||
                    (ctx.HasImpulse_M5 && ctx.IsAtrExpanding_M5)
                );

            bool breakout = cleanBreakout || structuralBreakout;

            // --- M1 confirmation uses flagDir (NOT TrendDirection) ---
            bool hasM1Confirmation =
                HasM1FollowThrough(ctx, flagDir) ||
                HasM1PullbackConfirm(ctx, flagDir);

            bool hasTrigger = breakout || hasM1Confirmation;
            bool isPreTrigger = !hasTrigger;

            if (tuning.RequireStrongEntryCandle)
            {
                if (!hasTrigger)
                {
                    // NOTE: keep your original "strong candle" intent, but evaluate candle vs flagDir
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
                    score >= tuning.MinScore + 2 &&
                    !ctx.IsRange_M5;

                if (!strongContext)
                    return Invalid(ctx, flagDir, "M1_TRIGGER_REQUIRED", score);

                ApplyPenalty(2);
                Console.WriteLine($"[{ctx.Symbol}][B_M1_SOFT] no M1 trigger, strongContext => penalty=2 score={score}");
            }

            // Phase 3.9: barsSinceBreak must follow flagDir
            int barsSinceBreak =
                flagDir == TradeDirection.Long
                    ? ctx.BarsSinceHighBreak_M5
                    : ctx.BarsSinceLowBreak_M5;

            // =====================================================
            // Phase 3.9 ADD: ASIA late continuation must follow flagDir
            // (no delete of earlier block; this is the corrected direction-based check)
            // =====================================================
            if (ctx.Session == FxSession.Asia)
            {
                int asiaBarsSinceBreak2 = barsSinceBreak;
                if (asiaBarsSinceBreak2 > 2)
                    return Invalid(ctx, flagDir, $"ASIA_LATE_CONT_DIR({asiaBarsSinceBreak2})", score);
            }

            // =====================================================
            // LONDON HTF TRANSITION SWEEP GUARD
            // =====================================================
            if (ctx.Session == FxSession.London &&
                htfTransitionZone &&
                !breakout &&
                !hasM1Confirmation)
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
                    bool lateStructure = barsSinceBreak > 3; // Phase 3.9: use barsSinceBreak (flagDir)

                    if (veryHighAdx && rollingHard && noEnergy && lateStructure)
                    {
                        return Invalid(ctx, flagDir,
                            $"ADX_EXHAUSTION_BLOCK adx={adxNow:F1} slope={adxSlopeNow:F2}",
                            score);
                    }

                    if (adxNow >= 40.0 && adxSlopeNow <= -0.5)
                    {
                        ApplyPenalty(6);
                        minBoost += 1;
                    }
                }

                ctx.Log?.Invoke($"[FX_FLAG ADX] adx={adxNow2:F1}");
                ctx.Log?.Invoke($"[FX_FLAG ADX] slope={adxSlopeNow:F2} atrExp={ctx.IsAtrExpanding_M5}");
            }

            // =====================================================
            // NY + HTF TRANSITION GUARD
            // =====================================================
            if (ctx.Session == FxSession.NewYork && htfTransitionZone && !breakout && !hasM1Confirmation)
            {
                int strictMin = tuning.MinScore + 6;

                if (score < strictMin)
                {
                    return Invalid(ctx, flagDir,
                        $"NY_HTF_TRANSITION_NEEDS_CONFIRM conf={ctx.FxHtfConfidence01:F2}",
                        score);
                }

                ApplyPenalty(3);
                minBoost += 2;
            }

            bool softM1 =
                ctx.Session == FxSession.London &&
                score >= tuning.MinScore + 2 &&
                !ctx.IsRange_M5;

            // =====================================================
            // EARLY ENTRY RETEST GUARD (use flagDir + hi/lo)
            // =====================================================
            bool needsRetestGuard =
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

            if (needsRetestGuard && ctx.Session == FxSession.London)
            {
                ApplyPenalty(5);
            }

            if (needsRetestGuard && ctx.Session == FxSession.NewYork)
            {
                ApplyPenalty(6);
            }

            // =====================================================
            // CONTINUATION CHARACTER FILTER (ANTI LATE FX)
            // =====================================================
            if (isPreTrigger &&
                !ctx.IsAtrExpanding_M5 &&
                ctx.IsRange_M5)
            {
                bool strongTrendContext =
                    ctx.IsRange_M5 == false &&
                    lastClosesInFlagDir &&
                    TryGetDouble(ctx, "Adx_M5", out var adxNow3) &&
                    adxNow3 >= 28;

                bool meh =
                    ctx.IsRange_M5 ||
                    (!ctx.HasImpulse_M5 && !ctx.HasReactionCandle_M5 && !lastClosesInFlagDir);

                if (meh)
                    return Invalid(ctx, flagDir, "LOW_ENERGY_CONT", score);

                if (strongTrendContext)
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
                    return Invalid(ctx, flagDir, $"CONT_TOO_LATE({barsSinceBreak})", score);

                if (TryGetDouble(ctx, "TotalMoveSinceBreakAtr", out var totalMoveAtr))
                {
                    if (totalMoveAtr > fx.MaxContinuationRatr)
                        return Invalid(ctx, flagDir,
                            $"CONT_STRETCHED({totalMoveAtr:F2}>{fx.MaxContinuationRatr})",
                            score);
                }

                if (htfTransitionZone && !hasTrigger)
                    minBoost += 2;

                if (fx.RequireHtfAlignmentForContinuation &&
                    ctx.FxHtfAllowedDirection != TradeDirection.None &&
                    ctx.FxHtfAllowedDirection != flagDir) // Phase 3.9: compare to flagDir
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

                    ctx.Log?.Invoke($"[FX_FLAG HTF_SOFT_ALIGN] conf={conf:F2} penalty={penalty}");
                }
            }

            // =====================================================
            // STRUCTURE FRESHNESS GUARD (ANTI MULTI-ENTRY)
            // =====================================================
            if (barsSinceBreak > 3 && isPreTrigger)
            {
                ApplyPenalty(3);

                if (barsSinceBreak > 5 && ctx.Session == FxSession.London && !hasM1Confirmation)
                    ApplyPenalty(2);
            }

            // =====================================================
            // SESSION-AWARE CONTINUATION SCORING (VOLATILITY ADAPTIVE)
            // =====================================================

            if (isPreTrigger && !ctx.IsAtrExpanding_M5)
            {
                int basePenalty;

                switch (ctx.Session)
                {
                    case FxSession.NewYork:
                        basePenalty = 5;
                        break;
                    case FxSession.London:
                        basePenalty = 4;
                        break;
                    case FxSession.Asia:
                        basePenalty = 5;
                        break;
                    default:
                        basePenalty = 4;
                        break;
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

            ctx.Log?.Invoke($"[FX_FLAG TRIGGER] breakout={breakout} m1={hasM1Confirmation} hasTrigger={hasTrigger}");

            if (softM1 && isPreTrigger)
            {
                ApplyReward(1);
            }

            if (breakout)
            {
                ApplyReward(3);

                if (ctx.HasImpulse_M5 && ctx.IsAtrExpanding_M5)
                    ApplyReward(2);

                if (strongBody)
                    ApplyReward(1);
            }

            // =====================================================
            // HTF CONFLICT – SOFT ONLY (NO BLOCK) + NEW MAPPING
            // =====================================================
            bool htfHasDir = ctx.FxHtfAllowedDirection != TradeDirection.None;
            bool htfConflict = htfHasDir && flagDir != ctx.FxHtfAllowedDirection; // Phase 3.9: use flagDir

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

            // =====================================================
            // 4C. STRUCTURAL TREND ALIGNMENT (EMA50 / EMA200 M5)
            // KEEP as SOFT bias BUT evaluate against flagDir for the entry direction correctness.
            // =====================================================

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
                        (ctx.HasImpulse_M5 || hasM1Confirmation);

                    if (!transitionLong)
                    {
                        ApplyPenalty(8);

                        if (ctx.FxHtfConfidence01 > 0.65)
                            ApplyPenalty(3);
                    }
                    else
                    {
                        ApplyReward(4);
                    }
                }
                else
                {
                    ApplyReward(2);

                    if (m15Bull)
                        ApplyReward(2);
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
                        (ctx.HasImpulse_M5 || hasM1Confirmation);

                    if (!transitionShort)
                    {
                        ApplyPenalty(8);

                        if (ctx.FxHtfConfidence01 > 0.65 &&
                            ctx.FxHtfAllowedDirection == TradeDirection.Long)
                        {
                            ApplyPenalty(3);
                        }
                    }
                    else
                    {
                        ApplyReward(4);
                    }
                }
                else
                {
                    ApplyReward(2);

                    if (m15Bear)
                        ApplyReward(2);
                }
            }

            // =====================================================
            // A+ GATE: FX-en csak TRIGGER-rel mehetünk élesre
            // =====================================================
            if (!hasTrigger && !ctx.IsAtrExpanding_M5 && score < tuning.MinScore + 2)
                ApplyPenalty(3);

            // =====================================================
            // 5. FINAL MIN SCORE
            // =====================================================
            int min = tuning.MinScore;

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

            ctx.Log?.Invoke($"[FX_FLAG FINAL] score={score} min={min}");

            if (score < min)
                return Invalid(ctx, flagDir,
                    $"LOW_SCORE({score}<{min}) htf={ctx.FxHtfAllowedDirection}/{ctx.FxHtfConfidence01:F2} session={ctx.Session} boost={minBoost} flagDir={flagDir}({flagDirReason})",
                    score);

            // HARD SYSTEM SAFETY – keep existing gate, but it is NOT used for direction anymore
            if (ctx.TrendDirection == TradeDirection.None)
                return Invalid(ctx, flagDir, "NO_TREND_DIR", score);

            return Valid(ctx, flagDir, score, rangeAtr, $"FX_FLAG_V2_{ctx.Session}", flagDirReason, hi, lo, flagSlopeAtr, barsSinceBreak);
        }

        // =====================================================
        // HELPERS
        // =====================================================

        private static bool TryComputeSimpleFlag(
            EntryContext ctx,
            int bars,
            out double hi,
            out double lo,
            out double rangeAtr)
        {
            hi = double.MinValue;
            lo = double.MaxValue;

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

            rangeAtr = (hi - lo) / ctx.AtrM5;
            return hi > lo;
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

        // =====================================================
        // Phase 3.9: Valid/Invalid must carry flagDir to TR/TC
        // =====================================================

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
            int barsSinceBreak)
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
                    $"hi={hi:F5} lo={lo:F5} slopeATR={flagSlopeAtr:F2} bSinceBreak={barsSinceBreak}"
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

        // =====================================================
        // Phase 3.9: M1 confirmation must use flagDir (not TrendDirection)
        // =====================================================

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

        private static bool HasM1PullbackConfirm(EntryContext ctx, TradeDirection dir)
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
                return last.Close > last.Open && last.Low > prev.Low;

            if (dir == TradeDirection.Short)
                return last.Close < last.Open && last.High < prev.High;

            return false;
        }

        // =====================================================
        // REFLECTION-SAFE CTX ACCESSORS (NO MEMBER ASSUMPTIONS)
        // =====================================================

        private static bool TryGetDouble(object obj, string propName, out double value)
        {
            value = 0;
            if (obj == null) return false;

            var p = obj.GetType().GetProperty(propName);
            if (p == null) return false;

            var v = p.GetValue(obj, null);
            if (v == null) return false;

            try
            {
                value = Convert.ToDouble(v);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetInt(object obj, string propName, out int value)
        {
            value = 0;
            if (obj == null) return false;

            var p = obj.GetType().GetProperty(propName);
            if (p == null) return false;

            var v = p.GetValue(obj, null);
            if (v == null) return false;

            try
            {
                value = Convert.ToInt32(v);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}