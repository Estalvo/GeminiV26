using cAlgo.API;
using cAlgo.API.Internals;
using Gemini.Memory;
using GeminiV26.Instruments.FX;
using GeminiV26.Instruments.INDEX;
using GeminiV26.Core;
using GeminiV26.Core.Matrix;
using GeminiV26.Core.Logging;
using System;

namespace GeminiV26.Core.Entry
{
    public class EntryContext
    {
        // =========================
        // CORE
        // =========================
        public bool IsReady { get; set; }
        public string Symbol;
        public string TempId { get; set; }
        public string EntryAttemptId
        {
            get => TempId;
            set => TempId = value;
        }
        public Action<string> Log { get; set; }

        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;
        public Robot Bot { get; }

        // =========================
        // ⚠️ LEGACY TREND (READ ONLY)
        // =========================
        // NE írja detector!
        public TradeDirection TrendDirection;

        // =========================
        // BARS
        // =========================
        public Bars M1;
        public Bars M5;
        public Bars M15;

        public int BarsSinceHighBreak_M5 { get; set; }
        public int BarsSinceLowBreak_M5 { get; set; }
        public int BarsSinceStart { get; set; }

        public double PipSize { get; set; }

        // =========================
        // EMA
        // =========================
        public double Ema8_M5;
        public double Ema21_M5;
        public double Ema21_M15;

        public double Ema50_M5;
        public double Ema200_M5;

        public double Ema50_M15;
        public double Ema200_M15;

        public double Ema21Slope_M5;
        public double Ema21Slope_M15;

        // =========================
        // ATR
        // =========================
        public double AtrM5;
        public double AtrM15;

        public double AtrSlope_M5;
        public double AtrAcceleration_M5;

        public double AtrPips_M5 { get; set; }
        public double FlagAtr_M5 { get; set; }

        // =========================
        // ADX / DMS
        // =========================
        public double Adx_M5 { get; set; }
        public double AdxSlope_M5 { get; set; }
        public double AdxAcceleration_M5 { get; set; }

        public double PlusDI_M5 { get; set; }
        public double MinusDI_M5 { get; set; }
        public double DiSpread_M5 { get; set; }

        // =========================
        // STRUCTURE
        // =========================
        public bool BrokeLastSwingHigh_M5;
        public bool BrokeLastSwingLow_M5;

        // =========================
        // M1 SIGNALS
        // =========================
        public bool HasImpulse_M1;

        // ⚠️ LEGACY (NE használd új logikában)
        public TradeDirection ImpulseDirection;

        public bool HasBreakout_M1;
        // LEGACY / DIAGNOSTIC ONLY
        public TradeDirection BreakoutDirection;

        // =========================
        // M5 LEGACY STATE
        // =========================
        // ⚠️ csak backward compatibility
        public bool HasImpulse_M5;
        public int BarsSinceImpulse_M5 { get; set; } = 999;

        public int PullbackBars_M5;
        public double PullbackDepthAtr_M5;

        // =========================
        // 2-SIDED IMPULSE
        // =========================
        public bool HasImpulseLong_M5 { get; set; }
        public bool HasImpulseShort_M5 { get; set; }

        public int BarsSinceImpulseLong_M5 { get; set; } = 999;
        public int BarsSinceImpulseShort_M5 { get; set; } = 999;

        // =========================
        // 2-SIDED PULLBACK
        // =========================
        public bool HasPullbackLong_M5 { get; set; }
        public bool HasPullbackShort_M5 { get; set; }

        public int PullbackBarsLong_M5 { get; set; }
        public int PullbackBarsShort_M5 { get; set; }

        // R alapú (új)
        public double PullbackDepthRLong_M5 { get; set; }
        public double PullbackDepthRShort_M5 { get; set; }

        // =========================
        // PULLBACK MICRO STRUCTURE
        // =========================
        public bool PullbackTouchedEma21_M5;

        public bool IsPullbackDecelerating_M5 { get; set; }
        public bool IsTransition_M5 { get; set; }
        public bool HasEarlyPullback_M5 { get; set; }
        public bool HasRejectionWick_M5 { get; set; }
        public bool HasReactionCandle_M5 { get; set; }

        public double AvgBodyLast3_M5 { get; set; }
        public double AvgBodyPrev3_M5 { get; set; }

        public bool LastClosedBarInTrendDirection { get; set; }

        // =========================
        // 2-SIDED FLAG
        // =========================
        public bool HasFlagLong_M5 { get; set; }
        public bool HasFlagShort_M5 { get; set; }

        public int FlagBarsLong_M5 { get; set; }
        public int FlagBarsShort_M5 { get; set; }

        public double FlagCompressionScoreLong_M5 { get; set; }
        public double FlagCompressionScoreShort_M5 { get; set; }

        // =========================
        // FLAG RANGE
        // =========================
        public double FlagHigh { get; set; }
        public double FlagLow { get; set; }

        // =========================
        // 2-SIDED BREAKOUT
        // =========================
        public bool FlagBreakoutUp { get; set; }
        public bool FlagBreakoutDown { get; set; }

        public bool FlagBreakoutUpConfirmed { get; set; }
        public bool FlagBreakoutDownConfirmed { get; set; }

        public int BreakoutUpBarsSince { get; set; } = 999;
        public int BreakoutDownBarsSince { get; set; } = 999;

        // =========================
        // LEGACY BREAKOUT
        // =========================
        public bool FlagBreakoutConfirmed { get; set; }
        public int BreakoutBarsSince { get; set; } = 999;

        // =========================
        // TRANSITION (2-SIDED)
        // =========================
        public TransitionEvaluation TransitionLong { get; set; }
        public TransitionEvaluation TransitionShort { get; set; }

        // =========================
        // LEGACY TRANSITION
        // =========================
        public bool TransitionValid { get; set; }
        public int TransitionScoreBonus { get; set; }
        public TransitionEvaluation Transition { get; set; }

        // =========================
        // RANGE
        // =========================
        public bool IsRange_M5;
        public int RangeBarCount_M5;
        public TradeDirection RangeBreakDirection;
        public double RangeBreakAtrSize_M5;
        public int RangeFakeoutBars_M1;

        // =========================
        // VOLATILITY
        // =========================
        public bool IsAtrExpanding_M5;
        public bool IsVolumeIncreasing_M5;
        public bool IsVolatilityAcceptable_Crypto { get; set; }

        // =========================
        // MARKET STATE
        // =========================
        public IndexMarketState MarketState { get; set; }

        // =========================
        // M1 TRIGGERS
        // =========================
        public bool M1TriggerInTrendDirection;
        public bool M1FlagBreakTrigger;
        public bool M1ReversalTrigger;

        // =========================
        // REVERSAL
        // =========================
        public int ReversalEvidenceScore;
        // LEGACY / DIAGNOSTIC ONLY
        public TradeDirection ReversalDirection;

        // =========================
        // HTF BIAS
        // =========================
        public TradeDirection FxHtfAllowedDirection { get; set; } = TradeDirection.None;
        public double FxHtfConfidence01 { get; set; } = 0.0;
        public string FxHtfReason { get; set; }

        public TradeDirection CryptoHtfAllowedDirection { get; set; } = TradeDirection.None;
        public double CryptoHtfConfidence01 { get; set; } = 0.0;
        public string CryptoHtfReason { get; set; }

        public TradeDirection IndexHtfAllowedDirection { get; set; } = TradeDirection.None;
        public double IndexHtfConfidence01 { get; set; } = 0.0;
        public string IndexHtfReason { get; set; }

        public TradeDirection MetalHtfAllowedDirection { get; set; } = TradeDirection.None;
        public double MetalHtfConfidence01 { get; set; } = 0.0;
        public string MetalHtfReason { get; set; }

        // Selected active HTF (single-source runtime fields)
        public TradeDirection ActiveHtfDirection { get; set; } = TradeDirection.None;
        public double ActiveHtfConfidence { get; set; } = 0.0;

        // =========================
        // DIRECTION FLOW (SSOT)
        // =========================
        // LEGACY / DIAGNOSTIC ONLY (pre-routing analytics)
        public TradeDirection LogicBiasDirection { get; set; } = TradeDirection.None;
        public int LogicBiasConfidence { get; set; } = 0;
        public TradeDirection RoutedDirection { get; set; } = TradeDirection.None;
        public TradeDirection FinalDirection { get; set; } = TradeDirection.None;
        public bool DirectionDebugLogged { get; set; }

        // Finalized snapshot fields (read-only source for audit snapshot generation)
        public int EntryScore { get; set; }
        public int FinalConfidence { get; set; }
        public int RiskConfidence { get; set; }

        public double TotalMoveSinceBreakAtr { get; set; }

        // =========================
        // SESSION
        // =========================
        public FxSession Session { get; set; }

        public SessionMatrixConfig SessionMatrixConfig { get; set; }
            = SessionMatrixDefaults.Neutral;

        // =========================
        // MEMORY / RESOLVER SNAPSHOT
        // =========================
        public bool RuntimeResolved { get; set; }
        public bool MemoryResolved { get; set; }
        public bool MemoryUsable { get; set; }
        public SymbolMemoryState Memory { get; set; }
        public bool HasMemory => Memory != null;
        public SymbolMemoryState MemoryState { get; set; }
        public MovePhase MovePhase => MemoryState?.MovePhase ?? MovePhase.Unknown;
        public int BarsSinceFirstPullback => MemoryState?.BarsSinceFirstPullback ?? -1;
        public MemoryAssessment MemoryAssessment { get; set; }
        public ContinuationWindowState MemoryContinuationWindow { get; set; } = ContinuationWindowState.Unknown;
        public MoveExtensionState MemoryMoveExtension { get; set; } = MoveExtensionState.Unknown;
        public double MemoryImpulseFreshnessScore { get; set; }
        public double MemoryContinuationFreshnessScore { get; set; }
        public double MemoryTriggerLateScore { get; set; }
        public int MemoryTimingPenalty { get; set; }

        // =========================
        // SIDE-AWARE TIMING
        // =========================
        // CONTINUATION TIMING
        public bool HasFreshPullbackLong { get; set; }
        public bool HasFreshPullbackShort { get; set; }
        public bool HasEarlyContinuationLong { get; set; }
        public bool HasEarlyContinuationShort { get; set; }
        public bool HasLateContinuationLong { get; set; }
        public bool HasLateContinuationShort { get; set; }
        public bool IsOverextendedLong { get; set; }
        public bool IsOverextendedShort { get; set; }
        public bool IsTimingLongActive { get; set; }
        public bool IsTimingShortActive { get; set; }

        // STRUCTURE AGE
        public int BarsSinceStructureBreakLong { get; set; } = -1;
        public int BarsSinceStructureBreakShort { get; set; } = -1;
        public int BarsSinceImpulseLong { get; set; } = -1;
        public int BarsSinceImpulseShort { get; set; } = -1;
        public int ContinuationAttemptCountLong { get; set; }
        public int ContinuationAttemptCountShort { get; set; }

        // DISTANCE
        public double DistanceFromFastStructureAtrLong { get; set; }
        public double DistanceFromFastStructureAtrShort { get; set; }

        // QUALITY
        public double ContinuationFreshnessLong { get; set; }
        public double ContinuationFreshnessShort { get; set; }
        public double TriggerLateScoreLong { get; set; }
        public double TriggerLateScoreShort { get; set; }

        // =========================
        // TEMP BACKWARD COMPAT
        // =========================
        [Obsolete("LEGACY – use LogicBiasDirection")]
        public TradeDirection LogicBias => LogicBiasDirection;

        [Obsolete("LEGACY – use LogicBiasConfidence")]
        public int LogicConfidence => LogicBiasConfidence;

        [Obsolete("LEGACY – use ActiveHtfDirection")]
        public TradeDirection HtfDirection => ActiveHtfDirection;

        [Obsolete("LEGACY – use ActiveHtfConfidence")]
        public double HtfConfidence => ActiveHtfConfidence;

        public TradeDirection ResolveAssetHtfAllowedDirection() => ActiveHtfDirection;

        public double ResolveAssetHtfConfidence01() => ActiveHtfConfidence;

        public string SymbolName => Symbol;

        public int GetBarsSinceImpulse(TradeDirection direction)
        {
            return direction switch
            {
                TradeDirection.Long => BarsSinceImpulseLong_M5,
                TradeDirection.Short => BarsSinceImpulseShort_M5,
                _ => BarsSinceImpulse_M5
            };
        }

        public bool HasDirectionalPullback(TradeDirection direction)
        {
            return direction switch
            {
                TradeDirection.Long => HasPullbackActiveSide_M5(direction),
                TradeDirection.Short => HasPullbackActiveSide_M5(direction),
                _ => GetCrossSidePullbackFallback()
            };
        }

        public bool HasPullbackActiveSide_M5(TradeDirection dir)
            => dir == TradeDirection.Long
                ? HasPullbackLong_M5
                : HasPullbackShort_M5;

        public bool HasPullbackInactiveSide_M5(TradeDirection dir)
            => dir == TradeDirection.Long
                ? HasPullbackShort_M5
                : HasPullbackLong_M5;

        public bool IsValidFlagStructure_M5 =>
            HasFlagLong_M5 || HasFlagShort_M5;

        public bool IsValidFlagStructureSide_M5(TradeDirection dir)
        {
            return dir == TradeDirection.Long
                ? HasFlagLong_M5
                : HasFlagShort_M5;
        }

        private bool GetCrossSidePullbackFallback()
        {
            GlobalLogger.Log("[CTX][DIR_WARNING] cross-side logic retained (no direction available)");

            bool hasAnySidePullback = HasPullbackLong_M5;
            hasAnySidePullback |= HasPullbackShort_M5;

            return hasAnySidePullback || PullbackTouchedEma21_M5;
        }
    }
}
