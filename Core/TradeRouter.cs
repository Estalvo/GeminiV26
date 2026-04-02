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
using GeminiV26.Core.Logging;
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
        public EntryEvaluation SelectEntry(List<EntryEvaluation> signals, EntryContext entryContext = null)
        {
            var nonNullSignals = signals?.Where(e => e != null).ToList() ?? new List<EntryEvaluation>();
            var executable = nonNullSignals.Where(IsExecutable).ToList();

            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                $"[ROUTER][RANK_ONLY][INPUT] total={nonNullSignals.Count} executable={executable.Count}",
                entryContext));
            LogCandidates("RANK_ONLY_INPUT", nonNullSignals, entryContext);

            if (executable.Count == 0)
            {
                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId("[ROUTER][RANK_ONLY][NO_WINNER] reason=NO_EXECUTABLE_CANDIDATE", entryContext));
                return null;
            }

            var winner = executable
                .OrderByDescending(c => c.Score)
                .ThenBy(c => GetTypePriority(_bot.SymbolName, c.Type))
                .FirstOrDefault();

            if (winner == null)
            {
                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId("[ROUTER][RANK_ONLY][NO_WINNER] reason=EMPTY_AFTER_RANK", entryContext));
                return null;
            }

            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                $"[ROUTER][RANK_ONLY][WINNER] symbol={winner.Symbol ?? entryContext?.Symbol ?? _bot.SymbolName} type={winner.Type} dir={winner.Direction} score={winner.Score}",
                entryContext));
            return winner;
        }

        private static bool IsExecutable(EntryEvaluation c)
        {
            return c != null
                && c.IsValid
                && c.TriggerConfirmed;
        }

        // =========================================================
        // LOGGING
        // =========================================================
        private void LogCandidates(string scope, IEnumerable<EntryEvaluation> list, EntryContext entryContext)
        {
            if (list == null) return;

            foreach (var e in list)
            {
                if (e == null) continue;
                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[CANDIDATE] type={e.Type} dir={e.Direction} valid={e.IsValid.ToString().ToLowerInvariant()} score={e.Score} reason={e.Reason}", entryContext));
                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[TR] {scope} {e.Type} dir={e.Direction} valid={e.IsValid} state={e.State} trigger={e.TriggerConfirmed} score={e.Score} reason={e.Reason}", entryContext));
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
