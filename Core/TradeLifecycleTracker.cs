using GeminiV26.Core.Entry;

namespace GeminiV26.Core
{
    public static class TradeLifecycleTracker
    {
        public static void UpdateMfeMae(PositionContext ctx, double currentPrice)
        {
            if (ctx == null || ctx.EntryPrice <= 0 || ctx.RiskPriceDistance <= 0)
                return;
            if (ctx.Bot == null)
                return;
            if (ctx.FinalDirection == TradeDirection.None)
                return;

            ctx.Bot.Print($"[MFE_TRACKER] active dir={ctx.FinalDirection} entry={ctx.EntryPrice} risk={ctx.RiskPriceDistance}");
            ctx.Bot.Print($"[MFE_TICK] time={System.DateTime.UtcNow:HH:mm:ss.fff} price={currentPrice}");

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

            ctx.Bot.Print($"[MFE] value={ctx.MfeR:F2} price={currentPrice}");
            ctx.Bot.Print($"[MAE] value={ctx.MaeR:F2} price={currentPrice}");

        }
    }
}
