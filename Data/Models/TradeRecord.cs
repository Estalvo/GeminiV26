using System;

namespace GeminiV26.Data.Models
{
    public class TradeRecord
    {
        public DateTime CloseTimestamp { get; set; }

        public string Symbol { get; set; } = string.Empty;

        public long PositionId { get; set; }

        public string Direction { get; set; } = string.Empty;

        
        public string EntryType { get; set; } = string.Empty;

        public string EntryReason { get; set; } = string.Empty;
        
        public DateTime EntryTime { get; set; }
        public DateTime ExitTime { get; set; }

        public double EntryPrice { get; set; }
        public double ExitPrice { get; set; }

        public double VolumeInUnits { get; set; }

        // Analytics volumes (units)
        public double? EntryVolumeInUnits { get; set; }
        public double? Tp1ClosedVolumeInUnits { get; set; }
        public double? RemainingVolumeInUnits { get; set; }

        public int? Confidence { get; set; }
        public bool? Tp1Hit { get; set; }
        public bool? Tp2Hit { get; set; }

        public string ExitReason { get; set; } = string.Empty;

        public double NetProfit { get; set; }
        public double GrossProfit { get; set; }
        public double Commissions { get; set; }
        public double Swap { get; set; }

        public double Pips { get; set; }

        // --- Risk / sizing ---
        public double? RiskPercent { get; set; }
        public double? SlAtrMult { get; set; }
        public double? Tp1R { get; set; }
        public double? Tp2R { get; set; }
        public bool? LotCapHit { get; set; }

        // --- Exit diagnostics ---
        public bool? BeActivated { get; set; }
        public bool? TrailingActivated { get; set; }
        public string ExitMode { get; set; } // TP1 / TP2 / BE / TRAIL / HARDLOSS / SL

    }
}