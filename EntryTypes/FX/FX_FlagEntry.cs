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

            // === MIN SCORE DYNAMIC BOOST (no simplification, just additive) ===
            int minBoost = 0;

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
            // LOW ADX HARD FILTER (ANTI WEAK TREND CONTINUATION)
            // =====================================================

            if (TryGetDouble(ctx, "Adx_M5", out var adxCheck))
            {
                double minAdx =
                    ctx.Session == FxSession.NewYork ? 25.0 :
                    ctx.Session == FxSession.London ? 23.0 :
                    22.0;

                if (adxCheck < minAdx)
                    return Invalid(ctx, $"ADX_TOO_LOW {adxCheck:F1}<{minAdx}", score);
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
                        // CLIMAX + rolling/flat = classic stop-sweep zone for continuation
                        if (adxM5 >= 40.0 && adxSlope <= 0.0)
                        {
                            // stricter in NY/London
                            if (ctx.Session == FxSession.NewYork || ctx.Session == FxSession.London)
                                return Invalid(ctx, $"ADX_CLIMAX_ROLLING adx={adxM5:F1} slope={adxSlope:F3}", score);

                            score -= 8;
                        }
                        else if (adxM5 >= 38.0 && adxSlope <= 0.0)
                        {
                            score -= 5;
                        }
                    }
                    else
                    {
                        // slope unknown -> soft penalty only, no hard block
                        if (adxM5 >= 42.0 && (ctx.Session == FxSession.NewYork || ctx.Session == FxSession.London))
                            score -= 4;
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
                // score -= 2;        // small penalty to push only best setups through
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
                score -= 3;
                minBoost += 2;
            }

            // =====================================================
            // COMMON LAST CLOSED BAR (ONE SOURCE OF TRUTH)
            // =====================================================
            int lastClosedIndex = ctx.M5.Count - 2;     // utolsó LEZÁRT bar
            var lastBar = ctx.M5[lastClosedIndex];
            double lastClose = lastBar.Close;

            // =====================================================
            // 1. EMA POSITION FILTER (FX-SAFE)
            // =====================================================
            int lastClosed = ctx.M5.Count - 2;
            
            double emaDistAtr = Math.Abs(lastClose - ctx.Ema21_M5) / ctx.AtrM5;

            if (emaDistAtr < 0.10)
                score -= 3;   // ne öld meg, csak büntesd

            if (emaDistAtr < 0.18 && ctx.HasImpulse_M5)
                score += 2;

            // 🔧 OVEREXT ONLY IF NO IMPULSE (ANTI-CHASE)
            if (emaDistAtr > tuning.MaxPullbackAtr * 1.5 && !ctx.HasImpulse_M5)
                score -= 6;   // eddig kinyírta a setupok 30-40%-át

            // 🔧 MOMENTUM CONTINUATION BONUS
            if (emaDistAtr > tuning.MaxPullbackAtr * 1.1 && ctx.HasImpulse_M5)
                score += 4;

            // 🔧 EMA + TREND CONTEXT BONUS (amit te ránézésre látsz)
            if (ctx.TrendDirection == TradeDirection.Short && lastClose < ctx.Ema21_M5)
                score += 3;
            else if (ctx.TrendDirection == TradeDirection.Long && lastClose > ctx.Ema21_M5)
                score += 3;

            // =====================================================
            // 2. IMPULSE QUALITY – SCORE ONLY
            // =====================================================
            if (ctx.HasImpulse_M5)
            {
                double iq = GetImpulseQuality(ctx, 5);
                if (iq > 0.70) score += 8;
                else if (iq > 0.60) score += 5;
                else if (iq > 0.50) score += 2;
                else if (iq < 0.40) score -= 5;
            }
            else
            {
                score += 6; // agresszívebb kompressziós continuation engedés
            }

            // =====================================================
            // IMPULSE EXHAUSTION FILTER
            // =====================================================

            if (ctx.HasImpulse_M5 && ctx.BarsSinceImpulse_M5 > 4)
            {
                if (!ctx.IsAtrExpanding_M5)
                    score -= 6;

                if (ctx.IsRange_M5)
                    score -= 4;
            }

            // =====================================================
            // 2B. NO-IMPULSE PENALTY (ANTI CHOP)
            // =====================================================
            if (!ctx.HasImpulse_M5 &&
                !ctx.IsAtrExpanding_M5 &&
                ctx.IsRange_M5)
            {
                score -= 4; // chop-kompresszió kiszűrése
            }

            // =====================================================
            // 2C. LOW ENERGY COMPRESSION PENALTY (anti "meh" flags)
            // =====================================================
            if (!ctx.HasImpulse_M5 && !ctx.IsAtrExpanding_M5 && !ctx.IsRange_M5)
            {
                score -= 1; // enyhébb meh bünti
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
                    score -= 3;
            }

            // =====================================================
            // 3. FLAG RANGE (SIMPLE)
            // =====================================================
            if (!TryComputeSimpleFlag(ctx, tuning.FlagBars, out var hi, out var lo, out var rangeAtr))
                return Invalid(ctx, "FLAG_FAIL", score);

            if (rangeAtr > fx.MaxFlagAtrMult)
                return Invalid(ctx, "FLAG_TOO_WIDE", score);

            // 🔧 FLAG QUALITY SCORING
            if (rangeAtr < 0.6)
                score += 4;
            else if (rangeAtr < 0.9)
                score += 3;
            else
                score += 1;

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
            const double MaxDrift = 0.25;          // chase fölött
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
                if (flagSlopeAtr > MaxDrift)
                    return Invalid(ctx, "FLAG_TOO_UPWARD_SHORT", score);

                // Túl mély lefelé csapás → nem flag
                if (flagSlopeAtr < -MaxOppositeSlope)
                    return Invalid(ctx, "FLAG_TOO_STEEP_SHORT", score);

                // Szép, lapos / enyhén csorgó flag
                if (flagSlopeAtr >= RewardZoneLow && flagSlopeAtr <= RewardZoneHigh)
                {
                    score += 2;
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
                if (flagSlopeAtr > MaxDrift)
                    return Invalid(ctx, "FLAG_TOO_UPWARD_LONG", score);

                // Túl mély lefelé esés → nem egészséges pullback
                if (flagSlopeAtr < -MaxOppositeSlope)
                    return Invalid(ctx, "FLAG_TOO_STEEP_LONG", score);

                // Szép compression / flat flag
                if (flagSlopeAtr >= RewardZoneLow && flagSlopeAtr <= RewardZoneHigh)
                {
                    score += 2;
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
                score += 2;
            }

            // =====================================================
            // 4. CONTINUATION SIGNAL
            // =====================================================
            bool breakout =
                ctx.TrendDirection == TradeDirection.Long
                    ? lastClose > hi
                    : lastClose < lo;
            bool hasM1Confirmation =
             HasM1FollowThrough(ctx) ||
             HasM1PullbackConfirm(ctx);

            // =====================================================
            // NY + HTF TRANSITION GUARD (must have breakout OR M1 confirmation)
            // =====================================================
            if (ctx.Session == FxSession.NewYork && htfTransitionZone && !breakout && !hasM1Confirmation)
            {
                return Invalid(ctx, $"NY_HTF_TRANSITION_NEEDS_CONFIRM conf={ctx.FxHtfConfidence01:F2}", score);
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
                score -= 5; // ne blokkoljon, csak tolja fel a minőségi irányba
            }

            if (needsRetestGuard && ctx.Session == FxSession.NewYork)
            {
                score -= 6;
            }

            // =====================================================
            // STRUCTURE FRESHNESS GUARD (ANTI MULTI-ENTRY)
            // =====================================================

            int barsSinceBreak =
                ctx.TrendDirection == TradeDirection.Long
                    ? ctx.BarsSinceHighBreak_M5
                    : ctx.BarsSinceLowBreak_M5;

            // ha már túl sok gyertya eltelt a struktúra törés óta,
            // és nincs friss breakout, akkor late continuation
            if (barsSinceBreak > 3 && !breakout && !hasM1Confirmation)
            {
                score -= 3;

                if (barsSinceBreak > 5 && ctx.Session == FxSession.London && !hasM1Confirmation)
                    score -= 2;
            }

            // =====================================================
            // 4A. NEW YORK STRICT CONTINUATION RULE
            // =====================================================
            if (ctx.Session == FxSession.NewYork && !breakout && !hasM1Confirmation)
            {
                if (nyEarly)
                    score -= 4;        // ne block, csak bünti

                score -= 4;            // -8 túl sok volt
                if (ctx.FxHtfAllowedDirection != TradeDirection.None && ctx.FxHtfConfidence01 >= 0.55)
                    score -= 2;
            }

            if (!breakout && !hasM1Confirmation && !softM1 && ctx.Session != FxSession.NewYork)
            {
                score -= 4;
            }

            if (softM1 && !hasM1Confirmation)
                score += 1;

            // 🔧 CONTINUATION SCORE
            if (breakout) score += 8;
            else if (hasM1Confirmation) score += 5;
            else if (ctx.LastClosedBarInTrendDirection) score += 2; // agresszív: M5 irányba zárás is kapjon kredit

            // =====================================================
            // 4B. FX HTF DIRECTION FILTER (ANTI COUNTER-HTF)
            // =====================================================
            // =====================================================
            // 4B+. HTF CONFLICT HARDENING (both directions)
            // =====================================================

            // Erős HTF bias ellenirányban: ne engedjük át (főleg NY/London)
            bool htfHasDir = ctx.FxHtfAllowedDirection != TradeDirection.None;
            bool htfConflict = htfHasDir && ctx.TrendDirection != ctx.FxHtfAllowedDirection;

            if (htfConflict)
            {
                if (ctx.FxHtfConfidence01 >= 0.75 && ctx.Session == FxSession.London)
                    return Invalid(ctx,
                        $"FX_HTF_STRONG_BLOCK {ctx.FxHtfAllowedDirection} conf={ctx.FxHtfConfidence01:F2}",
                        score);

                if (ctx.FxHtfConfidence01 >= 0.60)
                    score -= 10;
                else if (ctx.FxHtfConfidence01 >= 0.45)
                    score -= 6;
                else
                    score -= 3;
            }

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
                        score -= 8;

                        if (ctx.FxHtfConfidence01 > 0.65)
                            score -= 4;
                    }

                    score -= 6;
                }
                else
                {
                    score += 2;

                    if (m15Bull)
                        score += 2;
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
                        score -= 5;

                        if (ctx.FxHtfConfidence01 > 0.65 &&
                            ctx.FxHtfAllowedDirection == TradeDirection.Long)
                        {
                            score -= 5;
                        }
                    }

                    // ✅ ezt ADD HOZZÁ (szimmetria + anti-wrong-structure)
                    score -= 6;
                }
                else
                {
                    score += 2;

                    if (m15Bear)
                        score += 2;
                }
            }

            int min = tuning.MinScore;

            // ===================================================== 
            // 5. FINAL MIN SCORE (FIX: NY + HTF transition must be STRICTER, not looser)
            // ===================================================== 
            // Session strictness
            if (ctx.Session == FxSession.NewYork) min += 2;
            if (ctx.Session == FxSession.London)  min += 1;

            if (htfTransitionZone)
                min += 4;

            // Apply minBoost gently
            min += Math.Max(0, minBoost - 2);

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
            double range = 0;
            double body = 0;

            for (int i = 1; i <= lookback; i++)
            {
                var bar = ctx.M5[ctx.M5.Count - i];
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
            if (ctx.M1 == null || ctx.M1.Count < 3)
                return false;

            var last = ctx.M1[ctx.M1.Count - 1];
            var prev = ctx.M1[ctx.M1.Count - 2];

            double body = Math.Abs(last.Close - last.Open);
            double range = last.High - last.Low;

            if (range <= 0)
                return false;

            // body dominance – ne doji legyen
            if (body / range < 0.55)
                return false;

            if (ctx.TrendDirection == TradeDirection.Long)
                return last.Close > prev.High && last.Close > last.Open;

            if (ctx.TrendDirection == TradeDirection.Short)
                return last.Close < prev.Low && last.Close < last.Open;

            return false;
        }

        private static bool HasM1PullbackConfirm(EntryContext ctx)
        {
            // meglévő, már bevált jel
            if (!ctx.M1TriggerInTrendDirection)
                return false;

            if (ctx.M1 == null || ctx.M1.Count < 2)
                return false;

            var last = ctx.M1[ctx.M1.Count - 1];
            var prev = ctx.M1[ctx.M1.Count - 2];

            // kis visszahúzás + irányba zárás
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
