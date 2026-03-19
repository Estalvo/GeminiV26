// =========================================================
// GEMINI V26 – TradeRouter (Rulebook 1.0 COMPLIANCE NOTE)
//
// ✅ LEGACY bug megszüntetve:
//    Korábban létezett olyan logika, ami MINDEN nem-null evalt
//    beengedett (IsValid || IsValid == false). EZ MEGSZŰNT.
//
// ✅ Score-alapú döntés:
//    A TradeRouter csak akkor enged jelöltet tovább, ha
//    IsValid == true ÉS Score >= MinScoreThreshold.
//
// ✅ A router ezután rangsorol:
//    A Score egyszerre gate és prioritás a megmaradt setupok között.
//
// ✅ Logolás átlátható:
//    jelöltek → valid setupok → nyertes kiválasztás
//
// Ez a fájl a Szabálykönyv 1.0 normatív implementációja.
// =========================================================
using cAlgo.API;
using GeminiV26.Core.Entry;
using System.Collections.Generic;
using System.Linq;

namespace GeminiV26.Core
{
    /// <summary>
    /// TradeRouter (Szabálykönyv 1.0)
    /// - CSAK hard-safe (IsValid == true) setupokkal dolgozik
    /// - Score gate = Score >= MinScoreThreshold
    /// - Winner = max Score (tie-break: determinisztikus priority)
    /// </summary>
    public class TradeRouter
    {
        private readonly Robot _bot;

        // =========================================================
        // ENTRY QUALITY GATES (NOT SCORE GATES)
        // =========================================================
        private const int MIN_ENTRY_SCORE_GLOBAL = 20;

        public TradeRouter(Robot bot)
        {
            _bot = bot;
        }

        // =========================================================
        // ENTRY PRIORITY SELECTION (RULEBOOK 1.0)
        // =========================================================
        public EntryEvaluation SelectEntry(List<EntryEvaluation> signals)
        {
            _bot.Print("[TR] SelectEntry CALLED");

            if (signals == null || signals.Count == 0)
                return null;

            // Candidate logging (raw)
            int nonNullCount = signals.Count(e => e != null);
            int validCount = signals.Count(e => e != null && e.IsValid);

            _bot.Print($"[TR] evals={signals.Count} nonNull={nonNullCount} valid={validCount}");
            LogCandidates("CAND", signals);

            var valid = signals
                .Where(e => e != null && e.IsValid && e.Score >= ResolveMinScoreThreshold(e))
                .ToList();

            foreach (var candidate in signals.Where(e => e != null))
            {
                int threshold = ResolveMinScoreThreshold(candidate);
                bool accepted = candidate.IsValid && candidate.Score >= threshold;
                _bot.Print($"[ENTRY DECISION] type={candidate.Type} dir={candidate.Direction} score={candidate.Score} threshold={threshold} valid={candidate.IsValid} => {(accepted ? "ACCEPT" : "REJECT")}");
            }

            if (valid.Count == 0)
            {
                _bot.Print("[TR] NO VALID SETUP AFTER SCORE GATE");
                return null;
            }

            var winner = valid
                .OrderByDescending(e => e.Score)
                .ThenBy(e => GetTypePriority(_bot.SymbolName, e.Type))
                .First();

            _bot.Print($"[TR] WINNER: {winner.Type} dir={winner.Direction} score={winner.Score} threshold={ResolveMinScoreThreshold(winner)} valid={winner.IsValid} reason={winner.Reason}");
            return winner;
        }

        // =========================================================
        // LOGGING
        // =========================================================
        private void LogCandidates(string scope, IEnumerable<EntryEvaluation> list)
        {
            if (list == null) return;

            foreach (var e in list)
            {
                if (e == null) continue;
                _bot.Print($"[TR] {scope} {e.Type} dir={e.Direction} valid={e.IsValid} score={e.Score} threshold={ResolveMinScoreThreshold(e)} reason={e.Reason}");
            }
        }


        private static int ResolveMinScoreThreshold(EntryEvaluation evaluation)
        {
            if (evaluation == null)
                return MIN_ENTRY_SCORE_GLOBAL;

            return evaluation.MinScoreThreshold > 0
                ? evaluation.MinScoreThreshold
                : MIN_ENTRY_SCORE_GLOBAL;
        }

        // Deterministic tie-break (only used when scores are equal)
        private int GetTypePriority(string symbol, EntryType type)
        {
            string sym = SymbolRouting.NormalizeSymbol(symbol);
            var instrumentClass = SymbolRouting.ResolveInstrumentClass(sym);

            // =========================
            // XAU
            // =========================
            if (instrumentClass == InstrumentClass.METAL)
            {
                switch (type)
                {
                    case EntryType.XAU_Flag: return 0;   // ⭐ trendforrás
                    case EntryType.XAU_Pullback: return 1;
                    case EntryType.XAU_Impulse: return 2;
                    case EntryType.XAU_Reversal: return 3;
                    default: return 100;
                }
            }

            // =========================
            // INDEX
            // =========================
            if (instrumentClass == InstrumentClass.INDEX)
            {
                switch (type)
                {
                    case EntryType.Index_Flag: return 0; // ⭐ legjobb, strukturált
                    case EntryType.Index_Pullback: return 1; // continuation
                    case EntryType.Index_Breakout: return 2; // csak ha más nincs
                }
            }

            // =========================
            // CRYPTO
            // =========================
            if (instrumentClass == InstrumentClass.CRYPTO)
            {
                switch (type)
                {
                    case EntryType.Crypto_Flag: return 0;
                    case EntryType.Crypto_Pullback: return 1;
                    case EntryType.Crypto_RangeBreakout: return 2;
                    case EntryType.Crypto_Impulse: return 3;
                    default: return 100;
                }
            }

            // =========================
            // FX (fallback)
            // =========================
            switch (type)
            {
                case EntryType.FX_Flag: return 0;

                case EntryType.FX_FlagContinuation: return 1;

                case EntryType.FX_MicroStructure: return 2;

                case EntryType.FX_MicroContinuation: return 3;

                case EntryType.FX_ImpulseContinuation: return 4;

                case EntryType.FX_Pullback: return 5;

                case EntryType.FX_RangeBreakout: return 6;

                case EntryType.FX_Reversal: return 7;

                default: return 100;
            }
        }
    }
}
