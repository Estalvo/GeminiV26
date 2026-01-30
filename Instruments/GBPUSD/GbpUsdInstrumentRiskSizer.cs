using GeminiV26.Core.Entry;
using GeminiV26.Risk;
using System;

namespace GeminiV26.Instruments.GBPUSD
{
    /// <summary>
    /// GBPUSD RiskSizer – Phase 3.7
    /// STRUKTÚRA: US30 klón (IInstrumentRiskSizer)
    /// LOGIKA: FX (GBP) → konzervatív risk, kissé szélesebb SL a noise miatt
    /// </summary>
    public class GbpUsdInstrumentRiskSizer : IInstrumentRiskSizer
    {
        // FX: konzervatívabb
        private const double RiskLow = 0.12;
        private const double RiskMed = 0.18;
        private const double RiskHigh = 0.25;
        private const double RiskMax = 0.30;

        // GBPUSD: noise miatt SL kicsit szélesebb, mint USDJPY
        private const double SlBase = 1.65;
        //private const double SlWide = 1.95;
        //private const double SlTight = 1.45;

        public double GetRiskPercent(int score)
        {
            if (score < 60) return 0.0;
            if (score < 70) return RiskLow;
            if (score < 80) return RiskMed;
            if (score < 90) return RiskHigh;
            return RiskMax;
        }

        public double GetStopLossAtrMultiplier(int score, EntryType _ /* FX ignores entry type */)
        {
            double baseMult = SlBase;

            if (score >= 85) baseMult -= 0.05;
            if (score >= 92) baseMult -= 0.05;

            return Math.Max(baseMult, 1.35);
        }

        public void GetTakeProfit(
            int score,
            out double tp1R,
            out double tp1Ratio,
            out double tp2R,
            out double tp2Ratio)
        {
            // GBPUSD – noise-os FX, kontrollált runner

            if (score < 70)
            {
                tp1R = 0.35; tp1Ratio = 0.55;
                tp2R = 1.10; tp2Ratio = 0.45;
                return;
            }

            if (score < 80)
            {
                tp1R = 0.45; tp1Ratio = 0.50;
                tp2R = 1.35; tp2Ratio = 0.50;
                return;
            }

            if (score < 90)
            {
                tp1R = 0.50; tp1Ratio = 0.45;
                tp2R = 1.60; tp2Ratio = 0.55;
                return;
            }

            // score >= 90
            tp1R = 0.55; tp1Ratio = 0.40;
            tp2R = 1.80; tp2Ratio = 0.60;
        }

        public double GetLotCap(int score)
        {
            // GBPUSD: volatilis, wickes → konzervatív skála
            if (score < 65) return 0.55;
            if (score < 75) return 0.65;
            if (score < 85) return 0.75;
            if (score < 92) return 0.85;
            return 0.95;   // soha nem megy 1.0 fölé
        }

    }
}
