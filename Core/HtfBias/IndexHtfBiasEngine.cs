using GeminiV26.Core.HtfBias;
using GeminiV26.Core.Entry;
using cAlgo.API;
using cAlgo.API.Indicators;
using System;
using System.Collections.Generic;

namespace GeminiV26.Core.HtfBias
{
    public sealed class IndexHtfBiasEngine : IHtfBiasProvider
    {
        private readonly Robot _bot;

        /*
        // H1 HTF snapshot (indexekhez ez a minimum értelmes)
        private readonly HtfBiasSnapshot _snapshot = new HtfBiasSnapshot();

        private DateTime _lastH1BarTime = DateTime.MinValue;
        private DateTime _lastH1Closed = DateTime.MinValue;
        */

        private sealed class IndexBiasContext
        {
            public Bars H1;
            public ExponentialMovingAverage Ema50;
            public ExponentialMovingAverage Ema200;
            public DirectionalMovementSystem Dms;
            public HtfBiasSnapshot Snapshot = new();
            public DateTime LastH1Closed = DateTime.MinValue;
        }

        private readonly Dictionary<string, IndexBiasContext> _ctx = new();

        public IndexHtfBiasEngine(Robot bot)
        {
            _bot = bot;
        }

        public HtfBiasSnapshot Get(string symbolName)
        {
            UpdateIfNeeded(symbolName);
            return _ctx[symbolName].Snapshot;
        }

        // =====================================================
        // UPDATE LOGIC – ONLY ON CLOSED H1 BAR
        // =====================================================
        private void UpdateIfNeeded(string symbolName)
        {
            if (!_ctx.TryGetValue(symbolName, out var c))
            {
                c = new IndexBiasContext
                {
                    H1 = _bot.MarketData.GetBars(TimeFrame.Hour, symbolName)
                };

                if (c.H1 == null || c.H1.Count < 210)
                    return;

                c.Ema50 = _bot.Indicators.ExponentialMovingAverage(c.H1.ClosePrices, 50);
                c.Ema200 = _bot.Indicators.ExponentialMovingAverage(c.H1.ClosePrices, 200);
                c.Dms = _bot.Indicators.DirectionalMovementSystem(c.H1, 14);

                _ctx[symbolName] = c;
            }

            if (c.H1.Count < 205)
                return;

            var lastClosed = c.H1.OpenTimes[c.H1.Count - 2];
            if (lastClosed <= c.LastH1Closed)
                return;

            c.LastH1Closed = lastClosed;
            c.Snapshot.LastUpdateH1Closed = lastClosed;

            EvaluateBias(c);
        }

        // =====================================================
        // BIAS EVALUATION (INDEX-SAFE)
        // =====================================================
        private void EvaluateBias(IndexBiasContext c)
        {
            double price = c.H1.ClosePrices.Last(1);
            double fast = c.Ema50.Result.Last(1);
            double slow = c.Ema200.Result.Last(1);
            double adx = c.Dms.ADX.Last(1);

            if (fast > slow && adx >= 20 && price >= slow)
            {
                double conf = price > fast ? 0.8 : 0.65;
                SetBias(c.Snapshot, HtfBiasState.Bull, TradeDirection.Long, conf, "INDEX_HTF_BULL");
                return;
            }

            if (fast < slow && adx >= 20 && price <= slow)
            {
                double conf = price < fast ? 0.8 : 0.65;
                SetBias(c.Snapshot, HtfBiasState.Bear, TradeDirection.Short, conf, "INDEX_HTF_BEAR");
                return;
            }

            if (adx >= 18)
            {
                SetBias(c.Snapshot, HtfBiasState.Transition, TradeDirection.None, 0.4, "INDEX_HTF_TRANSITION");
                return;
            }

            // ⚠️ KRITIKUS JAVÍTÁS
            SetBias(c.Snapshot, HtfBiasState.Neutral, TradeDirection.None, 0.2, "INDEX_HTF_NEUTRAL");
        }

        // =====================================================
        // SNAPSHOT UPDATE
        // =====================================================
        private void SetBias(
            HtfBiasSnapshot snap,
            HtfBiasState state,
            TradeDirection dir,
            double conf,
            string reason)
        {
            snap.State = state;
            snap.AllowedDirection = dir;
            snap.Confidence01 = conf;
            snap.Reason = reason;

            _bot.Print(
                $"[INDEX HTF] state={state} allow={dir} conf={conf:0.00} reason={reason}"
            );
        }
    }
}
