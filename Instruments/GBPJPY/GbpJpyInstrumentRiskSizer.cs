using GeminiV26.Core.Entry;
using GeminiV26.Risk;
using System;

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
            double n = NormalizeScore(score);

            // GBPJPY: extra volatilis → alacsonyabb plafon
            // 0.20% → 0.30%
            return 0.20 + n * (0.30 - 0.20);
        }

        // =========================
        // SL (ATR multiplier)
        // =========================
        public double GetStopLossAtrMultiplier(int score, EntryType entryType)
        {
            double n = NormalizeScore(score);

            // GBPJPY: nagyon wickes
            // 3.5 → 3.0
            return 3.5 - n * 0.5;
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

            // gyenge / zajos
            if (n < 0.50)
            {
                tp1R = 0.30;
                tp1Ratio = 0.70;
                tp2R = 0.9;
                tp2Ratio = 0.30;
                return;
            }

            // normál
            if (n < 0.75)
            {
                tp1R = 0.38;
                tp1Ratio = 0.55;
                tp2R = 1.3;
                tp2Ratio = 0.45;
                return;
            }

            // top impulzív setup
            tp1R = 0.50;
            tp1Ratio = 0.40;   // 60% runner
            tp2R = 1.8;
            tp2Ratio = 0.60;
        }

        // =========================
        // LOT CAP
        // =========================
        public double GetLotCap(int score)
        {
            double n = NormalizeScore(score);

            // GBPJPY: brutális vol → erős cap
            return 0.55 + n * 0.30; // 0.55 → 0.85
        }

        private static double NormalizeScore(int score)
        {
            // GBPJPY: új FX score-tartomány, de óvatos használat
            return Math.Clamp((score - 55) / 35.0, 0.0, 1.0);
        }
    }
}
