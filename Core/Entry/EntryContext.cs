using cAlgo.API;
using cAlgo.API.Internals;
using GeminiV26.Instruments.FX;
using GeminiV26.Instruments.INDEX;
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

        // =========================
        // DIRECTION FLOW (SSOT)
        // =========================
        // LEGACY / DIAGNOSTIC ONLY (pre-routing analytics)
        public TradeDirection LogicBiasDirection { get; set; } = TradeDirection.None;
        public int LogicBiasConfidence { get; set; } = 0;
        public TradeDirection RoutedDirection { get; set; } = TradeDirection.None;
        public TradeDirection FinalDirection { get; set; } = TradeDirection.None;
        public bool DirectionDebugLogged { get; set; }

        public double TotalMoveSinceBreakAtr { get; set; }

        // =========================
        // SESSION
        // =========================
        public FxSession Session { get; set; }

        public SessionMatrixConfig SessionMatrixConfig { get; set; }
            = SessionMatrixDefaults.Neutral;

        // =========================
        // TEMP BACKWARD COMPAT
        // =========================
        [Obsolete("LEGACY – use LogicBiasDirection")]
        public TradeDirection LogicBias => LogicBiasDirection;

        [Obsolete("LEGACY – use LogicBiasConfidence")]
        public int LogicConfidence => LogicBiasConfidence;

        [Obsolete("LEGACY – use the instrument-specific *HtfAllowedDirection property")]
        public TradeDirection HtfDirection
        {
            get
            {
                if (FxHtfAllowedDirection != TradeDirection.None)
                    return FxHtfAllowedDirection;

                if (CryptoHtfAllowedDirection != TradeDirection.None)
                    return CryptoHtfAllowedDirection;

                if (IndexHtfAllowedDirection != TradeDirection.None)
                    return IndexHtfAllowedDirection;

                if (MetalHtfAllowedDirection != TradeDirection.None)
                    return MetalHtfAllowedDirection;

                return TradeDirection.None;
            }
        }

        [Obsolete("LEGACY – use the instrument-specific *HtfConfidence01 property")]
        public double HtfConfidence
        {
            get
            {
                double maxConfidence = FxHtfConfidence01;

                if (CryptoHtfConfidence01 > maxConfidence)
                    maxConfidence = CryptoHtfConfidence01;

                if (IndexHtfConfidence01 > maxConfidence)
                    maxConfidence = IndexHtfConfidence01;

                if (MetalHtfConfidence01 > maxConfidence)
                    maxConfidence = MetalHtfConfidence01;

                return maxConfidence;
            }
        }

        public string SymbolName => Symbol;

        public void Print(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            if (Log != null)
            {
                Log(TradeLogIdentity.WithTempId(message, this));
                return;
            }

            Bot?.Print(TradeLogIdentity.WithTempId(message, this));
        }

        public bool IsValidFlagStructure_M5 =>
            HasFlagLong_M5 || HasFlagShort_M5;
    }
}
