using GeminiV26.Core.Entry;
using GeminiV26.Risk;

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
        private const double SlBase = 1.45;
        private const double SlWide = 1.75;
        private const double SlTight = 1.25;

        public double GetRiskPercent(int score)
        {
            if (score < 60) return 0.05;
            if (score < 70) return RiskLow;
            if (score < 80) return RiskMed;
            if (score < 90) return RiskHigh;
            return RiskMax;
        }

        public double GetStopLossAtrMultiplier(int score, EntryType entryType)
        {
            double baseMult = entryType switch
            {
                EntryType.TC_Pullback => SlTight,
                EntryType.TC_Flag => SlBase,
                EntryType.BR_RangeBreakout => SlWide,
                EntryType.TR_Reversal => SlWide,
                _ => SlBase
            };

            // magas confidence → picit feszesebb
            if (score >= 85) baseMult -= 0.05;
            if (score >= 92) baseMult -= 0.05;

            if (baseMult < 1.10) baseMult = 1.10;

            return baseMult;
        }

        public void GetTakeProfit(
            int score,
            out double tp1R,
            out double tp1Ratio,
            out double tp2R,
            out double tp2Ratio)
        {
            // USDJPY – VALIDÁCIÓS + RUNNER PROFIL
            // Cél: korai TP1 → BE / trailing,
            // de jó score-nál valódi futás engedése

            if (score < 70)
            {
                tp1R = 0.30; tp1Ratio = 0.70;
                tp2R = 0.80; tp2Ratio = 0.30;
                return;
            }

            if (score < 80)
            {
                tp1R = 0.35; tp1Ratio = 0.65;
                tp2R = 1.00; tp2Ratio = 0.35;
                return;
            }

            if (score < 90)
            {
                tp1R = 0.40; tp1Ratio = 0.55;
                tp2R = 1.40; tp2Ratio = 0.45;
                return;
            }

            // score >= 90 → prémium trend
            tp1R = 0.45;
            tp1Ratio = 0.45;     // több runner
            tp2R = 1.80;         // hagyjuk futni
            tp2Ratio = 0.55;
        }

        public double GetLotCap(int score)
        {
            // USDJPY: tisztább trend, de gyors
            if (score < 65) return 0.65;
            if (score < 75) return 0.80;
            if (score < 85) return 0.95;
            if (score < 92) return 1.05;
            return 1.10;   // abszolút plafon
        }
    }
}
