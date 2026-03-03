using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Instruments.FX;

namespace GeminiV26.EntryTypes.METAL
{
    public class XAU_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.XAU_Flag;

        private const int GlobalWidePenaltyPer01Atr = 2;
        private const int GlobalWidePenaltyCap = 8;

        private const int SessionWidePenaltyPer01Atr = 4;
        private const int SessionWidePenaltyCap = 10;
        private const double SessionWideHardFactor = 1.6;

        private const int OverextendedPenalty_LondonNy = 5;
        private const int OverextendedPenalty_Asia = 3;

        // ==========================================================
        // METAL LATE FILTER v1 (ADD-ONLY) – VERSIONED KNOBS
        // ==========================================================
        private const string LateFilterVersion = "METAL_LATE_FILTER_V1";

        // Hard block only on true climax roll (rare, but deadly if not blocked)
        private const double XauAdxClimax = 42.0;
        private const double XagAdxClimax = 38.0;
        private const double DefaultAdxClimax = 40.0;

        private const int ClimaxMinBarsSinceImpulse = 2;

        // Soft penalties / minScore boost
        private const int LateBreakSoftPenalty_London = 6;
        private const int LateBreakSoftPenalty_NewYork = 8;
        private const int LateBreakHardExtraPenalty_NY = 4; // only when very late + no signal

        private const int TransitionMinScoreBoost = 5; // “HTF-transition proxy” (no new ctx props needed)

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 20)
                return Invalid(ctx, "CTX_NOT_READY");

            if (ctx.TrendDirection == TradeDirection.None)
                return Invalid(ctx, "NO_TREND_DIR");

            var session = ctx.Session;
            if ((int)session == 0)
                session = FxSession.London;

            return session switch
            {
                FxSession.Asia => EvaluateSession(ctx, FxSession.Asia, "XAU_FLAG_ASIA"),
                FxSession.London => EvaluateSession(ctx, FxSession.London, "XAU_FLAG_LONDON"),
                FxSession.NewYork => EvaluateSession(ctx, FxSession.NewYork, "XAU_FLAG_NY"),
                _ => Invalid(ctx, "NO_SESSION")
            };
        }

        private EntryEvaluation EvaluateSession(
            EntryContext ctx,
            FxSession session,
            string tag)
        {
            var profile = XAU_InstrumentMatrix.Get(ctx.Symbol);
            if (profile == null)
                return Invalid(ctx, "NO_METAL_PROFILE");

            if (profile.FlagTuning == null ||
                !profile.FlagTuning.TryGetValue(session, out var tuning))
                return Invalid(ctx, "NO_FLAG_TUNING");

            int score = tuning.BaseScore;
            var reasons = new List<string>(16);
            reasons.Add($"Base={tuning.BaseScore}");
            reasons.Add(LateFilterVersion);

            if (!ctx.HasImpulse_M5)
                return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "NO_IMPULSE", reasons);

            if (ctx.BarsSinceImpulse_M5 > tuning.MaxBarsSinceImpulse)
                return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "STALE_IMPULSE", reasons);

            if (!TryComputeFlag(ctx, tuning.FlagBars, out var hi, out var lo, out var rangeAtr))
                return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "FLAG_FAIL", reasons);

            // =====================================================
            // Phase XAU FlagDir Patch (FX_FlagEntry Phase 3.9 mintára)
            // Goal:
            //  1) patternDir (strukturális irány) -> flagDir alapja
            //  2) breakoutDir (trigger irány) -> csak belépési trigger
            //  3) TrendDirection marad SOFT bias (nem entry dir)
            // NOTE: XAU ctx-ben nincs irányos M1 breakout -> itt a breakoutDir M5-re épül.
            // =====================================================

            // --- COMMON last closed bar (ONE source of truth) ---
            var bars = ctx.M5;
            int lastClosedIndex = bars.Count - 2;
            var lastBar = bars[lastClosedIndex];
            double lastClose = lastBar.Close;

            // --- 1) PATTERN DIRECTION (strukturális irány) ---
            // XAU-ban most még nincs “pattern-dir” külön: induljunk TrendDirection-ból (bias),
            // később finomítható (flag slope / pole dir / M1 impulse dir).
            TradeDirection patternDir = ctx.TrendDirection;
            string patternDirReason = "TREND_DIR";

            // Ha nincs irány, nincs flag setup (ugyanaz a policy mint FX-ben)
            if (patternDir == TradeDirection.None)
                return InvalidDecisionDir(ctx, session, tag, score, tuning.MinScore, TradeDirection.None, "NO_PATTERN_DIR", reasons);

            // --- 2) BREAKOUT CONFIRMATION (ENTRY TRIGGER) ---
            // M5 breakout direction számítás: hi/lo + ATR buffer
            double buf = ctx.AtrM5 * tuning.BreakoutAtrBuffer;

            bool brokeUp = lastClose > hi + buf;
            bool brokeDown = lastClose < lo - buf;

            TradeDirection m5BreakoutDir =
                (brokeUp && !brokeDown) ? TradeDirection.Long :
                (brokeDown && !brokeUp) ? TradeDirection.Short :
                TradeDirection.None;

            bool breakoutConfirmed = m5BreakoutDir != TradeDirection.None;
            TradeDirection breakoutDir = m5BreakoutDir;
            string breakoutReason = breakoutConfirmed ? "M5_RANGE_BREAK" : "NONE";

            // M1 trigger nálad csak bool és “trend irányban” értelmezett -> itt csak debug és későbbi bonus/confirm.
            // (Ellenirányú micro flaghez majd kell irányos M1 jel a ctx-ben.)
            bool m1TrendBool = ctx.M1TriggerInTrendDirection;

            // Debug – tisztán lássuk mi a pattern vs breakout
            reasons.Add($"DBG_FLAGDIR patternDir={patternDir}({patternDirReason}) breakout={breakoutConfirmed} breakoutDir={breakoutDir}({breakoutReason}) m1TrendBool={m1TrendBool}");

            // Ha nincs breakout -> WAIT (FX mintára)
            if (!breakoutConfirmed)
                return InvalidDecisionDir(ctx, session, tag, score, tuning.MinScore, patternDir, "WAIT_BREAKOUT", reasons);

            // Biztonság: breakout irány egyezzen pattern iránnyal (FX policy)
            if (breakoutDir != patternDir)
                return InvalidDecisionDir(ctx, session, tag, score, tuning.MinScore, patternDir, "BREAKOUT_AGAINST_PATTERN", reasons);

            // --- 3) FINAL FLAG DIRECTION = patternDir ---
            TradeDirection flagDir = patternDir;
            string flagDirReason = $"{patternDirReason}|{breakoutReason}";

            // Flag-struct/energy jellegű információk (későbbi scoringhoz)
            bool flagStruct = ctx.IsValidFlagStructure_M5;

            // M5 breakout bool (flagDir-hez kötve)
            bool m5Break = breakoutConfirmed && breakoutDir == flagDir;

            // M1 confirm csak akkor “érvényes”, ha a flagDir megegyezik a trend/bias iránnyal.
            // (mert a bool nem irányos, csak “trend irányban” igaz)
            bool m1 = (flagDir == ctx.TrendDirection) && m1TrendBool;

            // FX-szerű “energyOk”
            bool energyOk = m5Break || m1 || ctx.IsAtrExpanding_M5;
            bool noFreshSignal = !m5Break && !m1;

            bool exhaustionContext =
                noFreshSignal &&
                ctx.AtrSlope_M5 < 0 &&
                ctx.AdxSlope_M5 < -0.02 &&
                Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < 6 &&
                ctx.BarsSinceImpulse_M5 >= 3;

            if (exhaustionContext && IsImpulseExhaustedXau(ctx, 6, 1.4, 0.6))
                return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "IMPULSE_EXHAUSTED", reasons);

            // ===============================
            // TREND FATIGUE (EXISTING)
            // ===============================
            bool trendFatigue =
                ctx.Adx_M5 > 40 &&
                ctx.AdxSlope_M5 < -0.02 &&
                ctx.AtrSlope_M5 < 0 &&
                Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < 6 &&
                ctx.BarsSinceImpulse_M5 > 3;

            if (trendFatigue)
                return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "TREND_FATIGUE", reasons);

            // ===============================
            // CONTINUATION STRENGTH FLOOR (NEW)
            // ===============================
            if (ctx.Adx_M5 < 23)
            {
                score -= 5;
                reasons.Add("ADX_LOW_SOFT(-5)");
            }

            // ==========================================================
            // METAL LATE FILTER v1 – 1) CLIMAX ROLL HARD BLOCK (ADD-ONLY)
            // ==========================================================
            // Goal: block only the "climax then roll" continuation entries that very often go full loss.
            // Matrix-aware: XAU higher climax threshold than XAG.
            double adxClimax = ResolveAdxClimax(profile);

            bool climaxRoll =
                ctx.Adx_M5 >= adxClimax &&
                ctx.AdxSlope_M5 < -0.02 &&
                ctx.BarsSinceImpulse_M5 >= ClimaxMinBarsSinceImpulse &&
                !ctx.IsAtrExpanding_M5; // expanding ATR can still be a valid continuation in metals

            if (climaxRoll)
            {
                reasons.Add($"CLIMAX_ROLL_BLOCK(adx={ctx.Adx_M5:F1}>=thr{adxClimax:F0} slope={ctx.AdxSlope_M5:F3} bsi={ctx.BarsSinceImpulse_M5} atrExp={ctx.IsAtrExpanding_M5})");
                return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "CLIMAX_ROLL", reasons);
            }

            // ===============================
            // WIDE FLAG penalties (EXISTING)
            // ===============================
            if (profile.MaxFlagAtrMult > 0 && rangeAtr > profile.MaxFlagAtrMult)
            {
                int p = ComputeStepPenalty(rangeAtr - profile.MaxFlagAtrMult, GlobalWidePenaltyPer01Atr, GlobalWidePenaltyCap);
                score -= p;
                reasons.Add($"FLAG_WIDE_GLOBAL(-{p})");
            }

            double sessionLimit = tuning.MaxFlagAtrMult > 0
                ? tuning.MaxFlagAtrMult
                : profile.MaxFlagAtrMult;

            if (sessionLimit > 0 && rangeAtr > sessionLimit)
            {
                if (rangeAtr > sessionLimit * SessionWideHardFactor)
                    return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "FLAG_TOO_WIDE_HARD", reasons);

                int p = ComputeStepPenalty(rangeAtr - sessionLimit, SessionWidePenaltyPer01Atr, SessionWidePenaltyCap);
                score -= p;
                reasons.Add($"FLAG_WIDE_SESSION(-{p})");
            }
            
            // ==========================================================
            // METAL LATE FILTER v1 – 2) LATE CONTINUATION PENALTY (ADD-ONLY)
            // ==========================================================
            // Goal: if structure break is not fresh AND no breakout / no M1 confirmation,
            // penalize heavily (NY stronger), but do NOT fully block -> not "no trading".
            int barsSinceBreak =
            flagDir == TradeDirection.Long
                ? ctx.BarsSinceHighBreak_M5
                : ctx.BarsSinceLowBreak_M5;

            if (barsSinceBreak > 4 && noFreshSignal)
            {
                int p = session == FxSession.NewYork ? LateBreakSoftPenalty_NewYork : LateBreakSoftPenalty_London;
                score -= p;
                reasons.Add($"LATE_BREAK(-{p}) bsb={barsSinceBreak} m5Break={m5Break} m1={m1}");

                // NY extra hardening only if very late AND no struct energy
                if (session == FxSession.NewYork && barsSinceBreak > 6 && !flagStruct)
                {
                    score -= LateBreakHardExtraPenalty_NY;
                    reasons.Add($"LATE_BREAK_NY_EXTRA(-{LateBreakHardExtraPenalty_NY}) bsb={barsSinceBreak} flagStruct={flagStruct}");
                }
            }

            // ==========================================================
            // METAL LATE FILTER v1 – 3) TRANSITION MIN SCORE BOOST (ADD-ONLY)
            // ==========================================================
            // HTF transition proxy without adding new ctx fields:
            // When trend loses "push" (ADX slope down / DI converging / ATR not expanding),
            // require a bit higher MinScore so only good signals pass.
            int minScoreAdj = tuning.MinScore;

            bool transitionRisk =
                (ctx.Adx_M5 >= 28 && ctx.AdxSlope_M5 < -0.02 && Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < 8 && !ctx.IsAtrExpanding_M5)
                || (ctx.IsRange_M5 && !ctx.IsAtrExpanding_M5 && ctx.BarsSinceImpulse_M5 > 2);

            if (transitionRisk && !m1 && !m5Break)
            {
                int before = minScoreAdj;
                minScoreAdj += 3;
                reasons.Add($"TRANSITION_MIN+3(min={before}->{minScoreAdj}) adx={ctx.Adx_M5:F1} slope={ctx.AdxSlope_M5:F3} diΔ={Math.Abs(ctx.PlusDI_M5-ctx.MinusDI_M5):F1} atrExp={ctx.IsAtrExpanding_M5} range={ctx.IsRange_M5}");
            }

            // ===============================
            // STRUCTURE CONFIRM (EXISTING) + DEBUG
            // ===============================
            int last = lastClosedIndex;
            double lastHigh = lastBar.High;
            double lastLow  = lastBar.Low;
            
            // --- safety ---
            if (last < 2)
                return InvalidDecision(ctx, session, tag, score, minScoreAdj, "BARS_NOT_READY", reasons);
            
            // NOTE: ctx.AtrM5 a helyes property nálad
            double atr = ctx.AtrM5;

            // DEBUG: alap state (minden esetben)
            reasons.Add(
                $"DBG_FLAG_STRUCT dir={ctx.TrendDirection} m1={m1} " +
                $"lastClose={lastClose:F2} lastHigh={lastHigh:F2} lastLow={lastLow:F2} " +
                $"hi={hi:F2} lo={lo:F2} atrM5={atr:F5}"
            );

        if (flagDir == TradeDirection.Long)
        {
            bool closeBreak = lastClose > hi;
            bool wickBreak  = lastHigh >= hi;

            reasons.Add(
                $"DBG_FLAG_BREAK_LONG closeBreak={closeBreak} wickBreak={wickBreak} " +
                $"(lastClose>hi={lastClose:F2}>{hi:F2}) (lastHigh>=hi={lastHigh:F2}>={hi:F2})"
            );

            // ===== XAU SOFT WICK CONTINUATION (VERY CONTROLLED) =====
            bool isXau = ctx.Symbol.ToUpper().Contains("XAU");

            bool strongBullBody =
                bars[last].Close > bars[last].Open;

            bool xauSoftBreak =
                isXau &&
                wickBreak &&
                strongBullBody &&
                ctx.Adx_M5 >= 24;   // finom erőszűrő

            reasons.Add(
                $"DBG_XAU_SOFT_LONG isXau={isXau} soft={xauSoftBreak} adx={ctx.Adx_M5:F1}"
            );

            if (!m1 && !closeBreak && !xauSoftBreak)
            {
                reasons.Add(
                    $"DBG_FLAG_REJECT_LONG reason=NO_FLAG_HIGH_BREAK " +
                    $"m1={m1} closeBreak={closeBreak} soft={xauSoftBreak}"
                );
                return InvalidDecisionDir(ctx, session, tag, score, minScoreAdj, flagDir, "NO_FLAG_HIGH_BREAK", reasons);
            }

            bool lowerHighSeq = IsLowerHighSequence(bars, last);
            reasons.Add($"DBG_FLAG_SEQ_LONG lowerHighSeq={lowerHighSeq}");

            if (lowerHighSeq)
                return InvalidDecision(ctx, session, tag, score, minScoreAdj, "LOWER_HIGH_SEQUENCE", reasons);
        }

        if (flagDir == TradeDirection.Short)
        {
            bool closeBreak = lastClose < lo;
            bool wickBreak  = lastLow <= lo;

            reasons.Add(
                $"DBG_FLAG_BREAK_SHORT closeBreak={closeBreak} wickBreak={wickBreak} " +
                $"(lastClose<lo={lastClose:F2}<{lo:F2}) (lastLow<=lo={lastLow:F2}<={lo:F2})"
            );

            // ===== XAU SOFT WICK CONTINUATION (VERY CONTROLLED) =====
            bool isXau = ctx.Symbol.ToUpper().Contains("XAU");

            bool strongBearBody =
                bars[last].Close < bars[last].Open;

            bool xauSoftBreak =
                isXau &&
                wickBreak &&
                strongBearBody &&
                ctx.Adx_M5 >= 24;   // finom erőszűrő

            reasons.Add(
                $"DBG_XAU_SOFT_SHORT isXau={isXau} soft={xauSoftBreak} adx={ctx.Adx_M5:F1}"
            );

            if (!m1 && !closeBreak && !xauSoftBreak)
            {
                reasons.Add(
                    $"DBG_FLAG_REJECT_SHORT reason=NO_FLAG_LOW_BREAK " +
                    $"m1={m1} closeBreak={closeBreak} soft={xauSoftBreak}"
                );
                return InvalidDecisionDir(ctx, session, tag, score, minScoreAdj, flagDir, "NO_FLAG_LOW_BREAK", reasons);
            }

            bool higherLowSeq = IsHigherLowSequence(bars, last);
            reasons.Add($"DBG_FLAG_SEQ_SHORT higherLowSeq={higherLowSeq}");

            if (higherLowSeq)
                return InvalidDecision(ctx, session, tag, score, minScoreAdj, "HIGHER_LOW_SEQUENCE", reasons);
        }

            // ===============================
            // BODY DOMINANCE (EXISTING)
            // ===============================
            double range = bars[last].High - bars[last].Low;
            double body = Math.Abs(bars[last].Close - bars[last].Open);

            if (range > 0)
            {
                double ratio = body / range;
                if (ratio < 0.55)
                {
                    score -= 4;
                    reasons.Add("WEAK_BODY(-4)");
                }
            }

            if (!BodyAligned(ctx, flagDir))
                return InvalidDecisionDir(ctx, session, tag, score, minScoreAdj, flagDir, "BODY_MISMATCH", reasons);

            if (m1)
            {
                score += tuning.M1TriggerBonus;
                reasons.Add($"+M1({tuning.M1TriggerBonus})");
            }
            else if (ctx.M1TriggerInTrendDirection && flagDir != ctx.TrendDirection)
            {
                reasons.Add("DBG_M1_IGNORED(oppositeDir_noDirectionalM1)");
            }

            if (flagStruct)
            {
                score += tuning.FlagQualityBonus;
                reasons.Add($"+FQ({tuning.FlagQualityBonus})");
            }

            if (score < minScoreAdj)
                return InvalidDecision(ctx, session, tag, score, minScoreAdj, "LOW_SCORE", reasons);

            reasons.Add("ACCEPT");

            return ValidDecisionDir(ctx, session, tag, score, minScoreAdj, flagDir, rangeAtr, reasons);
        }

        // ===============================
        // Helpers (EXISTING + ADD-ONLY)
        // ===============================

        private static double ResolveAdxClimax(XAU_InstrumentProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.Symbol))
                return DefaultAdxClimax;

            string s = profile.Symbol.ToUpperInvariant();

            if (s.Contains("XAU"))
                return XauAdxClimax;

            if (s.Contains("XAG"))
                return XagAdxClimax;

            // placeholder for platinum etc (kept generic)
            return DefaultAdxClimax;
        }

        private static bool IsLowerHighSequence(Bars bars, int last)
        {
            int h1 = last - 1;
            int h2 = last - 2;
            int h3 = last - 3;
            if (h3 < 0) return false;

            return bars[h1].High < bars[h2].High &&
                   bars[h2].High < bars[h3].High;
        }

        private static bool IsHigherLowSequence(Bars bars, int last)
        {
            int l1 = last - 1;
            int l2 = last - 2;
            int l3 = last - 3;
            if (l3 < 0) return false;

            return bars[l1].Low > bars[l2].Low &&
                   bars[l2].Low > bars[l3].Low;
        }

        private static int ComputeStepPenalty(double diffAtr, int per01, int cap)
        {
            if (diffAtr <= 0) return 0;
            int steps = (int)Math.Ceiling(diffAtr / 0.10);
            int p = steps * per01;
            return Math.Min(p, cap);
        }

        private static bool TryComputeFlag(
            EntryContext ctx,
            int flagBars,
            out double hi,
            out double lo,
            out double rangeAtr)
        {
            hi = double.MinValue;
            lo = double.MaxValue;
            rangeAtr = 999;

            var bars = ctx.M5;
            int lastClosed = bars.Count - 2;      // utolsó lezárt
            int start = lastClosed - flagBars + 1;
            if (start < 2) return false;

            for (int i = start; i <= lastClosed; i++)
            {
                hi = Math.Max(hi, bars[i].High);
                lo = Math.Min(lo, bars[i].Low);
            }

            if (ctx.AtrM5 <= 0)
                return false;

            rangeAtr = (hi - lo) / ctx.AtrM5;
            return true;
        }

        private static bool BreakoutClose(
            EntryContext ctx,
            double hi,
            double lo,
            double bufAtr)
        {
            var bars = ctx.M5;
            int i = bars.Count - 2;
            double close = bars[i].Close;
            double buf = ctx.AtrM5 * bufAtr;

            if (ctx.TrendDirection == TradeDirection.Long)
                return close > hi + buf;

            if (ctx.TrendDirection == TradeDirection.Short)
                return close < lo - buf;

            return false;
        }

        private static bool BodyAligned(EntryContext ctx)
        {
            var bars = ctx.M5;
            int i = bars.Count - 2;

            if (ctx.TrendDirection == TradeDirection.Long)
                return bars[i].Close > bars[i].Open;

            if (ctx.TrendDirection == TradeDirection.Short)
                return bars[i].Close < bars[i].Open;

            return false;
        }

        private static bool BodyAligned(EntryContext ctx, TradeDirection dir)
        {
            var bars = ctx.M5;
            int i = bars.Count - 2;

            if (dir == TradeDirection.Long)
                return bars[i].Close > bars[i].Open;

            if (dir == TradeDirection.Short)
                return bars[i].Close < bars[i].Open;

            return false;
        }

        private static EntryEvaluation InvalidDecision(
            EntryContext ctx,
            FxSession session,
            string tag,
            int score,
            int minScore,
            string reason,
            List<string> reasons)
        {
            string note =
                $"[{tag}] {ctx?.Symbol} {session} dir={ctx?.TrendDirection} " +
                $"Score={score} Min={minScore} Decision=REJECT Reason={reason} | " +
                string.Join(" | ", reasons);

            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = ctx?.TrendDirection ?? TradeDirection.None,
                Score = Math.Max(0, score),
                IsValid = false,
                Reason = note
            };
        }

        private static EntryEvaluation ValidDecisionDir(
            EntryContext ctx,
            FxSession session,
            string tag,
            int score,
            int minScore,
            TradeDirection dir,
            double rangeAtr,
            List<string> reasons)
        {
            string note =
                $"[{tag}] {ctx.Symbol} {session} trendDir={ctx.TrendDirection} flagDir={dir} " +
                $"Score={score} Min={minScore} RangeATR={rangeAtr:F2} " +
                $"Decision=ACCEPT | " + string.Join(" | ", reasons);

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason = note
            };
        }

        private static EntryEvaluation InvalidDecisionDir(
            EntryContext ctx,
            FxSession session,
            string tag,
            int score,
            int minScore,
            TradeDirection dir,
            string reason,
            List<string> reasons)
        {
            string note =
                $"[{tag}] {ctx?.Symbol} {session} trendDir={ctx?.TrendDirection} flagDir={dir} " +
                $"Score={score} Min={minScore} Decision=REJECT Reason={reason} | " +
                string.Join(" | ", reasons);

            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = dir,
                Score = Math.Max(0, score),
                IsValid = false,
                Reason = note
            };
        }

        private static EntryEvaluation Invalid(EntryContext ctx, string reason) =>
            new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = TradeDirection.None,
                IsValid = false,
                Reason = reason
            };

        private static bool IsImpulseExhaustedXau(
            EntryContext ctx,
            int lookback,
            double netAtrMult,
            double bodyAtrMult)
        {
            var bars = ctx.M5;
            if (bars.Count < lookback + 2 || ctx.AtrM5 <= 0)
                return false;

            int end = bars.Count - 2;
            int start = end - lookback;

            double netMove = Math.Abs(bars[end].Close - bars[start].Close);
            double body = Math.Abs(bars[end].Close - bars[end].Open);

            return netMove > ctx.AtrM5 * netAtrMult &&
                   body > ctx.AtrM5 * bodyAtrMult;
        }
    }
}
