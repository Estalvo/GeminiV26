using GeminiV26.Core.Entry;
using GeminiV26.Risk;
using System;

namespace GeminiV26.Instruments.USDJPY
{
    /// <summary>
    /// USDJPY RiskSizer – Phase 3.7
    /// STRUKTÚRA: US30 klón (IInstrumentRiskSizer)
    /// LOGIKA: FX (trend) → alacsonyabb risk, feszesebb SL, gyorsabb TP1
    /// </summary>
    public class UsdJpyInstrumentRiskSizer : IInstrumentRiskSizer
    {
        // FX: legyen konzervatívabb, mint index
        private const double RiskLow = 0.15;
        private const double RiskMed = 0.22;
        private const double RiskHigh = 0.30;
        private const double RiskMax = 0.35;

        // USDJPY: trend/pullback → SL feszesebb, mint US30
        // USDJPY: widened SL (+40%)
        private const double SlBase = 2.05;   // volt 1.45
        private const double SlWide = 2.45;   // volt 1.75
        private const double SlTight = 1.75;  // volt 1.25

        public double GetRiskPercent(int score)
        {
            double n = NormalizeScore(score);

            // USDJPY: tisztább trend, de gyors
            // 0.15% → 0.32%
            return 0.15 + n * (0.32 - 0.15);
        }

        public double GetStopLossAtrMultiplier(int score, EntryType entryType)
        {
            double n = NormalizeScore(score);

            double baseMult = SlBase; // FX-only: egységes baseline

            if (n >= 0.75) baseMult -= 0.05;
            if (n >= 0.90) baseMult -= 0.05;

            return Math.Max(baseMult, 1.10);
        }

        public void GetTakeProfit(
            int score,
            out double tp1R,
            out double tp1Ratio,
            out double tp2R,
            out double tp2Ratio)
        {
            double n = NormalizeScore(score);

            if (n < 0.40)
            {
                tp1R = 0.30; tp1Ratio = 0.70;
                tp2R = 0.80; tp2Ratio = 0.30;
                return;
            }

            if (n < 0.65)
            {
                tp1R = 0.35; tp1Ratio = 0.65;
                tp2R = 1.00; tp2Ratio = 0.35;
                return;
            }

            if (n < 0.85)
            {
                tp1R = 0.40; tp1Ratio = 0.55;
                tp2R = 1.40; tp2Ratio = 0.45;
                return;
            }

            // prémium trend
            tp1R = 0.45;
            tp1Ratio = 0.45;
            tp2R = 1.80;
            tp2Ratio = 0.55;
        }

        public double GetLotCap(int score)
        {
            double n = NormalizeScore(score);

            if (n < 0.45) return 0.65;
            if (n < 0.65) return 0.80;
            if (n < 0.85) return 0.95;
            if (n < 0.95) return 1.05;
            return 1.10;
        }

        private static double NormalizeScore(int score)
        {
            // USDJPY – új FX score-tartomány
            return Math.Clamp((score - 55) / 35.0, 0.0, 1.0);
        }

    }
}
