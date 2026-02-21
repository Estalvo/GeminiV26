using GeminiV26.Core.Entry;
using cAlgo.API;
using cAlgo.API.Indicators;
using System;

namespace GeminiV26.Core.HtfBias
{
    // FX HTF Bias v2
    // - Bias TF: H4
    // - Update tick: H1 closed bar
    // - KEY RULE: FX-en nem fordulunk Bear-ra csak azért, mert a close lecsúszott az EMA50 alá.
    //            Bear = csak EMA alignment alapján (EMA50 < EMA200).
    // - Price location csak confidence (minőség), nem irány.
    public sealed class FxHtfBiasEngine : IHtfBiasProvider
    {
        private readonly Robot _bot;

        private static readonly TimeFrame BiasTf = TimeFrame.Hour4;
        private static readonly TimeFrame UpdateTf = TimeFrame.Hour;

        private const int EmaFast = 50;
        private const int EmaSlow = 200;

        // Energiaküszöb: ha EMA gap túl kicsi a H4 ATR-hez képest, akkor "Transition" (nem tiszta trend)
        private const double MinEmaGapAtr = 0.10;

        // Location: ha nagyon messze van a close az EMA50-től, akkor romlik a confidence
        private const double MaxDistFromFastAtr = 3.0;

        // FX-ben a confidence sose legyen 0 (hogy a router ne "teljesen vakon" büntessen)
        private const double MinConf = 0.25;

        // Ha EMA alignment long, de close az EMA50 alatt van → csak Transition/Neutral (nem Bear)
        private const double UnderFastTransitionConf = 0.55;

        private sealed class FxBiasContext
        {
            public Bars H4;
            public Bars H1;
            public ExponentialMovingAverage Ema50;
            public ExponentialMovingAverage Ema200;
            public AverageTrueRange AtrH4;
            public HtfBiasSnapshot Snapshot = new();
        }

        private readonly System.Collections.Generic.Dictionary<string, FxBiasContext> _ctx = new();

        public FxHtfBiasEngine(Robot bot)
        {
            _bot = bot ?? throw new ArgumentNullException(nameof(bot));
        }

        public HtfBiasSnapshot Get(string symbolName)
        {
            UpdateIfNeeded(symbolName);
            return _ctx[symbolName].Snapshot;
        }

        private void UpdateIfNeeded(string symbolName)
        {
            if (!_ctx.TryGetValue(symbolName, out var c))
            {
                c = new FxBiasContext
                {
                    H4 = _bot.MarketData.GetBars(BiasTf, symbolName),
                    H1 = _bot.MarketData.GetBars(UpdateTf, symbolName)
                };

                // Default safe snapshot (sosem hagyjuk "üresen")
                SetState(c.Snapshot, HtfBiasState.Neutral, TradeDirection.None, "FX_HTF INIT NEUTRAL", 0.5);

                // Ha nincs elég adat, ismerjük be, de legyen snapshot
                if (c.H4 == null || c.H4.Count < EmaSlow + 5 || c.H1 == null || c.H1.Count < 10)
                {
                    _ctx[symbolName] = c;
                    return;
                }

                c.Ema50 = _bot.Indicators.ExponentialMovingAverage(c.H4.ClosePrices, EmaFast);
                c.Ema200 = _bot.Indicators.ExponentialMovingAverage(c.H4.ClosePrices, EmaSlow);
                c.AtrH4 = _bot.Indicators.AverageTrueRange(c.H4, 14, MovingAverageType.Exponential);

                _ctx[symbolName] = c;
            }

            // Still not enough? keep last snapshot
            if (c.H1 == null || c.H4 == null || c.H1.Count < 10 || c.H4.Count < EmaSlow + 5)
                return;

            int h1Closed = c.H1.Count - 2;
            if (h1Closed < 1) return;

            DateTime tH1 = c.H1.OpenTimes[h1Closed];
            if (tH1 == c.Snapshot.LastUpdateH1Closed)
                return;

            c.Snapshot.LastUpdateH1Closed = tH1;

            // Use last CLOSED H4 bar
            int i = c.H4.Count - 2;
            if (i < EmaSlow + 2) return;

            double e50 = c.Ema50.Result[i];
            double e200 = c.Ema200.Result[i];
            double close = c.H4.ClosePrices[i];

            double atr = c.AtrH4.Result[i];
            if (atr <= 0 || double.IsNaN(atr))
                atr = Math.Max(_bot.Symbol.TickSize * 10, 1e-6);

            bool bullAlign = e50 > e200;
            bool bearAlign = e50 < e200;

            double gapAtr = Math.Abs(e50 - e200) / atr;
            bool energyOk = gapAtr >= MinEmaGapAtr;

            // Location / extension
            double distFastAtr = Math.Abs(close - e50) / atr;

            // Confidence model: 1 -> közel EMA50; 0 -> túl messze
            double conf = 1.0 - Math.Min(distFastAtr / MaxDistFromFastAtr, 1.0);
            conf = Math.Max(conf, MinConf);

            // ===== RULE #1: alignment dönti el az irányt =====
            if (bullAlign)
            {
                // ===== 1️⃣ Weak structure (EMA gap too small) =====
                if (!energyOk)
                {
                    SetState(
                        c.Snapshot,
                        HtfBiasState.Transition,
                        TradeDirection.None,
                        $"FX_HTF TRANSITION (BULL weak structure) gapATR={gapAtr:0.00} distATR={distFastAtr:0.00}",
                        0.45   // ↓ csökkentett confidence
                    );
                    return;
                }

                // ===== 2️⃣ Healthy pullback (trend él, csak retrace) =====
                if (close < e50)
                {
                    double pullbackConf = Math.Max(conf, UnderFastTransitionConf);

                    SetState(
                        c.Snapshot,
                        HtfBiasState.Transition,
                        TradeDirection.None,
                        $"FX_HTF TRANSITION (BULL pullback) conf={pullbackConf:0.00} distATR={distFastAtr:0.00}",
                        pullbackConf
                    );
                    return;
                }

                // ===== 3️⃣ Clean bullish structure =====
                SetState(
                    c.Snapshot,
                    HtfBiasState.Bull,
                    TradeDirection.Long,
                    $"FX_HTF BULL conf={conf:0.00} gapATR={gapAtr:0.00} distATR={distFastAtr:0.00}",
                    conf
                );
                return;
            }

            if (bearAlign)
            {
                // ===== 1️⃣ Weak structure =====
                if (!energyOk)
                {
                    SetState(
                        c.Snapshot,
                        HtfBiasState.Transition,
                        TradeDirection.None,
                        $"FX_HTF TRANSITION (BEAR weak structure) gapATR={gapAtr:0.00} distATR={distFastAtr:0.00}",
                        0.45
                    );
                    return;
                }

                // ===== 2️⃣ Healthy pullback =====
                if (close > e50)
                {
                    double pullbackConf = Math.Max(conf, UnderFastTransitionConf);

                    SetState(
                        c.Snapshot,
                        HtfBiasState.Transition,
                        TradeDirection.None,
                        $"FX_HTF TRANSITION (BEAR pullback) conf={pullbackConf:0.00} distATR={distFastAtr:0.00}",
                        pullbackConf
                    );
                    return;
                }

                // ===== 3️⃣ Clean bearish structure =====
                SetState(
                    c.Snapshot,
                    HtfBiasState.Bear,
                    TradeDirection.Short,
                    $"FX_HTF BEAR conf={conf:0.00} gapATR={gapAtr:0.00} distATR={distFastAtr:0.00}",
                    conf
                );
                return;
            }

            // Flat/rare tie
            SetState(c.Snapshot, HtfBiasState.Neutral, TradeDirection.None, "FX_HTF NEUTRAL (no alignment)", 0.50);
        }

        private void SetState(HtfBiasSnapshot snap, HtfBiasState st, TradeDirection dir, string reason, double conf01)
        {
            snap.State = st;
            snap.AllowedDirection = dir;
            snap.Reason = reason;
            snap.Confidence01 = Clamp01(conf01);
        }

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
    }
}
