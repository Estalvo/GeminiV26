using GeminiV26.Core.Entry;
using cAlgo.API;
using cAlgo.API.Indicators;
using System;
using System.Collections.Generic;

namespace GeminiV26.Core.HtfBias
{
    /// <summary>
    /// FX HTF Bias Engine.
    ///
    /// Design intent:
    /// - conservative, structure-first FX bias model
    /// - H4 structure, recalculated on closed H1 bars
    /// - EMA50/EMA200 alignment defines directional premise
    /// - pullbacks stay directional when macro structure is still healthy
    /// - Transition is reserved for weak / unclear / early structure only
    /// </summary>
    public sealed class FxHtfBiasEngine : IHtfBiasProvider
    {
        private readonly Robot _bot;
        private readonly RuntimeSymbolResolver _runtimeSymbols;

        private static readonly TimeFrame BiasTf = TimeFrame.Hour4;
        private static readonly TimeFrame UpdateTf = TimeFrame.Hour;

        private const int EmaFast = 50;
        private const int EmaSlow = 200;
        private const int AtrPeriod = 14;
        private const int AdxPeriod = 14;
        private const int SlopeLookback = 3;

        private const double MinGapAtrDirectional = 0.10;
        private const double HealthyGapAtr = 0.18;
        private const double StrongGapAtr = 0.32;

        private const double MinAdxDirectional = 12.0;
        private const double HealthyAdx = 16.0;
        private const double StrongAdx = 24.0;

        private const double MildPullbackAtr = 0.35;
        private const double HealthyPullbackAtr = 0.90;
        private const double DeepPullbackAtr = 1.35;
        private const double MaxDistFromFastAtr = 2.60;
        private const double StrongSlopeAtr = 0.18;

        private const double MinTrendConfidence = 0.35;
        private const double BaseTrendConfidence = 0.42;
        private const double StrongTrendFloor = 0.70;
        private const double TransitionBaseConfidence = 0.32;

        private sealed class FxBiasContext
        {
            public Bars H4;
            public Bars H1;
            public ExponentialMovingAverage Ema50;
            public ExponentialMovingAverage Ema200;
            public AverageTrueRange AtrH4;
            public DirectionalMovementSystem Dms;
            public HtfBiasSnapshot Snapshot = new();
        }

        private readonly Dictionary<string, FxBiasContext> _ctx = new(StringComparer.OrdinalIgnoreCase);

        public FxHtfBiasEngine(Robot bot)
        {
            _bot = bot ?? throw new ArgumentNullException(nameof(bot));
            _runtimeSymbols = new RuntimeSymbolResolver(_bot);
        }

        public HtfBiasSnapshot Get(string symbolName)
        {
            UpdateIfNeeded(symbolName);
            return _ctx.TryGetValue(symbolName, out var context)
                ? context.Snapshot
                : CreateUnavailableSnapshot(symbolName);
        }

        private void UpdateIfNeeded(string symbolName)
        {
            if (!_ctx.TryGetValue(symbolName, out var c))
            {
                c = new FxBiasContext();
                _ctx[symbolName] = c;
                SetState(c.Snapshot, HtfBiasState.NotReady, TradeDirection.None, "FX_HTF NOT_READY INIT", 0.20);

                if (!_runtimeSymbols.TryGetBars(BiasTf, symbolName, out c.H4) ||
                    !_runtimeSymbols.TryGetBars(UpdateTf, symbolName, out c.H1))
                {
                    var unavailable = CreateUnavailableSnapshot(symbolName);
                    c.Snapshot.State = unavailable.State;
                    c.Snapshot.AllowedDirection = unavailable.AllowedDirection;
                    c.Snapshot.Confidence01 = unavailable.Confidence01;
                    c.Snapshot.Reason = unavailable.Reason;
                    return;
                }

                if (c.H4.Count < EmaSlow + 8 || c.H1.Count < 10)
                {
                    SetState(c.Snapshot, HtfBiasState.NotReady, TradeDirection.None, "FX_HTF NOT_READY INSUFFICIENT_DATA", 0.20);
                    return;
                }

                c.Ema50 = _bot.Indicators.ExponentialMovingAverage(c.H4.ClosePrices, EmaFast);
                c.Ema200 = _bot.Indicators.ExponentialMovingAverage(c.H4.ClosePrices, EmaSlow);
                c.AtrH4 = _bot.Indicators.AverageTrueRange(c.H4, AtrPeriod, MovingAverageType.Exponential);
                c.Dms = _bot.Indicators.DirectionalMovementSystem(c.H4, AdxPeriod);

                _ctx[symbolName] = c;
            }

            if (c.H1 == null || c.H4 == null || c.Ema50 == null || c.Ema200 == null || c.AtrH4 == null || c.Dms == null)
            {
                SetState(c.Snapshot, HtfBiasState.NotReady, TradeDirection.None, "FX_HTF NOT_READY NULL_INDICATOR", 0.20);
                return;
            }

            if (c.H1.Count < 10 || c.H4.Count < EmaSlow + 8)
            {
                SetState(c.Snapshot, HtfBiasState.NotReady, TradeDirection.None, "FX_HTF NOT_READY INSUFFICIENT_DATA", 0.20);
                return;
            }

            int h1Closed = c.H1.Count - 2;
            if (h1Closed < 1)
                return;

            DateTime tH1 = c.H1.OpenTimes[h1Closed];
            if (tH1 == c.Snapshot.LastUpdateH1Closed)
                return;

            c.Snapshot.LastUpdateH1Closed = tH1;

            int i = c.H4.Count - 2;
            if (i < EmaSlow + 4)
                return;

            EvaluateBias(c, i);
        }

        private void EvaluateBias(FxBiasContext c, int i)
        {
            double e50 = c.Ema50.Result[i];
            double e200 = c.Ema200.Result[i];
            double close = c.H4.ClosePrices[i];

            double atr = c.AtrH4.Result[i];
            if (atr <= 0 || double.IsNaN(atr))
                atr = Math.Max(_bot.Symbol.TickSize * 10, 1e-6);

            double adx = c.Dms.ADX[i];
            if (double.IsNaN(adx) || adx < 0)
                adx = 0;

            bool bullAlign = e50 > e200;
            bool bearAlign = e50 < e200;

            if (!bullAlign && !bearAlign)
            {
                SetState(c.Snapshot, HtfBiasState.Neutral, TradeDirection.None, "FX_HTF NEUTRAL no alignment", 0.45);
                return;
            }

            double gapAtr = Math.Abs(e50 - e200) / atr;
            double signedDistFastAtr = (close - e50) / atr;
            double distFastAtr = Math.Abs(signedDistFastAtr);
            double slope50Atr = ComputeAtrSlope(c.Ema50.Result, i, SlopeLookback, atr);

            bool bullishPremise = bullAlign;
            bool pullback = bullishPremise ? signedDistFastAtr < 0 : signedDistFastAtr > 0;
            bool mildPullback = pullback && distFastAtr <= MildPullbackAtr;
            bool healthyPullback = pullback && distFastAtr <= HealthyPullbackAtr;
            bool deepPullback = pullback && distFastAtr > HealthyPullbackAtr && distFastAtr <= DeepPullbackAtr;
            bool excessiveDislocation = distFastAtr > DeepPullbackAtr;

            double gapScore = Clamp01((gapAtr - MinGapAtrDirectional) / (StrongGapAtr - MinGapAtrDirectional));
            double adxScore = Clamp01((adx - MinAdxDirectional) / (StrongAdx - MinAdxDirectional));
            double slopeScore = bullishPremise
                ? Clamp01(slope50Atr / StrongSlopeAtr)
                : Clamp01((-slope50Atr) / StrongSlopeAtr);

            double locationScore = 1.0 - Clamp01(distFastAtr / MaxDistFromFastAtr);
            double cleanlinessScore = BuildCleanlinessScore(gapScore, adxScore, slopeScore, locationScore, mildPullback, healthyPullback, deepPullback);

            bool weakStructure = gapAtr < MinGapAtrDirectional;
            bool strongStructure = gapAtr >= StrongGapAtr;
            bool adxAcceptable = adx >= MinAdxDirectional;
            bool adxHealthy = adx >= HealthyAdx;
            bool slopeSupportsDirection = bullishPremise ? slope50Atr > 0 : slope50Atr < 0;
            bool directionClear = (bullAlign || bearAlign) && slopeSupportsDirection;
            bool structureTradable = gapAtr >= MinGapAtrDirectional && adxAcceptable;
            bool strongTrend = strongStructure && adxHealthy;
            bool weakTrendButTradable = structureTradable && !strongTrend;
            bool earlyTrendFormation = gapAtr < HealthyGapAtr && adx < HealthyAdx && slopeScore < 0.35;

            if ((weakStructure && !directionClear) || (!adxAcceptable && !slopeSupportsDirection))
            {
                double conf = Clamp01(TransitionBaseConfidence + 0.10 * gapScore + 0.08 * adxScore + 0.05 * slopeScore);
                string weakReason = weakStructure ? "weak structure, direction unclear" : "low ADX, slope not confirming";
                SetState(
                    c.Snapshot,
                    HtfBiasState.Transition,
                    TradeDirection.None,
                    $"FX_HTF TRANSITION {weakReason}, gapATR={gapAtr:0.00}, adx={adx:0.0}, slopeATR={slope50Atr:0.00}, distATR={distFastAtr:0.00}",
                    conf);
                return;
            }

            double trendConfidence = BuildTrendConfidence(gapScore, adxScore, slopeScore, locationScore, cleanlinessScore, strongTrend, healthyPullback, deepPullback);
            HtfBiasState state = bullishPremise ? HtfBiasState.Bull : HtfBiasState.Bear;
            TradeDirection direction = bullishPremise ? TradeDirection.Long : TradeDirection.Short;

            if (strongTrend)
            {
                double strongConf = excessiveDislocation
                    ? Clamp01(Math.Max(0.42, trendConfidence * 0.60))
                    : trendConfidence;
                string reason = excessiveDislocation
                    ? $"FX_HTF {(bullishPremise ? "BULL" : "BEAR")} extended, reduced confidence, conf={strongConf:0.00}, gapATR={gapAtr:0.00}, adx={adx:0.0}, distATR={distFastAtr:0.00}"
                    : healthyPullback
                        ? $"FX_HTF {(bullishPremise ? "BULL" : "BEAR")} strong structure, healthy pullback, conf={strongConf:0.00}, gapATR={gapAtr:0.00}, adx={adx:0.0}, distATR={distFastAtr:0.00}"
                        : $"FX_HTF {(bullishPremise ? "BULL" : "BEAR")} strong structure, conf={strongConf:0.00}, gapATR={gapAtr:0.00}, adx={adx:0.0}, distATR={distFastAtr:0.00}";

                SetState(c.Snapshot, state, direction, reason, strongConf);
                return;
            }

            if (earlyTrendFormation)
            {
                double earlyConf = Clamp01(0.30 + 0.05 * gapScore + 0.03 * adxScore + 0.04 * slopeScore);
                earlyConf = Math.Min(earlyConf, 0.40);
                string reason = pullback
                    ? $"FX_HTF {(bullishPremise ? "BULL" : "BEAR")} early trend, low confidence, conf={earlyConf:0.00}, gapATR={gapAtr:0.00}, adx={adx:0.0}, slopeATR={slope50Atr:0.00}"
                    : $"FX_HTF {(bullishPremise ? "BULL" : "BEAR")} early trend formation, low confidence, conf={earlyConf:0.00}, gapATR={gapAtr:0.00}, adx={adx:0.0}, slopeATR={slope50Atr:0.00}";

                SetState(c.Snapshot, state, direction, reason, earlyConf);
                return;
            }

            if (weakTrendButTradable)
            {
                double weakConf = Math.Max(MinTrendConfidence, Math.Min(trendConfidence, 0.60));
                if (excessiveDislocation)
                    weakConf = Clamp01(Math.Max(0.30, weakConf * 0.60));

                string reason;

                if (excessiveDislocation)
                {
                    reason = $"FX_HTF {(bullishPremise ? "BULL" : "BEAR")} extended, reduced confidence, conf={weakConf:0.00}, gapATR={gapAtr:0.00}, adx={adx:0.0}, distATR={distFastAtr:0.00}";
                }
                else if (healthyPullback || deepPullback)
                {
                    reason = $"FX_HTF {(bullishPremise ? "BULL" : "BEAR")} weak structure, pullback intact, conf={weakConf:0.00}, gapATR={gapAtr:0.00}, adx={adx:0.0}, distATR={distFastAtr:0.00}";
                }
                else
                {
                    reason = $"FX_HTF {(bullishPremise ? "BULL" : "BEAR")} weak structure, low confidence, conf={weakConf:0.00}, gapATR={gapAtr:0.00}, adx={adx:0.0}, distATR={distFastAtr:0.00}";
                }

                SetState(c.Snapshot, state, direction, reason, weakConf);
                return;
            }

            if (!adxAcceptable)
            {
                double lowAdxConf = Clamp01(0.30 + 0.08 * gapScore + 0.06 * slopeScore + 0.04 * locationScore);
                lowAdxConf = Math.Min(lowAdxConf, 0.45);
                SetState(
                    c.Snapshot,
                    state,
                    direction,
                    $"FX_HTF {(bullishPremise ? "BULL" : "BEAR")} low ADX, directional bias preserved, conf={lowAdxConf:0.00}, gapATR={gapAtr:0.00}, adx={adx:0.0}, slopeATR={slope50Atr:0.00}",
                    lowAdxConf);
                return;
            }

            if (weakStructure)
            {
                double weakStructureConf = Clamp01(0.32 + 0.06 * adxScore + 0.08 * slopeScore + 0.03 * locationScore);
                weakStructureConf = Math.Min(weakStructureConf, 0.48);
                string reason = pullback
                    ? $"FX_HTF {(bullishPremise ? "BULL" : "BEAR")} weak structure, tradable pullback, conf={weakStructureConf:0.00}, gapATR={gapAtr:0.00}, adx={adx:0.0}, distATR={distFastAtr:0.00}"
                    : $"FX_HTF {(bullishPremise ? "BULL" : "BEAR")} weak structure, tradable, conf={weakStructureConf:0.00}, gapATR={gapAtr:0.00}, adx={adx:0.0}, distATR={distFastAtr:0.00}";

                SetState(c.Snapshot, state, direction, reason, weakStructureConf);
                return;
            }

            if (excessiveDislocation)
            {
                double extendedConf = Clamp01(Math.Max(0.30, trendConfidence * 0.60));
                string reason = $"FX_HTF {(bullishPremise ? "BULL" : "BEAR")} extended, reduced confidence, conf={extendedConf:0.00}, gapATR={gapAtr:0.00}, adx={adx:0.0}, distATR={distFastAtr:0.00}";
                SetState(c.Snapshot, state, direction, reason, extendedConf);
                return;
            }

            double fallbackConf = Clamp01(TransitionBaseConfidence + 0.08 * gapScore + 0.08 * adxScore + 0.05 * slopeScore);
            SetState(
                c.Snapshot,
                HtfBiasState.Transition,
                TradeDirection.None,
                $"FX_HTF TRANSITION conflicting signals, gapATR={gapAtr:0.00}, adx={adx:0.0}, slopeATR={slope50Atr:0.00}, distATR={distFastAtr:0.00}",
                fallbackConf);
        }

        private static double BuildCleanlinessScore(
            double gapScore,
            double adxScore,
            double slopeScore,
            double locationScore,
            bool mildPullback,
            bool healthyPullback,
            bool deepPullback)
        {
            double pullbackPenalty = deepPullback ? 0.22 : (healthyPullback ? 0.10 : 0.0);
            double mildPullbackBoost = mildPullback ? 0.04 : 0.0;

            return Clamp01(
                0.34 * gapScore +
                0.24 * adxScore +
                0.20 * slopeScore +
                0.22 * locationScore +
                mildPullbackBoost -
                pullbackPenalty);
        }

        private static double BuildTrendConfidence(
            double gapScore,
            double adxScore,
            double slopeScore,
            double locationScore,
            double cleanlinessScore,
            bool strongTrend,
            bool healthyPullback,
            bool deepPullback)
        {
            double conf = BaseTrendConfidence
                + 0.16 * gapScore
                + 0.13 * adxScore
                + 0.09 * slopeScore
                + 0.10 * locationScore
                + 0.10 * cleanlinessScore;

            if (healthyPullback)
                conf -= 0.06;

            if (deepPullback)
                conf -= 0.12;

            if (strongTrend)
                conf = Math.Max(conf, StrongTrendFloor + 0.08 * cleanlinessScore + 0.06 * locationScore);

            return Clamp01(conf);
        }

        private static double ComputeAtrSlope(IndicatorDataSeries series, int index, int lookback, double atr)
        {
            if (series == null || atr <= 0 || index - lookback < 0)
                return 0;

            return (series[index] - series[index - lookback]) / atr;
        }

        private void SetState(HtfBiasSnapshot snap, HtfBiasState st, TradeDirection dir, string reason, double conf01)
        {
            snap.State = st;
            snap.AllowedDirection = dir;
            snap.Reason = reason;
            snap.Confidence01 = Clamp01(conf01);
        }


        private HtfBiasSnapshot CreateUnavailableSnapshot(string symbolName)
        {
            GlobalLogger.Log($"[RESOLVER][HTF_FAIL] symbol={symbolName} reason=unresolved_runtime_symbol");
            return new HtfBiasSnapshot
            {
                State = HtfBiasState.NotReady,
                AllowedDirection = TradeDirection.None,
                Confidence01 = 0.20,
                Reason = "HTF_UNAVAILABLE unresolved_runtime_symbol"
            };
        }
        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
    }
}
