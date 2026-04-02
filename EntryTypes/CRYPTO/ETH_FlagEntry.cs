using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes.Crypto
{
    public sealed class ETH_FlagEntry : CryptoFlagEntryBase
    {
        protected override string SymbolTag => "ETH";
        protected override int MaxBarsSinceImpulse => 7;
        protected override int MaxLateBreakoutBars => 2;
        protected override double MinImpulseStrength => 0.40;
    }
}
