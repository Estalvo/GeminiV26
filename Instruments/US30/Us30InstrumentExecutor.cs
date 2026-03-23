using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Logging;
using GeminiV26.Instruments.INDEX;
using GeminiV26.Core.Risk.PositionSizing;

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

        public void ExecuteEntry(EntryEvaluation entry, EntryContext entryContext)
        {
            if (entry == null)
            {
                _bot.Print("[DIR][EXEC_ABORT] Missing entry");
                return;
            }

            if (entryContext == null || entryContext.FinalDirection == TradeDirection.None)
            {
                _bot.Print("[DIR][EXEC_ABORT] Missing FinalDirection");
                return;
            }
            DirectionGuard.Validate(entryContext, null, _bot.Print);


            _bot.Print(TradeLogIdentity.WithTempId($"[DIR][EXEC_FINAL] symbol={_bot.SymbolName} finalDir={entryContext.FinalDirection}", entryContext));

            if (entry.Direction != entryContext.FinalDirection)
            {
                _bot.Print(TradeLogIdentity.WithTempId($"[DIR][EXEC_MISMATCH] entryDir={entry.Direction} finalDir={entryContext.FinalDirection}", entryContext));
                // DO NOT TRUST entry.Direction
            }
            if (_marketStateDetector != null)
            {
                var ms = _marketStateDetector.Evaluate();
                if (ms != null && ms.IsLowVol)
                    _bot.Print("[US30][STATE] LOWVOL");
            }

            // =========================
            // ENTRY LOGIC � US30
            // =========================
            _entryLogic.Evaluate();
            _entryLogic.ApplyToEntryEvaluation(entry);
            int logicConfidence = _entryLogic.LastLogicConfidence;

            // =========================
            // MARKET STATE � SOFT
            // =========================
            int statePenalty = 0;

            if (_marketStateDetector != null)
            {
                var ms = _marketStateDetector.Evaluate();
                if (ms != null)
                {
                    if (ms.IsLowVol)
                        statePenalty -= 15;   // US30 �rz�kenyebb, mint NAS
                    if (ms.IsTrend)
                        statePenalty += 5;
                }
            }

            int finalConfidence = PositionContext.ComputeFinalConfidenceValue(entry.Score, logicConfidence);
            int riskConfidence = PositionContext.ClampRiskConfidence(finalConfidence + statePenalty);

            var tradeType =
                entryContext.FinalDirection == TradeDirection.Long
                    ? TradeType.Buy
                    : TradeType.Sell;

            double riskPercent = _riskSizer.GetRiskPercent(riskConfidence);

            if (riskPercent <= 0)
                return;

            double slPriceDist = CalculateStopLossPriceDistance(riskConfidence, entry.Type);

            if (slPriceDist <= 0)
                return;

            _riskSizer.GetTakeProfit(
                riskConfidence,
                out double tp1R,
                out double tp1Ratio,
                out double tp2R,
                out double tp2Ratio);

            long volumeUnits = CalculateVolumeInUnits(riskPercent, slPriceDist, riskConfidence);

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
                tp2Pips,
                entryContext.TempId);

            if (!result.IsSuccessful || result.Position == null)
                return;

            long positionKey = Convert.ToInt64(result.Position.Id);
            _bot.Print($"[TRADE LINK] tempId={entryContext.TempId} posId={positionKey} symbol={result.Position.SymbolName}");
            _bot.Print(TradeLogIdentity.WithPositionIds($"[EXEC] order placed volume={volumeUnits}", result.Position.Id, entryContext.TempId));

            var ctx = new PositionContext
            {
                PositionId = positionKey,
                Symbol = result.Position.SymbolName,
                TempId = entryContext.TempId,
                EntryType = entry.Type.ToString(),
                EntryReason = entry.Reason,
                FinalDirection = entryContext.FinalDirection,
                EntryScore = entry.Score,
                LogicConfidence = logicConfidence,
                EntryTime = _bot.Server.Time,
                EntryPrice = result.Position.EntryPrice,

                // TP1 fix: keep full R-context so ExitManager can evaluate TP1 deterministically
                RiskPriceDistance = slPriceDist,

                Tp1R = tp1R,
                Tp1Ratio = tp1Ratio,
                Tp2R = tp2R,
                Tp2Ratio = tp2Ratio,
                Tp2Price = tp2Price,

                BeOffsetR = 0.10,

                Tp1Hit = false,
                Tp1CloseFraction = tp1Ratio,

                BeMode = BeMode.AfterTp1,

                MarketTrend = entryContext.FinalDirection != TradeDirection.None,
                Adx_M5 = entryContext.Adx_M5,

                TrailingMode =
                    riskConfidence >= 85 ? TrailingMode.Loose :
                    riskConfidence >= 75 ? TrailingMode.Normal :
                                                TrailingMode.Tight,

                EntryVolumeInUnits = volumeUnits,
                RemainingVolumeInUnits = volumeUnits,

            };

            ctx.ComputeFinalConfidence();

            _positionContexts[positionKey] = ctx;
            _bot.Print(TradeLogIdentity.WithPositionIds($"[DIR][SET] posId={ctx.PositionId} finalDir={ctx.FinalDirection}", ctx));
            _exitManager.RegisterContext(ctx);
            _bot.Print(TradeLogIdentity.WithPositionIds($"[OPEN] entryPrice={ctx.EntryPrice}", ctx));
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
            return IndexPositionSizer.Calculate(
                _bot,
                riskPercent,
                slPriceDist,
                _riskSizer.GetLotCap(score));
        }
    }
}
