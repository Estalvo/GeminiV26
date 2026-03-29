using GeminiV26.Core.Entry;

namespace GeminiV26.Core
{
    public static class TradeLifecycleTracker
    {
        public static void UpdateMfeMae(PositionContext ctx, double currentPrice)
        {
            if (ctx == null || ctx.EntryPrice <= 0 || ctx.RiskPriceDistance <= 0)
                return;

            double rMove;

            if (ctx.FinalDirection == TradeDirection.Long)
                rMove = (currentPrice - ctx.EntryPrice) / ctx.RiskPriceDistance;
            else
                rMove = (ctx.EntryPrice - currentPrice) / ctx.RiskPriceDistance;

            if (rMove > ctx.MfeR)
                ctx.MfeR = rMove;

            if (rMove < ctx.MaeR)
                ctx.MaeR = rMove;

        }
    }
}
