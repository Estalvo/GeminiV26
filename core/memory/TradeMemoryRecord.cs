using System;

namespace GeminiV26.Core.Memory
{
    public class TradeMemoryRecord
    {
        public string Instrument { get; set; } = string.Empty;

        public string EntryType { get; set; } = string.Empty;

        public string SetupType { get; set; } = string.Empty;

        public string Direction { get; set; } = string.Empty;

        public double RMultiple { get; set; }

        public double MFE { get; set; }

        public double MAE { get; set; }

        public string MarketRegime { get; set; } = string.Empty;

        public double TransitionQuality { get; set; }

        public double Confidence { get; set; }

        public DateTime EntryTime { get; set; }

        public DateTime ExitTime { get; set; }
    }
}
