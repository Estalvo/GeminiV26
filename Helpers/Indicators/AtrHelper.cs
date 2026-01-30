using cAlgo.API;

namespace GeminiV26.Helpers.Indicators
{
    public static class AtrHelper
    {
        public static double GetAtr(
            Robot bot,
            Bars bars,
            int period = 14
        )
        {
            var atr = bot.Indicators.AverageTrueRange(
                bars,
                period,
                MovingAverageType.Simple
            );

            return atr.Result.LastValue;
        }
    }
}
