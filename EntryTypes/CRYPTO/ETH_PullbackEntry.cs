using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes.Crypto
{
    public sealed class ETH_PullbackEntry : CryptoPullbackEntryBase
    {
        protected override string SymbolTag => "ETH";
        protected override int MaxBarsSinceImpulse => 8;
        protected override double MaxPullbackDepth => 0.82;
    }
}
