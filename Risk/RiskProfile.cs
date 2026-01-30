using GeminiV26.Core.Entry;

namespace GeminiV26.Risk
{
    /// <summary>
    /// Phase 3.2 – score-alapú risk döntés eredménye
    /// </summary>
    public class RiskProfile
    {
        // ENTRY META
        public EntryType EntryType { get; init; }
        public TradeDirection Direction { get; init; }
        public int Score { get; init; }

        // RISK
        public double RiskPercent { get; init; }

        // SL
        public double StopLossAtrMultiplier { get; init; }
        public double StopLossDistance { get; init; }

        // TP
        public double TakeProfit1R { get; init; }
        public double TakeProfit1CloseRatio { get; init; }

        public double TakeProfit2R { get; init; }
        public double TakeProfit2CloseRatio { get; init; }

        // LOT
        public double LotSize { get; init; }
        public double LotCap { get; init; }

        // POST ENTRY
        public bool MoveStopToBreakevenAfterTp1 { get; init; }
        public bool EnableTrailing { get; init; }
    }
}
