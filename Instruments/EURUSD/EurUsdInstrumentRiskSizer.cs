using GeminiV26.Core.Entry;
using GeminiV26.Risk;
using System;

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
            double n = NormalizeScore(score);

            // FX – konzervatív, de skálázódó            
            // 0.35% → 0.65%
            return 0.35 + n * (0.95 - 0.35);
        }

        // =========================
        // SL (ATR multiplier)
        // =========================
        public double GetStopLossAtrMultiplier(int score, EntryType entryType)
        {
            double n = NormalizeScore(score);

            // Jobb score → feszesebb SL
            // 3.4 → 2.9
            return 3.4 - n * 0.5;
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
            tp1R = 0.45;

            // =====================================================
            // TP1 RATIO – DINAMIKUS
            // Jó score → több runner
            // =====================================================
            double n = NormalizeScore(score);

            // TP1 mindig biztos, de jobb score → több runner
            tp1Ratio = 0.55 - n * 0.20; // 0.55 → 0.35

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
            // 0.90 → 1.20
            return 0.90 + n * 0.30;
        }

        private static double NormalizeScore(int score)
        {
            // FX új valós tartomány: ~55–90
            return Math.Clamp((score - 55) / 35.0, 0.0, 1.0);
        }
    }
}
