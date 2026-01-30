// =========================================================
// GEMINI V26 – BTCUSD Instrument RiskSizer
// Rulebook 1.0 COMPLIANT
//
// SZEREP:
// - Instrument-specifikus risk adaptáció BTCUSD-re
// - NEM belépési gate
// - NEM score-alapú
// - NEM tilt trade-et
//
// INPUT:
// - FinalConfidence (0–100)
//   → már kiszámítva a PositionContext-ben
//
// FELELŐSSÉG:
// - risk percent meghatározása
// - SL ATR szorzó
// - TP1 / TP2 R arányok
// - instrument-specifikus lot cap
//
// TILOS:
// - score használata
// - confidence threshold gate (return 0, abort)
// - belépés tiltása
//
// Alapelv:
// Alacsony confidence = kisebb risk,
// de SOHA nem 0.
// =========================================================

using GeminiV26.Core.Entry;
using GeminiV26.Risk;

namespace GeminiV26.Instruments.BTCUSD
{
    public class BtcUsdInstrumentRiskSizer : IInstrumentRiskSizer
    {
        // =========================================================
        // RISK PERCENT
        // =========================================================
        public double GetRiskPercent(int finalConfidence)
        {
            // High confidence – valóban prémium setup
            if (finalConfidence >= 85)
                return 0.28;   // ⬆ volt 0.22

            // Normal confidence – most már tisztább belépések
            if (finalConfidence >= 75)
                return 0.20;   // ⬆ volt 0.16

            // Low confidence – változatlan (védelem)
            return 0.10;
        }

        // =========================================================
        // STOP LOSS ATR MULTIPLIER
        // =========================================================
        public double GetStopLossAtrMultiplier(int finalConfidence, EntryType entryType)
        {
            // Magas confidence → feszesebb SL
            return finalConfidence >= 85
                ? 2.6
                : 3.0;
        }

        // =========================================================
        // TAKE PROFIT STRUCTURE – BTC M5 (Phase 3.8)
        // =========================================================
        public void GetTakeProfit(
            int finalConfidence,
            out double tp1R,
            out double tp1Ratio,
            out double tp2R,
            out double tp2Ratio)
        {
            if (finalConfidence >= 85)
            {
                tp1R = 0.45;
                tp2R = 1.90;     // ⬅ hagyjuk kifutni a jó BTC tradeket
                tp1Ratio = 0.30; // ⬅ kevesebb korai zárás
            }
            else if (finalConfidence >= 75)
            {
                tp1R = 0.35;
                tp2R = 1.40;
                tp1Ratio = 0.40;
            }
            else
            {
                tp1R = 0.30;
                tp2R = 1.00;
                tp1Ratio = 0.55; // ⬅ védelem gyenge setupnál
            }

            tp2Ratio = 1.0 - tp1Ratio;
        }

        // =========================================================
        // LOT CAP (BTC specifikus)
        // =========================================================
        public double GetLotCap(int confidence)
        {
            return 50000; // units, nem lot
        }
    }
}
