using cAlgo.API;
using GeminiV26.Interfaces;
using GeminiV26.Core.Entry;

namespace GeminiV26.Core
{
    /// <summary>
    /// RiskManager
    /// - pip-alapú kockázatkezelés
    /// - score → risk %
    /// - SL (pip) + risk % → volume (units)
    /// </summary>
    public class RiskManager : IRiskManager
    {
        private readonly Robot _bot;

        public RiskManager(Robot bot)
        {
            _bot = bot;
        }

        // =====================================================
        // 1️⃣ SCORE → RISK %
        // =====================================================
        public double GetRiskPercent(int score)
        {
            if (score >= 85) return 1.2;
            if (score >= 75) return 1.0;
            if (score >= 65) return 0.7;

            return 0.0; // safety
        }

        // =====================================================
        // 2️⃣ ENTRY → STOP LOSS (PIPS)
        // TEMP: fix értékek, KÉSŐBB ATR / instrument
        // =====================================================
        public double GetStopLossPips(EntryEvaluation entry)
        {
            return entry.Type switch
            {
                EntryType.TC_Flag => 120,
                EntryType.TC_Pullback => 100,
                EntryType.BR_RangeBreakout => 140,
                EntryType.TR_Reversal => 160,
                _ => 120
            };
        }

        // =====================================================
        // 3️⃣ RISK % + SL → VOLUME (UNIT)
        // MEGLÉVŐ LOGIKA – ÉRINTETLEN
        // =====================================================
        public long CalculateVolume(double riskPercent, double stopLossPips)
        {
            if (riskPercent <= 0 || stopLossPips <= 0)
                return 0;

            var symbol = _bot.Symbol;

            double riskAmount =
                _bot.Account.Balance * (riskPercent / 100.0);

            if (riskAmount <= 0)
                return 0;

            double pipValuePerUnit =
                symbol.TickValue / symbol.TickSize * symbol.PipSize;

            if (pipValuePerUnit <= 0)
                return 0;

            double rawVolume =
                riskAmount / (stopLossPips * pipValuePerUnit);

            if (rawVolume <= 0)
                return 0;

            double normalized =
                symbol.NormalizeVolumeInUnits(
                    rawVolume,
                    RoundingMode.Down
                );

            if (normalized <= 0)
                return 0;

            return (long)normalized;
        }
    }
}
