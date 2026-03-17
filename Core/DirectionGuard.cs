using System;
using GeminiV26.Core.Entry;

namespace GeminiV26.Core
{
    public static class DirectionGuard
    {
        public static void Validate(EntryContext entryCtx, PositionContext posCtx)
            => Validate(entryCtx, posCtx, null);

        public static void Validate(EntryContext entryCtx, PositionContext posCtx, Action<string> log)
        {
            if (entryCtx == null)
            {
                log?.Invoke("[DIR][GUARD] Missing EntryContext");
                return;
            }

            if (entryCtx.FinalDirection == TradeDirection.None)
            {
                log?.Invoke("[DIR][GUARD] EntryContext FinalDirection=None");
            }

            if (posCtx != null && posCtx.FinalDirection != entryCtx.FinalDirection)
            {
                log?.Invoke(
                    $"[DIR][GUARD_MISMATCH] posId={posCtx.PositionId} posFinal={posCtx.FinalDirection} entryFinal={entryCtx.FinalDirection}");
            }
        }
    }
}
