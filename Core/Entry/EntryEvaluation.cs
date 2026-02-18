namespace GeminiV26.Core.Entry
{
    public class EntryEvaluation
    {
        public string Symbol;          // kötelező, instrument-scope
        public EntryType Type;
        //public int LogicConfidence = 0;   // 0–100, instrument bias

        public bool IsValid;
        public TradeDirection Direction;

        public int Score;              // 0–100
        public string Reason;
    }
}
