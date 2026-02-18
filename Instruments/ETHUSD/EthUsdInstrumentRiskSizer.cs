// =========================================================
// GEMINI V26 – ETHUSD Instrument RiskSizer
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

namespace GeminiV26.Instruments.ETHUSD
{
    public class EthUsdInstrumentRiskSizer : IInstrumentRiskSizer
    {
        // =========================================================
        // RISK PERCENT
        // =========================================================
        public double GetRiskPercent(int finalConfidence)
        {
            // Magas confidence → nagyobb risk (skálázódó)
            if (finalConfidence >= 85) return 1.30;
            if (finalConfidence >= 75) return 0.90;
            return 0.55;
        }

        // =========================================================
        // STOP LOSS ATR MULTIPLIER
        // =========================================================
        public double GetStopLossAtrMultiplier(int finalConfidence, EntryType entryType)
        {
            return finalConfidence >= 85
                ? 1.8
                : 2.2;
        }

        // =========================================================
        // TAKE PROFIT STRUCTURE – ETH M5 (Phase 3.7.4)
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
                tp1R = 0.40;
                tp1Ratio = 0.25;

                tp2R = 2.5;
            }
            else if (finalConfidence >= 75)
            {
                tp1R = 0.35;
                tp1Ratio = 0.35;

                tp2R = 1.8;
            }
            else
            {
                tp1R = 0.30;
                tp1Ratio = 0.45;

                tp2R = 1.3;
            }

            tp2Ratio = 1.0 - tp1Ratio;
        }

        // =========================================================
        // LOT CAP (ETH specifikus)
        // =========================================================
        public double GetLotCap(int confidence)
        {
            return 100000; // units, nem lot
        }
    }
}
