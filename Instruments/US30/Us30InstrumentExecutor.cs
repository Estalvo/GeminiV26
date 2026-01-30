using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Instruments.INDEX;

namespace GeminiV26.Instruments.US30
{
    public class Us30InstrumentExecutor
    {
        private readonly Robot _bot;
        private readonly Us30InstrumentRiskSizer _riskSizer;
        private readonly Us30ExitManager _exitManager;
        private readonly Dictionary<long, PositionContext> _positionContexts;
        private readonly string _botLabel;
        private readonly Us30EntryLogic _entryLogic;
        private readonly IndexMarketStateDetector _marketStateDetector;

        public Us30InstrumentExecutor(
            Robot bot,
            Us30EntryLogic entryLogic,
            Us30InstrumentRiskSizer riskSizer,
            Us30ExitManager exitManager,
            IndexMarketStateDetector marketStateDetector,
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
        }

        public void ExecuteEntry(EntryEvaluation entry)
        {
            if (_marketStateDetector != null)
            {
                var ms = _marketStateDetector.Evaluate();
                if (ms != null && ms.IsLowVol)
                    _bot.Print("[US30][STATE] LOWVOL");
            }

            // =========================
            // ENTRY LOGIC – US30
            // =========================
            _entryLogic.Evaluate();
            int logicConfidence = _entryLogic.LastLogicConfidence;

            // =========================
            // MARKET STATE – SOFT
            // =========================
            int statePenalty = 0;

            if (_marketStateDetector != null)
            {
                var ms = _marketStateDetector.Evaluate();
                if (ms != null)
                {
                    if (ms.IsLowVol)
                        statePenalty -= 15;   // US30 érzékenyebb, mint NAS
                    if (ms.IsTrend)
                        statePenalty += 5;
                }
            }

            int tempFinalConfidence =
                Math.Max(0, Math.Min(100, entry.Score + logicConfidence + statePenalty));

            var tradeType =
                entry.Direction == TradeDirection.Long
                    ? TradeType.Buy
                    : TradeType.Sell;

            double riskPercent = _riskSizer.GetRiskPercent(tempFinalConfidence);

            if (riskPercent <= 0)
                return;

            double slPriceDist = CalculateStopLossPriceDistance(tempFinalConfidence, entry.Type);

            if (slPriceDist <= 0)
                return;

            _riskSizer.GetTakeProfit(
                entry.Score,
                out double tp1R,
                out double tp1Ratio,
                out double tp2R,
                out double tp2Ratio);

            long volumeUnits = CalculateVolumeInUnits(riskPercent, slPriceDist, tempFinalConfidence);

            if (volumeUnits <= 0)
                return;

            double slPips = slPriceDist / _bot.Symbol.PipSize;
            if (slPips <= 0)
                return;

            double entryPrice =
                tradeType == TradeType.Buy
                    ? _bot.Symbol.Ask
                    : _bot.Symbol.Bid;

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

            long positionKey = Convert.ToInt64(result.Position.Id);

            var ctx = new PositionContext
            {
                PositionId = positionKey,
                Symbol = result.Position.SymbolName,
                EntryType = entry.Type.ToString(),
                EntryReason = entry.Reason,
                EntryTime = _bot.Server.Time,
                EntryPrice = result.Position.EntryPrice,

                Tp1Hit = false,
                Tp1CloseFraction = tp1Ratio,

                BeMode = BeMode.AfterTp1,

                TrailingMode =
                    tempFinalConfidence >= 85 ? TrailingMode.Loose :
                    tempFinalConfidence >= 75 ? TrailingMode.Normal :
                                                TrailingMode.Tight,

            };

            _positionContexts[positionKey] = ctx;
            _exitManager.RegisterContext(ctx);
        }

        private double CalculateStopLossPriceDistance(int score, EntryType entryType)
        {
            var atrInd = _bot.Indicators.AverageTrueRange(14, MovingAverageType.Exponential);

            double atr = atrInd.Result.LastValue;
            if (atr <= 0)
                return 0;

            double atrMult = _riskSizer.GetStopLossAtrMultiplier(score, entryType);
            return atr * atrMult;
        }

        private long CalculateVolumeInUnits(double riskPercent, double slPriceDist, int score)
        {
            double balance = _bot.Account.Balance;
            double riskUsd = balance * (riskPercent / 100.0);

            var s = _bot.Symbol;

            if (riskUsd <= 0 || slPriceDist <= 0)
                return 0;

            if (s.TickSize <= 0 || s.LotSize <= 0)
                return 0;

            // =========================
            // INDEX-HELYES RISK SIZING
            // =========================
            double valuePerUnitPerPrice =
                (s.TickValue / s.LotSize) / s.TickSize;

            double lossPerUnit = slPriceDist * valuePerUnitPerPrice;
            if (lossPerUnit <= 0)
                return 0;

            double rawUnits = riskUsd / lossPerUnit;

            double capLots = _riskSizer.GetLotCap(score);
            long capUnits = Convert.ToInt64(s.QuantityToVolumeInUnits(capLots));

            long normalized = Convert.ToInt64(
                s.NormalizeVolumeInUnits(Math.Min(rawUnits, capUnits))
            );

            return normalized < s.VolumeInUnitsMin ? 0 : normalized;
        }
    }
}