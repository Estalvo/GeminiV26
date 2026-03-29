using GeminiV26.Core.Entry;

namespace GeminiV26.Core
{
    public static class TradeLifecycleTracker
    {
        public static void UpdateMfeMae(PositionContext ctx, double currentPrice)
        {
            System.Console.WriteLine($"[MFE_CALL] time={System.DateTime.UtcNow:HH:mm:ss.fff} ctxNull={ctx == null} price={currentPrice}");
            if (ctx != null)
            {
                System.Console.WriteLine($"[MFE_CTX] hasBot={(ctx.Bot != null)} entry={ctx.EntryPrice} side={ctx.Side}");
            }

            if (ctx == null)
            {
                System.Console.WriteLine("[MFE_GUARD] ctx null");
                return;
            }

            if (ctx.EntryPrice <= 0)
            {
                System.Console.WriteLine("[MFE_GUARD] invalid entry price");
                return;
            }

            if (ctx.RiskPriceDistance <= 0)
            {
                System.Console.WriteLine("[MFE_GUARD] invalid risk distance");
                return;
            }

            if (currentPrice <= 0)
            {
                System.Console.WriteLine("[MFE_GUARD] invalid price");
                return;
            }

            if (ctx.FinalDirection == TradeDirection.None)
            {
                System.Console.WriteLine("[MFE_GUARD] final direction none");
                return;
            }

            double rMove;
            double riskDistance = ctx.RiskPriceDistance;
            System.Console.WriteLine($"[MFE_BEFORE] mfe={ctx.MfeR} mae={ctx.MaeR}");

            if (ctx.FinalDirection == TradeDirection.Long)
                rMove = (currentPrice - ctx.EntryPrice) / riskDistance;
            else
                rMove = (ctx.EntryPrice - currentPrice) / riskDistance;

            if (rMove > ctx.MfeR)
                ctx.MfeR = rMove;

            if (rMove < ctx.MaeR)
                ctx.MaeR = rMove;

            System.Console.WriteLine($"[MFE_AFTER] mfe={ctx.MfeR} mae={ctx.MaeR}");
            System.Console.WriteLine($"[MFE_LOG_CONSOLE] mfe={ctx.MfeR} mae={ctx.MaeR}");
            if (ctx.Bot != null)
            {
                ctx.Bot.Print($"[MFE_LOG_BOT] mfe={ctx.MfeR} mae={ctx.MaeR}");
            }
            else
            {
                System.Console.WriteLine("[MFE_LOG_BOT] bot NULL");
            }

        }
    }
}
