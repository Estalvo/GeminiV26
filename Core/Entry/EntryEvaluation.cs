namespace GeminiV26.Core.Entry
{
    public class EntryEvaluation
    {
        public string Symbol;
        public EntryType Type;

        // Raw validity from entry evaluator (before downstream filters).
        public bool RawValid;

        // Existing final validity used in routing/decision.
        public bool IsValid;
        public bool FinalValid
        {
            get => IsValid;
            set => IsValid = value;
        }
        public TradeDirection Direction;

        // 0–100
        public int Score;

        // Score tracing (observability only).
        public int PreQualityScore;
        public int PostQualityScore;
        public int PostCapScore;
        public bool HasQualityScoreTrace;

        // instrument bias
        public int LogicConfidence;

        public string Reason;
        public string RejectReason;

        public EntryState State = EntryState.NONE;

        public bool TriggerConfirmed;

        public bool HasTrigger
        {
            get => TriggerConfirmed;
            set => TriggerConfirmed = value;
        }

        public bool IsHTFMisaligned;
        public bool IgnoreHTFForDecision;
        public double HtfConfidence01;
    }
}
