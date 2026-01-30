using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes
{
    public interface IEntryType
    {
        EntryType Type { get; }

        EntryEvaluation Evaluate(EntryContext ctx);
    }
}
