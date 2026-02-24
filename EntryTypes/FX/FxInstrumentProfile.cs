using System.Collections.Generic;
using GeminiV26.Core;

namespace GeminiV26.Instruments.FX
{
    // ======================================================
    // ===== Volatility kategória
    // ======================================================
    public enum FxVolatilityClass
    {
        VeryLow,
        Low,
        Medium,
        High
    }

    // ======================================================
    // ===== Domináns session viselkedés
    // ======================================================
    public enum FxSessionBias
    {
        Asia,
        London,
        NewYork,
        Mixed
    }

    // ======================================================
    // ===== Pullback tipikus mélysége
    // ======================================================
    public enum FxPullbackStyle
    {
        Shallow,
        EMA21,
        EMA50,
        Structure
    }

    // ======================================================
    // ===== Gating / scoring mode (matrix-driven)
    // ======================================================
    public enum FxGateMode
    {
        Off,
        Penalize,
        Block
    }

    // ======================================================
    // ===== FX FLAG – session specifikus tuning
    // ======================================================
    public sealed class FxFlagSessionTuning
    {
        // ===== SCORE =====
        public int BaseScore { get; init; }
        public int MinScore { get; init; }

        // ===== IMPULSE =====
        public int MaxBarsSinceImpulse { get; init; }
        public int AtrExpandPenalty { get; init; }
        public int NoImpulsePenalty { get; init; } = 20;
        public bool RequireImpulse { get; init; } = false;

        // ===== FLAG STRUCTURE =====
        public int FlagBars { get; init; }
        public double MaxFlagAtrMult { get; init; }
        public double MaxOverextensionAtr { get; init; }
        public double BreakoutBufAtr { get; init; }

        // ===== CANDLE / TRIGGER =====
        public int BodyMisalignPenalty { get; init; }
        public int M1TriggerBonus { get; init; }
        public int NoM1Penalty { get; init; }
        public bool RequireM1Trigger { get; init; }

        // ===== EARLY LOSS PREVENTION (matrix-driven gates; enforced by EntryTypes) =====
        // Require ATR slope to be positive (rising volatility / momentum) before allowing entry
        public bool RequireAtrSlopePositive { get; init; }

        // Require a strong entry candle in the trade direction (no weak/misaligned body)
        public bool RequireStrongEntryCandle { get; init; }

        // ===== COMPAT ALIASES (EntryTypes expect these names) =====

        // Max pullback distance in ATR units (used by FX_FlagEntry)
        public double MaxPullbackAtr { get; init; }

        // Breakout buffer in ATR units (used by FX_FlagEntry)
        public double BreakoutAtrBuffer { get; init; }

        // ===== QUALITY =====
        public int FlagQualityBonus { get; init; }

        // ===== HTF =====
        public int HtfBasePenalty { get; init; }
        public int HtfScalePenalty { get; init; }

        // ===== VOLATILITY =====
        public bool AtrExpansionHardBlock { get; init; }

        // FX Instrument Profile
        public double OverextAtrSoft = 1.2;   // score penalty
        public double OverextAtrHard = 1.8;   // hard reject
        public double OverextAdxRelax = 0.4;  // ADX relax factor

    }

    // ======================================================
    // ===== FX instrument statikus profil
    // ======================================================
    public sealed class FxInstrumentProfile
    {
        // ===== Alap =====
        public string Symbol { get; init; }

        // ===== Viselkedés =====
        public FxVolatilityClass Volatility { get; init; }
        public FxSessionBias SessionBias { get; init; }
        public FxPullbackStyle PullbackStyle { get; init; }

        // ===== Napi mozgás =====
        public double TypicalAdrPips { get; init; }

        // ===== Impulse / wick =====
        public double MinImpulseAtr { get; init; }
        public double MaxWickRatio { get; init; }

        // ===== Session karakter =====
        public bool AllowAsianSession { get; init; }
        public bool MeanReversionFriendly { get; init; }

        // ==================================================
        // ===== FX FLAG – TELJES SESSION TUNING
        // ==================================================
        public Dictionary<FxSession, FxFlagSessionTuning> FlagTuning { get; init; }
        public Dictionary<FxSession, double> MinAdxBySession { get; set; }

        // ==================================================
        // ===== ENTRY CONTEXT skálázás
        // ==================================================
        public double ImpulseAtrMult_M5 { get; init; }
        public double ImpulseAtrMult_M1 { get; init; }
        public double MaxFlagAtrMult { get; init; }
        public double MinAdxTrend { get; init; }
        public double MinAtrPips { get; init; }

        // ==================================================
        // ===== PULLBACK tuning
        // ==================================================
        public double PbMinSlopeM15 { get; init; } = 0.00015;
        public double PbMinSlopeM5 { get; init; } = 0.00015;

        public int PbAsiaBarsMin { get; init; } = 2;
        public int PbAsiaBarsMax { get; init; } = 3;
        public double PbAsiaDepthAtrMax { get; init; } = 1.5;

        public int PbLondonBarsMin { get; init; } = 2;
        public int PbLondonBarsMax { get; init; } = 7;
        public double PbLondonDepthAtrMax { get; init; } = 1.2;
        public int PbLondonAtrExpandPenalty { get; init; } = 10;
        public int PbLondonFlagPriorityPenalty { get; init; } = 10;

        public double PbNySlopeMult { get; init; } = 1.5;
        public int PbNyBarsMin { get; init; } = 2;
        public int PbNyBarsMax { get; init; } = 5;
        public double PbNyDepthAtrMax { get; init; } = 0.8;
        public int PbNyNoM1Penalty { get; init; } = 10;
        public int PbNyFlagPriorityPenalty { get; init; } = 10;

        // ==================================================
        // ===== CONTINUATION CHARACTER (Instrument-level)
        // ==================================================
        public double MaxContinuationRatr { get; init; } = 1.4;

        public int MaxContinuationBarsSinceBreak { get; init; } = 3;

        public bool RequireHtfAlignmentForContinuation { get; init; } = true;

        public bool AllowContinuationDuringHtfTransition { get; init; } = false;

        // ==================================================
        // ===== PULLBACK – False-Continuation / Viability filter (matrix-driven)
        // Goal: block low-MFE + stagnation + micro-structure degradation BEFORE TP1
        // without killing good continuation / winners.
        // ==================================================
        public FxGateMode PbFcfMode { get; init; } = FxGateMode.Block;
        public int PbFcfPenalty { get; init; } = 18;

        // "Low MFE" proxy at entry-time: pullback already too deep relative to session depth budget
        public double PbFcfDeepRatio { get; init; } = 0.85;

        // "Time MFE stagnation" proxy: impulse got old, continuation didn't trigger promptly
        public int PbFcfStagnationBarsM5 { get; init; } = 6;

        // Micro-structure deterioration proxy: if stagnant, require an M1 trigger; otherwise it's often churn/false continuation
        public bool PbFcfRequireM1TriggerWhenStagnant { get; init; } = true;

        // Keep winners intact: apply mainly when HTF is neutral and/or volatility is expanding
        public bool PbFcfHtfNeutralOnly { get; init; } = true;
        public bool PbFcfAtrExpandingOnly { get; init; } = true;

        // ==================================================
        // ===== Session score delta
        // ==================================================
        public Dictionary<FxSession, int> SessionScoreDelta { get; init; }
    }
}
