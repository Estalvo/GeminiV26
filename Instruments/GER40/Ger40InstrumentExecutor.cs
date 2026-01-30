using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Instruments.INDEX;

namespace GeminiV26.Instruments.GER40
{
    public class Ger40InstrumentExecutor
    {
        private readonly Robot _bot;
        private readonly Ger40InstrumentRiskSizer _riskSizer;
        private readonly Ger40ExitManager _exitManager;
        private readonly Dictionary<long, PositionContext> _positionContexts;
        private readonly string _botLabel;
        private readonly Ger40EntryLogic _entryLogic;
        private readonly IndexMarketStateDetector _marketStateDetector;

        public Ger40InstrumentExecutor(
            Robot bot,
            Ger40EntryLogic entryLogic,
            Ger40InstrumentRiskSizer riskSizer,
            Ger40ExitManager exitManager,
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
            // =========================
            // ENTRY LOGIC – GER40
            // =========================
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
                        statePenalty -= 15;

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

            // =========================
            // RISK POLICY
            // =========================
            double riskPercent = _riskSizer.GetRiskPercent(tempFinalConfidence);

            if (riskPercent <= 0)
                return;

            double slPriceDist = CalculateStopLossPriceDistance(tempFinalConfidence, entry.Type);

            if (slPriceDist <= 0)
                return;

            _riskSizer.GetTakeProfit(
                tempFinalConfidence,
                out double tp1R,
                out double tp1Ratio,
                out double tp2R,
                out double tp2Ratio);

            double slAtrMult = _riskSizer.GetStopLossAtrMultiplier(tempFinalConfidence, entry.Type);

            double lotCap = _riskSizer.GetLotCap(tempFinalConfidence);

            long volumeUnits = CalculateVolumeInUnits(riskPercent, slPriceDist, tempFinalConfidence);
            if (volumeUnits <= 0)
                return;

            // =========================
            // LOG – KÖTELEZŐ DEBUG
            // =========================
            _bot.Print(
                $"[GER40 RISK] score={entry.Score} " +
                $"risk%={riskPercent:F2} " +
                $"slATR={slAtrMult:F2} " +
                $"TP1={tp1R:F1}R({tp1Ratio:P0}) " +
                $"TP2={tp2R:F1}R({tp2Ratio:P0}) " +
                $"lotCap={lotCap:F2}"
            );

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

            // =========================
            // CONTEXT – TELJES, R-ALAPÚ
            // =========================
            var ctx = new PositionContext
            {
                PositionId = positionKey,
                Symbol = result.Position.SymbolName,
                EntryType = entry.Type.ToString(),
                EntryReason = entry.Reason,
                EntryScore = entry.Score,

                FinalConfidence = tempFinalConfidence,   // ⬅️ KÖTELEZŐ

                EntryTime = _bot.Server.Time,
                EntryPrice = result.Position.EntryPrice,

                RiskPriceDistance = slPriceDist,

                Tp1R = tp1R,
                Tp1Ratio = tp1Ratio,
                Tp2R = tp2R,
                Tp2Ratio = tp2Ratio,
                Tp2Price = tp2Price,

                BeOffsetR = 0.10,
                Tp1Hit = false,
                Tp1CloseFraction = tp1Ratio,

                TrailingMode =
                    tempFinalConfidence >= 85 ? TrailingMode.Loose :
                    tempFinalConfidence >= 75 ? TrailingMode.Normal :
                                                TrailingMode.Tight,

                EntryVolumeInUnits = volumeUnits,
                RemainingVolumeInUnits = volumeUnits
            };

            _positionContexts[positionKey] = ctx;
            _exitManager.RegisterContext(ctx);
        }

        private double CalculateStopLossPriceDistance(int confidence, EntryType entryType)
        {
            var atr = _bot.Indicators.AverageTrueRange(14, MovingAverageType.Exponential);
            double atrVal = atr.Result.LastValue;
            if (atrVal <= 0)
                return 0;

            double atrMult = _riskSizer.GetStopLossAtrMultiplier(confidence, entryType);
            return atrVal * atrMult;
        }

        private long CalculateVolumeInUnits(double riskPercent, double slPriceDist, int score)
        {
            double balance = _bot.Account.Balance;
            double riskUsd = balance * (riskPercent / 100.0);

            var s = _bot.Symbol;

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
