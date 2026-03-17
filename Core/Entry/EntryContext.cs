using cAlgo.API;
using cAlgo.API.Internals;
using GeminiV26.Instruments.FX;
using GeminiV26.Instruments.INDEX;
using GeminiV26.Core.Matrix;
using System;

namespace GeminiV26.Core.Entry
{
    public class EntryContext
    {
        // =========================
        // CORE
        // =========================
        public bool IsReady { get; set; }
        public TradeDirection TrendDirection;

        public string Symbol;
        public Action<string> Log { get; set; }

        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;
        public Robot Bot { get; }

        // =========================
        // BARS
        // =========================
        public Bars M1;
        public Bars M5;
        public Bars M15;

        public int BarsSinceHighBreak_M5 { get; set; }
        public int BarsSinceLowBreak_M5 { get; set; }
        public int BarsSinceImpulse_M5 { get; set; }

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
        public TradeDirection ImpulseDirection;

        public bool HasBreakout_M1;
        public TradeDirection BreakoutDirection;

        // =========================
        // M5 CORE LOGIC
        // =========================
        public bool HasImpulse_M5;
        public bool IsValidFlagStructure_M5;

        // =========================
        // RANGE
        // =========================
        public bool IsRange_M5;
        public int RangeBarCount_M5;
        public TradeDirection RangeBreakDirection;
        public double RangeBreakAtrSize_M5;
        public int RangeFakeoutBars_M1;

        // =========================
        // PULLBACK
        // =========================
        public bool PullbackTouchedEma21_M5;
        public double PullbackDepthAtr_M5;
        public int PullbackBars_M5;

        public bool IsPullbackDecelerating_M5 { get; set; }
        public bool HasRejectionWick_M5 { get; set; }
        public bool HasReactionCandle_M5 { get; set; }

        public double AvgBodyLast3_M5 { get; set; }
        public double AvgBodyPrev3_M5 { get; set; }

        public bool LastClosedBarInTrendDirection { get; set; }

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
        public TradeDirection ReversalDirection;

        // =========================
        // TRANSITION
        // =========================
        public bool TransitionValid { get; set; }
        public int TransitionScoreBonus { get; set; }
        public TransitionEvaluation Transition { get; set; }

        // =========================
        // FLAG RANGE
        // =========================
        public double FlagHigh { get; set; }
        public double FlagLow { get; set; }

        // =================================================
        // 🔥 NEW: 2-SIDED FLAG BREAKOUT (CORE FIX)
        // =================================================

        // instant breakout (current bar)
        public bool FlagBreakoutUp { get; set; }
        public bool FlagBreakoutDown { get; set; }

        // confirmed breakout (state machine)
        public bool FlagBreakoutUpConfirmed { get; set; }
        public bool FlagBreakoutDownConfirmed { get; set; }

        public int BreakoutUpBarsSince { get; set; } = 999;
        public int BreakoutDownBarsSince { get; set; } = 999;

        // =========================
        // BACKWARD COMPATIBILITY
        // =========================
        public bool FlagBreakoutConfirmed { get; set; }
        public int BreakoutBarsSince { get; set; } = 999;

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

        public double TotalMoveSinceBreakAtr { get; set; }

        // =========================
        // SESSION
        // =========================
        public FxSession Session { get; set; }

        public SessionMatrixConfig SessionMatrixConfig { get; set; }
            = SessionMatrixDefaults.Neutral;
    }
}