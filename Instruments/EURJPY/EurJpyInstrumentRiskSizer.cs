using GeminiV26.Core.Entry;
using GeminiV26.Risk;
using System;

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
            double n = NormalizeScore(score);

            // JPY: óvatosabb plafon
            // 0.22% → 0.32%
            return 0.22 + n * (0.32 - 0.22);
        }

        // =========================
        // SL (ATR multiplier)
        // =========================
        public double GetStopLossAtrMultiplier(int score, EntryType entryType)
        {
            double n = NormalizeScore(score);

            // JPY: szélesebb alap, kisebb szűkülés
            // 3.3 → 2.9
            return 3.3 - n * 0.4;
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

            // gyenge / bizonytalan
            if (n < 0.45)
            {
                tp1R = 0.30;
                tp1Ratio = 0.70;
                tp2R = 0.9;
                tp2Ratio = 0.30;
                return;
            }

            // normál
            if (n < 0.70)
            {
                tp1R = 0.35;
                tp1Ratio = 0.55;
                tp2R = 1.1;
                tp2Ratio = 0.45;
                return;
            }

            // jó / impulzív continuation
            tp1R = 0.45;
            tp1Ratio = 0.45;
            tp2R = 1.4;
            tp2Ratio = 0.55;
        }

        // =========================
        // LOT CAP
        // =========================
        public double GetLotCap(int score)
        {
            double n = NormalizeScore(score);

            // JPY: soha nem full cap
            return 0.60 + n * 0.30; // 0.60 → 0.90
        }

        private static double NormalizeScore(int score)
        {
            // JPY FX: score-tartomány ugyanaz, de óvatosabban használjuk
            return Math.Clamp((score - 55) / 35.0, 0.0, 1.0);
        }
    }
}
