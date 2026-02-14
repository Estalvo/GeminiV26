using cAlgo.API;
using cAlgo.API.Internals;
using GeminiV26.Instruments.FX;
using GeminiV26.Instruments.INDEX;

namespace GeminiV26.Core.Entry
{
    public class EntryContext
    {
        public bool IsReady { get; set; }
        public TradeDirection TrendDirection;

        // =========================
        // Instrument scope
        // =========================
        public string Symbol;

        // =========================
        // Bars
        // =========================
        public Bars M1;
        public Bars M5;
        public Bars M15;

        // =========================
        // EMA
        // =========================
        public double Ema8_M5;
        public double Ema21_M5;
        public double Ema21_M15;

        // =========================
        // EMA STRUCTURE (M5 / M15)
        // =========================
        public double Ema50_M5;
        public double Ema200_M5;

        public double Ema50_M15;
        public double Ema200_M15;

        // slope = direction / strength
        public double Ema21Slope_M5;
        public double Ema21Slope_M15;

        // =========================
        // ATR
        // =========================
        public double AtrM5;
        public double AtrM15;
        public double AtrSlope_M5;
        public double AtrAcceleration_M5;
        
        // =========================
        // ADX / DMS (M5)
        // =========================
        public double Adx_M5 { get; set; }
        public double AdxSlope_M5 { get; set; }
        public double AdxAcceleration_M5 { get; set; }

        public double PlusDI_M5 { get; set; }
        public double MinusDI_M5 { get; set; }
        public double DiSpread_M5 { get; set; }

        // =================================================
        // FX FLAG SUPPORT (Phase 3.8)
        // =================================================
        public double AtrPips_M5 { get; set; }

        public double FlagAtr_M5 { get; set; }
        
        // =========================
        // Structure (M5)
        // =========================
        public bool BrokeLastSwingHigh_M5;
        public bool BrokeLastSwingLow_M5;

        // ===== M1 impulse / breakout =====
        public bool HasImpulse_M1;
        public TradeDirection ImpulseDirection;

        public bool HasBreakout_M1;
        public TradeDirection BreakoutDirection;

        // =========================
        // Impulse / Flag
        // =========================
        public bool HasImpulse_M5;
        public bool IsValidFlagStructure_M5;

        // =========================
        // Range
        // =========================
        public bool IsRange_M5;
        public int RangeBarCount_M5;
        public TradeDirection RangeBreakDirection;
        public double RangeBreakAtrSize_M5;
        public int RangeFakeoutBars_M1;

        // =========================
        // Pullback
        // =========================
        public bool PullbackTouchedEma21_M5;
        public double PullbackDepthAtr_M5;

        // =========================
        // Time memory (M5)
        // =========================
        public int BarsSinceImpulse_M5;
        public int PullbackBars_M5;

        // =========================
        // Volatility / Volume
        // =========================
        public bool IsAtrExpanding_M5;
        public bool IsVolumeIncreasing_M5;
        public bool IsVolatilityAcceptable_Crypto { get; set; }

        // =========================
        // Market State (INDEX / XAU / FX context)
        // =========================
        public IndexMarketState MarketState { get; set; }

        // =========================
        // M1 Triggers (EDGE)
        // =========================
        public bool M1TriggerInTrendDirection;
        public bool M1FlagBreakTrigger;
        public bool M1ReversalTrigger;

        // =========================
        // Reversal
        // =========================
        public int ReversalEvidenceScore;
        public TradeDirection ReversalDirection;

        // =================================================
        // NEW: Pullback Deceleration + Reaction (M5)
        // =================================================
        public bool IsPullbackDecelerating_M5 { get; set; }
        public bool HasRejectionWick_M5 { get; set; }
        public bool HasReactionCandle_M5 { get; set; }

        public double AvgBodyLast3_M5 { get; set; }
        public double AvgBodyPrev3_M5 { get; set; }

        // =========================
        // Pullback confirmation
        // =========================
        public bool LastClosedBarInTrendDirection { get; set; }

        // ===== FX HTF bias (lightweight) =====
        public TradeDirection FxHtfAllowedDirection { get; set; } = TradeDirection.None;
        public double FxHtfConfidence01 { get; set; } = 0.0;
        public string FxHtfReason { get; set; }

        // =================================================
        // CRYPTO HTF BIAS
        // =================================================
        public TradeDirection CryptoHtfAllowedDirection { get; set; } = TradeDirection.None;
        public double CryptoHtfConfidence01 { get; set; } = 0.0;
        public string CryptoHtfReason { get; set; }

        // =================================================
        // INDEX HTF BIAS
        // =================================================
        public TradeDirection IndexHtfAllowedDirection { get; set; } = TradeDirection.None;
        public double IndexHtfConfidence01 { get; set; } = 0.0;
        public string IndexHtfReason { get; set; }

        // =================================================
        // METAL HTF BIAS
        // =================================================
        public TradeDirection MetalHtfAllowedDirection { get; set; } = TradeDirection.None;
        public double MetalHtfConfidence01 { get; set; } = 0.0;
        public string MetalHtfReason { get; set; }

        // =========================
        // Session
        // =========================
        public FxSession Session { get; set; }
    }
}
