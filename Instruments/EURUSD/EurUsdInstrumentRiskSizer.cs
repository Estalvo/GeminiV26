using GeminiV26.Core.Entry;
using GeminiV26.Risk;

namespace GeminiV26.Instruments.EURUSD
{
    /// <summary>
    /// EURUSD money-risk policy – Phase 3.8 (BASELINE, NAS 1:1 clone)
    /// Policy szint:
    /// - Risk% (score alapján)
    /// - SL ATR multiplier (score + entryType alapján)
    /// - TP1/TP2 R struktúra (score alapján)
    /// - Lot cap (score alapján)
    ///
    /// Nem számol árat, nem nyit/zár, nem kezel lifecycle-t.
    /// </summary>
    public class EurUsdInstrumentRiskSizer : IInstrumentRiskSizer
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
            // EURUSD: biztosabb TP1 – marad
            tp1R = 0.3;

            // =====================================================
            // TP1 RATIO – DINAMIKUS
            // Jó score → több runner
            // =====================================================
            if (score >= 85)
                tp1Ratio = 0.45;   // 55% fut tovább
            else if (score >= 75)
                tp1Ratio = 0.55;
            else
                tp1Ratio = 0.70;   // baseline

            // =====================================================
            // TP2 R – HELYES GÖRBE
            // Jó score → távolabbi TP2
            // =====================================================
            if (score >= 85)
                tp2R = 1.8;
            else if (score >= 75)
                tp2R = 1.4;
            else
                tp2R = 1.1;

            // TP2 a maradékra
            tp2Ratio = 1.0 - tp1Ratio;
        }

        // =========================
        // LOT CAP
        // =========================
        public double GetLotCap(int score)
        {
            return 0.70;
        }
    }
}
