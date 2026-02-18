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

            // EURUSD – kontrollált FX
            // 0.35% → 0.65%
            return 0.35 + n * (0.65 - 0.35);
        }

        // =========================
        // SL (ATR multiplier)
        // =========================
        public double GetStopLossAtrMultiplier(int score, EntryType entryType)
        {
            double n = NormalizeScore(score);

            // EURUSD – realisztikus FX SL
            // 1.9 → 1.5 ATR
            return 1.9 - n * 0.4;
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
            double n = NormalizeScore(score);

            tp1R = 0.50;                    // fix 0.5R
            tp1Ratio = 0.60 - n * 0.15;     // 0.60 → 0.45

            tp2R = 1.2 + n * 0.6;           // 1.2 → 1.8
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
