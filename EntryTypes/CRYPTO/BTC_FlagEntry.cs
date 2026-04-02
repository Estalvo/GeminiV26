using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes.Crypto
{
    public sealed class BTC_FlagEntry : CryptoFlagEntryBase
    {
        protected override string SymbolTag => "BTC";
        protected override int MaxBarsSinceImpulse => 8;
        protected override int MaxLateBreakoutBars => 2;
        protected override double MinImpulseStrength => 0.45;
    }
}
