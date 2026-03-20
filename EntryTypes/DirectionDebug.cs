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
            ctx.Print($"[DIR DEBUG] symbol={ctx.SymbolName} bias={ctx.LogicBiasDirection} conf={ctx.LogicBiasConfidence}");

            if (SymbolRouting.NormalizeSymbol(ctx.Symbol) == "AUDNZD")
            {
                ctx.Print($"[AUDNZD TRACE] step3_entry={ctx.LogicBiasDirection} conf={ctx.LogicBiasConfidence}");
            }
        }
    }
}
