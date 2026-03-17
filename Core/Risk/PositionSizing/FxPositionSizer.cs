using System;
using cAlgo.API;

namespace GeminiV26.Core.Risk.PositionSizing
{
    public static class FxPositionSizer
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
            double slPips = slPriceDistance / bot.Symbol.PipSize;

            if (slPips <= 0)
                return 0;

            // ✅ FIX: pipValue PER UNIT (nem per lot!)
            double pipValuePerUnit = bot.Symbol.PipValue;
            if (pipValuePerUnit <= 0)
                return 0;

            // ✅ közvetlen units számítás
            double rawUnits = riskAmount / (slPips * pipValuePerUnit);

            // cap már eleve units-ben kellene legyen
            double capUnits = lotCap * bot.Symbol.LotSize;

            double finalUnits = Math.Min(rawUnits, capUnits);

            long normalized =
                (long)bot.Symbol.NormalizeVolumeInUnits(
                    finalUnits,
                    RoundingMode.Down);

            bot.Print(
                $"[POSITION SIZER] {bot.SymbolName} " +
                $"balance={balance:F2} risk%={riskPercent:F3} " +
                $"riskAmount={riskAmount:F2} slPips={slPips:F2} " +
                $"rawUnits={rawUnits:F0} " +
                $"capUnits={capUnits:F0} normalized={normalized}");

            if (normalized < bot.Symbol.VolumeInUnitsMin)
                return 0;

            return normalized;
        }
    }
}
