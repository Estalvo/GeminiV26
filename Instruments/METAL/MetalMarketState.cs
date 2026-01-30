using cAlgo.API;
using cAlgo.API.Indicators;

namespace GeminiV26.Instruments.METAL
{
    /// <summary>
    /// METAL Market State – környezeti állapot
    /// (XAU mellett későbbi fémekhez is)
    /// </summary>
    public sealed class MetalMarketState
    {
        public double AtrPips { get; init; }
        public bool IsRange { get; init; }
    }
}
