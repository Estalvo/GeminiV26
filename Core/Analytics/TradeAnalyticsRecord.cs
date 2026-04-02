using System;

namespace GeminiV26.Core.Analytics
{
    public class TradeAnalyticsRecord
    {
        public string Symbol;
        public string PositionId;
        public string SetupType;
        public string EntryType;
        public string InstrumentClass;
        public string MarketRegime;
        public double MfeR;
        public double MaeR;
        public double RMultiple;
        public double TransitionQuality;
        // Backward-compatible alias column; authoritative value is FinalConfidence.
        public double FinalConfidence;
        public double Confidence;
        public double Profit;
        public DateTime OpenTimeUtc;
        public DateTime CloseTimeUtc;
    }
}
