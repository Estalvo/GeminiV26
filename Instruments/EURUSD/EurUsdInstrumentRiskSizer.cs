using GeminiV26.Core.Entry;
using GeminiV26.Risk;
using System;

namespace GeminiV26.Instruments.EURUSD
{
    /// <summary>
    /// EURUSD money-risk policy – Phase 3.9 (Adaptive LotCap Upgrade)
    /// 
    /// Változás:
    /// - Dinamikusabb LotCap skálázás
    /// - Magas score esetén érezhető méretnövekedés
    /// - Borderline tradek továbbra is kontroll alatt
    /// 
    /// NINCS egyszerűsítés.
    /// NINCS logika törlés.
    /// </summary>
    public class EurUsdInstrumentRiskSizer : IInstrumentRiskSizer
    {
        // =========================
        // TUNING – változatlan
        // =========================
        private const double RiskMin = 0.20;
        private const double RiskLow = 0.25;
        private const double RiskMed = 0.35;
        private const double RiskHigh = 0.45;
        private const double RiskMax = 0.65;   // kis emelés 0.55 → 0.65

        private const double SlBase = 2.0;
        private const double SlWide = 2.4;
        private const double SlTight = 1.8;

        // =========================
        // RISK %
        // =========================
        public double GetRiskPercent(int score)
        {
            double n = NormalizeScore(score);

            // 0.40% → 0.75% (kicsit erősebb FX London trendhez)
            return 0.40 + n * (0.75 - 0.40);
        }

        // =========================
        // SL (ATR multiplier)
        // =========================
        public double GetStopLossAtrMultiplier(int score, EntryType entryType)
        {
            double n = NormalizeScore(score);

            // változatlan struktúra
            return 1.9 - n * 0.4;
        }

        // =========================
        // TP struktúra
        // =========================
        public void GetTakeProfit(
            int score,
            out double tp1R,
            out double tp1Ratio,
            out double tp2R,
            out double tp2Ratio)
        {
            double n = NormalizeScore(score);

            tp1R = 0.50;
            tp1Ratio = 0.60 - n * 0.15;

            tp2R = 1.2 + n * 0.6;
            tp2Ratio = 1.0 - tp1Ratio;
        }

        // =========================
        // LOT CAP – UPGRADED
        // =========================
        public double GetLotCap(int score)
        {
            double n = NormalizeScore(score);

            // Dinamikusabb skálázás:
            // 1.0 → 1.8 lineárisan
            double baseCap = 1.0 + n * 0.8;

            // Extra boost magas minőségű setupra
            if (score >= 80)
                baseCap += 0.3;   // max ~2.1

            if (score >= 85)
                baseCap += 0.2;   // max ~2.3

            return baseCap;
        }

        // =========================
        // SCORE NORMALIZATION – változatlan
        // =========================
        private static double NormalizeScore(int score)
        {
            // FX reális tartomány: 55–90
            return Math.Clamp((score - 55) / 35.0, 0.0, 1.0);
        }
    }
}