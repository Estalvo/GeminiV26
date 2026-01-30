using GeminiV26.Core.Entry;
using GeminiV26.Instruments.FX;
using GeminiV26.Core;

namespace GeminiV26.EntryTypes.FX
{
    public class FX_ReversalEntry : IEntryType
    {
        public EntryType Type => EntryType.FX_Reversal;

        // FX RESET: egységesített küszöbök – nem session-tiltás, hanem score
        private const int MIN_EVIDENCE = 2;
        private const int MIN_SCORE = 38;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady)
                return Invalid(ctx, "CTX_NOT_READY");

            // =========================================================
            // FX REVERSAL – ASIA SESSION HARD BLOCK (Phase 3.8)
            // =========================================================
            if (ctx.Session == FxSession.Asia)
                return Invalid(ctx, "FX_REV_ASIA_BLOCKED");

            var fx = FxInstrumentMatrix.Get(ctx.Symbol);

            if (fx == null)
                return Invalid(ctx, "NO_FX_PROFILE");

            if (!fx.MeanReversionFriendly)
                return Invalid(ctx, "NO_MEAN_REVERSION");

            if (ctx.ReversalDirection == TradeDirection.None)
                return Invalid(ctx, "NO_DIRECTION");

            if (ctx.ReversalEvidenceScore < MIN_EVIDENCE)
                return Invalid(ctx, "WEAK_EVIDENCE");

            int score = 0;

            // evidence a mag
            score += ctx.ReversalEvidenceScore * 12;   // 2->24, 3->36, 4->48

            // base kontextus
            score += 18; // eddig +20 volt, de session nélkül kiegyenlítjük

            // range = ideális FX reversal
            if (ctx.IsRange_M5)
                score += 12;

            // M1 trigger: nem kötelező, csak minőség-jel
            if (ctx.M1ReversalTrigger)
                score += 10;
            else
                score -= 2;

            // ATR expanding: reversalnél lehet stoprun -> nem tiltjuk, csak jelzés
            if (ctx.IsAtrExpanding_M5)
                score += 0;

            if (ctx.MarketState == null)
                return Invalid(ctx, "NO_MARKETSTATE");

            // =========================
            // FX HTF bias – Reversal weighting (soft)
            // =========================
            if (ctx.FxHtfAllowedDirection != TradeDirection.None &&
                ctx.FxHtfConfidence01 > 0.0)
            {
                if (ctx.ReversalDirection != ctx.FxHtfAllowedDirection)
                {
                    // Reversal HTF ellen: enyhe büntetés
                    int htfPenalty = (int)(4 + 3 * ctx.FxHtfConfidence01);
                    score -= htfPenalty;
                }
                else
                {
                    // Reversal HTF irányába: enyhe jutalom (ritkább, de értékes)
                    int htfBonus = (int)(2 + 2 * ctx.FxHtfConfidence01);
                    score += htfBonus;
                }
            }

            var eval = BaseEval(ctx);
            eval.Direction = ctx.ReversalDirection;
            eval.Score = score;
            eval.IsValid = score >= MIN_SCORE;

            eval.Reason =
                $"FX_REV dir={eval.Direction} score={score} " +
                $"evid={ctx.ReversalEvidenceScore} m1={ctx.M1ReversalTrigger} " +
                $"range={ctx.IsRange_M5} sess={ctx.Session};";

            return eval;
        }

        private EntryEvaluation BaseEval(EntryContext ctx) =>
            new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = TradeDirection.None,
                Score = 0,
                IsValid = false,
                Reason = ""
            };

        private EntryEvaluation Invalid(EntryContext ctx, string reason) =>
            new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                IsValid = false,
                Reason = reason
            };
    }
}
