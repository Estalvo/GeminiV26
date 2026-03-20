using System;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace GeminiV26.Instruments.FX
{
    internal static class FxBiasTuningHelper
    {
        private const double TrendDeadzoneAtr = 0.08;
        private const double StructureLookbackAtr = 2.20;
        private const double StructuredConfidence = 68.0;
        private const double StrongTrendFallbackConfidence = 52.0;
        private const double TrendOnlyConfidence = 34.0;

        internal struct FxBiasResult
        {
            public FxBiasResult(TradeType bias, int confidence, string state, string details)
            {
                Bias = bias;
                Confidence = confidence;
                State = state;
                Details = details;
            }

            public TradeType Bias { get; }
            public int Confidence { get; }
            public string State { get; }
            public string Details { get; }
        }

        public static FxBiasResult Evaluate(
            Bars m5,
            Bars m15,
            IndicatorDataSeries ema50,
            IndicatorDataSeries ema200,
            IndicatorDataSeries emaHtf,
            AverageDirectionalMovementIndexRating adx,
            AverageTrueRange atr)
        {
            int i = m5.Count - 1;
            double atrValue = atr.Result.LastValue;
            double adxValue = adx.ADX.LastValue;
            double lastClose = m5.ClosePrices[i];
            double lastOpen = m5.OpenPrices[i];
            double ema50Value = ema50[i];
            double ema200Value = ema200[i];
            double emaDiff = ema50Value - ema200Value;
            double emaAbs = Math.Abs(emaDiff);
            double deadzone = atrValue > 0 ? atrValue * TrendDeadzoneAtr : 0;

            bool trendLong = emaDiff > deadzone && lastClose >= ema50Value;
            bool trendShort = emaDiff < -deadzone && lastClose <= ema50Value;

            if (!trendLong && !trendShort)
            {
                if (emaDiff > 0 && lastClose >= ema50Value - atrValue * 0.10)
                    trendLong = true;
                else if (emaDiff < 0 && lastClose <= ema50Value + atrValue * 0.10)
                    trendShort = true;
            }

            var trendDirection = trendLong ? TradeType.Buy : trendShort ? TradeType.Sell : TradeType.Buy;

            bool hhhlLong = i >= 4 &&
                m5.HighPrices[i] > m5.HighPrices[i - 2] &&
                m5.LowPrices[i] > m5.LowPrices[i - 2] &&
                m5.LowPrices[i - 1] >= m5.LowPrices[i - 3];

            bool lhllShort = i >= 4 &&
                m5.HighPrices[i] < m5.HighPrices[i - 2] &&
                m5.LowPrices[i] < m5.LowPrices[i - 2] &&
                m5.HighPrices[i - 1] <= m5.HighPrices[i - 3];

            double pullbackDepthLong = atrValue > 0 ? (ema50Value - GetRecentLow(m5, i, 4)) / atrValue : 0;
            double pullbackDepthShort = atrValue > 0 ? (GetRecentHigh(m5, i, 4) - ema50Value) / atrValue : 0;

            bool bullishClose = lastClose > lastOpen;
            bool bearishClose = lastClose < lastOpen;

            bool pullbackLong = trendLong && pullbackDepthLong >= 0.15 && pullbackDepthLong <= 1.20 &&
                                lastClose >= ema50Value - atrValue * 0.10 && bullishClose;
            bool pullbackShort = trendShort && pullbackDepthShort >= 0.15 && pullbackDepthShort <= 1.20 &&
                                 lastClose <= ema50Value + atrValue * 0.10 && bearishClose;

            bool flagLong = trendLong && HasCompression(m5, i, atrValue) && pullbackDepthLong >= 0.12 && pullbackDepthLong <= 1.10 &&
                            lastClose >= m5.ClosePrices[i - 1];
            bool flagShort = trendShort && HasCompression(m5, i, atrValue) && pullbackDepthShort >= 0.12 && pullbackDepthShort <= 1.10 &&
                             lastClose <= m5.ClosePrices[i - 1];

            bool breakoutLong = trendLong && i >= 5 && lastClose > GetRecentHigh(m5, i - 1, 5) && bullishClose;
            bool breakoutShort = trendShort && i >= 5 && lastClose < GetRecentLow(m5, i - 1, 5) && bearishClose;

            bool structureLong = hhhlLong || pullbackLong || flagLong || breakoutLong;
            bool structureShort = lhllShort || pullbackShort || flagShort || breakoutShort;

            bool oppositeSignal = trendLong
                ? (lhllShort || (bearishClose && lastClose < ema50Value - atrValue * 0.20))
                : trendShort && (hhhlLong || (bullishClose && lastClose > ema50Value + atrValue * 0.20));

            double recentStructureRange = atrValue > 0 ? (GetRecentHigh(m5, i, 6) - GetRecentLow(m5, i, 6)) / atrValue : 0;
            bool choppy = (adxValue < 14 && emaAbs <= deadzone * 1.25) || recentStructureRange < 0.90 || recentStructureRange > StructureLookbackAtr;
            bool strongTrendContext =
                !choppy &&
                adxValue >= 18.0 &&
                atrValue > 0 &&
                emaAbs >= atrValue * 0.18;

            double htfEmaValue = emaHtf.LastValue;
            double htfPrice = m15.ClosePrices.LastValue;
            bool htfBull = htfPrice >= htfEmaValue;
            bool htfAligned = (trendLong && htfBull) || (trendShort && !htfBull);
            bool continuationLong = trendLong && (hhhlLong || breakoutLong || (pullbackLong && bullishClose));
            bool continuationShort = trendShort && (lhllShort || breakoutShort || (pullbackShort && bearishClose));

            int confidence = 35;
            string state = "NO_TREND";
            string pattern = "none";

            if (trendLong || trendShort)
            {
                confidence = (int)TrendOnlyConfidence;
                state = "TREND_ONLY";

                if ((trendLong && structureLong) || (trendShort && structureShort))
                {
                    confidence = (int)StructuredConfidence;
                    state = "STRUCTURED_BIAS";
                    pattern = trendLong
                        ? flagLong ? "flag" : pullbackLong ? "pullback" : breakoutLong ? "breakout" : "hhhl"
                        : flagShort ? "flag" : pullbackShort ? "pullback" : breakoutShort ? "breakout" : "lhll";
                }
                else if (strongTrendContext && !oppositeSignal && htfAligned)
                {
                    confidence = (int)StrongTrendFallbackConfidence;
                    state = "FX_FALLBACK";
                    pattern = "trend";
                }
            }

            bool structureAligned = (trendLong && structureLong) || (trendShort && structureShort);
            bool continuationAligned = continuationLong || continuationShort;
            bool clearStructure = structureAligned && !choppy && !oppositeSignal;
            bool strongSetup =
                clearStructure &&
                continuationAligned &&
                adxValue >= 23.0 &&
                atrValue > 0 &&
                emaAbs >= atrValue * 0.22;

            if (trendLong || trendShort)
                confidence += 4;
            if (adxValue >= 16.0)
                confidence += 4;
            if (adxValue >= 23.0)
                confidence += structureAligned ? 6 : 4;
            if (atrValue > 0 && emaAbs > atrValue * 0.22)
                confidence += structureAligned ? 6 : 4;
            if (htfAligned)
                confidence += structureAligned ? 5 : 3;
            if (continuationAligned)
                confidence += structureAligned ? 6 : 3;
            if (choppy)
                confidence -= structureAligned ? 14 : 18;
            if (oppositeSignal)
                confidence -= structureAligned ? 14 : 18;
            if ((state == "STRUCTURED_BIAS") && ((trendLong && (flagLong || pullbackLong)) || (trendShort && (flagShort || pullbackShort))))
                confidence += 4;
            if (strongSetup)
                confidence += 6;
            else if (clearStructure && continuationAligned)
                confidence += 3;

            if (state == "TREND_ONLY")
            {
                confidence -= strongTrendContext && htfAligned && !choppy && !oppositeSignal ? 2 : 6;
            }
            else if (state == "NO_TREND")
            {
                confidence -= 6;
            }

            confidence = Math.Max(0, Math.Min(100, confidence));

            return new FxBiasResult(
                trendDirection,
                confidence,
                state,
                $"ema50={ema50Value:F5} ema200={ema200Value:F5} diff={emaDiff:F5} adx={adxValue:F1} atr={atrValue:F5} pattern={pattern} chop={choppy} opp={oppositeSignal} htfAlign={htfAligned}");
        }

        private static bool HasCompression(Bars bars, int endIndex, double atr)
        {
            if (endIndex < 3)
                return false;

            double range = GetRecentHigh(bars, endIndex, 4) - GetRecentLow(bars, endIndex, 4);
            if (atr <= 0)
                return range > 0;

            return range / atr <= 1.35;
        }

        private static double GetRecentHigh(Bars bars, int endIndex, int length)
        {
            int start = Math.Max(0, endIndex - length + 1);
            double value = double.MinValue;
            for (int idx = start; idx <= endIndex; idx++)
                value = Math.Max(value, bars.HighPrices[idx]);
            return value;
        }

        private static double GetRecentLow(Bars bars, int endIndex, int length)
        {
            int start = Math.Max(0, endIndex - length + 1);
            double value = double.MaxValue;
            for (int idx = start; idx <= endIndex; idx++)
                value = Math.Min(value, bars.LowPrices[idx]);
            return value;
        }
    }
}
