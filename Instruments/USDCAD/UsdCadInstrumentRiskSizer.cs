using GeminiV26.Core.Entry;
using GeminiV26.Risk;
using System;

namespace GeminiV26.Instruments.USDCAD
{
    /// <summary>
    /// USDCAD money-risk policy – Phase 3.6 (BASELINE, NAS 1:1 clone)
    /// Policy szint:
    /// - Risk% (score alapján)
    /// - SL ATR multiplier (score + entryType alapján)
    /// - TP1/TP2 R struktúra (score alapján)
    /// - Lot cap (score alapján)
    ///
    /// Nem számol árat, nem nyit/zár, nem kezel lifecycle-t.
    /// </summary>
    public class UsdCadInstrumentRiskSizer : IInstrumentRiskSizer
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
            double n = NormalizeScore(score);

            // FX – konzervatív, de skálázódó
            // 0.22% → 0.35%
            return 0.22 + n * (0.35 - 0.22);
        }

        // =========================
        // SL (ATR multiplier)
        // =========================
        public double GetStopLossAtrMultiplier(int score, EntryType entryType)
        {
            double n = NormalizeScore(score);

            // USDCAD – grind FX, nem spike instrument
            // 2.1 → 1.6 ATR
            return 2.1 - n * 0.5;
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
            // AUDNZD: biztosabb TP1 – marad
            tp1R = 0.3;

            // =====================================================
            // TP1 RATIO – DINAMIKUS
            // Jó score → több runner
            // =====================================================
            double n = NormalizeScore(score);

            // TP1 mindig biztos, de jobb score → több runner
            tp1Ratio = 0.70 - n * 0.25; // 0.70 → 0.45

            // =====================================================
            // TP2 R – HELYES JUTALMAZÓ GÖRBE
            // AUDNZD kicsit „lustább”, mint EURUSD
            // =====================================================
            tp2R = 1.0 + n * 0.6; // 1.0 → 1.6

            // TP2 a maradékra
            tp2Ratio = 1.0 - tp1Ratio;
        }

        // =========================
        // LOT CAP
        // =========================
        public double GetLotCap(int score)
        {
            double n = NormalizeScore(score);

            // FX: ne legyen azonnal full cap
            // 65% → 100%
            return 0.65 + n * 0.35;
        }

        private static double NormalizeScore(int score)
        {
            // FX új valós tartomány: ~55–90
            return Math.Clamp((score - 55) / 35.0, 0.0, 1.0);
        }
    }
}
