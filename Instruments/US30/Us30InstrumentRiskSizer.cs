using GeminiV26.Core.Entry;
using GeminiV26.Risk;

namespace GeminiV26.Instruments.US30
{
    /// <summary>
    /// US30 money-risk policy – Phase 3.6 (BASELINE, NAS 1:1 clone)
    /// Policy szint:
    /// - Risk% (score alapján)
    /// - SL ATR multiplier (score + entryType alapján)
    /// - TP1/TP2 R struktúra (score alapján)
    /// - Lot cap (score alapján)
    ///
    /// Nem számol árat, nem nyit/zár, nem kezel lifecycle-t.
    /// </summary>
    public class Us30InstrumentRiskSizer : IInstrumentRiskSizer
    {
        // =========================
        // TUNING (egy helyen) – baseline NAS
        // =========================
        private const double RiskMin = 0.20;
        private const double RiskLow = 0.25;
        private const double RiskMed = 0.35;
        private const double RiskHigh = 0.45;
        private const double RiskMax = 0.55;

        private const double SlBase = 2.4;
        private const double SlWide = 2.8;
        private const double SlTight = 2.1;

        // =========================
        // RISK %
        // =========================
        public double GetRiskPercent(int score)
        {
            if (score < 60) return 0.0;
            if (score < 70) return RiskLow;
            if (score < 80) return RiskMed;
            if (score < 90) return RiskHigh;
            return RiskMax;
        }

        // =========================
        // SL (ATR multiplier)
        // =========================
        public double GetStopLossAtrMultiplier(int score, EntryType entryType)
        {
            double baseMult = SlBase;

            switch (entryType)
            {
                case EntryType.TC_Pullback:
                    baseMult = SlTight;
                    break;

                case EntryType.TC_Flag:
                    baseMult = SlBase;
                    break;

                case EntryType.BR_RangeBreakout:
                    baseMult = SlWide;
                    break;

                case EntryType.TR_Reversal:
                    baseMult = SlWide;
                    break;

                default:
                    baseMult = SlBase;
                    break;
            }

            if (score >= 85) baseMult -= 0.1;
            if (score >= 92) baseMult -= 0.1;

            if (baseMult < 1.6) baseMult = 1.6;

            return baseMult;
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
            // US30 – impulse + continuation index
            tp1R = 0.35; // biztosítás, egységes

            // =========================
            // TP1 RATIO – DINAMIKUS
            // jobb score = több runner
            // =========================
            if (score < 70)
                tp1Ratio = 0.65;   // védekező
            else if (score < 80)
                tp1Ratio = 0.55;
            else if (score < 90)
                tp1Ratio = 0.45;
            else
                tp1Ratio = 0.35;   // prémium setup → futtatjuk

            // =========================
            // TP2 R – MONOTON GÖRBE
            // =========================
            if (score < 70)
                tp2R = 1.0;
            else if (score < 80)
                tp2R = 1.4;
            else if (score < 90)
                tp2R = 2.0;
            else
                tp2R = 3.0;

            // maradék megy TP2-re
            tp2Ratio = 1.0 - tp1Ratio;
        }

        // =========================
        // LOT CAP
        // =========================
        public double GetLotCap(int score)
        {
            if (score < 70) return 1.0;
            if (score < 80) return 1.5;
            if (score < 90) return 2.0;
            return 2.5;
        }
    }
}
