using GeminiV26.Core.Entry;
using GeminiV26.Risk;

namespace GeminiV26.Instruments.EURJPY
{
    /// <summary>
    /// EURJPY money-risk policy – Phase 3.6 (BASELINE, NAS 1:1 clone)
    /// Policy szint:
    /// - Risk% (score alapján)
    /// - SL ATR multiplier (score + entryType alapján)
    /// - TP1/TP2 R struktúra (score alapján)
    /// - Lot cap (score alapján)
    ///
    /// Nem számol árat, nem nyit/zár, nem kezel lifecycle-t.
    /// </summary>
    public class EurJpyInstrumentRiskSizer : IInstrumentRiskSizer
    {
        // =========================
        // TUNING (egy helyen) – baseline NAS
        // =========================
        private const double RiskMin = 0.20;
        private const double RiskLow = 0.25;
        private const double RiskMed = 0.35;
        private const double RiskHigh = 0.45;
        private const double RiskMax = 0.55;

        private const double SlBase = 2.0;
        private const double SlWide = 2.4;
        private const double SlTight = 1.8;

        // =========================
        // RISK %
        // =========================
        public double GetRiskPercent(int score)
        {
            // FX: konzervatívabb, nincs gate
            if (score >= 85) return 0.35;
            if (score >= 75) return 0.30;
            return 0.25;
        }

        // =========================
        // SL (ATR multiplier)
        // =========================
        public double GetStopLossAtrMultiplier(int score, EntryType entryType)
        {
            if (score >= 85) return 2.8;
            if (score >= 75) return 3.0;
            return 3.2;
        }

        // =========================
        // TP struktúra (R + arány)
        // =========================
        public void GetTakeProfit(
            int score,
            out double tp1R,
            out double tp1Ratio,
            out double tp2R,
            out double tp2Ratio)
        {
            // EURJPY – gyors, impulzív FX pár
            // cél: ne vegyük ki túl korán a jó tradeket,
            // de gyenge score-nál maradjon védekező

            if (score >= 85)
            {
                tp1R = 0.45;
                tp1Ratio = 0.45;
                tp2R = 1.4;
            }
            else if (score >= 75)
            {
                tp1R = 0.35;
                tp1Ratio = 0.55;
                tp2R = 1.1;
            }
            else
            {
                tp1R = 0.30;
                tp1Ratio = 0.70;
                tp2R = 0.9;
            }

            tp2Ratio = 1.0 - tp1Ratio;
        }

        // =========================
        // LOT CAP
        // =========================
        public double GetLotCap(int score)
        {
            return 1.0;
        }
    }
}
