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
            DirectionDebug.LogOnce(ctx);
            if (ctx == null || !ctx.IsReady)
            {
                return CreateInvalid(ctx, "CTX_NOT_READY;");
            }

            if (ctx.LogicBias == TradeDirection.None)
            {
                return CreateInvalid(ctx, "NO_LOGIC_BIAS");
            }

            if (!ctx.IsRange_M5 || ctx.RangeBarCount_M5 < MinRangeBars)
            {
                return CreateInvalid(ctx, "NoRange;");
            }

            if (ctx.HtfConfidence >= 0.6 && ctx.HtfDirection != ctx.LogicBias)
            {
                return CreateInvalid(ctx, "HTF_MISMATCH");
            }

            if (ctx.LogicBias == TradeDirection.Long)
            {
                var eval = EvaluateSide(ctx, TradeDirection.Long);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), eval, null, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }
            else if (ctx.LogicBias == TradeDirection.Short)
            {
                var eval = EvaluateSide(ctx, TradeDirection.Short);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), null, eval, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }

            return CreateInvalid(ctx, "NO_LOGIC_BIAS");
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

            int minScore = MIN_SCORE;
            int score = 0;
            int setupScore = 0;
            bool hasStructureForTiming = false;
            bool strongTriggerForTiming = false;

            var timing = ContinuationTimingGate.Evaluate(ctx, dir, Type.ToString());
            if (!timing.IsAllowed)
            {
                eval.Reason += $"{timing.Reason};";
                return eval;
            }

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
                eval.Score = ApplyMandatoryEntryAdjustments(ctx, dir, eval.Score, false);
                return eval;
            }

            var instrumentClass = SymbolRouting.ResolveInstrumentClass(ctx.Symbol);

            if (instrumentClass == InstrumentClass.METAL)
            {
                bool hasStructure =
                    ctx.IsValidFlagStructure_M5
                    || (ctx.PullbackBars_M5 >= 2 && ctx.IsPullbackDecelerating_M5)
                    || ctx.HasEarlyPullback_M5;
                hasStructureForTiming = hasStructure;

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
                    dir == TradeDirection.Long ? ctx.HasPullbackLong_M5 : ctx.HasPullbackShort_M5;
                hasStructureForTiming = hasStructure;
                ctx.Log?.Invoke("[ENTRY][DIR_FIX] using direction-pure structure selection");

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
                hasStructureForTiming = hasStructure;

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
                hasStructureForTiming = hasStructure;

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
            strongTriggerForTiming = ctx.M1TriggerInTrendDirection || ctx.RangeBreakDirection == dir;

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
            score = TriggerScoreModel.Apply(ctx, $"BR_RANGE_BREAKOUT_{dir}", score, breakoutDetected, strongCandle, followThrough, "NO_RANGE_BREAK_TRIGGER");

            // =========================================================
            // MIN SCORE – ENTRYTYPE SZINTEN
            // =========================================================
            if (timing.RequireStrongStructure && !hasStructureForTiming)
            {
                eval.Reason += "TIMING_LATE_NEEDS_STRONG_STRUCTURE;";
                return eval;
            }

            if (timing.RequireStrongTrigger && !strongTriggerForTiming)
            {
                eval.Reason += "TIMING_LATE_NEEDS_STRONG_TRIGGER;";
                return eval;
            }

            score += timing.ScoreAdjustment;
            minScore += timing.MinScoreAdjustment;
            score += setupScore;

            if (setupScore <= 0)
                score = Math.Min(score, minScore - 10);
            score = ApplyMandatoryEntryAdjustments(ctx, dir, score, false);

            eval.Score = score;
            eval.IsValid = score >= minScore;

            if (!eval.IsValid)
                eval.Reason += $"ScoreBelowMin({score});";

            return eval;
        }

        private static int ApplyMandatoryEntryAdjustments(EntryContext ctx, TradeDirection direction, int score, bool applyTrendRegimePenalty)
        {
            return EntryDirectionQuality.Apply(
                ctx,
                direction,
                score,
                new DirectionQualityRequest
                {
                    TypeTag = "BR_RangeBreakoutEntry",
                    ApplyTrendRegimePenalty = applyTrendRegimePenalty
                });
        }

        private EntryEvaluation CreateInvalid(EntryContext ctx, string reason)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = TradeDirection.None,
                Score = ApplyMandatoryEntryAdjustments(ctx, TradeDirection.None, 0, false),
                IsValid = false,
                Reason = reason
            };
        }

    }
}
