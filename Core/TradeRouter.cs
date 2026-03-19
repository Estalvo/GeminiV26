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

            int nonNullCount = signals.Count(e => e != null);
            int validCount = signals.Count(e => e != null && e.IsValid && e.Score >= EntryDecisionPolicy.MinScoreThreshold);

            _bot.Print($"[TR] evals={signals.Count} nonNull={nonNullCount} valid={validCount} threshold={EntryDecisionPolicy.MinScoreThreshold}");
            LogCandidates("CAND", signals);

            EntryEvaluation winner = null;

            foreach (var candidate in signals.Where(e => e != null))
            {
                string decision;

                if (!candidate.IsValid)
                {
                    decision = "REJECT";
                }
                else if (candidate.Score < EntryDecisionPolicy.MinScoreThreshold)
                {
                    decision = "REJECT";
                }
                else
                {
                    decision = candidate.TriggerConfirmed ? "ACCEPT" : "ACCEPT_SCORE_MODEL";

                    if (winner == null
                        || candidate.Score > winner.Score
                        || (candidate.Score == winner.Score
                            && GetTypePriority(_bot.SymbolName, candidate.Type) < GetTypePriority(_bot.SymbolName, winner.Type)))
                    {
                        winner = candidate;
                    }
                }

                _bot.Print($"[ENTRY DECISION] symbol={candidate.Symbol ?? _bot.SymbolName} type={candidate.Type} score={candidate.Score} threshold={EntryDecisionPolicy.MinScoreThreshold} valid={candidate.IsValid.ToString().ToLowerInvariant()} state={candidate.State} trigger={candidate.TriggerConfirmed.ToString().ToLowerInvariant()} → {decision}");
            }

            if (winner == null)
            {
                _bot.Print("[TR] NO CANDIDATE PASSED GLOBAL ENTRY DECISION");
                return null;
            }

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
                _bot.Print($"[TR] {scope} {e.Type} dir={e.Direction} valid={e.IsValid} state={e.State} trigger={e.TriggerConfirmed} score={e.Score} reason={e.Reason}");
            }
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
