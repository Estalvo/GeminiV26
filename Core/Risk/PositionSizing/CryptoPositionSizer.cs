using System;
using cAlgo.API;

namespace GeminiV26.Core.Risk.PositionSizing
{
    public static class CryptoPositionSizer
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
            double rawUnits = riskAmount / slPriceDistance;
            double capUnits = lotCap * bot.Symbol.LotSize;
            double finalUnits = Math.Min(rawUnits, capUnits);

            long normalized =
                (long)bot.Symbol.NormalizeVolumeInUnits(
                    finalUnits,
                    RoundingMode.Down);

            bot.Print(
                $"[POSITION SIZER] {bot.SymbolName} " +
                $"balance={balance:F2} risk%={riskPercent:F3} " +
                $"riskAmount={riskAmount:F2} slPips={(slPriceDistance / bot.Symbol.PipSize):F2} " +
                $"rawLots=0.0000 rawUnits={rawUnits:F0} " +
                $"capUnits={capUnits:F0} normalized={normalized}");

            if (normalized < bot.Symbol.VolumeInUnitsMin)
                return 0;

            return normalized;
        }
    }
}
