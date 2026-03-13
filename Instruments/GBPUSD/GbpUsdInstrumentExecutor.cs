using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Instruments.FX;
using cAlgo.API.Indicators;
using GeminiV26.Core.Risk.PositionSizing;

namespace GeminiV26.Instruments.GBPUSD
{
    public class GbpUsdInstrumentExecutor
    {
        private readonly Robot _bot;
        private readonly GbpUsdInstrumentRiskSizer _riskSizer;
        private readonly GbpUsdExitManager _exitManager;
        private readonly FxMarketStateDetector _marketStateDetector;
        private readonly Dictionary<long, PositionContext> _positionContexts;
        private readonly string _botLabel;
        private readonly GbpUsdEntryLogic _entryLogic;
        private AverageTrueRange _atr14;

        // =========================
        // HARD GUARDS (Phase 3.7.1 hotfix)
        // =========================
        private const double MinAtrPips = 5.5; // under this: NO TRADE (kills micro-FX)
        private const double MinSlPips  = 8.0; // floor SL so TP1 isn't 2-3 pips

        public GbpUsdInstrumentExecutor(
            Robot bot,
            GbpUsdEntryLogic entryLogic,
            GbpUsdInstrumentRiskSizer riskSizer,
            GbpUsdExitManager exitManager,
            FxMarketStateDetector marketStateDetector,
            Dictionary<long, PositionContext> positionContexts,
            string botLabel)
        {
            _bot = bot;
            _entryLogic = entryLogic;
            _riskSizer = riskSizer;
            _exitManager = exitManager;
            _marketStateDetector = marketStateDetector;
            _positionContexts = positionContexts;
            _botLabel = botLabel;

            _atr14 = _bot.Indicators.AverageTrueRange(14, MovingAverageType.Exponential);
        }

        public void ExecuteEntry(EntryEvaluation entry)
        {
            // =====================================================
            // FX MARKET STATE – SOFT GATE (GBPUSD / FX)
            // =====================================================
            int statePenalty = 0;

            if (_marketStateDetector == null)
            {
                _bot.Print("[GBP EXEC] WARN: MarketStateDetector NULL");
            }
            else
            {
                var ms = _marketStateDetector.Evaluate();

                if (ms == null)
                {
                    _bot.Print("[GBP EXEC] WARN: MarketState NULL");
                }
                else
                {
                    if (ms.IsLowVol)
                    {
                        statePenalty -= 10;
                        _bot.Print("[GBP EXEC] MarketState: LowVol → penalty -10");
                    }

                    if (!ms.IsTrend)
                    {
                        statePenalty -= 10;
                        _bot.Print("[GBP EXEC] MarketState: NoTrend → penalty -10");
                    }
                }
            }

            // =====================================================
            // ENTRY LOGIC – PRE-EXEC CONFIDENCE (GBPUSD)
            // =====================================================
            _entryLogic.Evaluate();
            int logicConfidence = _entryLogic.LastLogicConfidence;

            int finalConfidence = PositionContext.ComputeFinalConfidenceValue(entry.Score, logicConfidence);
            int riskConfidence = PositionContext.ClampRiskConfidence(finalConfidence + statePenalty);

            // =====================================================
            // HARD ATR GATE (GBPUSD) – NO MICRO VOL
            // =====================================================
            double atr = _atr14.Result.LastValue;
            if (atr <= 0)
            {
                _bot.Print("[GBP EXEC] ATR_NOT_READY");
                return;
            }

            double atrPips = atr / _bot.Symbol.PipSize;
            if (atrPips < MinAtrPips)
            {
                _bot.Print($"[GBP EXEC] ATR_GATE block atrPips={atrPips:F2} < {MinAtrPips:F2}");
                return;
            }

            // =====================================================
            // EXECUTION LOGIC
            // =====================================================
            var tradeType =
                entry.Direction == TradeDirection.Long
                    ? TradeType.Buy
                    : TradeType.Sell;

            double riskPercent = _riskSizer.GetRiskPercent(riskConfidence);
            if (riskPercent <= 0)
                return;

            double slPriceDist = CalculateStopLossPriceDistance(riskConfidence, entry.Type);
            if (slPriceDist <= 0)
                return;

            // =====================================================
            // HARD SL FLOOR (GBPUSD) – keep TP/BE/trailing consistent
            // =====================================================
            double slPipsRaw = slPriceDist / _bot.Symbol.PipSize;
            if (slPipsRaw < MinSlPips)
            {
                _bot.Print($"[GBP EXEC] SL_FLOOR applied slPips {slPipsRaw:F1} -> {MinSlPips:F1}");
                slPriceDist = MinSlPips * _bot.Symbol.PipSize;
            }

            _riskSizer.GetTakeProfit(
                riskConfidence,
                out double tp1R,
                out double tp1Ratio,
                out double tp2R,
                out double tp2Ratio);

            // sizing MUST use the same slPriceDist we will actually place
            long volumeUnits = CalculateVolumeInUnits(riskPercent, slPriceDist, riskConfidence);
            if (volumeUnits <= 0)
                return;

            double slPips = slPriceDist / _bot.Symbol.PipSize;
            if (slPips <= 0)
                return;

            double entryPrice =
                tradeType == TradeType.Buy ? _bot.Symbol.Ask : _bot.Symbol.Bid;

            double tp2Price =
                tradeType == TradeType.Buy
                    ? entryPrice + slPriceDist * tp2R
                    : entryPrice - slPriceDist * tp2R;

            double tp2Pips = Math.Abs(tp2Price - entryPrice) / _bot.Symbol.PipSize;
            if (tp2Pips <= 0)
                return;

            var result = _bot.ExecuteMarketOrder(
                tradeType,
                _bot.SymbolName,
                volumeUnits,
                _botLabel,
                slPips,
                tp2Pips);

            if (!result.IsSuccessful || result.Position == null)
                return;

            long positionKey = result.Position.Id;

            var ctx = new PositionContext
            {
                PositionId = positionKey,
                Symbol = result.Position.SymbolName,
                EntryType = entry.Type.ToString(),
                EntryReason = entry.Reason,
                EntryScore = entry.Score,
                LogicConfidence = logicConfidence,
                EntryTime = _bot.Server.Time,
                EntryPrice = result.Position.EntryPrice,

                RiskPriceDistance = slPriceDist, // now consistent with sizing & SL & TP

                Tp1R = tp1R,
                Tp1Hit = false,
                Tp1CloseFraction = tp1Ratio,
                MarketTrend = entry.Direction != TradeDirection.None,

                BeMode = BeMode.AfterTp1,

                TrailingMode =
                    riskConfidence >= 85 ? TrailingMode.Loose :
                    riskConfidence >= 75 ? TrailingMode.Normal :
                                                TrailingMode.Tight
            };

            ctx.ComputeFinalConfidence();

            _positionContexts[positionKey] = ctx;
            _exitManager.RegisterContext(ctx);
        }

        private double CalculateStopLossPriceDistance(int score, EntryType entryType)
        {
            double atr = _atr14.Result.LastValue;
            if (atr <= 0)
                return 0;

            double atrMult = _riskSizer.GetStopLossAtrMultiplier(score, entryType);
            return atr * atrMult;
        }

        private long CalculateVolumeInUnits(double riskPercent, double slPriceDist, int score)
        {
            return FxPositionSizer.Calculate(
                _bot,
                riskPercent,
                slPriceDist,
                _riskSizer.GetLotCap(score));
        }
    }
}
