using System.Diagnostics;
using GeminiV26.Core;
using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes
{
    internal static class DirectionDebug
    {
        public static void LogOnce(EntryContext ctx)
        {
            if (ctx == null || ctx.DirectionDebugLogged)
                return;

            ctx.DirectionDebugLogged = true;
            GlobalLogger.Log($"[DIR DEBUG] symbol={ctx.SymbolName} bias={ctx.LogicBiasDirection} conf={ctx.LogicBiasConfidence}");

            if (SymbolRouting.NormalizeSymbol(ctx.Symbol) == "AUDNZD")
            {
                var entryBias = ctx.LogicBiasDirection;
                if (ctx.SymbolName == "AUDNZD")
                {
                    Debug.Assert(ctx.LogicBias == entryBias);
                }

                GlobalLogger.Log($"[AUDNZD TRACE] step3_entry={entryBias} conf={ctx.LogicBiasConfidence}");
            }
        }
    }
}
