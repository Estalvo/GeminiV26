using GeminiV26.Core.Entry;

namespace GeminiV26.Core
{
    public static class TradeLifecycleTracker
    {
        public static void UpdateMfeMae(PositionContext ctx, double currentPrice)
        {
            GlobalLogger.Log(ctx.Bot, $"[MFE_CALL] time={System.DateTime.UtcNow:HH:mm:ss.fff} ctxNull={ctx == null} price={currentPrice}");
            if (ctx != null)
            {
                GlobalLogger.Log(ctx.Bot, $"[MFE_CTX] hasBot={(ctx.Bot != null)} entry={ctx.EntryPrice} side={ctx.FinalDirection}");
            }

            if (ctx == null)
            {
                GlobalLogger.Log(ctx.Bot, "[MFE_GUARD] ctx null");
                return;
            }

            if (ctx.EntryPrice <= 0)
            {
                GlobalLogger.Log(ctx.Bot, "[MFE_GUARD] invalid entry price");
                return;
            }

            if (ctx.RiskPriceDistance <= 0)
            {
                GlobalLogger.Log(ctx.Bot, "[MFE_GUARD] invalid risk distance");
                return;
            }

            if (currentPrice <= 0)
            {
                GlobalLogger.Log(ctx.Bot, "[MFE_GUARD] invalid price");
                return;
            }

            if (ctx.FinalDirection == TradeDirection.None)
            {
                GlobalLogger.Log(ctx.Bot, "[MFE_GUARD] final direction none");
                return;
            }

            double rMove;
            double riskDistance = ctx.RiskPriceDistance;
            GlobalLogger.Log(ctx.Bot, $"[MFE_BEFORE] mfe={ctx.MfeR} mae={ctx.MaeR}");

            if (ctx.FinalDirection == TradeDirection.Long)
                rMove = (currentPrice - ctx.EntryPrice) / riskDistance;
            else
                rMove = (ctx.EntryPrice - currentPrice) / riskDistance;

            if (rMove > ctx.MfeR)
                ctx.MfeR = rMove;

            if (rMove < ctx.MaeR)
                ctx.MaeR = rMove;

            GlobalLogger.Log(ctx.Bot, $"[MFE_AFTER] mfe={ctx.MfeR} mae={ctx.MaeR}");
            GlobalLogger.Log(ctx.Bot, $"[MFE_LOG_CONSOLE] mfe={ctx.MfeR} mae={ctx.MaeR}");
            if (ctx.Bot != null)
            {
                GlobalLogger.Log(ctx.Bot, $"[MFE_LOG_BOT] mfe={ctx.MfeR} mae={ctx.MaeR}");
            }
            else
            {
                GlobalLogger.Log(ctx.Bot, "[MFE_LOG_BOT] bot NULL");
            }

        }
    }
}
