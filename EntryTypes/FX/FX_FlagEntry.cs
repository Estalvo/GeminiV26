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
                return Invalid(ctx, "CTX_NOT_READY", score);

            // 🔒 ATR SAFETY GUARD – IDE
            if (ctx.AtrM5 <= 0)
                return Invalid(ctx, "ATR_NOT_READY", score);

            var fx = FxInstrumentMatrix.Get(ctx.Symbol);
            if (fx == null)
                return Invalid(ctx, "NO_FX_PROFILE", score);

            if (fx.FlagTuning == null || !fx.FlagTuning.TryGetValue(ctx.Session, out var tuning))
                return Invalid(ctx, "NO_FLAG_TUNING", score);

            score = tuning.BaseScore + 6;

            // =====================================================
            // 0. LATE TREND FILTER v1 – (ADD ONLY, NO DELETE)
            // =====================================================
            // Goal:
            // - block/penalize "late continuation" in NY/London when ADX is climactic and rolling
            // - harden HTF transition (allow=None style)
            // - delay NY continuation in first bars after session open (if the context exposes it)

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
                    return Invalid(ctx,
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
                        return Invalid(ctx, $"VERY_LOW_ADX {adxNow:F1}<{hardFloor:F1}", score);

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
            // We don't delete your existing min relax; we add a boost so transition doesn't auto-pass.
            bool htfTransitionZone =
                ctx.FxHtfAllowedDirection == TradeDirection.None &&
                ctx.FxHtfConfidence01 >= 0.50;

            if (htfTransitionZone)
            {
                // make it harder to pass borderline 50-score trades
                minBoost += 2;     // raises the bar without hard-blocking
                // ApplyPenalty(2);        // small penalty to push only best setups through
            }

            // --- NY Session Impulse Delay (first bars after NY open if available) ---
            // Uses (if present): BarsSinceSessionOpen_M5 (int) or SessionBarIndex_M5 (int)
            // We apply later too (after breakout/confirm), but we can set a pre-penalty here.
            int nyBars = int.MaxValue;
            bool hasNyBars =
                TryGetInt(ctx, "BarsSinceSessionOpen_M5", out nyBars) ||
                TryGetInt(ctx, "SessionBarIndex_M5", out nyBars);

            bool nyEarly = ctx.Session == FxSession.NewYork && hasNyBars && nyBars <= 2;

            if (nyEarly)
            {
                // discourage first-spike continuation entries; still allow breakout+confirm later
                ApplyPenalty(3);
                minBoost += 2;
            }

            // =====================================================
            // ASIA CONTINUATION HARD FILTER (ANTI LATE GRIND)
            // =====================================================
            if (ctx.Session == FxSession.Asia)
            {
                // Low volatility continuation = trap
                if (ctx.IsRange_M5 == false && ctx.IsAtrExpanding_M5 == false)
                    return Invalid(ctx, "ASIA_NO_ATR_EXPANSION", score);

                // Late continuation (structure already old)
                int asiaBarsSinceBreak =
                    ctx.TrendDirection == TradeDirection.Long
                    ? ctx.BarsSinceHighBreak_M5
                    : ctx.BarsSinceLowBreak_M5;

                if (asiaBarsSinceBreak > 2)
                    return Invalid(ctx, $"ASIA_LATE_CONT({asiaBarsSinceBreak})", score);

                // HTF transition + Asia = dangerous continuation
                if (ctx.FxHtfAllowedDirection == TradeDirection.None &&
                    ctx.FxHtfConfidence01 >= 0.50)
                {
                    return Invalid(ctx,
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

            bool lastClosesInDir =
                (ctx.TrendDirection == TradeDirection.Long  && lastBar.Close > lastBar.Open) ||
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
                    return Invalid(ctx, "ATR_SLOPE_REQUIRED", score);
            }

            // =====================================================
            // 1. EMA POSITION FILTER (FX-SAFE)
            // =====================================================
            int lastClosed = ctx.M5.Count - 2;
            
            double emaDistAtr = Math.Abs(lastClose - ctx.Ema21_M5) / ctx.AtrM5;

            if (emaDistAtr < 0.10)
                ApplyPenalty(3);   // ne öld meg, csak büntesd

            if (emaDistAtr < 0.18 && ctx.HasImpulse_M5)
                ApplyPenalty(2);

            // 🔧 OVEREXT ONLY IF NO IMPULSE (ANTI-CHASE)
            if (emaDistAtr > tuning.MaxPullbackAtr * 1.5 && !ctx.HasImpulse_M5)
                ApplyPenalty(6);   // eddig kinyírta a setupok 30-40%-át

            // 🔧 MOMENTUM CONTINUATION BONUS
            if (emaDistAtr > tuning.MaxPullbackAtr * 1.1 &&
                ctx.HasImpulse_M5 &&
                !ctx.IsAtrExpanding_M5)
            {
                ApplyPenalty(4);   // chase only if energy fading
            }

            // 🔧 EMA + TREND CONTEXT BONUS (amit te ránézésre látsz)
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

                // 🔴 túl erős = chase danger
                if (iq > 0.80)
                    ApplyPenalty(5);
                else if (iq > 0.72)
                    ApplyPenalty(3);

                // 🟡 középtartomány – NE büntesd
                // 0.50–0.72 → ideális continuation impulse

                // 🔴 gyenge impulse → nincs valódi struktúra
                else if (iq < 0.38)
                    ApplyPenalty(4);
            }
            else
            {
                bool compressionValid =
                    !ctx.IsRange_M5 &&
                    ctx.LastClosedBarInTrendDirection &&
                    emaDistAtr < tuning.MaxPullbackAtr;

                if (compressionValid)
                    ApplyReward(2);
                else
                    ApplyPenalty(2);
            }

            // =====================================================
            // IMPULSE EXHAUSTION FILTER
            // =====================================================

            if (ctx.HasImpulse_M5 && ctx.BarsSinceImpulse_M5 > 4)
            {
                if (!ctx.IsAtrExpanding_M5)
                    ApplyPenalty(6);

                if (ctx.IsRange_M5)
                    ApplyPenalty(4);
            }

            // =====================================================
            // 2B. NO-IMPULSE PENALTY (ANTI CHOP)
            // =====================================================
            if (!ctx.HasImpulse_M5 &&
                !ctx.IsAtrExpanding_M5 &&
                ctx.IsRange_M5)
            {
                ApplyPenalty(4); // chop-kompresszió kiszűrése
            }

            // =====================================================
            // 2C. LOW ENERGY COMPRESSION PENALTY (anti "meh" flags)
            // =====================================================
            if (!ctx.HasImpulse_M5 && !ctx.IsAtrExpanding_M5 && !ctx.IsRange_M5)
            {
                ApplyPenalty(1); // enyhébb meh bünti
            }

            // =====================================================
            // 2D. NO-IMPULSE REQUIRES REACTION (both directions)
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
                return Invalid(ctx, "FLAG_FAIL", score);

            double maxFlagAtr = tuning.MaxFlagAtrMult;

            // London: kicsit több levegő
            if (ctx.Session == FxSession.London)
                maxFlagAtr += 0.10;

            // NY: még picit több, mert fake spike gyakori
            if (ctx.Session == FxSession.NewYork)
                maxFlagAtr += 0.30;

            // Low vol FX (EURUSD tipikusan)
            if (fx.Volatility == FxVolatilityClass.Low)
                maxFlagAtr += 0.20;
                
            if (rangeAtr > maxFlagAtr)
                return Invalid(ctx, "FLAG_TOO_WIDE", score);

            // 🔧 FLAG QUALITY SCORING – CORRECTED
            if (rangeAtr < 0.6)
                ApplyReward(2);          // kompakt, energikus
            else if (rangeAtr < 0.9)
                ApplyReward(1);          // normál egészséges
            else
                ApplyPenalty(2);         // kezd szétesni

            // =====================================================
            // 3B. FLAG SLOPE VALIDATION
            // =====================================================

            int firstFlagIndex = lastClosedIndex - tuning.FlagBars + 1;

            if (firstFlagIndex < 0)
                return Invalid(ctx, "FLAG_SLOPE_FAIL", score);

            double firstClose = ctx.M5[firstFlagIndex].Close;
            double lastFlagClose = ctx.M5[lastClosedIndex].Close;

            double flagSlopeAtr = (lastFlagClose - firstClose) / ctx.AtrM5;

            // -----------------------------------------------------
            // FX-SAFE THRESHOLDS (NO ADX DEPENDENCY)
            // -----------------------------------------------------
            // Session-aware drift tolerance (FX adaptive)
            double maxDrift =
                ctx.Session == FxSession.London ? 0.35 :
                ctx.Session == FxSession.NewYork ? 0.30 :
                0.25;

            const double MaxOppositeSlope = 0.8;   // túl mély korrekció

            const double RewardZoneLow = -0.1;    // compression / flat
            const double RewardZoneHigh = 0.15;

            bool slopeRewarded = false;

            // =====================================================
            // BEAR FLAG (SHORT)
            // Enyhén felfelé csorgó korrekció
            // =====================================================
            if (ctx.TrendDirection == TradeDirection.Short)
            {
                // Túl erős felfelé drift → chase
                if (flagSlopeAtr > maxDrift)
                    return Invalid(ctx, "FLAG_TOO_UPWARD_SHORT", score);

                // Túl mély lefelé csapás → nem flag
                if (flagSlopeAtr < -MaxOppositeSlope)
                    return Invalid(ctx, "FLAG_TOO_STEEP_SHORT", score);

                // Szép, lapos / enyhén csorgó flag
                if (flagSlopeAtr >= RewardZoneLow && flagSlopeAtr <= RewardZoneHigh)
                {
                    ApplyReward(2);
                    slopeRewarded = true;
                }
            }

            // =====================================================
            // BULL FLAG (LONG)
            // Enyhén lefelé csorgó vagy lapos korrekció
            // =====================================================
            if (ctx.TrendDirection == TradeDirection.Long)
            {
                // Túl erős felfelé drift → chase
                if (flagSlopeAtr > maxDrift)
                    return Invalid(ctx, "FLAG_TOO_UPWARD_LONG", score);

                // Túl mély lefelé esés → nem egészséges pullback
                if (flagSlopeAtr < -MaxOppositeSlope)
                    return Invalid(ctx, "FLAG_TOO_STEEP_LONG", score);

                // Szép compression / flat flag
                if (flagSlopeAtr >= RewardZoneLow && flagSlopeAtr <= RewardZoneHigh)
                {
                    ApplyReward(2);
                    slopeRewarded = true;
                }
            }

            // =====================================================
            // EXTRA FX REWARD – bull continuation compression
            // (csak ha még nem kaptunk slope rewardot)
            // =====================================================
            if (ctx.TrendDirection == TradeDirection.Long &&
                !slopeRewarded &&
                flagSlopeAtr >= -0.1 &&
                flagSlopeAtr <= 0.25 &&
                !ctx.IsRange_M5)
            {
                ApplyReward(2);
            }

            // =====================================================
            // 4. CONTINUATION SIGNAL
            // =====================================================
            double buffer = ctx.AtrM5 * tuning.BreakoutAtrBuffer;

            bool rawBreakout =
                ctx.TrendDirection == TradeDirection.Long
                    ? lastClose > hi + buffer
                    : lastClose < lo - buffer;

            // Body dominance check
            double body = Math.Abs(lastBar.Close - lastBar.Open);
            double range = lastBar.High - lastBar.Low;
            bool strongBody = range > 0 && body / range >= 0.55;

            // ===========================================
            // BREAKOUT LOGIC – FX SAFE VERSION
            // ===========================================

            // Level 1: Clean momentum breakout
            bool cleanBreakout =
                rawBreakout &&
                strongBody &&
                ctx.IsAtrExpanding_M5 &&
                ctx.LastClosedBarInTrendDirection;

            // Level 2: Structural breakout (less strict, still safe)
            bool structuralBreakout =
                rawBreakout &&
                ctx.LastClosedBarInTrendDirection &&
                (
                    strongBody ||
                    (ctx.HasImpulse_M5 && ctx.IsAtrExpanding_M5)
                );

            bool breakout = cleanBreakout || structuralBreakout;

            // --- M1 confirmation ELŐBB ---
            bool hasM1Confirmation =
                HasM1FollowThrough(ctx) ||
                HasM1PullbackConfirm(ctx);

            // --- trigger státusz ---
            bool hasTrigger = breakout || hasM1Confirmation;
            bool isPreTrigger = !hasTrigger;

            if (tuning.RequireStrongEntryCandle)
            {
                // csak akkor érdekel, ha még nincs trigger
                // (trigger esetén úgyis a breakout/M1 a minőség)
                // -> ezt a blokkot tedd át AZUTÁNRA, hogy kiszámoltad hasTrigger-t
                if (!hasTrigger)
                {
                    if (!lastStrongBody || !lastClosesInDir || !ctx.LastClosedBarInTrendDirection)
                    {
                        ApplyPenalty(8);
                        minBoost += 1;
                    }
                }
            }

            if (tuning.RequireM1Trigger && !breakout && !hasM1Confirmation)
            {
                bool strongContext =
                    score >= tuning.MinScore + 2 &&      // +4 helyett +2
                    !ctx.IsRange_M5;

                if (!strongContext)
                    return Invalid(ctx, "M1_TRIGGER_REQUIRED", score);

                ApplyPenalty(2); // 3 -> 2
                Console.WriteLine($"[{ctx.Symbol}][B_M1_SOFT] no M1 trigger, strongContext => penalty=2 score={score}");
            }

            int barsSinceBreak =
                ctx.TrendDirection == TradeDirection.Long
                    ? ctx.BarsSinceHighBreak_M5
                    : ctx.BarsSinceLowBreak_M5;

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
                    bool rollingHard  = adxSlopeNow <= -2.0;
                    bool noEnergy     = !ctx.IsAtrExpanding_M5;
                    bool lateStructure =
                        (ctx.TrendDirection == TradeDirection.Long
                            ? ctx.BarsSinceHighBreak_M5
                            : ctx.BarsSinceLowBreak_M5) > 3;

                    // HARD BLOCK csak akkor, ha MINDEN exhaustion jel fennáll
                    if (veryHighAdx && rollingHard && noEnergy && lateStructure)
                    {
                        return Invalid(ctx,
                            $"ADX_EXHAUSTION_BLOCK adx={adxNow:F1} slope={adxSlopeNow:F2}",
                            score);
                    }

                    // SOFT PENALTY ha csak részben exhaustion
                    if (adxNow >= 42.0 && adxSlopeNow <= -1.0)
                    {
                        ApplyPenalty(4);
                        minBoost += 1;
                    }
                }
            }

            // =====================================================
            // NY + HTF TRANSITION GUARD (must have breakout OR M1 confirmation)
            // =====================================================
            if (ctx.Session == FxSession.NewYork && htfTransitionZone && !breakout && !hasM1Confirmation)
            {
                // Csak borderline setupokat tiltsunk
                int strictMin = tuning.MinScore + 6;

                if (score < strictMin)
                {
                    return Invalid(ctx,
                        $"NY_HTF_TRANSITION_NEEDS_CONFIRM conf={ctx.FxHtfConfidence01:F2}",
                        score);
                }

                // Erősebb setup átmehet, de kap büntetést
                ApplyPenalty(3);
                minBoost += 2;
            }

            bool softM1 =
                ctx.Session == FxSession.London &&
                score >= tuning.MinScore + 2 &&
                !ctx.IsRange_M5;

            // 🔴 EARLY ENTRY RETEST GUARD
            // Ne lépjünk be, ha a flag széle / EMA21 még nem volt rendesen visszatesztelve
            // és nincs M5-ös irányba záró reakció
            
            bool needsRetestGuard =
                !breakout &&
                !ctx.HasReactionCandle_M5 &&
                !ctx.LastClosedBarInTrendDirection
                &&
                (
                    (ctx.TrendDirection == TradeDirection.Long &&
                    lastClose > lo &&
                    lastBar.Low > lo)

                    ||

                    (ctx.TrendDirection == TradeDirection.Short &&
                    lastClose < hi &&
                    lastBar.High < hi)
                );

            if (needsRetestGuard && ctx.Session == FxSession.London)
            {
                ApplyPenalty(5); // ne blokkoljon, csak tolja fel a minőségi irányba
            }

            if (needsRetestGuard && ctx.Session == FxSession.NewYork)
            {
                ApplyPenalty(6);
            }

            // =====================================================
            // CONTINUATION CHARACTER FILTER (ANTI LATE FX)
            // =====================================================
            // --- LOW ENERGY CONTINUATION GUARD (balanced) ---
            if (isPreTrigger &&
                !ctx.IsAtrExpanding_M5 &&
                ctx.IsRange_M5)   // <-- EZ AZ EGYETLEN ÚJ FELTÉTEL
            {
                bool strongTrendContext =
                    ctx.IsRange_M5 == false &&
                    ctx.LastClosedBarInTrendDirection &&
                    TryGetDouble(ctx, "Adx_M5", out var adxNow3) &&
                    adxNow3 >= 28;

                bool meh =
                    ctx.IsRange_M5 ||
                    (!ctx.HasImpulse_M5 && !ctx.HasReactionCandle_M5 && !ctx.LastClosedBarInTrendDirection);

                // Chop marad block
                if (meh)
                    return Invalid(ctx, "LOW_ENERGY_CONT", score);

                // Erős trend grind → csak soft penalty
                if (strongTrendContext)
                {
                    ApplyPenalty(1);   // nem 3
                }
                else
                {
                    ApplyPenalty(3);
                    minBoost += 1;
                }
            }

            if (!breakout && !hasM1Confirmation)
            {
                // --- 1️⃣ TOO LATE STRUCTURE ---
                if (barsSinceBreak > fx.MaxContinuationBarsSinceBreak)
                    return Invalid(ctx, $"CONT_TOO_LATE({barsSinceBreak})", score);

                // --- 2️⃣ TOTAL MOVE STRETCH (NOT FLAG WIDTH!) ---
                if (TryGetDouble(ctx, "TotalMoveSinceBreakAtr", out var totalMoveAtr))
                {
                    if (totalMoveAtr > fx.MaxContinuationRatr)
                        return Invalid(ctx,
                            $"CONT_STRETCHED({totalMoveAtr:F2}>{fx.MaxContinuationRatr})",
                            score);
                }

                // --- 3️⃣ HTF TRANSITION CONTROL ---
                if (htfTransitionZone && !hasTrigger)
                    minBoost += 2;

                // --- 4️⃣ HTF ALIGNMENT REQUIREMENT ---
                if (fx.RequireHtfAlignmentForContinuation &&
                    ctx.FxHtfAllowedDirection != TradeDirection.None &&
                    ctx.FxHtfAllowedDirection != ctx.TrendDirection)
                {
                    return Invalid(ctx, "HTF_NOT_ALIGNED", score);
                }
            }

            // =====================================================
            // STRUCTURE FRESHNESS GUARD (ANTI MULTI-ENTRY)
            // =====================================================

            // ✅ itt már NEM deklarálunk újra barsSinceBreak-et
            // (már megvan a continuation filter blokkban)
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

                // Volatility adaptive scaling
                double volMultiplier =
                    fx.Volatility == FxVolatilityClass.High ? 1.2 :
                    fx.Volatility == FxVolatilityClass.Medium ? 1.0 :
                    fx.Volatility == FxVolatilityClass.Low ? 0.8 :
                    0.7; // VeryLow

                int finalPenalty = (int)Math.Round(basePenalty * volMultiplier);

                ApplyPenalty(finalPenalty);

                // HTF conflict soft penalty (ONLY if conflict)
                if (ctx.FxHtfAllowedDirection != TradeDirection.None &&
                    ctx.TrendDirection != ctx.FxHtfAllowedDirection &&
                    ctx.FxHtfConfidence01 >= 0.55)
                {
                    ApplyPenalty(2);
                }
            }

            // =====================================================
            // SOFT M1 BONUS (csak borderline setupok)
            // =====================================================

            if (softM1 && isPreTrigger)
            {
                ApplyReward(1); 
            }

            // ===========================================
            // TRIGGER REWARD – BALANCED FX VERSION
            // ===========================================

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
            bool htfConflict = htfHasDir && ctx.TrendDirection != ctx.FxHtfAllowedDirection;

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

            // High volatility pairs tolerate HTF conflict better
            if (fx.Volatility == FxVolatilityClass.High)
                penalty = Math.Max(0, penalty - 1);

            ApplyPenalty(penalty);
            }
            
            /*
            // =====================================================
            // HTF CONFLICT – ADAPTIVE FX VERSION
            // =====================================================

            bool htfHasDir = ctx.FxHtfAllowedDirection != TradeDirection.None;
            bool htfConflict = htfHasDir && ctx.TrendDirection != ctx.FxHtfAllowedDirection;

            if (htfConflict)
            {
                double conf = ctx.FxHtfConfidence01;

                // Hard block only if:
                // - strong HTF bias
                // - no trigger yet
                // - low volatility pair (less fake reversals)
                if (conf >= 0.80 &&
                    isPreTrigger &&
                    fx.Volatility == FxVolatilityClass.Low)
                {
                    return Invalid(ctx,
                        $"HTF_STRONG_BLOCK conf={conf:F2}",
                        score);
                }

                int penalty;

                if (conf >= 0.75)
                {
                    // Strong HTF bias against us → NO TRADE
                    return Invalid(ctx,
                        $"HTF_STRONG_CONFLICT_BLOCK conf={conf:F2}",
                        score);
                }
                else if (conf >= 0.60)
                {
                    penalty = 8;
                }
                else if (conf >= 0.45)
                {
                    penalty = 5;
                }
                else
                {
                    penalty = 3;
                }

                // If breakout already happened, reduce penalty
                if (hasTrigger)
                    penalty -= 1;

                // High volatility pairs tolerate HTF conflict better
                if (fx.Volatility == FxVolatilityClass.High)
                    penalty -= 1;

                if (penalty < 0) penalty = 0;

                ApplyPenalty(penalty);
            }
*/

            // =====================================================
            // 4C. STRUCTURAL TREND ALIGNMENT (EMA50 / EMA200 M5)
            // =====================================================

            // Valódi M5 trend struktúra
            bool m5Bull = ctx.Ema50_M5 > ctx.Ema200_M5;
            bool m5Bear = ctx.Ema50_M5 < ctx.Ema200_M5;

            // Opciós: M15 csak soft megerősítés
            bool m15Bull = ctx.Ema50_M15 > ctx.Ema200_M15;
            bool m15Bear = ctx.Ema50_M15 < ctx.Ema200_M15;

            // -----------------------------------------------------
            // LONG
            // -----------------------------------------------------
            if (ctx.TrendDirection == TradeDirection.Long)
            {
                if (!m5Bull)
                {
                    bool transitionLong =
                        ctx.Ema8_M5 > ctx.Ema21_M5 &&
                        ctx.Ema21Slope_M5 > 0 &&
                        ctx.LastClosedBarInTrendDirection &&
                        (ctx.HasImpulse_M5 || hasM1Confirmation);

                    if (!transitionLong)
                    {
                        ApplyPenalty(8); // egységes kemény bünti

                        if (ctx.FxHtfConfidence01 > 0.65)
                            ApplyPenalty(3); // extra HTF bünti, de nem brutál
                    }
                    else
                    {
                        ApplyReward(4); // transition esetben enyhébb bünti
                    }
                }
                else
                {
                    ApplyReward(2);

                    if (m15Bull)
                        ApplyReward(2);
                }
            }

            // -----------------------------------------------------
            // SHORT
            // -----------------------------------------------------
            if (ctx.TrendDirection == TradeDirection.Short)
            {
                if (!m5Bear)
                {
                    bool transitionShort =
                        ctx.Ema8_M5 < ctx.Ema21_M5 &&
                        ctx.Ema21Slope_M5 < 0 &&
                        ctx.LastClosedBarInTrendDirection &&
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
            // (kevesebb trade, nagyobb winrate)
            // =====================================================
            if (!hasTrigger && !ctx.IsAtrExpanding_M5 && score < tuning.MinScore + 2)
                ApplyPenalty(3);

            // ===================================================== 
            // 5. FINAL MIN SCORE (FIX: NY + HTF transition must be STRICTER, not looser)
            // ===================================================== 
            int min = tuning.MinScore;

            // Session strictness csak akkor, ha VAN trigger (breakout/M1 confirm)
            int sessionStrictness =
                ctx.Session == FxSession.NewYork ? 2 :
                ctx.Session == FxSession.London  ? 1 :
                0;

            // Controlled boost (marad a logikád)
            int effectiveBoost =
                (TryGetDouble(ctx, "Adx_M5", out var adxNow4) && adxNow4 >= 30)
                ? Math.Min(1, minBoost)
                : Math.Min(3, minBoost);

            // ✅ Pretrigger flag-vadászat: NE legyen dupla szigor
            if (hasTrigger)
            {
                min += sessionStrictness;
                min += effectiveBoost;          // trigger után lehet szigor
            }
            else
            {
                // pretrigger: csak minimális “minBoost” hasson
                min += Math.Min(1, effectiveBoost);
                // sessionStrictness = 0 pretriggerben
            }

            if (min < 0) min = 0;

            if (score < min)
                return Invalid(ctx,
                    $"LOW_SCORE({score}<{min}) htf={ctx.FxHtfAllowedDirection}/{ctx.FxHtfConfidence01:F2} session={ctx.Session} boost={minBoost}",
                    score);

            // HARD SYSTEM SAFETY
            if (ctx.TrendDirection == TradeDirection.None)
                return Invalid(ctx, "NO_TREND_DIR", score);

            return Valid(ctx, score, rangeAtr, $"FX_FLAG_V2_{ctx.Session}");
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

            // kell legalább: bars db lezárt + 1 futó + 1 biztonság
            if (ctx.M5 == null || ctx.M5.Count < bars + 3)
            {
                rangeAtr = 0;
                return false;
            }

            int lastClosed = ctx.M5.Count - 2;          // utolsó LEZÁRT
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
            if (ctx.M5 == null || ctx.M5.Count < lookback + 3) // kell lezárt lookback + safety
                return 0;

            int lastClosed = ctx.M5.Count - 2;

            double range = 0;
            double body = 0;

            for (int k = 0; k < lookback; k++)
            {
                var bar = ctx.M5[lastClosed - k]; // csak LEZÁRT barok
                range += bar.High - bar.Low;
                body += Math.Abs(bar.Close - bar.Open);
            }

            return range > 0 ? body / range : 0;
        }

        private static EntryEvaluation Valid(EntryContext ctx, int score, double rangeAtr, string tag)
            => new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.FX_Flag,
                Direction = ctx.TrendDirection,
                Score = score,
                IsValid = true,
                Reason = $"{tag} score={score} rATR={rangeAtr:F2}"
            };

        private static EntryEvaluation Invalid(EntryContext ctx, string reason, int score)
            => new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.FX_Flag,
                Direction = ctx?.TrendDirection ?? TradeDirection.None,
                Score = score,
                IsValid = false,
                Reason = $"{reason} raw={score}"
            };

        private static bool HasM1FollowThrough(EntryContext ctx)
        {
            if (ctx.M1 == null || ctx.M1.Count < 4) // kell lezárt last + lezárt prev + safety
                return false;

            int lastClosed = ctx.M1.Count - 2;   // utolsó LEZÁRT
            int prevClosed = ctx.M1.Count - 3;

            var last = ctx.M1[lastClosed];
            var prev = ctx.M1[prevClosed];

            double body = Math.Abs(last.Close - last.Open);
            double range = last.High - last.Low;

            if (range <= 0) return false;
            if (body / range < 0.55) return false;

            if (ctx.TrendDirection == TradeDirection.Long)
                return last.Close > prev.High && last.Close > last.Open;

            if (ctx.TrendDirection == TradeDirection.Short)
                return last.Close < prev.Low && last.Close < last.Open;

            return false;
        }

        private static bool HasM1PullbackConfirm(EntryContext ctx)
        {
            if (!ctx.M1TriggerInTrendDirection)
                return false;

            if (ctx.M1 == null || ctx.M1.Count < 3)
                return false;

            int lastClosed = ctx.M1.Count - 2;
            int prevClosed = ctx.M1.Count - 3;

            var last = ctx.M1[lastClosed];
            var prev = ctx.M1[prevClosed];

            if (ctx.TrendDirection == TradeDirection.Long)
                return last.Close > last.Open && last.Low > prev.Low;

            if (ctx.TrendDirection == TradeDirection.Short)
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
