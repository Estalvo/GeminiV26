namespace GeminiV26.Core.Entry
{
    public class EntryEvaluation
    {
        // Instrument scope
        public string Symbol;      

        // Entry type (Flag, Pullback stb.)
        public EntryType Type;

        // Router validáció
        public bool IsValid;

        // Trade irány
        public TradeDirection Direction;

        // Entry setup score (0–100)
        public double Score;

        // Instrument logic confidence (0–100)
        public double LogicConfidence;

        // Debug / log
        public string Reason;
    }
}