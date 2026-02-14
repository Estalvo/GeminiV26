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

            score = tuning.BaseScore;

            // =====================================================
            // 1. EMA POSITION FILTER (FX-SAFE)
            // =====================================================
            int lastClosed = ctx.M5.Count - 2;
            double lastClose = ctx.M5[lastClosed].Close;

            double emaDistAtr = Math.Abs(lastClose - ctx.Ema21_M5) / ctx.AtrM5;

            if (emaDistAtr < 0.10)
                return Invalid(ctx, "PRICE_ON_EMA", score);

            if (emaDistAtr < 0.18 && ctx.HasImpulse_M5)
                score += 2;

            // 🔧 OVEREXT ONLY IF NO IMPULSE (ANTI-CHASE)
            if (emaDistAtr > tuning.MaxPullbackAtr * 1.5 && !ctx.HasImpulse_M5)
                return Invalid(ctx, "OVEREXT_PRICE_NO_IMPULSE", score);

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
                score += 6; // 🔧 FX-ben a jó flag gyakran kompresszió
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
                // nem range-nek detektált, de energiátlan kompresszió -> kevés edge
                score -= 3;
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
                    score -= 6;
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
                score += 6;
            else if (rangeAtr < 0.9)
                score += 4;
            else
                score += 2;

            // =====================================================
            // 3B. FLAG SLOPE VALIDATION
            // =====================================================

            int lastClosedIndex = ctx.M5.Count - 2;
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
                if (flagSlopeAtr >= -RewardZoneHigh && flagSlopeAtr <= +RewardZoneLow)
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

            bool softM1 =
                ctx.Session == FxSession.London &&
                score >= tuning.MinScore + 2 &&
                !ctx.IsRange_M5;

            // 🔴 EARLY ENTRY RETEST GUARD
            // Ne lépjünk be, ha a flag széle / EMA21 még nem volt rendesen visszatesztelve
            // és nincs M5-ös irányba záró reakció

            bool needsRetestGuard =
                !breakout &&                       // még nincs tiszta breakout
                !ctx.HasReactionCandle_M5 &&       // nincs M5 reakció
                !ctx.LastClosedBarInTrendDirection // nincs irányba zárás
                &&
                (
                    // LONG: még nem tesztelte vissza a flag low / EMA21 zónát
                    (ctx.TrendDirection == TradeDirection.Long &&
                     lastClose > lo &&
                     ctx.M5[ctx.M5.Count - 1].Low > lo)

                    ||

                    // SHORT: még nem tesztelte vissza a flag high / EMA21 zónát
                    (ctx.TrendDirection == TradeDirection.Short &&
                     lastClose < hi &&
                     ctx.M5[ctx.M5.Count - 1].High < hi)
                );

            if (needsRetestGuard && ctx.Session == FxSession.London)
            {
                return Invalid(ctx, "EARLY_FLAG_NO_RETEST", score);
            }

            if (needsRetestGuard && ctx.Session == FxSession.NewYork)
            {
                score -= 6;
            }

            // =====================================================
            // 4A. NEW YORK STRICT CONTINUATION RULE
            // =====================================================
            if (ctx.Session == FxSession.NewYork && !breakout && !hasM1Confirmation)
            {
                score -= 8;

                if (ctx.FxHtfAllowedDirection != TradeDirection.None && ctx.FxHtfConfidence01 >= 0.55)
                    score -= 4;
            }

            if (!breakout && !hasM1Confirmation && !softM1 && ctx.Session != FxSession.NewYork)
            {
                score -= 4;
            }

            if (softM1 && !hasM1Confirmation)
                score += 1;

            // 🔧 CONTINUATION SCORE
            if (breakout)
                score += 5;
            else if (hasM1Confirmation)
                score += 3;

            if (ctx.Session == FxSession.Asia && !breakout)
                return Invalid(ctx, "ASIA_NO_BREAKOUT", score);

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
                        if (ctx.Session == FxSession.London)
                            return Invalid(ctx, "M5_STRUCT_NOT_BULL", score);
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
                        if (ctx.Session == FxSession.London)
                            return Invalid(ctx, "M5_STRUCT_NOT_BEAR", score);
                    }

                    score -= 6;
                }
                else
                {
                    score += 2;

                    if (m15Bear)
                        score += 2;
                }
            }

            // =====================================================
            // 5. FINAL SCORE
            // =====================================================
            // Debug assist: show key gating context in reason via score only (keeps pipeline simple)
            if (score < tuning.MinScore)
                return Invalid(ctx, $"LOW_SCORE({score}) htf={ctx.FxHtfAllowedDirection}/{ctx.FxHtfConfidence01:F2} ny={(ctx.Session == FxSession.NewYork)}", score);

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

            if (ctx.M5.Count < bars + 2)
            {
                rangeAtr = 0;
                return false;
            }

            for (int i = 1; i <= bars; i++)
            {
                var bar = ctx.M5[ctx.M5.Count - i];
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
    }
}
