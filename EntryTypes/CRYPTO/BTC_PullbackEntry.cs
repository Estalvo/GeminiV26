using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes.Crypto
{
    public sealed class BTC_PullbackEntry : CryptoPullbackEntryBase
    {
        protected override string SymbolTag => "BTC";
        protected override int MaxBarsSinceImpulse => 9;
        protected override double MaxPullbackDepth => 0.78;
    }
}
