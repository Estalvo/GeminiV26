using GeminiV26.Core.Entry;
using GeminiV26.Data;
using GeminiV26.Data.Models;
using GeminiV26.Risk;
using cAlgo.API;

namespace GeminiV26.Core
{
    public static class DryRunExecutor
    {
        public static void Execute(
            Robot bot,
            EntryEvaluation evaluation,
            EntryContext ctx,
            double atr,
            EventLogger eventLogger
        )
        {
            // 1️⃣ Risk sizer kiválasztása (instrument-specifikus)
            var riskSizer = RiskSizerFactory.Create(ctx.Symbol);

            // 2️⃣ Risk profile számítás
            RiskProfile risk = RiskProfileResolver.Resolve(
                evaluation,
                atr,
                evaluation.Type,
                riskSizer
            );

            // 3️⃣ Konzol log (cTrader)
            bot.Print(
                $"[DRY-RUN] {ctx.Symbol} | " +
                $"Score={risk.Score} | " +
                $"Entry={risk.EntryType} | " +
                $"Dir={risk.Direction} | " +
                $"Risk={risk.RiskPercent:F2}% | " +
                $"SL={risk.StopLossDistance:F2} | " +
                $"TP1={risk.TakeProfit1R:F2}R ({risk.TakeProfit1CloseRatio:P0}) | " +
                $"TP2={risk.TakeProfit2R:F2}R ({risk.TakeProfit2CloseRatio:P0}) | " +
                $"Lot={risk.LotSize:F2} | Cap={risk.LotCap:F2}"
            );

            // 4️⃣ CSV / Event log
            eventLogger.Log(new EventRecord
            {
                EventTimestamp = bot.Server.Time,
                Symbol = ctx.Symbol,
                EventType = "DryRunTrade",
                Reason =
                    $"Score={risk.Score};" +
                    $"Entry={risk.EntryType};" +
                    $"Dir={risk.Direction};" +
                    $"Risk={risk.RiskPercent};" +
                    $"SL_ATR={risk.StopLossAtrMultiplier};" +
                    $"SL_Dist={risk.StopLossDistance};" +
                    $"TP1R={risk.TakeProfit1R};" +
                    $"TP1Close={risk.TakeProfit1CloseRatio};" +
                    $"TP2R={risk.TakeProfit2R};" +
                    $"TP2Close={risk.TakeProfit2CloseRatio};" +
                    $"Lot={risk.LotSize};" +
                    $"LotCap={risk.LotCap};" +
                    $"BE={risk.MoveStopToBreakevenAfterTp1};" +
                    $"Trail={risk.EnableTrailing}"
            });
        }
    }
}
