// =========================================================
// GEMINI V26 – TR_ReversalEntry (Quality + Format pass)
// Rulebook 1.0 compliant EntryType
//
// Alapelv:
// - Reversal csak akkor "létezik", ha van elég EVIDENCE (min. 3)
// - Direction nélkül NINCS setup (hard rule)
// - M1 trigger erős minőségi faktor (nem hard), de trigger nélkül ritkán nyer
// - Minimum score (50) ITT dől el (EntryType szinten)
//
// Megjegyzés:
// - Csak a meglévő ctx mezőket használjuk:
//   ReversalEvidenceScore, ReversalDirection, M1ReversalTrigger,
//   IsRange_M5, IsAtrExpanding_M5
// =========================================================

using GeminiV26.Core;
using GeminiV26.Core.Entry;
using System;

namespace GeminiV26.EntryTypes
{
    public class TR_ReversalEntry : IEntryType
    {
        public EntryType Type => EntryType.TR_Reversal;

        // --- Rulebook 1.0 ---
        private const int MIN_SCORE = EntryDecisionPolicy.MinScoreThreshold;

        // --- Quality gates ---
        private const int MIN_EVIDENCE = 2;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            DirectionDebug.LogOnce(ctx);
            if (ctx == null || !ctx.IsReady)
            {
                return CreateInvalid(ctx, "CTX_NOT_READY;");
            }

            if (ctx.LogicBias == TradeDirection.None)
                return CreateInvalid(ctx, "NO_LOGIC_BIAS");

            if (ctx.ReversalEvidenceScore < MIN_EVIDENCE)
            {
                return CreateInvalid(ctx, $"WeakEvidence({ctx.ReversalEvidenceScore});");
            }

            if (ctx.HtfConfidence >= 0.6 && ctx.HtfDirection != ctx.LogicBias)
                return CreateInvalid(ctx, "HTF_MISMATCH");

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

            int score = 0;
            int setupScore = 0;

            score += ctx.ReversalEvidenceScore * 12;
            score += 20;

            if (ctx.ReversalDirection == dir)
                score += 12;
            else if (ctx.ReversalDirection != TradeDirection.None)
                score -= 12;

            // =========================================================
            // 3️⃣ TRIGGER MINŐSÉG (soft, de erős hatás)
            // =========================================================
            if (ctx.M1ReversalTrigger)
            {
                score += 15;
            }
            else
            {
                // Trigger hiányában a reversal sokszor "korai hős trade",
                // ezért erős levonás: átmehet, de ritkán fog nyerni.
                score -= 12;
                eval.Reason += "NoM1Trigger;";
            }

            // =========================================================
            // 4️⃣ KONTEXTUS MINŐSÉG (soft)
            // =========================================================
            // Reversal inkább trend-végi jelleg: range-ben kevésbé megbízható
            if (!ctx.IsRange_M5)
            {
                score += 10;
            }
            else
            {
                score -= 5;
                eval.Reason += "RangeEnv;";
            }

            // ATR expanzió: gyakran trendvégi felfokozottság / kilengés
            if (ctx.IsAtrExpanding_M5)
            {
                score += 5;
            }

            var instrumentClass = SymbolRouting.ResolveInstrumentClass(ctx.Symbol);

            if (instrumentClass == InstrumentClass.METAL)
            {
                bool hasStructure =
                    (dir == TradeDirection.Long ? ctx.HasFlagLong_M5 : ctx.HasFlagShort_M5)
                    || (ctx.PullbackBars_M5 >= 2 && ctx.IsPullbackDecelerating_M5)
                    || ctx.HasEarlyPullback_M5;

                if (!hasStructure)
                    setupScore -= 40;
                else
                    setupScore += 20;

                bool hasConfirmation =
                    ctx.M1ReversalTrigger || ctx.LastClosedBarInTrendDirection;

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
                    ctx.HasPullbackLong_M5 || ctx.HasPullbackShort_M5;

                if (hasStructure)
                    setupScore += 10;

                bool continuationSignal = ctx.M1ReversalTrigger;
                bool breakoutConfirmed = continuationSignal;

                if (continuationSignal || breakoutConfirmed)
                    setupScore += 20;
            }
            else if (instrumentClass == InstrumentClass.CRYPTO)
            {
                if (!ctx.IsAtrExpanding_M5)
                    setupScore -= 30;

                bool hasStructure =
                    (dir == TradeDirection.Long ? ctx.HasFlagLong_M5 : ctx.HasFlagShort_M5)
                    || (ctx.PullbackBars_M5 >= 2 && ctx.IsPullbackDecelerating_M5);

                if (!hasStructure)
                    setupScore -= 30;
                else
                    setupScore += 15;

                bool continuationSignal = ctx.M1ReversalTrigger;

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

                if (!hasStructure)
                    setupScore -= 35;
                else
                    setupScore += 15;

                bool continuationSignal = ctx.M1ReversalTrigger;

                if (continuationSignal)
                    setupScore += 20;
            }

            int lastClosed = ctx.M5.Count - 2;
            var bar = ctx.M5[lastClosed];
            bool breakoutDetected = ctx.M1ReversalTrigger || (ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir);
            bool strongCandle =
                (dir == TradeDirection.Long && bar.Close > bar.Open) ||
                (dir == TradeDirection.Short && bar.Close < bar.Open);
            bool followThrough = breakoutDetected || ctx.HasReactionCandle_M5;
            score = TriggerScoreModel.Apply(ctx, $"TR_REV_{dir}", score, breakoutDetected, strongCandle, followThrough, "NO_REVERSAL_TRIGGER");

            score += setupScore;

            if (setupScore <= 0)
                score = Math.Min(score, MIN_SCORE - 10);
            score = ApplyMandatoryEntryAdjustments(ctx, dir, score, false);

            eval.Score = score;
            eval.IsValid = score >= MIN_SCORE;

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
                    TypeTag = "TR_ReversalEntry",
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
