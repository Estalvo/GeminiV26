using System.Collections.Generic;
using GeminiV26.Core;              // FxSession
using GeminiV26.Instruments.FX;    // FxPullbackStyle, FxFlagSessionTuning

namespace GeminiV26.EntryTypes.METAL
{
    /// <summary>
    /// XAU/XAG statikus profil (policy paraméterek).
    /// CSAK adat. NEM dönt, NEM számol.
    /// </summary>
    public sealed class XAU_InstrumentProfile
    {
        public string Symbol { get; init; } = "XAUUSD";

        // ===== MarketState küszöbök =====
        public double MinAtrPips { get; init; }
        public double MinAdxTrend { get; init; }
        public double MaxWickRatio { get; init; }

        public int RangeLookbackBars { get; init; }
        public double RangeMaxWidthAtr { get; init; }

        // ===== Session gate =====
        public bool AllowAsian { get; init; }
        public int SessionStartHour { get; init; }
        public int SessionEndHour { get; init; }

        // ===== ENTRY – structure / pullback policy (FX-kompatibilis) =====
        public double MaxFlagAtrMult { get; init; }
        public double MaxPullbackAtr { get; init; }
        public FxPullbackStyle PullbackStyle { get; init; }

        // ===== ENTRY – impulse / ATR policy =====
        public bool AtrExpansionHardBlock { get; init; }
        public int AtrExpandPenalty { get; init; }

        // ===== ENTRY – session score delta =====
        public Dictionary<FxSession, int>? SessionScoreDelta { get; init; }

        // ===== FLAG session tuning (FX-minta) =====
        public Dictionary<FxSession, FxFlagSessionTuning>? FlagTuning { get; init; }

        // ===== RiskSizer tuning =====
        public double LotCap { get; init; }
        public double SlAtrMultLow { get; init; }
        public double SlAtrMultNormal { get; init; }
        public double SlAtrMultHigh { get; init; }

        // ===== TP/Trailing policy (ExitManagerhez) =====
        public double Tp1R_High { get; init; }
        public double Tp1R_Normal { get; init; }
        public double Tp1R_Low { get; init; }

        public double BeOffsetR { get; init; }

        public double TrailAtrTight { get; init; }
        public double TrailAtrNormal { get; init; }
        public double TrailAtrLoose { get; init; }
        public double MinTrailImprovePips { get; init; }

        // ===== HTF bias tuning =====
        public int HtfBasePenalty { get; init; }
        public int HtfScalePenalty { get; init; }
    }
}
