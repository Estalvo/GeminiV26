using GeminiV26.Core.Entry;
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
        public double GetRiskPercent(int score)
        {
            // Indexeken óvatosabb, mint XAU
            if (score >= 85) return 0.60;   // top quality
            if (score >= 75) return 0.45;   // jó setup
            return 0.35;                    // baseline
        }

        // =====================================================
        // STOP LOSS – ATR multiplier
        // EntryType NEM játszik szerepet
        // =====================================================
        public double GetStopLossAtrMultiplier(int score, EntryType entryType)
        {
            // GER40 zajos, de trendelhető
            if (score >= 85) return 1.8;    // tiszta trend
            if (score >= 75) return 2.0;    // normál környezet
            return 2.2;                     // zajosabb szakasz
        }

        // =====================================================
        // TAKE PROFIT – R struktúra
        // =====================================================
        public void GetTakeProfit(
            int score,
            out double tp1R,
            out double tp1Ratio,
            out double tp2R,
            out double tp2Ratio)
        {
            // GER40 – index mikrostruktúra
            // cél: zaj felett biztosítás, jó trendnél nagy futás

            if (score >= 85)
            {
                tp1R = 0.60;
                tp1Ratio = 0.40;   // 60% runner
                tp2R = 2.0;
            }
            else if (score >= 75)
            {
                tp1R = 0.50;
                tp1Ratio = 0.50;
                tp2R = 1.6;
            }
            else
            {
                tp1R = 0.45;
                tp1Ratio = 0.60;
                tp2R = 1.2;
            }

            tp2Ratio = 1.0 - tp1Ratio;
        }

        // =====================================================
        // LOT CAP – HARD LIMIT
        // =====================================================
        public double GetLotCap(int score)
        {
            // Index cél: max 1 lot
            return 1.0;
        }
    }
}
