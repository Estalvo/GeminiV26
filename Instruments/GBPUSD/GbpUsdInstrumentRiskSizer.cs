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
        private const double RiskLow = 0.25;
        private const double RiskMed = 0.35;
        private const double RiskHigh = 0.50;
        private const double RiskMax = 0.65;

        // GBPUSD: noise miatt SL kicsit szélesebb, mint USDJPY
        private const double SlBase = 1.65;
        //private const double SlWide = 1.95;
        //private const double SlTight = 1.45;

        public double GetRiskPercent(int score)
        {
            double n = NormalizeScore(score);

            if (n < 0.15) return 0.0;          // régi <60
            if (n < 0.40) return RiskLow;      // ~65–70
            if (n < 0.65) return RiskMed;      // ~75
            if (n < 0.85) return RiskHigh;     // ~82–85
            return RiskMax;                    // top setup
        }

        public double GetStopLossAtrMultiplier(int score, EntryType _ /* FX ignores entry type */)
        {
            double baseMult = SlBase;

            double n = NormalizeScore(score);

            if (n >= 0.75) baseMult -= 0.05;
            if (n >= 0.90) baseMult -= 0.05;

            return Math.Max(baseMult, 1.35);
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
            // GBPUSD: biztosabb TP1 – marad
            tp1R = 0.45;

            // =====================================================
            // TP1 RATIO – DINAMIKUS
            // Jó score → több runner
            // =====================================================
            double n = NormalizeScore(score);

            // TP1 mindig biztos, de jobb score → több runner
            tp1Ratio = 0.60 - n * 0.20; // 0.70 → 0.45

            // =====================================================
            // TP2 R – HELYES JUTALMAZÓ GÖRBE
            // AUDNZD kicsit „lustább”, mint EURUSD
            // =====================================================
            tp2R = 1.0 + n * 0.6; // 1.0 → 1.6

            // TP2 a maradékra
            tp2Ratio = 1.0 - tp1Ratio;
        }

        public double GetLotCap(int score)
        {
            double n = NormalizeScore(score);

            if (n < 0.30) return 0.55;
            if (n < 0.50) return 0.65;
            if (n < 0.70) return 0.75;
            if (n < 0.85) return 0.85;
            return 0.95;
        }

        private static double NormalizeScore(int score)
        {
            // FX új score-tartomány: ~55–90
            return Math.Clamp((score - 55) / 35.0, 0.0, 1.0);
        }
    }
}
