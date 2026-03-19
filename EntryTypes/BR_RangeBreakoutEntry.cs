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
            if (ctx == null || !ctx.IsReady)
            {
                return new EntryEvaluation
                {
                    Symbol = ctx?.Symbol,
                    Type = Type,
                    Direction = TradeDirection.None,
                    Score = 0,
                    IsValid = false,
                    Reason = "CTX_NOT_READY;"
                };
            }

            if (!ctx.IsRange_M5 || ctx.RangeBarCount_M5 < MinRangeBars)
            {
                return new EntryEvaluation
                {
                    Symbol = ctx.Symbol,
                    Type = Type,
                    Direction = TradeDirection.None,
                    Score = 0,
                    IsValid = false,
                    Reason = "NoRange;"
                };
            }

            var longEval = EvaluateSide(ctx, TradeDirection.Long);
            var shortEval = EvaluateSide(ctx, TradeDirection.Short);

            return EntryDecisionPolicy.Normalize(EntryDecisionPolicy.SelectBalancedEvaluation(ctx, Type, longEval, shortEval));
        }

        private EntryEvaluation EvaluateSide(EntryContext ctx, TradeDirection dir)
        {
            var eval = new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
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
                    dir == TradeDirection.Short
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

            if (ctx.RangeBreakDirection == dir)
                score += 15;
            else if (ctx.RangeBreakDirection != TradeDirection.None)
                score -= 15;
            else
                eval.Reason += "NoBreak;";

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
                    ctx.RangeBreakDirection == dir
                    || ctx.M1TriggerInTrendDirection;

                if (hasConfirmation)
                    setupScore += 20;
            }
            else if (instrumentClass == InstrumentClass.INDEX)
            {
                bool continuationSignal =
                    ctx.RangeBreakDirection == dir;
                bool breakoutConfirmed = continuationSignal;

                if (continuationSignal || breakoutConfirmed)
                    setupScore += 20;
            }
            else if (instrumentClass == InstrumentClass.CRYPTO)
            {
                bool continuationSignal =
                    ctx.RangeBreakDirection == dir;

                if (continuationSignal)
                    setupScore += 20;
            }
            else
            {
                bool continuationSignal =
                    ctx.RangeBreakDirection == dir;

                if (continuationSignal)
                    setupScore += 20;
            }

            // =========================================================
            // HARD RULE – irány nélkül nincs setup
            // =========================================================
            int lastClosed = ctx.M5.Count - 2;
            var bar = ctx.M5[lastClosed];
            bool breakoutDetected = ctx.RangeBreakDirection == dir;
            bool strongCandle =
                (dir == TradeDirection.Long && bar.Close > bar.Open) ||
                (dir == TradeDirection.Short && bar.Close < bar.Open);
            bool followThrough = ctx.M1TriggerInTrendDirection || (ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir);
            score = ApplyMandatoryEntryAdjustments(ctx, dir, score, false);
            score = TriggerScoreModel.Apply(ctx, $"BR_RANGE_BREAKOUT_{dir}", score, breakoutDetected, strongCandle, followThrough, "NO_RANGE_BREAK_TRIGGER");

            // =========================================================
            // MIN SCORE – ENTRYTYPE SZINTEN
            // =========================================================
            score += setupScore;

            if (setupScore <= 0)
                score = Math.Min(score, MIN_SCORE - 10);

            eval.Score = score;
            eval.IsValid = score >= MIN_SCORE;

            if (!eval.IsValid)
                eval.Reason += $"ScoreBelowMin({score});";

            return eval;
        }

        private static int ApplyMandatoryEntryAdjustments(EntryContext ctx, TradeDirection direction, int score, bool applyTrendRegimePenalty)
        {
            const int htfPenalty = 30;
            const int logicPenalty = 12;
            const int rangePenalty = 25;

            TradeDirection htfDirection = TradeDirection.None;
            double htfConfidence = 0.0;

            switch (SymbolRouting.ResolveInstrumentClass(ctx.Symbol))
            {
                case InstrumentClass.FX:
                    htfDirection = ctx.FxHtfAllowedDirection;
                    htfConfidence = ctx.FxHtfConfidence01;
                    break;
                case InstrumentClass.CRYPTO:
                    htfDirection = ctx.CryptoHtfAllowedDirection;
                    htfConfidence = ctx.CryptoHtfConfidence01;
                    break;
                case InstrumentClass.INDEX:
                    htfDirection = ctx.IndexHtfAllowedDirection;
                    htfConfidence = ctx.IndexHtfConfidence01;
                    break;
                case InstrumentClass.METAL:
                    htfDirection = ctx.MetalHtfAllowedDirection;
                    htfConfidence = ctx.MetalHtfConfidence01;
                    break;
            }

            if (htfDirection != TradeDirection.None && htfConfidence >= 0.70 && direction != htfDirection)
            {
                score -= htfPenalty;
                ctx.Log?.Invoke($"[ENTRY HTF ALIGN] dir={direction} htf={htfDirection} conf={htfConfidence:0.00} penalty={htfPenalty}");
            }

            var logicBias = ctx.LogicBiasDirection;
            var logicConfidence = ctx.LogicBiasConfidence;
            if (logicBias != TradeDirection.None && logicConfidence >= 60 && direction != logicBias)
            {
                score -= logicPenalty;
                ctx.Log?.Invoke($"[ENTRY LOGIC ALIGN] dir={direction} logic={logicBias} conf={logicConfidence} penalty={logicPenalty}");
            }

            if (applyTrendRegimePenalty && ctx.Adx_M5 < 15.0)
            {
                score -= rangePenalty;
                ctx.Log?.Invoke($"[ENTRY REGIME] adx={ctx.Adx_M5:0.0} penalty={rangePenalty}");
            }

            return score;
        }

    }
}
