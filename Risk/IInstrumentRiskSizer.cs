// =========================================================
// GEMINI V26 – IInstrumentRiskSizer
// Rulebook 1.0 COMPLIANT
//
// SZEREP:
// - Instrument-specifikus risk adapter INTERFACE
// - A CORE risk-elv (FinalConfidence) instrumentre fordítása
//
// FONTOS:
// - Ez NEM belépési gate
// - Ez NEM score-alapú
// - Ez NEM dönt trade indításról
//
// INPUT AXIÓMA:
// - FinalConfidence (0–100)
//   → már kiszámítva a PositionContext-ben
//
// KIMENET:
// - Risk százalék
// - SL ATR szorzó
// - TP struktúra (R-alapú)
// - Lot cap (hard safety)
//
// TILOS:
// - score használata
// - threshold alapú tiltás (return 0, abort)
// - belépés blokkolása
//
// Alapelv:
// Alacsony confidence = kisebb risk,
// de SOHA nem nulla.
// =========================================================

using GeminiV26.Core.Entry;

namespace GeminiV26.Risk
{
    /// <summary>
    /// Instrument-specifikus risk adapter szerződés.
    /// A FinalConfidence értelmezését végzi instrument szinten.
    ///
    /// Phase 3.7.1.3-tól:
    /// - Score NEM használható
    /// - Csak FinalConfidence a bemenet
    /// </summary>
    public interface IInstrumentRiskSizer
    {
        // =========================================================
        // RISK PERCENT
        // =========================================================

        /// <summary>
        /// Meghatározza a trade risk százalékát a FinalConfidence alapján.
        /// NEM gate, mindig visszaad egy >0 értéket.
        /// </summary>
        double GetRiskPercent(int finalConfidence);

        // =========================================================
        // STOP LOSS
        // =========================================================

        /// <summary>
        /// Meghatározza az SL ATR szorzót.
        /// Instrument-specifikus finomhangolás.
        /// </summary>
        double GetStopLossAtrMultiplier(int finalConfidence, EntryType entryType);

        // =========================================================
        // TAKE PROFIT
        // =========================================================

        /// <summary>
        /// Take-profit struktúra R-alapú meghatározása.
        /// TP1 = részleges zárás
        /// TP2 = teljes zárás
        /// </summary>
        void GetTakeProfit(
            int finalConfidence,
            out double tp1R,
            out double tp1Ratio,
            out double tp2R,
            out double tp2Ratio
        );

        // =========================================================
        // LOT CAP
        // =========================================================

        /// <summary>
        /// Instrument-specifikus maximális lot cap.
        /// Safety jellegű korlát, nem confidence-gate.
        /// </summary>
        double GetLotCap(int finalConfidence);
    }
}
