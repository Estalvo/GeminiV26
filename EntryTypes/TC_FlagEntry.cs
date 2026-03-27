// =========================================================
// GEMINI V26 – TC_FlagEntry (STRICT FLAG VERSION)
// Rulebook 1.0 compliant EntryType
//
// Alapelv:
// - Flag setup csak VALÓDI M5 impulse UTÁN értelmezhető
// - Flag struktúra opcionális minőségi jel
// - Trend + impulse irány dönti el az irányt
// - M1 breakout trigger HARD belépési pecsét
// - Score = minőség, nem létezési feltétel
// =========================================================

using GeminiV26.Core;
using GeminiV26.Core.Entry;
using System;

namespace GeminiV26.EntryTypes
{
    public class TC_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.TC_Flag;

        // --- Paraméterek ---
        private const double MinSlope = 0.0005;
        private const int ImpulseLookback = 5;
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

            if (!ctx.HasImpulse_M5)
            {
                return CreateInvalid(ctx, "NoImpulse;");
            }

            if (ctx.M5.Count < ImpulseLookback + 1)
            {
                return CreateInvalid(ctx, "NotEnoughBars;");
            }

            double impulseMove =
                ctx.M5.ClosePrices.LastValue -
                ctx.M5.ClosePrices[ctx.M5.Count - 1 - ImpulseLookback];

            // Gyenge impulse kiszűrése
            if (Math.Abs(impulseMove) < ctx.AtrM5 * 0.8)
            {
                return CreateInvalid(ctx, "WeakImpulse;");
            }

            if (ctx.HtfConfidence >= 0.6 && ctx.HtfDirection != ctx.LogicBias)
            {
                return CreateInvalid(ctx, "HTF_MISMATCH");
            }

            if (ctx.LogicBias == TradeDirection.Long)
            {
                var eval = EvaluateSide(ctx, impulseMove, TradeDirection.Long);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), eval, null, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }
            else if (ctx.LogicBias == TradeDirection.Short)
            {
                var eval = EvaluateSide(ctx, impulseMove, TradeDirection.Short);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), null, eval, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }

            return CreateInvalid(ctx, "NO_LOGIC_BIAS");
        }
        private EntryEvaluation EvaluateSide(EntryContext ctx, double impulseMove, TradeDirection dir)
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

            TradeDirection impulseDirection =
                impulseMove > 0 ? TradeDirection.Long : TradeDirection.Short;

            score += 25;

            // =========================================================
            // 2️⃣ FLAG STRUKTÚRA – SOFT (M5)
            // =========================================================
            if (!ctx.IsValidFlagStructure_M5)
            {
                eval.Reason += "FLAG_WEAK_OR_FORMING;";
                score -= 2;
            }
            else
            {
                score += 25;
            }

            // =========================================================
            // 3️⃣ TREND + IMPULSE ALIGNMENT – HARD
            // =========================================================
            bool trendUp = ctx.Ema21Slope_M15 > MinSlope;
            bool trendDown = ctx.Ema21Slope_M15 < -MinSlope;

            if ((dir == TradeDirection.Long && trendUp && impulseDirection == dir) ||
                (dir == TradeDirection.Short && trendDown && impulseDirection == dir))
            {
                score += 25;
            }
            else
            {
                eval.Reason += "TrendImpulseMismatch;";
                score -= 20;
            }

            // =========================================================
            // 4️⃣ M1 FLAG BREAK TRIGGER – SCORE ONLY
            // =========================================================
            if (!ctx.M1FlagBreakTrigger)
            {
                eval.Reason += "NoM1Break;";
                score -= 10;
            }
            else
            {
                score += 30;
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

                bool hasConfirmation =
                    ctx.M1FlagBreakTrigger || (ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir);
                strongTriggerForTiming = hasConfirmation;

                if (hasConfirmation)
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
                    ctx.HasPullbackLong_M5 || ctx.HasPullbackShort_M5 || ctx.IsValidFlagStructure_M5;
                hasStructureForTiming = hasStructure;

                if (hasStructure)
                    setupScore += 10;

                bool continuationSignal =
                    ctx.M1FlagBreakTrigger || (ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir);
                bool breakoutConfirmed = continuationSignal;
                strongTriggerForTiming = continuationSignal || breakoutConfirmed;

                if (continuationSignal || breakoutConfirmed)
                    setupScore += 20;
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

                bool continuationSignal =
                    ctx.M1FlagBreakTrigger || (ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir);
                strongTriggerForTiming = continuationSignal;

                if (continuationSignal)
                    setupScore += 20;
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

                bool continuationSignal =
                    ctx.M1FlagBreakTrigger || (ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir);
                strongTriggerForTiming = continuationSignal;

                if (continuationSignal)
                    setupScore += 20;
            }

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

            // =========================================================
            // 5️⃣ MINŐSÉGI BOOSTOK – SOFT
            // =========================================================
            if (ctx.IsAtrExpanding_M5)
                score += 5;

            if (ctx.IsVolumeIncreasing_M5)
                score += 5;

            bool breakoutDetected =
                ctx.M1FlagBreakTrigger ||
                (ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir);
            int lastClosed = ctx.M5.Count - 2;
            var bar = ctx.M5[lastClosed];
            bool strongCandle =
                (dir == TradeDirection.Long && bar.Close > bar.Open) ||
                (dir == TradeDirection.Short && bar.Close < bar.Open);
            bool followThrough = breakoutDetected || ctx.IsAtrExpanding_M5;
            score = TriggerScoreModel.Apply(ctx, $"TC_FLAG_{dir}", score, breakoutDetected, strongCandle, followThrough, "NO_FLAG_BREAK_TRIGGER");

            score = ApplyMandatoryEntryAdjustments(ctx, dir, score, true);

            // =========================================================
            // 6️⃣ MIN SCORE – ENTRYTYPE SZINT
            // =========================================================
            score += timing.ScoreAdjustment;
            minScore += timing.MinScoreAdjustment;
            score += setupScore;

            if (setupScore <= 0)
                score = Math.Min(score, minScore - 10);

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
                    TypeTag = "TC_FlagEntry",
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
                Score = ApplyMandatoryEntryAdjustments(ctx, TradeDirection.None, 0, true),
                IsValid = false,
                Reason = reason
            };
        }

    }
}
