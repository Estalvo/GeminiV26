// =========================================================
// GEMINI V26 – PositionContext
// Rulebook 1.0 – Single Source of Truth
//
// Szerepe:
// - Egyetlen, kanonikus state objektum egy trade teljes életciklusára
// - Entry → Risk → Exit → CSV → Analytics MIND innen dolgozik
//
// FONTOS ELVEK:
// - TradeCore / TradeRouter NEM számol confidence-et
// - EntryType → EntryScore-t ad
// - EntryLogic (instrument) → LogicConfidence-t ad
// - FinalConfidence ITT kerül kiszámításra és eltárolásra
//
// FinalConfidence felhasználása:
// - Risk sizing
// - TP / BE / Trailing policy
// - CSV tanulási adat
//
// FinalConfidence = kombinált minőségmutató,
// NEM belépési gate, NEM stratégiai döntés.
//
// Ez a fájl normatív, nem heurisztikus.
// =========================================================

using System;

namespace GeminiV26.Core
{
    /// <summary>
    /// Trade lifecycle context.
    /// Phase 3.4-től:
    /// - Entry audit
    /// - RiskSizer → ExitManager policy bridge
    /// - Analytics
    /// </summary>
    public class PositionContext
    {
        // =========================
        // Identity / audit
        // =========================
        public long PositionId { get; set; }

        public string Symbol { get; set; } = string.Empty;

        public string EntryType { get; set; } = string.Empty;

        public string EntryReason { get; set; } = string.Empty;

        public double RiskPriceDistance { get; set; }   // SL distance price-ban (entrykori)


        // =========================================================
        // CONFIDENCE PIPELINE (Rulebook 1.0)
        // =========================================================

        /// <summary>
        /// EntryType által számolt score (0–100).
        /// Setup minőségét írja le.
        /// </summary>
        public int EntryScore { get; set; }

        /// <summary>
        /// Instrument-specifikus EntryLogic által adott confidence (0–100).
        /// Bias / környezeti megerősítés.
        /// </summary>
        public int LogicConfidence { get; set; }

        /// <summary>
        /// Kombinált confidence érték.
        /// CSAK management és risk input.
        /// NEM belépési gate.
        /// </summary>
        public int FinalConfidence { get; set; }

        public DateTime EntryTime { get; set; }

        public double EntryPrice { get; set; }

        // =========================
        // TP / state flags
        // =========================
        public bool Tp1Hit { get; set; }

        // =========================
        // Take profit prices (resolved at entry time)
        // =========================
        public double? Tp1Price { get; set; }

        /// <summary>
        /// TP1 partial close fraction (0..1).
        /// Used by executors to close a portion at TP1.
        /// </summary>
        public double Tp1CloseFraction { get; set; }

        public double Tp2Hit { get; set; }

        public bool Tp1DbgInit { get; set; }

        // =========================
        // Analytics volumes (units)
        // =========================
        public double EntryVolumeInUnits { get; set; }

        public double Tp1ClosedVolumeInUnits { get; set; }

        public double RemainingVolumeInUnits { get; set; }

        // =========================
        // ANALYTICS / EXIT STATE
        // =========================

        // Maximum elért pozitív R a trade élete során
        public double MaxFavorableR { get; set; }

        // Egységes kilépési ok (CSV / tanulás)
        public ExitReason ExitReason { get; set; }

        // =========================
        // Prices (resolved by executor)
        // =========================
        public double? Tp2Price { get; set; }

        public double BePrice { get; set; }

        // =========================
        // Modes
        // =========================
        public BeMode BeMode { get; set; }

        public TrailingMode TrailingMode { get; set; }

        // =========================================================
        // RiskSizer → ExitManager bridge
        // (policy values, NOT prices)
        // =========================================================

        /// <summary>
        /// Initial SL ATR multiplier (instrument-specific).
        /// </summary>
        public double StopLossAtrMultiplier { get; set; }

        /// <summary>
        /// Initial stop-loss distance expressed in R.
        /// </summary>
        public double InitialStopLossR { get; set; }

        /// <summary>
        /// Take-profit structure (R-based).
        /// </summary>
        public double Tp1R { get; set; }
        public double Tp1Ratio { get; set; }

        public double Tp2R { get; set; }
        public double Tp2Ratio { get; set; }

        /// <summary>
        /// Maximum allowed lot size for this trade (policy).
        /// </summary>
        public double LotCap { get; set; }

        // =========================
        // Break-even policy (R-based)
        // =========================
        public double BeTriggerR { get; set; }

        public double BeOffsetR { get; set; }

        // =========================
        // Trailing policy (R / ATR-based)
        // =========================
        public double TrailingStartR { get; set; }

        public double TrailingAtrMultiplier { get; set; }

        public bool TrailingActivated { get; set; }

        public bool BeActivated { get; set; }

        // =========================================================
        // NEW: MFE / MAE / Viability state (NO LOGIC)
        // Phase 3.8+ prep: dead-trade prevention + reward routing
        //
        // CÉL:
        // - Trade közbeni életképesség mérés (MFE/MAE)
        // - Későbbi ExitManager/TVM döntések inputja
        // - Analytics / tanulási adat
        //
        // FONTOS:
        // - Ezek a mezők önmagukban NEM döntenek
        // - Csak state, amit az Executor/ExitManager frissít
        // =========================================================

        /// <summary>
        /// Best favorable price since entry.
        /// Long: max(high), Short: min(low).
        /// Default: 0 (ExitManager initkor érdemes entry árral indulni).
        /// </summary>
        public double BestFavorablePrice { get; set; }

        /// <summary>
        /// Worst adverse price since entry.
        /// Long: min(low), Short: max(high).
        /// Default: 0 (ExitManager initkor érdemes entry árral indulni).
        /// </summary>
        public double WorstAdversePrice { get; set; }

        /// <summary>
        /// Max Favorable Excursion expressed in R.
        /// (MFE_R = favorableMove / RiskPriceDistance).
        /// </summary>
        public double MfeR { get; set; }

        /// <summary>
        /// Max Adverse Excursion expressed in R.
        /// (MAE_R = adverseMove / RiskPriceDistance).
        /// </summary>
        public double MaeR { get; set; }

        /// <summary>
        /// EntryTime snapshot UTC-ban (külön viability window-hoz, ha kell).
        /// EntryTime már létezik; ez opcionális, de determinisztikus.
        /// </summary>
        public DateTime EntryTimeUtc { get; set; }

        /// <summary>
        /// M5 bar-számláló a trade életkorához (viability window).
        /// (ExitManager/Executor frissíti.)
        /// </summary>
        public int BarsSinceEntryM5 { get; set; }

        /// <summary>
        /// Viability ellenőrzés már lefutott-e (hogy ne ismételjük).
        /// </summary>
        public bool ViabilityChecked { get; set; }

        /// <summary>
        /// Dead trade jelölés (MFE alacsony + MAE nő → későbbi early-exit / büntetés alap).
        /// </summary>
        public bool IsDeadTrade { get; set; }

        /// <summary>
        /// Opcionális: miért lett dead trade (audit/analytics).
        /// </summary>
        public string DeadTradeReason { get; set; } = string.Empty;

        // =========================================================
        // FinalConfidence calculation
        // =========================================================

        /// <summary>
        /// Computes FinalConfidence from EntryScore and LogicConfidence.
        ///
        /// Rulebook 1.0:
        /// - EntryScore = setup minőség (primer)
        /// - LogicConfidence = instrument bias (szekunder)
        ///
        /// Súlyozás:
        /// - EntryScore: 70%
        /// - LogicConfidence: 30%
        ///
        /// FinalConfidence:
        /// - NEM belépési gate
        /// - CSAK risk / management input
        /// - Determinisztikus, egyszer számolt érték
        /// </summary>
        public void ComputeFinalConfidence()
        {
            // Safety clamp (defenzív)
            int entry = Math.Max(0, Math.Min(100, EntryScore));
            int logic = Math.Max(0, Math.Min(100, LogicConfidence));

            double combined =
                entry * 0.7 +
                logic * 0.3;

            FinalConfidence = (int)Math.Round(combined, MidpointRounding.AwayFromZero);
        }

        // =========================================================
        // LEGACY COMPATIBILITY BRIDGE
        // Phase 3.7.x → 3.8 migration
        //
        // CÉL:
        // - Régi instrumentumok még ctx.Confidence-et használnak
        // - Új architektúrában FinalConfidence az egyetlen forrás
        //
        // MEGJEGYZÉS:
        // - Ez NEM új state
        // - NEM számol új értéket
        // - Csak alias a FinalConfidence-re
        // - Később 1 sor törlés
        // =========================================================
        [Obsolete("LEGACY – use FinalConfidence")]
        public int Confidence => FinalConfidence;

        // =========================================================
        // Phase 3.7.x → v2 PREP (SAFE, NO LOGIC)
        //
        // CÉL:
        // - Restart / rehydrate esetén állapotmegőrzés
        // - TP1 / Trailing duplikációk későbbi elkerülése
        //
        // FONTOS:
        // - Ezek az értékek JELENLEG nem vesznek részt döntésben
        // - Csak adat-előkészítés
        // =========================================================

        /// <summary>
        /// Entrykori teljes volume snapshot (units).
        /// Rehydrate után NEM szabad felülírni.
        /// </summary>
        public double InitialVolumeInUnits { get; set; }

        /// <summary>
        /// Rehydrate-ből származik-e a context.
        /// Csak meta / debug cél.
        /// </summary>
        public bool IsRehydrated { get; set; }

        /// <summary>
        /// Utolsó ismert SL ár (pozíció az igazság).
        /// Trailing restore előkészítés.
        /// </summary>
        public double? LastStopLossPrice { get; set; }
    }
}
