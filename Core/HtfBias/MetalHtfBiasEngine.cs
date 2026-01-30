using GeminiV26.Core.HtfBias;
using GeminiV26.Core.Entry;
using cAlgo.API;
using cAlgo.API.Indicators;
using System;
using System.Collections.Generic;

namespace GeminiV26.Core.HtfBias
{ 
    /// <summary>
    /// METAL HTF Bias Engine (FX-style)
    /// - Bias TF: H1
    /// - Update TF: M15 close (only recalc on closed M15)
    /// - Output: HtfBiasSnapshot (State + AllowedDirection + Reason)
    /// </summary>
    public sealed class MetalHtfBiasEngine : IHtfBiasProvider
    {
        private readonly Robot _bot;

        // === HTF settings (XAU) ===
        private static readonly TimeFrame BiasTf = TimeFrame.Hour;        // H1 bias
        private static readonly TimeFrame UpdateTf = TimeFrame.Minute15;  // update on M15 close

        // === EMA / filters (XAU) ===
        private const int EmaFast = 21;
        private const int EmaSlow = 55;

        // ADX trend küszöb (XAU gyorsan vált, ezért enyhébb)
        private const double MinAdxTrend = 16.0;
        private const double MinAdxNeutral = 14.0;

        // EMA gap ATR arány (ha túl kicsi → nincs edge)
        private const double MinEmaGapAtr = 0.10;

        // Overextended tilt (ne csúcson long / aljon short)
        private const double MaxDistFromFastAtr = 0.90;

        // Transition: friss EMA cross után ne tradeljünk (fakeout védelem)
        private const int CrossLookbackBars = 5;

        // ATR “energia” (ha nem nő, XAU gyakran csak drift)
        private const int AtrSlopeLookback = 3;

        /*
        private Bars _h1;
        private Bars _m15;

        private ExponentialMovingAverage _ema21;
        private ExponentialMovingAverage _ema55;
        private AverageTrueRange _atrH1;
        private DirectionalMovementSystem _dms; // ADX

        private readonly HtfBiasSnapshot _snapshot = new();
        */

        private sealed class MetalBiasContext
        {
            public Bars H1;
            public Bars M15;
            public ExponentialMovingAverage Ema21;
            public ExponentialMovingAverage Ema55;
            public AverageTrueRange AtrH1;
            public DirectionalMovementSystem Dms;
            public HtfBiasSnapshot Snapshot = new();
        }

        private readonly Dictionary<string, MetalBiasContext> _ctx = new();

        public MetalHtfBiasEngine(Robot bot)
        {
            _bot = bot ?? throw new ArgumentNullException(nameof(bot)); 
        }

        /*
        private void Wire()
        {
            _h1 = _bot.MarketData.GetBars(BiasTf);
            _m15 = _bot.MarketData.GetBars(UpdateTf);

            if (_h1 != null)
            {
                _ema21 = _bot.Indicators.ExponentialMovingAverage(_h1.ClosePrices, EmaFast);
                _ema55 = _bot.Indicators.ExponentialMovingAverage(_h1.ClosePrices, EmaSlow);
                _atrH1 = _bot.Indicators.AverageTrueRange(_h1, 14, MovingAverageType.Exponential);
                _dms = _bot.Indicators.DirectionalMovementSystem(_h1, 14);
            }
        }
        */

        public HtfBiasSnapshot Get(string symbolName)
        {
            UpdateIfNeeded(symbolName);
            return _ctx[symbolName].Snapshot;
        }

        private void UpdateIfNeeded(string symbolName)
        {
            if (!_ctx.TryGetValue(symbolName, out var c))
            {
                c = new MetalBiasContext
                {
                    H1 = _bot.MarketData.GetBars(BiasTf, symbolName),
                    M15 = _bot.MarketData.GetBars(UpdateTf, symbolName)
                };

                if (c.H1 == null || c.M15 == null)
                    return;

                c.Ema21 = _bot.Indicators.ExponentialMovingAverage(c.H1.ClosePrices, EmaFast);
                c.Ema55 = _bot.Indicators.ExponentialMovingAverage(c.H1.ClosePrices, EmaSlow);
                c.AtrH1 = _bot.Indicators.AverageTrueRange(c.H1, 14, MovingAverageType.Exponential);
                c.Dms = _bot.Indicators.DirectionalMovementSystem(c.H1, 14);

                _ctx[symbolName] = c;
            }

            if (c.M15.Count < 10 || c.H1.Count < Math.Max(EmaSlow, 80))
                return;

            int m15Closed = c.M15.Count - 2;
            if (m15Closed < 1) return;

            DateTime t = c.M15.OpenTimes[m15Closed];
            if (t == c.Snapshot.LastUpdateH1Closed)
                return;

            c.Snapshot.LastUpdateH1Closed = t;

            int i = c.H1.Count - 2;
            if (i < 10) return;

            double e21 = c.Ema21.Result[i];
            double e55 = c.Ema55.Result[i];
            double close = c.H1.ClosePrices[i];

            double atr = c.AtrH1.Result[i];
            if (atr <= 0 || double.IsNaN(atr))
                atr = Math.Max(_bot.Symbol.TickSize * 10, 1e-6);

            double adx = c.Dms.ADX[i];

            bool bullAlign = e21 > e55;
            bool bearAlign = e21 < e55;

            // === recent EMA cross → Transition
            if (IsRecentCross(c, i))
            {
                SetState(c.Snapshot, HtfBiasState.Transition, TradeDirection.None,
                    $"METAL_HTF TRANSITION (recent EMA cross) adx={adx:0.0}");
                return;
            }

            double emaGap = Math.Abs(e21 - e55);
            double gapAtr = emaGap / atr;

            if (adx < MinAdxNeutral || gapAtr < MinEmaGapAtr)
            {
                SetState(c.Snapshot, HtfBiasState.Neutral, TradeDirection.None,
                    $"METAL_HTF NEUTRAL adx={adx:0.0} gapATR={gapAtr:0.00}", 0.2);
                return;
            }

            bool atrExpanding = IsAtrExpanding(c, i);

            double distFastAtr = Math.Abs(close - e21) / atr;
            if (distFastAtr > MaxDistFromFastAtr)
            {
                SetState(c.Snapshot, HtfBiasState.Transition, TradeDirection.None,
                    $"METAL_HTF TRANSITION (overextended) distATR={distFastAtr:0.00}");
                return;
            }

            if (bullAlign)
            {
                if (adx < MinAdxTrend || !atrExpanding)
                {
                    SetState(c.Snapshot, HtfBiasState.Transition, TradeDirection.None,
                        $"METAL_HTF TRANSITION (bull weak) adx={adx:0.0}");
                    return;
                }

                double conf = Clamp01(0.55 + (Math.Min(gapAtr, 0.40) / 0.40) * 0.35);
                SetState(c.Snapshot, HtfBiasState.Bull, TradeDirection.Long,
                    $"METAL_HTF BULL", conf);
                return;
            }

            if (bearAlign)
            {
                if (adx < MinAdxTrend || !atrExpanding)
                {
                    SetState(c.Snapshot, HtfBiasState.Transition, TradeDirection.None,
                        $"METAL_HTF TRANSITION (bear weak) adx={adx:0.0}");
                    return;
                }

                double conf = Clamp01(0.55 + (Math.Min(gapAtr, 0.40) / 0.40) * 0.35);
                SetState(c.Snapshot, HtfBiasState.Bear, TradeDirection.Short,
                    $"METAL_HTF BEAR", conf);
                return;
            }

            SetState(c.Snapshot, HtfBiasState.Neutral, TradeDirection.None, "METAL_HTF NEUTRAL");
        }

        private bool IsAtrExpanding(MetalBiasContext c, int i)
        {
            int j = i - AtrSlopeLookback;
            if (j < 1) return false;

            double now = c.AtrH1.Result[i];
            double prev = c.AtrH1.Result[j];
            if (prev <= 0) return false;

            return now > prev * 1.02;
        }

        private bool IsRecentCross(MetalBiasContext c, int i)
        {
            int start = Math.Max(2, i - CrossLookbackBars);
            double prevDiff = c.Ema21.Result[start - 1] - c.Ema55.Result[start - 1];

            for (int k = start; k <= i; k++)
            {
                double diff = c.Ema21.Result[k] - c.Ema55.Result[k];
                if ((prevDiff > 0) != (diff > 0) && Math.Abs(diff) > _bot.Symbol.TickSize)
                    return true;

                prevDiff = diff;
            }
            return false;
        }

        private void SetState(HtfBiasSnapshot snap, HtfBiasState st, TradeDirection dir, string reason, double conf01 = 0.0)
        {
            snap.State = st;
            snap.AllowedDirection = dir;
            snap.Reason = reason;
            snap.Confidence01 = Clamp01(conf01);
        }

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
    }
}
