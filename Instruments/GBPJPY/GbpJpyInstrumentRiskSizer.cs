using GeminiV26.Core.Entry;
using GeminiV26.Risk;

namespace GeminiV26.Instruments.GBPJPY
{
    /// <summary>
    /// GBPJPY money-risk policy – Phase 3.8
    /// Policy szint:
    /// - Risk% (score alapján)
    /// - SL ATR multiplier (score + entryType alapján)
    /// - TP1/TP2 R struktúra (score alapján)
    /// - Lot cap (score alapján)
    ///
    /// Nem számol árat, nem nyit/zár, nem kezel lifecycle-t.
    /// </summary>
    public class GbpJpyInstrumentRiskSizer : IInstrumentRiskSizer
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
            // GBPJPY – nagyon impulzív FX pár
            // cél: jó setupnál hagyjuk futni, gyengénél gyors biztosítás

            if (score >= 85)
            {
                tp1R = 0.50;
                tp1Ratio = 0.40;   // 60% runner
                tp2R = 1.8;
            }
            else if (score >= 75)
            {
                tp1R = 0.38;
                tp1Ratio = 0.55;
                tp2R = 1.3;
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
            return 0.80;
        }
    }
}
