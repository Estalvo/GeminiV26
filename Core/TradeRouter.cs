// =========================================================
// GEMINI V26 – TradeRouter (Rulebook 1.0 COMPLIANCE NOTE)
//
// ✅ LEGACY bug megszüntetve:
//    Korábban létezett olyan logika, ami MINDEN nem-null evalt
//    beengedett (IsValid || IsValid == false). EZ MEGSZŰNT.
//
// ✅ Nincs több instrument-specifikus threshold / score gate:
//    A TradeRouter SOHA nem tilt score alapján.
//
// ✅ A router CSAK rangsorol:
//    Kizárólag IsValid == true setupok között választ,
//    a Score kizárólag prioritás, nem belépési engedély.
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
    /// - CSAK IsValid == true setupokkal dolgozik
    /// - Score csak rangsorol (nem gate)
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

            // RULEBOOK: only IsValid == true is eligible
            var valid = signals
                .Where(e => e != null && e.IsValid)
                // ENTRY QUALITY GATE (Rulebook-safe)
                .Where(e => e.Score >= MIN_ENTRY_SCORE_GLOBAL)
                .ToList();

            var rejectedByScore = signals
                .Where(e => e != null && e.IsValid && e.Score < MIN_ENTRY_SCORE_GLOBAL)
                .ToList();

            foreach (var r in rejectedByScore)
            {
                _bot.Print($"[TR] REJECTED_LOW_SCORE {r.Type} score={r.Score} reason={r.Reason}");
            }

            if (valid.Count == 0)
            {
                _bot.Print("[TR] NO VALID SETUP AFTER QUALITY GATE");
                return null; // ⛔ NINCS ENTRY, PONT
            }

            // RULEBOOK: Score ranks, doesn't gate.
            // Winner = max score. Tie-breaker keeps deterministic behavior.
            var winner = valid
                .OrderByDescending(e => e.Score)
                .ThenBy(e => GetTypePriority(_bot.SymbolName, e.Type))
                .First();

            _bot.Print($"[TR] WINNER: {winner.Type} dir={winner.Direction} score={winner.Score} valid={winner.IsValid} reason={winner.Reason}");
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
                _bot.Print($"[TR] {scope} {e.Type} dir={e.Direction} valid={e.IsValid} score={e.Score} reason={e.Reason}");
            }
        }

        // Deterministic tie-break (only used when scores are equal)
        private int GetTypePriority(string symbol, EntryType type)
        {
            string sym = symbol.ToUpper().Replace(" ", "");

            // =========================
            // XAU
            // =========================
            if (sym.Contains("XAU"))
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
            if (
                sym.Contains("NAS") ||
                sym.Contains("USTECH100") ||
                sym.Contains("US100") ||
                sym.Contains("US30") ||
                sym.Contains("DJ30") ||
                sym.Contains("DAX") ||
                sym.Contains("GER40")
            )
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
            if (sym.Contains("BTC") || sym.Contains("ETH") || sym.Contains("XBT"))
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
                case EntryType.FX_MicroContinuation: return 2; // 👈 ÚJ
                case EntryType.FX_Pullback: return 3;
                case EntryType.FX_RangeBreakout: return 4;
                case EntryType.FX_Reversal: return 5;
                default: return 100;
            }
        }
    }
}
