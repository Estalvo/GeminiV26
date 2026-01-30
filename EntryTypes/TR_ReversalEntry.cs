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

using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes
{
    public class TR_ReversalEntry : IEntryType
    {
        public EntryType Type => EntryType.TR_Reversal;

        // --- Rulebook 1.0 ---
        private const int MIN_SCORE = 50;

        // --- Quality gates ---
        private const int MIN_EVIDENCE = 2;

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

            // =========================================================
            // 1️⃣ REVERSAL EVIDENCE (HARD QUALITY GATE)
            // =========================================================
            // EvidenceScore egy összegzett kontextus jel:
            // (pl. divergencia / túlterjedés / SR / momentum gyengülés stb.)
            if (ctx.ReversalEvidenceScore < MIN_EVIDENCE)
            {
                eval.Score = 0;
                eval.IsValid = false;
                eval.Reason += $"WeakEvidence({ctx.ReversalEvidenceScore});";
                return eval;
            }

            // Evidence pontozás: minél több, annál jobb (de nem végtelen)
            // 3 = ok, 4-5 = jobb, 6+ = nagyon erős
            score += ctx.ReversalEvidenceScore * 12; // 3->36, 4->48, 5->60

            // =========================================================
            // 2️⃣ IRÁNY (HARD RULE)
            // =========================================================
            if (ctx.ReversalDirection == TradeDirection.None)
            {
                eval.Score = 0;
                eval.IsValid = false;
                eval.Reason += "NoDirection;";
                return eval;
            }

            eval.Direction = ctx.ReversalDirection;
            score += 20;

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

            // =========================================================
            // 5️⃣ MIN SCORE (EntryType szinten)
            // =========================================================
            eval.Score = score;
            eval.IsValid = score >= MIN_SCORE;

            if (!eval.IsValid)
                eval.Reason += $"ScoreBelowMin({score});";

            return eval;
        }
    }
}
