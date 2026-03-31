using System;
using cAlgo.API;

namespace GeminiV26.Core.Risk.PositionSizing
{
    public static class MetalPositionSizer
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

            double pipValuePerLot = bot.Symbol.PipValue * bot.Symbol.LotSize;
            if (pipValuePerLot <= 0)
                return 0;

            double rawLots = riskAmount / (slPips * pipValuePerLot);
            double rawUnits = rawLots * bot.Symbol.LotSize;
            double capUnits = lotCap * bot.Symbol.LotSize;
            double finalUnits = Math.Min(rawUnits, capUnits);

            long normalized =
                (long)bot.Symbol.NormalizeVolumeInUnits(
                    finalUnits,
                    RoundingMode.Down);

            GlobalLogger.Log(bot, 
                $"[POSITION SIZER] {bot.SymbolName} " +
                $"balance={balance:F2} risk%={riskPercent:F3} " +
                $"riskAmount={riskAmount:F2} slPips={slPips:F2} " +
                $"rawLots={rawLots:F4} rawUnits={rawUnits:F0} " +
                $"capUnits={capUnits:F0} normalized={normalized}");

            bool capped = rawUnits > capUnits;
            double effectiveRisk = (Math.Min(rawUnits, capUnits) / bot.Symbol.LotSize) * slPips * pipValuePerLot;
            double riskDeviationPercent = riskAmount > 0
                ? ((riskAmount - effectiveRisk) / riskAmount) * 100.0
                : 0.0;
            GlobalLogger.Log(bot, $"[SCALING][LOTCAP] accountSize={bot.Account.Balance:0.##} desiredVolume={rawUnits:0.####} actualVolume={finalUnits:0.####} capped={capped.ToString().ToLowerInvariant()} riskDeviationPercent={riskDeviationPercent:0.####}");

            if (normalized < bot.Symbol.VolumeInUnitsMin)
                return 0;

            return normalized;
        }
    }
}
