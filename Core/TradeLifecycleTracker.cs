using GeminiV26.Core.Entry;

namespace GeminiV26.Core
{
    public static class TradeLifecycleTracker
    {
        public static void UpdateMfeMae(PositionContext ctx, double currentPrice)
        {
            if (ctx == null || ctx.EntryPrice <= 0 || ctx.RiskPriceDistance <= 0)
                return;
            if (ctx.FinalDirection == TradeDirection.None)
                return;

            double rMove;
            double riskDistance = ctx.RiskPriceDistance;

            if (ctx.FinalDirection == TradeDirection.Long)
                rMove = (currentPrice - ctx.EntryPrice) / riskDistance;
            else
                rMove = (ctx.EntryPrice - currentPrice) / riskDistance;

            if (rMove > ctx.MfeR)
                ctx.MfeR = rMove;

            if (rMove < ctx.MaeR)
                ctx.MaeR = rMove;

        }
    }
}
