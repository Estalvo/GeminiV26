using System;
using cAlgo.API;

namespace GeminiV26.Core.Risk.PositionSizing
{
    public static class IndexPositionSizer
    {
        public static long Calculate(
            Robot bot,
            double riskPercent,
            double slPriceDistance,
            double lotCap)
        {
            if (riskPercent <= 0 || slPriceDistance <= 0 || lotCap <= 0)
                return 0;

            double balance = bot.Account.Balance;
            double riskAmount = balance * (riskPercent / 100.0);
            double slPoints = Math.Abs(slPriceDistance) / bot.Symbol.TickSize;
            if (slPoints <= 0)
                return 0;

            double valuePerPoint = bot.Symbol.TickValue;
            if (valuePerPoint <= 0)
                return 0;

            double rawLots = riskAmount / (slPoints * valuePerPoint);
            double rawUnits = rawLots * bot.Symbol.LotSize;
            double capUnits = lotCap * bot.Symbol.LotSize;
            double finalUnits = Math.Min(rawUnits, capUnits);

            long normalized =
                (long)bot.Symbol.NormalizeVolumeInUnits(
                    finalUnits,
                    RoundingMode.Down);

            GlobalLogger.Log(
                $"[POSITION SIZER] {bot.SymbolName} " +
                $"balance={balance:F2} risk%={riskPercent:F3} " +
                $"riskAmount={riskAmount:F2} slPips={slPoints:F2} " +
                $"rawLots={rawLots:F4} rawUnits={rawUnits:F0} " +
                $"capUnits={capUnits:F0} normalized={normalized}");

            if (normalized < bot.Symbol.VolumeInUnitsMin)
                return 0;

            return normalized;
        }
    }
}
