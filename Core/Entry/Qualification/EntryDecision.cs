namespace GeminiV26.Core.Entry.Qualification
{
    public enum EntryDecisionType
    {
        Block,
        Penalize,
        Pass
    }

    public sealed class EntryDecision
    {
        public EntryDecisionType Type { get; set; }
        public double Penalty { get; set; } = 0.0;
        public string Reason { get; set; } = string.Empty;

        public static EntryDecision Block(string reason)
            => new EntryDecision { Type = EntryDecisionType.Block, Reason = reason ?? string.Empty };

        public static EntryDecision Pass()
            => new EntryDecision { Type = EntryDecisionType.Pass };

        public static EntryDecision Penalize(double penalty, string reason)
            => new EntryDecision
            {
                Type = EntryDecisionType.Penalize,
                Penalty = penalty < 0 ? 0 : penalty,
                Reason = reason ?? string.Empty
            };
    }
}
