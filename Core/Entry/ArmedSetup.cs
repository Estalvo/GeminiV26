using System;

namespace GeminiV26.Core.Entry
{
    public sealed class ArmedSetup
    {
        public string Symbol { get; set; } = string.Empty;
        public TradeDirection Direction { get; set; } = TradeDirection.None;
        public double Score { get; set; }
        public DateTime DetectedAt { get; set; }
        public int BarsSince { get; set; }

        public EntryType EntryType { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
