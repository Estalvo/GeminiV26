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
            string symbol = bot.SymbolName ?? "UNKNOWN";

            long normalized =
                (long)bot.Symbol.NormalizeVolumeInUnits(
                    finalUnits,
                    RoundingMode.Down);

            GlobalLogger.Log(bot, $"[CRYPTO SIZE RAW] symbol={symbol} balance={balance:0.##} riskPercent={riskPercent:0.###} riskAmount={riskAmount:0.###} slDistance={slPriceDistance:0.########} rawUnits={rawUnits:0.###} capUnits={capUnits:0.###} finalUnits={finalUnits:0.###}");
            GlobalLogger.Log(bot, $"[CRYPTO SIZE NORMALIZED] symbol={symbol} normalizedUnits={normalized} minVolume={bot.Symbol.VolumeInUnitsMin} step={bot.Symbol.VolumeInUnitsStep} lotSize={bot.Symbol.LotSize}");

            if (normalized < bot.Symbol.VolumeInUnitsMin)
            {
                GlobalLogger.Log(bot, $"[CRYPTO SIZE ZERO] symbol={symbol} reason=below_min_after_normalization finalUnits={finalUnits:0.###} normalizedUnits={normalized} minVolume={bot.Symbol.VolumeInUnitsMin} step={bot.Symbol.VolumeInUnitsStep} riskPercent={riskPercent:0.###} slDistance={slPriceDistance:0.########}");
                return 0;
            }

            return normalized;
        }
    }
}
