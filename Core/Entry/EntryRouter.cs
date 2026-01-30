using System.Collections.Generic;
using GeminiV26.EntryTypes;

namespace GeminiV26.Core.Entry
{
    /// <summary>
    /// EntryRouter
    /// Feladata:
    /// - instrumentenként összegyűjti az EntryType-ok javaslatait
    /// - NEM dönt, NEM priorizál, NEM nyit trade-et
    /// </summary>
    public class EntryRouter
    {
        private readonly List<IEntryType> _entryTypes;

        public EntryRouter(IEnumerable<IEntryType> entryTypes)
        {
            _entryTypes = new List<IEntryType>(entryTypes);
        }

        /// <summary>
        /// Instrumentenként kiértékeli az összes EntryType-ot,
        /// és visszaadja az EntryEvaluation-öket.
        /// </summary>
        public Dictionary<string, List<EntryEvaluation>> Evaluate(
            IEnumerable<EntryContext> contexts)
        {
            var result = new Dictionary<string, List<EntryEvaluation>>();

            foreach (var ctx in contexts)
            {
                if (!result.TryGetValue(ctx.Symbol, out var evalList))
                {
                    evalList = new List<EntryEvaluation>();
                    result[ctx.Symbol] = evalList;
                }

                foreach (var entryType in _entryTypes)
                {
                    var eval = entryType.Evaluate(ctx);

                    if (eval != null)
                        eval.Reason = "[ROUTER] " + eval.Reason;

                    // DEBUG – marad
                    System.Diagnostics.Debug.WriteLine(
                        $"[DEBUG_ROUTER] {ctx.Symbol} {entryType.GetType().Name} " +
                        $"{(eval == null ? "eval=NULL" : $"score={eval.Score} valid={eval.IsValid} dir={eval.Direction} reason={eval.Reason}")}"
                    );

                    if (eval == null)
                        continue;

                    // Instrument-keveredés kizárása
                    if (eval.Symbol != ctx.Symbol)
                        continue;

                    // ❗ FONTOS:
                    // Router NEM gate-el Direction / IsValid alapján
                    // Ezek executor szintű döntések

                    evalList.Add(eval);
                }
            }

            return result;
        }
    }
}
