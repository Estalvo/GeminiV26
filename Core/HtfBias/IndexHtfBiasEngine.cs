using GeminiV26.Core.Entry;
using cAlgo.API;
using cAlgo.API.Indicators;
using System;
using System.Collections.Generic;

namespace GeminiV26.Core.HtfBias
{
    /// <summary>
    /// Index HTF Bias Engine v2.
    ///
    /// Design intent:
    /// - continuation-first model for index products
    /// - H1 structure, recalculated on closed M15 bars for faster reaction
    /// - EMA alignment + EMA slope + ADX + ATR-normalized structure/location
    /// - pullbacks and overextension handled explicitly
    /// - confidence is continuous (0..1)
    /// </summary>
    public sealed class IndexHtfBiasEngine : IHtfBiasProvider
    {
        private readonly Robot _bot;
        private readonly RuntimeSymbolResolver _runtimeSymbols;

        private static readonly TimeFrame BiasTf = TimeFrame.Hour;
        private static readonly TimeFrame UpdateTf = TimeFrame.Minute15;

        private const int EmaImpulse = 21;
        private const int EmaStructure = 50;
        private const int EmaAnchor = 200;
        private const int AtrPeriod = 14;
        private const int AdxPeriod = 14;

        private const double MinAdxDirectional = 15.0;
        private const double MinAdxTrend = 18.0;
        private const double StrongAdxTrend = 24.0;

        private const double MinGapAtrDirectional = 0.08;
        private const double HealthyGapAtr = 0.18;
        private const double StrongGapAtr = 0.45;

        private const double MinSlopeStructAtr = 0.07;
        private const double MinSlopeImpulseAtr = 0.12;
        private const double StrongSlopeImpulseAtr = 0.38;

        private const double EarlyGapSlackAtr = 0.12;
        private const double PullbackLimitAtr = 1.35;
        private const double OverextendedFastAtr = 1.60;
        private const double ExcessiveStructureDistAtr = 2.40;

        private sealed class IndexBiasContext
        {
            public Bars H1;
            public Bars M15;
            public ExponentialMovingAverage Ema21;
            public ExponentialMovingAverage Ema50;
            public ExponentialMovingAverage Ema200;
            public AverageTrueRange AtrH1;
            public DirectionalMovementSystem Dms;
            public HtfBiasSnapshot Snapshot = new HtfBiasSnapshot();
        }

        private readonly Dictionary<string, IndexBiasContext> _ctx = new Dictionary<string, IndexBiasContext>(StringComparer.OrdinalIgnoreCase);

        public IndexHtfBiasEngine(Robot bot)
        {
            _bot = bot ?? throw new ArgumentNullException(nameof(bot));
            _runtimeSymbols = new RuntimeSymbolResolver(_bot);
        }

        public HtfBiasSnapshot Get(string symbolName)
        {
            var c = EnsureContext(symbolName);
            if (c == null)
                return CreateUnavailableSnapshot(symbolName);

            UpdateIfNeeded(symbolName, c);
            return c.Snapshot;
        }

        private IndexBiasContext EnsureContext(string symbolName)
        {
            if (_ctx.TryGetValue(symbolName, out var c))
                return c;

            c = new IndexBiasContext();
            SetState(c.Snapshot, HtfBiasState.Neutral, TradeDirection.None, "INDEX_HTF INIT NEUTRAL", 0.30);
            _ctx[symbolName] = c;

            if (!_runtimeSymbols.TryGetBars(BiasTf, symbolName, out c.H1) ||
                !_runtimeSymbols.TryGetBars(UpdateTf, symbolName, out c.M15))
            {
                var unavailable = CreateUnavailableSnapshot(symbolName);
                c.Snapshot.State = unavailable.State;
                c.Snapshot.AllowedDirection = unavailable.AllowedDirection;
                c.Snapshot.Confidence01 = unavailable.Confidence01;
                c.Snapshot.Reason = unavailable.Reason;
                return c;
            }

            c.Ema21 = _bot.Indicators.ExponentialMovingAverage(c.H1.ClosePrices, EmaImpulse);
            c.Ema50 = _bot.Indicators.ExponentialMovingAverage(c.H1.ClosePrices, EmaStructure);
            c.Ema200 = _bot.Indicators.ExponentialMovingAverage(c.H1.ClosePrices, EmaAnchor);
            c.AtrH1 = _bot.Indicators.AverageTrueRange(c.H1, AtrPeriod, MovingAverageType.Exponential);
            c.Dms = _bot.Indicators.DirectionalMovementSystem(c.H1, AdxPeriod);

            return c;
        }

        private void UpdateIfNeeded(string symbolName, IndexBiasContext c)
        {
            if (c.H1 == null || c.M15 == null || c.Ema21 == null || c.Ema50 == null || c.Ema200 == null || c.AtrH1 == null || c.Dms == null)
                return;

            if (c.M15.Count < 10 || c.H1.Count < EmaAnchor + 12)
            {
                SetState(c.Snapshot, HtfBiasState.Neutral, TradeDirection.None, "INDEX_HTF NEUTRAL (insufficient data)", 0.25);
                return;
            }

            int m15Closed = c.M15.Count - 2;
            if (m15Closed < 1)
                return;

            DateTime t = c.M15.OpenTimes[m15Closed];
            if (t == c.Snapshot.LastUpdateH1Closed)
                return;

            c.Snapshot.LastUpdateH1Closed = t;
            EvaluateBias(symbolName, c);
        }

        private void EvaluateBias(string symbolName, IndexBiasContext c)
        {
            int i = c.H1.Count - 2;
            if (i < EmaAnchor + 8)
            {
                SetState(c.Snapshot, HtfBiasState.Neutral, TradeDirection.None, "INDEX_HTF NEUTRAL (warmup)", 0.25);
                return;
            }

            double close = c.H1.ClosePrices[i];
            double e21 = c.Ema21.Result[i];
            double e50 = c.Ema50.Result[i];
            double e200 = c.Ema200.Result[i];

            double atr = c.AtrH1.Result[i];
            if (atr <= 0 || double.IsNaN(atr))
                atr = GetAtrFallback(symbolName);

            double adx = c.Dms.ADX[i];
            double diPlus = c.Dms.DIPlus[i];
            double diMinus = c.Dms.DIMinus[i];
            double diEdge = diPlus - diMinus;

            double slope21Atr = ComputeAtrSlope(c.Ema21.Result, i, 3, atr);
            double slope50Atr = ComputeAtrSlope(c.Ema50.Result, i, 5, atr);
            double slope200Atr = ComputeAtrSlope(c.Ema200.Result, i, 8, atr);

            double gapAtr = Math.Abs(e50 - e200) / atr;
            double signedGapAtr = (e50 - e200) / atr;
            double impulseGapAtr = Math.Abs(e21 - e50) / atr;
            double signedDistFastAtr = (close - e21) / atr;
            double distFastAtr = Math.Abs(signedDistFastAtr);
            double signedDistStructAtr = (close - e50) / atr;
            double distStructAtr = Math.Abs(signedDistStructAtr);

            bool bullAlign = e50 > e200;
            bool bearAlign = e50 < e200;

            bool earlyBull =
                slope21Atr >= MinSlopeImpulseAtr &&
                slope50Atr >= MinSlopeStructAtr &&
                signedGapAtr >= -EarlyGapSlackAtr &&
                close >= e50 &&
                diEdge > 0;

            bool earlyBear =
                slope21Atr <= -MinSlopeImpulseAtr &&
                slope50Atr <= -MinSlopeStructAtr &&
                signedGapAtr <= EarlyGapSlackAtr &&
                close <= e50 &&
                diEdge < 0;

            bool bullPremise = bullAlign || earlyBull;
            bool bearPremise = bearAlign || earlyBear;

            double slopeScoreBull = Clamp01((Math.Max(slope21Atr, slope50Atr) - 0.03) / (StrongSlopeImpulseAtr - 0.03));
            double slopeScoreBear = Clamp01((Math.Max(-slope21Atr, -slope50Atr) - 0.03) / (StrongSlopeImpulseAtr - 0.03));
            double adxScore = Clamp01((adx - MinAdxDirectional) / (StrongAdxTrend - MinAdxDirectional));
            double gapScore = Clamp01((gapAtr - MinGapAtrDirectional) / (StrongGapAtr - MinGapAtrDirectional));
            double locationScore = 1.0 - Clamp01((distFastAtr - 0.35) / (OverextendedFastAtr - 0.35));
            double trendBaseScoreBull = BuildTrendConfidence(slopeScoreBull, adxScore, gapScore, locationScore, bullAlign, earlyBull, distStructAtr, impulseGapAtr);
            double trendBaseScoreBear = BuildTrendConfidence(slopeScoreBear, adxScore, gapScore, locationScore, bearAlign, earlyBear, distStructAtr, impulseGapAtr);

            bool structureWeak = gapAtr < HealthyGapAtr || impulseGapAtr < 0.05;
            bool directionalEnergyWeak = adx < MinAdxTrend;
            bool deeplyExtended = distFastAtr >= OverextendedFastAtr || distStructAtr >= ExcessiveStructureDistAtr;

            if (!bullPremise && !bearPremise)
            {
                if (adx < MinAdxDirectional && Math.Abs(slope21Atr) < 0.05 && Math.Abs(slope50Atr) < 0.04)
                {
                    SetState(
                        c.Snapshot,
                        HtfBiasState.Neutral,
                        TradeDirection.None,
                        $"INDEX_HTF NEUTRAL flat structure adx={adx:0.0} slope21ATR={slope21Atr:0.00} gapATR={gapAtr:0.00}",
                        0.22);
                    return;
                }

                SetState(
                    c.Snapshot,
                    HtfBiasState.Transition,
                    TradeDirection.None,
                    $"INDEX_HTF TRANSITION mixed structure adx={adx:0.0} slope21ATR={slope21Atr:0.00} slope50ATR={slope50Atr:0.00} gapATR={gapAtr:0.00}",
                    Clamp01(0.30 + 0.20 * adxScore + 0.20 * Math.Max(slopeScoreBull, slopeScoreBear)));
                return;
            }

            if (bullPremise && !bearPremise)
            {
                bool pullback = signedDistStructAtr < 0 && signedDistStructAtr >= -PullbackLimitAtr && slope50Atr > -0.03 && slope200Atr >= -0.02;
                bool earlyOnly = earlyBull && !bullAlign;
                bool weakTrendButTradable = adx >= MinAdxDirectional && (structureWeak || directionalEnergyWeak);

                if (deeplyExtended && signedDistFastAtr > 0)
                {
                    double conf = Clamp01(0.42 + 0.20 * slopeScoreBull + 0.10 * adxScore - 0.10 * Clamp01((distFastAtr - OverextendedFastAtr) / 1.2));
                    SetState(
                        c.Snapshot,
                        HtfBiasState.Transition,
                        TradeDirection.None,
                        $"INDEX_HTF TRANSITION bullish but overextended slope21ATR={slope21Atr:0.00} adx={adx:0.0} distFastATR={distFastAtr:0.00} gapATR={gapAtr:0.00}",
                        conf);
                    return;
                }

                if (pullback)
                {
                    double conf = Clamp01(0.44 + 0.18 * slopeScoreBull + 0.12 * adxScore + 0.08 * gapScore - 0.10 * Clamp01(distStructAtr / PullbackLimitAtr));
                    SetState(
                        c.Snapshot,
                        HtfBiasState.Transition,
                        TradeDirection.None,
                        $"INDEX_HTF TRANSITION bullish pullback slope50ATR={slope50Atr:0.00} adx={adx:0.0} pullbackATR={distStructAtr:0.00} gapATR={gapAtr:0.00}",
                        conf);
                    return;
                }

                if (earlyOnly && adx < MinAdxTrend)
                {
                    double conf = Clamp01(0.38 + 0.20 * slopeScoreBull + 0.10 * gapScore);
                    SetState(
                        c.Snapshot,
                        HtfBiasState.Transition,
                        TradeDirection.None,
                        $"INDEX_HTF TRANSITION early bullish formation slope21ATR={slope21Atr:0.00} slope50ATR={slope50Atr:0.00} adx={adx:0.0}",
                        conf);
                    return;
                }

                if (weakTrendButTradable)
                {
                    double conf = Clamp01(Math.Max(0.34, trendBaseScoreBull * 0.72));
                    SetState(
                        c.Snapshot,
                        HtfBiasState.Bull,
                        TradeDirection.Long,
                        $"INDEX_HTF BULL weak-but-tradable slope21ATR={slope21Atr:0.00} adx={adx:0.0} gapATR={gapAtr:0.00} distFastATR={distFastAtr:0.00}",
                        conf);
                    return;
                }

                double finalConf = Clamp01(Math.Max(0.40, trendBaseScoreBull));
                SetState(
                    c.Snapshot,
                    HtfBiasState.Bull,
                    TradeDirection.Long,
                    $"INDEX_HTF BULL continuation slope21ATR={slope21Atr:0.00} slope50ATR={slope50Atr:0.00} adx={adx:0.0} gapATR={gapAtr:0.00} distFastATR={distFastAtr:0.00}",
                    finalConf);
                return;
            }

            if (bearPremise && !bullPremise)
            {
                bool pullback = signedDistStructAtr > 0 && signedDistStructAtr <= PullbackLimitAtr && slope50Atr < 0.03 && slope200Atr <= 0.02;
                bool earlyOnly = earlyBear && !bearAlign;
                bool weakTrendButTradable = adx >= MinAdxDirectional && (structureWeak || directionalEnergyWeak);

                if (deeplyExtended && signedDistFastAtr < 0)
                {
                    double conf = Clamp01(0.42 + 0.20 * slopeScoreBear + 0.10 * adxScore - 0.10 * Clamp01((distFastAtr - OverextendedFastAtr) / 1.2));
                    SetState(
                        c.Snapshot,
                        HtfBiasState.Transition,
                        TradeDirection.None,
                        $"INDEX_HTF TRANSITION bearish but overextended slope21ATR={slope21Atr:0.00} adx={adx:0.0} distFastATR={distFastAtr:0.00} gapATR={gapAtr:0.00}",
                        conf);
                    return;
                }

                if (pullback)
                {
                    double conf = Clamp01(0.44 + 0.18 * slopeScoreBear + 0.12 * adxScore + 0.08 * gapScore - 0.10 * Clamp01(distStructAtr / PullbackLimitAtr));
                    SetState(
                        c.Snapshot,
                        HtfBiasState.Transition,
                        TradeDirection.None,
                        $"INDEX_HTF TRANSITION bearish pullback slope50ATR={slope50Atr:0.00} adx={adx:0.0} pullbackATR={distStructAtr:0.00} gapATR={gapAtr:0.00}",
                        conf);
                    return;
                }

                if (earlyOnly && adx < MinAdxTrend)
                {
                    double conf = Clamp01(0.38 + 0.20 * slopeScoreBear + 0.10 * gapScore);
                    SetState(
                        c.Snapshot,
                        HtfBiasState.Transition,
                        TradeDirection.None,
                        $"INDEX_HTF TRANSITION early bearish formation slope21ATR={slope21Atr:0.00} slope50ATR={slope50Atr:0.00} adx={adx:0.0}",
                        conf);
                    return;
                }

                if (weakTrendButTradable)
                {
                    double conf = Clamp01(Math.Max(0.34, trendBaseScoreBear * 0.72));
                    SetState(
                        c.Snapshot,
                        HtfBiasState.Bear,
                        TradeDirection.Short,
                        $"INDEX_HTF BEAR weak-but-tradable slope21ATR={slope21Atr:0.00} adx={adx:0.0} gapATR={gapAtr:0.00} distFastATR={distFastAtr:0.00}",
                        conf);
                    return;
                }

                double finalConf = Clamp01(Math.Max(0.40, trendBaseScoreBear));
                SetState(
                    c.Snapshot,
                    HtfBiasState.Bear,
                    TradeDirection.Short,
                    $"INDEX_HTF BEAR continuation slope21ATR={slope21Atr:0.00} slope50ATR={slope50Atr:0.00} adx={adx:0.0} gapATR={gapAtr:0.00} distFastATR={distFastAtr:0.00}",
                    finalConf);
                return;
            }

            SetState(
                c.Snapshot,
                HtfBiasState.Transition,
                TradeDirection.None,
                $"INDEX_HTF TRANSITION conflicted bull/bear premise adx={adx:0.0} slope21ATR={slope21Atr:0.00} slope50ATR={slope50Atr:0.00} gapATR={gapAtr:0.00}",
                Clamp01(0.35 + 0.20 * adxScore));
        }

        private static double BuildTrendConfidence(
            double slopeScore,
            double adxScore,
            double gapScore,
            double locationScore,
            bool aligned,
            bool early,
            double distStructAtr,
            double impulseGapAtr)
        {
            double structureScore = Clamp01(0.65 * gapScore + 0.35 * Clamp01(impulseGapAtr / 0.35));
            double pullbackPenalty = Clamp01((distStructAtr - 0.90) / 1.50);

            double conf =
                0.34 * slopeScore +
                0.24 * adxScore +
                0.22 * structureScore +
                0.12 * locationScore +
                (aligned ? 0.06 : 0.0) +
                (early ? 0.03 : 0.0) -
                0.10 * pullbackPenalty;

            return Clamp01(conf);
        }

        private static double ComputeAtrSlope(IndicatorDataSeries series, int index, int lookback, double atr)
        {
            int j = index - lookback;
            if (j < 0 || atr <= 0)
                return 0.0;

            return (series[index] - series[j]) / atr;
        }

        private double GetAtrFallback(string symbolName)
        {
            double tick = _runtimeSymbols.TryGetSymbolMeta(symbolName, out var symbol)
                ? symbol.TickSize
                : _bot.Symbol.TickSize;
            return Math.Max(tick * 50.0, 1e-6);
        }

        private void SetState(HtfBiasSnapshot snap, HtfBiasState state, TradeDirection dir, string reason, double conf01)
        {
            snap.State = state;
            snap.AllowedDirection = dir;
            snap.Reason = reason;
            snap.Confidence01 = Clamp01(conf01);
        }


        private HtfBiasSnapshot CreateUnavailableSnapshot(string symbolName)
        {
            _bot.Print($"[RESOLVER][HTF_FAIL] symbol={symbolName} reason=unresolved_runtime_symbol");
            return new HtfBiasSnapshot
            {
                State = HtfBiasState.Neutral,
                AllowedDirection = TradeDirection.None,
                Confidence01 = 0.0,
                Reason = "HTF_UNAVAILABLE unresolved_runtime_symbol"
            };
        }
        private static double Clamp01(double value)
        {
            if (value < 0) return 0;
            if (value > 1) return 1;
            return value;
        }
    }
}
