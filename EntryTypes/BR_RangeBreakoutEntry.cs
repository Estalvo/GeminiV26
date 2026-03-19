// =========================================================
// GEMINI V26 – BR_RangeBreakoutEntry (Quality + Format pass)
// Rulebook 1.0 compliant EntryType
//
// Alapelv:
// - RangeBreakout csak VALÓDI range-ből indulhat
// - Breakout nélkül nincs setup (hard rule)
// - Fakeout és gyenge break minőségileg büntetett
// - Minimum score (50) ENTRYTYPE szinten dől el
//
// A score RELATÍV MINŐSÉG, nem belépési engedély.
// =========================================================

using GeminiV26.Core;
using GeminiV26.Core.Entry;
using System;

namespace GeminiV26.EntryTypes
{
    public class BR_RangeBreakoutEntry : IEntryType
    {
        public EntryType Type => EntryType.BR_RangeBreakout;

        // --- Strukturális paraméterek ---
        private const int MinRangeBars = 20;
        private const double MinBreakATR = 0.3;
        private const int MaxFakeoutBars = 1;
        private const double MaxSlopeForRange = 0.0005;

        // --- Szabálykönyv ---
        private const int MIN_SCORE = EntryDecisionPolicy.MinScoreThreshold;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            var eval = new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = TradeDirection.None,
                Score = 0,
                IsValid = false,
                Reason = ""
            };

            int score = 0;
            int setupScore = 0;

            // =========================================================
            // 1️⃣ RANGE KÖRNYEZET (HARD + SOFT MINŐSÉG)
            // =========================================================

            // HARD RULE – nem range → nincs range breakout
            if (ctx.IsRange_M5)
            {
                score += 20;
            }
            else
            {
                score -= 10;
                eval.Reason += "SoftRange;";
            }

            bool rangeLongEnough = ctx.RangeBarCount_M5 >= MinRangeBars;
            bool flatEma =
                Math.Abs(ctx.Ema21Slope_M15) <= MaxSlopeForRange &&
                Math.Abs(ctx.Ema21Slope_M5) <= MaxSlopeForRange;

            if (rangeLongEnough)
                score += 10;
            else
                eval.Reason += "RangeTooShort;";

            if (flatEma)
                score += 10;
            else
                eval.Reason += "TrendDetected;";

            // Quality gate – ha se hossz, se laposság → gyenge range
            if (!rangeLongEnough && !flatEma)
            {
                eval.Reason += "WeakRangeQuality;";
                return eval;
            }

            var instrumentClass = SymbolRouting.ResolveInstrumentClass(ctx.Symbol);

            if (instrumentClass == InstrumentClass.METAL)
            {
                bool hasStructure =
                    ctx.IsValidFlagStructure_M5
                    || (ctx.PullbackBars_M5 >= 2 && ctx.IsPullbackDecelerating_M5)
                    || ctx.HasEarlyPullback_M5;

                if (!hasStructure)
                    setupScore -= 40;
                else
                    setupScore += 20;
            }
            else if (instrumentClass == InstrumentClass.INDEX)
            {
                bool hasImpulse = ctx.HasImpulse_M5;
                if (!hasImpulse)
                    setupScore -= 40;
                else
                    setupScore += 15;

                bool hasStructure =
                    ctx.HasPullbackLong_M5 || ctx.HasPullbackShort_M5;

                if (hasStructure)
                    setupScore += 10;
            }
            else if (instrumentClass == InstrumentClass.CRYPTO)
            {
                if (!ctx.IsAtrExpanding_M5)
                    setupScore -= 30;

                bool hasStructure =
                    ctx.IsValidFlagStructure_M5 ||
                    (ctx.PullbackBars_M5 >= 2 && ctx.IsPullbackDecelerating_M5);

                if (!hasStructure)
                    setupScore -= 30;
                else
                    setupScore += 15;
            }
            else
            {
                double pullbackDepthR =
                    ctx.RangeBreakDirection == TradeDirection.Short
                        ? ctx.PullbackDepthRShort_M5
                        : ctx.PullbackDepthRLong_M5;

                bool hasStructure =
                    pullbackDepthR >= 0.15;

                if (!hasStructure)
                    setupScore -= 35;
                else
                    setupScore += 15;
            }

            // =========================================================
            // 2️⃣ BREAKOUT (HARD LÉTEZÉSI FELTÉTEL)
            // =========================================================

            if (ctx.RangeBreakDirection == TradeDirection.None)
            {
                eval.Reason += "NoBreak;";
                return eval;
            }

            eval.Direction = ctx.RangeBreakDirection;
            score += 15;

            // Break ereje ATR-ben (minőségi súlyozás)
            if (ctx.RangeBreakAtrSize_M5 >= MinBreakATR * 1.5)
                score += 15;           // erős breakout
            else if (ctx.RangeBreakAtrSize_M5 >= MinBreakATR)
                score += 8;            // elfogadható
            else
            {
                score -= 10;
                eval.Reason += "WeakBreak;";
            }

            // =========================================================
            // 3️⃣ FAKEOUT SZŰRÉS (M1)
            // =========================================================

            if (ctx.RangeFakeoutBars_M1 <= MaxFakeoutBars)
                score += 10;
            else
            {
                score -= 15;
                eval.Reason += "Fakeout;";
            }

            // =========================================================
            // 4️⃣ TRIGGER (soft, de fontos)
            // =========================================================

            if (ctx.M1TriggerInTrendDirection)
                score += 10;
            else
            {
                score -= 8;
                eval.Reason += "NoM1Trigger;";
            }

            // =========================================================
            // 5️⃣ KONFIRMÁLÓ PLUSZOK
            // =========================================================

            if (ctx.IsAtrExpanding_M5)
                score += 5;

            if (ctx.IsVolumeIncreasing_M5)
                score += 5;

            if (instrumentClass == InstrumentClass.METAL)
            {
                bool hasConfirmation =
                    ctx.RangeBreakDirection != TradeDirection.None
                    || ctx.M1TriggerInTrendDirection;

                if (hasConfirmation)
                    setupScore += 20;
            }
            else if (instrumentClass == InstrumentClass.INDEX)
            {
                bool continuationSignal =
                    ctx.RangeBreakDirection != TradeDirection.None;
                bool breakoutConfirmed = continuationSignal;

                if (continuationSignal || breakoutConfirmed)
                    setupScore += 20;
            }
            else if (instrumentClass == InstrumentClass.CRYPTO)
            {
                bool continuationSignal =
                    ctx.RangeBreakDirection != TradeDirection.None;

                if (continuationSignal)
                    setupScore += 20;
            }
            else
            {
                bool continuationSignal =
                    ctx.RangeBreakDirection != TradeDirection.None;

                if (continuationSignal)
                    setupScore += 20;
            }

            // =========================================================
            // HARD RULE – irány nélkül nincs setup
            // =========================================================
            if (eval.Direction == TradeDirection.None)
            {
                eval.Reason += "NoDirection;";
                return eval;
            }

            // =========================================================
            // MIN SCORE – ENTRYTYPE SZINTEN
            // =========================================================
            score += setupScore;

            if (setupScore <= 0)
                score = Math.Min(score, MIN_SCORE - 10);

            eval.Score = score;
            eval.IsValid = true;

            if (!eval.IsValid)
                eval.Reason += $"ScoreBelowMin({score});";

            return eval;
        }
    }
}
