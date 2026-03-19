namespace GeminiV26.Core.Entry
{
    public class EntryEvaluation
    {
        public string Symbol;
        public EntryType Type;

        public bool IsValid;
        public TradeDirection Direction;

        // 0–100
        public int Score;

        // instrument bias
        public int LogicConfidence;

        public string Reason;

        public EntryState State = EntryState.NONE;

        public bool TriggerConfirmed;
    }
}
