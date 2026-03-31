using System;

namespace GeminiV26.Core.Entry.Qualification
{
    public static class EntryQualificationEngine
    {
        public static EntryDecision Evaluate(EntryContext ctx, EntryType entryType)
        {
            if (ctx == null)
                return EntryDecision.Pass();

            if (IsContinuationEntry(entryType))
                return ContinuationPolicy.Evaluate(ctx, entryType);

            return EntryDecision.Pass();
        }

        private static bool IsContinuationEntry(EntryType entryType)
        {
            string typeName = entryType.ToString();
            return typeName.IndexOf("Flag", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Pullback", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Breakout", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
