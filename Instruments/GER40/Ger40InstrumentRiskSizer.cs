using GeminiV26.Core.Entry;
using GeminiV26.Core.Risk.Exposure;
using GeminiV26.Core.Risk.RiskProfiles;
using GeminiV26.Risk;

namespace GeminiV26.Instruments.GER40
{
    /// <summary>
    /// GER40 Instrument RiskSizer – Phase 3.7.x
    ///
    /// FILOZÓFIA:
    /// - Index-specifikus policy (NEM FX)
    /// - Score határozza meg a risket és az SL szélességét
    /// - SL ATR-alapú
    /// - TP1 = 0.6R (biztosítás)
    /// - TP2 ≈ 2.0R (reális index continuation)
    /// - Hard lot cap = 2.0
    ///
    /// A RiskSizer:
    /// - nem számol árat
    /// - nem nyit/zár
    /// - nem ismer account állapotot
    /// Csak policy-t ad.
    /// </summary>
    public class Ger40InstrumentRiskSizer : IInstrumentRiskSizer
    {
        // =====================================================
        // RISK % – score alapján
        // Cél: 1 jó trade = napi 100–200 USD
        // =====================================================
        public double GetRiskPercent(int finalConfidence)
        {
            return RiskProfileEngine.GetRiskPercent(finalConfidence);
        }

        // =====================================================
        // STOP LOSS – ATR multiplier
        // EntryType NEM játszik szerepet
        // =====================================================
        public double GetStopLossAtrMultiplier(int finalConfidence, EntryType entryType)
        {
            if (finalConfidence >= 85) return 2.3;
            if (finalConfidence >= 75) return 2.6;
            return 3.0;
        }

        // =====================================================
        // TAKE PROFIT – R struktúra
        // =====================================================
        public void GetTakeProfit(
            int finalConfidence,
            out double tp1R,
            out double tp1Ratio,
            out double tp2R,
            out double tp2Ratio)
        {
            if (finalConfidence >= 85)
            {
                tp1R = 0.5;
                tp1Ratio = 0.30; // 70% runner
                tp2R = 3.0;
            }
            else if (finalConfidence >= 75)
            {
                tp1R = 0.45;
                tp1Ratio = 0.40;
                tp2R = 2.6;
            }
            else
            {
                tp1R = 0.4;
                tp1Ratio = 0.50;
                tp2R = 2.2;
            }

            tp2Ratio = 1.0 - tp1Ratio;
        }

        // =====================================================
        // LOT CAP – HARD LIMIT
        // =====================================================
        public double GetLotCap(int finalConfidence)
        {
            double riskPercent = RiskProfileEngine.GetRiskPercent(finalConfidence);
            return LotCapEngine.CalculateLotCap(
                LotCapEngine.ReferenceBalance,
                LotCapEngine.ReferenceSlDistance,
                riskPercent);
        }
    }
}
