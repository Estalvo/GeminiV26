using GeminiV26.Core.Entry;
using cAlgo.API;
using cAlgo.API.Indicators;
using System;
using System.Collections.Generic;

namespace GeminiV26.Core.HtfBias
{
    /// <summary>
    /// Crypto HTF bias engine.
    ///
    /// Design intent:
    /// - crypto-specific, momentum-first bias model
    /// - H4 structure, recalculated on closed H1 bars for stability + responsiveness
    /// - EMA alignment + slope remain the core directional features
    /// - ATR normalization improves structure / pullback / extension handling
    /// - healthy pullbacks keep directional bias instead of flipping too early
    /// </summary>
    public sealed class CryptoHtfBiasEngine : IHtfBiasProvider
    {
        private readonly Robot _bot;
        private readonly RuntimeSymbolResolver _runtimeSymbols;

        private static readonly TimeFrame BiasTf = TimeFrame.Hour4;
        private static readonly TimeFrame UpdateTf = TimeFrame.Hour;

        private const int EmaFast = 21;
        private const int EmaTrend = 50;
        private const int EmaAnchor = 200;
        private const int AtrPeriod = 14;
        private const int AdxPeriod = 14;
        private const int FastSlopeLookback = 2;
        private const int TrendSlopeLookback = 2;

        private const double MinAdxDirectional = 14.0;
        private const double HealthyAdx = 20.0;
        private const double StrongAdx = 28.0;

        private const double MinGapAtrDirectional = 0.10;
        private const double HealthyGapAtr = 0.22;
        private const double StrongGapAtr = 0.50;

        private const double MinSlopeAtrDirectional = 0.02;
        private const double HealthySlopeAtr = 0.09;
        private const double StrongSlopeAtr = 0.18;

        private const double HealthyPullbackAtr = 0.85;
        private const double DeepPullbackAtr = 1.35;
        private const double MaxDirectionalPullbackAtr = 1.90;
        private const double OverextendedAtr = 2.30;
        private const double SevereExtensionAtr = 3.10;

        private sealed class CryptoBiasContext
        {
            public Bars H4;
            public Bars H1;
            public ExponentialMovingAverage Ema21;
            public ExponentialMovingAverage Ema50;
            public ExponentialMovingAverage Ema200;
            public AverageTrueRange AtrH4;
            public DirectionalMovementSystem Dms;
            public HtfBiasSnapshot Snapshot = new();
        }

        private readonly Dictionary<string, CryptoBiasContext> _ctx = new(StringComparer.OrdinalIgnoreCase);

        public CryptoHtfBiasEngine(Robot bot)
        {
            _bot = bot ?? throw new ArgumentNullException(nameof(bot));
            _runtimeSymbols = new RuntimeSymbolResolver(_bot);
        }

        public HtfBiasSnapshot Get(string symbolName)
        {
            UpdateIfNeeded(symbolName);

            if (_ctx.TryGetValue(symbolName, out var c))
                return c.Snapshot;

            return CreateUnavailableSnapshot(symbolName);
        }

        private void UpdateIfNeeded(string symbolName)
        {
            if (!_ctx.TryGetValue(symbolName, out var c))
            {
                c = new CryptoBiasContext();
                _ctx[symbolName] = c;
                SetState(c.Snapshot, HtfBiasState.NotReady, TradeDirection.None, "CRYPTO_HTF NOT_READY INIT", 0.20);

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

                c.Ema21 = _bot.Indicators.ExponentialMovingAverage(c.H4.ClosePrices, EmaFast);
                c.Ema50 = _bot.Indicators.ExponentialMovingAverage(c.H4.ClosePrices, EmaTrend);
                c.Ema200 = _bot.Indicators.ExponentialMovingAverage(c.H4.ClosePrices, EmaAnchor);
                c.AtrH4 = _bot.Indicators.AverageTrueRange(c.H4, AtrPeriod, MovingAverageType.Exponential);
                c.Dms = _bot.Indicators.DirectionalMovementSystem(c.H4, AdxPeriod);

                _ctx[symbolName] = c;
            }

            if (c.H4 == null || c.H1 == null || c.Ema21 == null || c.Ema50 == null || c.Ema200 == null || c.AtrH4 == null || c.Dms == null)
            {
                SetState(c.Snapshot, HtfBiasState.NotReady, TradeDirection.None, "CRYPTO_HTF NOT_READY NULL_INDICATOR", 0.20);
                return;
            }

            if (c.H1.Count < 10 || c.H4.Count < EmaAnchor + 8)
            {
                SetState(c.Snapshot, HtfBiasState.NotReady, TradeDirection.None, "CRYPTO_HTF NOT_READY INSUFFICIENT_DATA", 0.20);
                return;
            }

            int h1Closed = c.H1.Count - 2;
            if (h1Closed < 1)
                return;

            DateTime closedH1Time = c.H1.OpenTimes[h1Closed];
            if (closedH1Time == c.Snapshot.LastUpdateH1Closed)
                return;

            c.Snapshot.LastUpdateH1Closed = closedH1Time;

            int i = c.H4.Count - 2;
            if (i < EmaAnchor + 4)
            {
                SetState(c.Snapshot, HtfBiasState.NotReady, TradeDirection.None, "CRYPTO_HTF NOT_READY WARMUP", 0.20);
                return;
            }

            EvaluateBias(c, i);
        }

        private void EvaluateBias(CryptoBiasContext c, int i)
        {
            double e21 = c.Ema21.Result[i];
            double e50 = c.Ema50.Result[i];
            double e200 = c.Ema200.Result[i];
            double close = c.H4.ClosePrices[i];

            double atr = c.AtrH4.Result[i];
            if (atr <= 0 || double.IsNaN(atr))
                atr = 1e-6;

            double adx = c.Dms.ADX[i];
            if (double.IsNaN(adx) || adx < 0)
                adx = 0;

            bool bullAlign = e50 > e200;
            bool bearAlign = e50 < e200;
            bool fastAboveTrend = e21 >= e50;
            bool fastBelowTrend = e21 <= e50;
            bool bullishPremise = bullAlign;
            bool bearishPremise = bearAlign;

            if (!bullishPremise && !bearishPremise)
            {
                SetState(
                    c.Snapshot,
                    HtfBiasState.Neutral,
                    TradeDirection.None,
                    $"CRYPTO_HTF NEUTRAL no trend alignment adx={adx:0.0}",
                    0.18);
                return;
            }

            double gapAtr = Math.Abs(e50 - e200) / atr;
            double signedDistFastAtr = (close - e21) / atr;
            double distFastAtr = Math.Abs(signedDistFastAtr);

            double slope21Atr = ComputeAtrSlope(c.Ema21.Result, i, FastSlopeLookback, atr);
            double slope50Atr = ComputeAtrSlope(c.Ema50.Result, i, TrendSlopeLookback, atr);

            bool slopeSupportsDirection = bullishPremise
                ? slope21Atr > -MinSlopeAtrDirectional && slope50Atr > 0
                : slope21Atr < MinSlopeAtrDirectional && slope50Atr < 0;

            bool fastSlopeStrong = bullishPremise
                ? slope21Atr >= HealthySlopeAtr
                : slope21Atr <= -HealthySlopeAtr;

            bool trendSlopeStrong = bullishPremise
                ? slope50Atr >= HealthySlopeAtr * 0.60
                : slope50Atr <= -HealthySlopeAtr * 0.60;

            bool adxDirectional = adx >= MinAdxDirectional;
            bool adxHealthy = adx >= HealthyAdx;

            bool pullback = bullishPremise ? signedDistFastAtr < 0 : signedDistFastAtr > 0;
            bool healthyPullback = pullback && distFastAtr <= HealthyPullbackAtr;
            bool deepPullback = pullback && distFastAtr > HealthyPullbackAtr && distFastAtr <= DeepPullbackAtr;
            bool stillDirectionalPullback = pullback && distFastAtr <= MaxDirectionalPullbackAtr;
            bool extended = !pullback && distFastAtr >= OverextendedAtr;
            bool severelyExtended = !pullback && distFastAtr >= SevereExtensionAtr;

            double gapScore = Clamp01((gapAtr - MinGapAtrDirectional) / (StrongGapAtr - MinGapAtrDirectional));
            double adxScore = Clamp01((adx - MinAdxDirectional) / (StrongAdx - MinAdxDirectional));
            double fastSlopeScore = bullishPremise
                ? Clamp01((slope21Atr - MinSlopeAtrDirectional) / (StrongSlopeAtr - MinSlopeAtrDirectional))
                : Clamp01(((-slope21Atr) - MinSlopeAtrDirectional) / (StrongSlopeAtr - MinSlopeAtrDirectional));
            double trendSlopeScore = bullishPremise
                ? Clamp01(Math.Max(0.0, slope50Atr) / StrongSlopeAtr)
                : Clamp01(Math.Max(0.0, -slope50Atr) / StrongSlopeAtr);

            double locationScore = pullback
                ? 1.0 - Clamp01(distFastAtr / MaxDirectionalPullbackAtr)
                : 1.0 - Clamp01((distFastAtr - 0.30) / SevereExtensionAtr);

            bool weakStructure = gapAtr < MinGapAtrDirectional;
            bool structureHealthy = gapAtr >= HealthyGapAtr;
            bool structureStrong = gapAtr >= StrongGapAtr;
            bool fastTrendAligned = bullishPremise ? fastAboveTrend : fastBelowTrend;
            bool slopeHealthy = trendSlopeStrong && (fastSlopeStrong || fastTrendAligned);
            bool strongTrend = structureStrong && adxHealthy && slopeHealthy;
            bool tradableStructure = gapAtr >= MinGapAtrDirectional && slopeSupportsDirection && fastTrendAligned;
            bool weakButTradable = tradableStructure && (adxDirectional || fastSlopeStrong || trendSlopeStrong);
            bool earlyTrend = tradableStructure && !adxHealthy && !structureHealthy;
            bool slopeLost = bullishPremise ? slope21Atr < -HealthySlopeAtr * 0.35 || slope50Atr <= 0 : slope21Atr > HealthySlopeAtr * 0.35 || slope50Atr >= 0;
            bool unclearDirection = weakStructure || slopeLost;

            HtfBiasState directionalState = bullishPremise ? HtfBiasState.Bull : HtfBiasState.Bear;
            TradeDirection direction = bullishPremise ? TradeDirection.Long : TradeDirection.Short;
            string stateLabel = bullishPremise ? "BULL" : "BEAR";

            if (unclearDirection && !stillDirectionalPullback)
            {
                double conf = Clamp01(0.22 + 0.10 * gapScore + 0.10 * adxScore + 0.10 * fastSlopeScore);
                string detail = weakStructure
                    ? "weak structure"
                    : "slope no longer confirming";

                SetState(
                    c.Snapshot,
                    HtfBiasState.Transition,
                    TradeDirection.None,
                    $"CRYPTO_HTF TRANSITION {detail}, adx={adx:0.0}, gapATR={gapAtr:0.00}, slope21ATR={slope21Atr:0.00}, slope50ATR={slope50Atr:0.00}, distFastATR={distFastAtr:0.00}",
                    conf);
                return;
            }

            if (!tradableStructure && !stillDirectionalPullback)
            {
                double conf = Clamp01(0.16 + 0.10 * adxScore + 0.08 * fastSlopeScore);
                SetState(
                    c.Snapshot,
                    HtfBiasState.Neutral,
                    TradeDirection.None,
                    $"CRYPTO_HTF NEUTRAL no usable directional premise, adx={adx:0.0}, gapATR={gapAtr:0.00}, slope21ATR={slope21Atr:0.00}",
                    conf);
                return;
            }

            double confidence = BuildDirectionalConfidence(gapScore, adxScore, fastSlopeScore, trendSlopeScore, locationScore);
            confidence = ApplyContextAdjustments(confidence, healthyPullback, deepPullback, extended, severelyExtended, strongTrend, earlyTrend, adxDirectional, structureHealthy);

            if (strongTrend)
            {
                string trendReason;
                if (severelyExtended || extended)
                {
                    trendReason = $"CRYPTO_HTF {stateLabel} extended, reduced confidence, conf={confidence:0.00}, adx={adx:0.0}, gapATR={gapAtr:0.00}, slope21ATR={slope21Atr:0.00}, slope50ATR={slope50Atr:0.00}, distFastATR={distFastAtr:0.00}";
                }
                else if (healthyPullback || deepPullback)
                {
                    trendReason = $"CRYPTO_HTF {stateLabel} healthy pullback continuation, reduced confidence, conf={confidence:0.00}, adx={adx:0.0}, gapATR={gapAtr:0.00}, slope21ATR={slope21Atr:0.00}, slope50ATR={slope50Atr:0.00}, distFastATR={distFastAtr:0.00}";
                }
                else
                {
                    trendReason = $"CRYPTO_HTF {stateLabel} strong momentum continuation, conf={confidence:0.00}, adx={adx:0.0}, gapATR={gapAtr:0.00}, slope21ATR={slope21Atr:0.00}, slope50ATR={slope50Atr:0.00}, distFastATR={distFastAtr:0.00}";
                }

                SetState(c.Snapshot, directionalState, direction, trendReason, Math.Max(0.70, confidence));
                return;
            }

            if (stillDirectionalPullback && slopeSupportsDirection && tradableStructure)
            {
                double pullbackConf = confidence;
                if (deepPullback)
                    pullbackConf = Math.Min(pullbackConf, 0.58);
                else if (healthyPullback)
                    pullbackConf = Math.Min(Math.Max(pullbackConf, 0.46), 0.68);

                string pullbackReason = structureHealthy
                    ? $"CRYPTO_HTF {stateLabel} healthy pullback, reduced confidence, conf={pullbackConf:0.00}, adx={adx:0.0}, gapATR={gapAtr:0.00}, slope21ATR={slope21Atr:0.00}, slope50ATR={slope50Atr:0.00}, distFastATR={distFastAtr:0.00}"
                    : $"CRYPTO_HTF {stateLabel} pullback intact, weak structure but tradable, conf={pullbackConf:0.00}, adx={adx:0.0}, gapATR={gapAtr:0.00}, slope21ATR={slope21Atr:0.00}, slope50ATR={slope50Atr:0.00}, distFastATR={distFastAtr:0.00}";

                SetState(c.Snapshot, directionalState, direction, pullbackReason, pullbackConf);
                return;
            }

            if (extended || severelyExtended)
            {
                double extendedConf = Math.Min(confidence, structureStrong ? 0.72 : 0.58);
                extendedConf = Math.Max(extendedConf, structureStrong ? 0.45 : 0.32);
                SetState(
                    c.Snapshot,
                    directionalState,
                    direction,
                    $"CRYPTO_HTF {stateLabel} extended, reduced confidence, conf={extendedConf:0.00}, adx={adx:0.0}, gapATR={gapAtr:0.00}, slope21ATR={slope21Atr:0.00}, slope50ATR={slope50Atr:0.00}, distFastATR={distFastAtr:0.00}",
                    extendedConf);
                return;
            }

            if (earlyTrend)
            {
                double earlyConf = Clamp01(Math.Min(Math.Max(confidence, 0.32), 0.54));
                SetState(
                    c.Snapshot,
                    directionalState,
                    direction,
                    $"CRYPTO_HTF {stateLabel} early trend formation, low confidence, conf={earlyConf:0.00}, adx={adx:0.0}, gapATR={gapAtr:0.00}, slope21ATR={slope21Atr:0.00}, slope50ATR={slope50Atr:0.00}, distFastATR={distFastAtr:0.00}",
                    earlyConf);
                return;
            }

            if (weakButTradable)
            {
                double weakConf = Math.Min(Math.Max(confidence, 0.34), 0.62);
                string weakReason = structureHealthy
                    ? $"CRYPTO_HTF {stateLabel} normal trend continuation, conf={weakConf:0.00}, adx={adx:0.0}, gapATR={gapAtr:0.00}, slope21ATR={slope21Atr:0.00}, slope50ATR={slope50Atr:0.00}, distFastATR={distFastAtr:0.00}"
                    : $"CRYPTO_HTF {stateLabel} weak structure, tradable, conf={weakConf:0.00}, adx={adx:0.0}, gapATR={gapAtr:0.00}, slope21ATR={slope21Atr:0.00}, slope50ATR={slope50Atr:0.00}, distFastATR={distFastAtr:0.00}";

                SetState(c.Snapshot, directionalState, direction, weakReason, weakConf);
                return;
            }

            if (adxDirectional && slopeSupportsDirection)
            {
                double preservedConf = Clamp01(Math.Min(Math.Max(confidence, 0.30), 0.52));
                SetState(
                    c.Snapshot,
                    directionalState,
                    direction,
                    $"CRYPTO_HTF {stateLabel} low conviction but directional bias preserved, conf={preservedConf:0.00}, adx={adx:0.0}, gapATR={gapAtr:0.00}, slope21ATR={slope21Atr:0.00}, slope50ATR={slope50Atr:0.00}, distFastATR={distFastAtr:0.00}",
                    preservedConf);
                return;
            }

            double transitionConf = Clamp01(0.24 + 0.08 * gapScore + 0.06 * adxScore + 0.08 * fastSlopeScore);
            SetState(
                c.Snapshot,
                HtfBiasState.Transition,
                TradeDirection.None,
                $"CRYPTO_HTF TRANSITION mixed signals, adx={adx:0.0}, gapATR={gapAtr:0.00}, slope21ATR={slope21Atr:0.00}, slope50ATR={slope50Atr:0.00}, distFastATR={distFastAtr:0.00}",
                transitionConf);
        }

        private double BuildDirectionalConfidence(double gapScore, double adxScore, double fastSlopeScore, double trendSlopeScore, double locationScore)
        {
            return Clamp01(
                0.28 +
                0.20 * fastSlopeScore +
                0.17 * trendSlopeScore +
                0.17 * adxScore +
                0.12 * gapScore +
                0.06 * locationScore);
        }

        private double ApplyContextAdjustments(
            double confidence,
            bool healthyPullback,
            bool deepPullback,
            bool extended,
            bool severelyExtended,
            bool strongTrend,
            bool earlyTrend,
            bool adxDirectional,
            bool structureHealthy)
        {
            double adjusted = confidence;

            if (healthyPullback)
                adjusted -= 0.06;

            if (deepPullback)
                adjusted -= 0.12;

            if (extended)
                adjusted -= 0.12;

            if (severelyExtended)
                adjusted -= 0.18;

            if (strongTrend)
                adjusted += 0.08;

            if (earlyTrend)
                adjusted -= 0.04;

            if (!adxDirectional)
                adjusted -= 0.06;

            if (!structureHealthy)
                adjusted -= 0.04;

            return Clamp01(adjusted);
        }

        private static double ComputeAtrSlope(DataSeries series, int i, int lookback, double atr)
        {
            int j = i - lookback;
            if (j < 0 || atr <= 0 || double.IsNaN(atr))
                return 0.0;

            double now = series[i];
            double prev = series[j];
            if (double.IsNaN(now) || double.IsNaN(prev))
                return 0.0;

            return (now - prev) / atr;
        }

        private void SetState(HtfBiasSnapshot snapshot, HtfBiasState state, TradeDirection direction, string reason, double confidence01)
        {
            snapshot.State = state;
            snapshot.AllowedDirection = direction;
            snapshot.Reason = reason;
            snapshot.Confidence01 = Clamp01(confidence01);
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
        private static double Clamp01(double value)
        {
            if (value < 0.0)
                return 0.0;
            if (value > 1.0)
                return 1.0;
            return value;
        }
    }
}
