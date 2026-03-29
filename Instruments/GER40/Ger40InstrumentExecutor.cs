using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Logging;
using GeminiV26.Instruments.INDEX;
using GeminiV26.Core.Risk.PositionSizing;

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

        public void ExecuteEntry(EntryEvaluation entry, EntryContext entryContext)
        {
            if (entry == null)
            {
                GlobalLogger.Log("[DIR][EXEC_ABORT] Missing entry");
                return;
            }

            if (entryContext == null || entryContext.FinalDirection == TradeDirection.None)
            {
                GlobalLogger.Log("[DIR][EXEC_ABORT] Missing FinalDirection");
                return;
            }

            TradeAuditLog.EnsureAttemptId(entryContext, _bot.Server.Time);
            DirectionGuard.Validate(entryContext, null, _bot.Print);


            GlobalLogger.Log(TradeLogIdentity.WithTempId($"[DIR][EXEC_FINAL] symbol={_bot.SymbolName} finalDir={entryContext.FinalDirection}", entryContext));

            if (entry.Direction != entryContext.FinalDirection)
            {
                GlobalLogger.Log(TradeLogIdentity.WithTempId($"[DIR][EXEC_MISMATCH] entryDir={entry.Direction} finalDir={entryContext.FinalDirection}", entryContext));
                // DO NOT TRUST entry.Direction
            }
            // =========================
            // ENTRY LOGIC – GER40
            // =========================
            _entryLogic.Evaluate();
            _entryLogic.ApplyToEntryEvaluation(entry);
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

            var ctx = new PositionContext
            {
                Symbol = _bot.SymbolName,
                Bot = _bot,
                TempId = entryContext.TempId,
                EntryType = entry.Type.ToString(),
                EntryReason = entry.Reason,
                FinalDirection = entryContext.FinalDirection,
                EntryScore = entry.Score,
                LogicConfidence = logicConfidence
            };
            ctx.ComputeFinalConfidence();

            GlobalLogger.Log(TradeLogIdentity.WithTempId(TradeAuditLog.BuildEntrySnapshot(_bot, entryContext, entry), entryContext));

            GlobalLogger.Log(TradeLogIdentity.WithTempId(TradeAuditLog.BuildDirectionSnapshot(entryContext, entry), entryContext));

            if (statePenalty != 0)

                GlobalLogger.Log(TradeLogIdentity.WithTempId($"[SOFT_PENALTY] value={statePenalty} riskFinal={PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty)}", entryContext));

            var tradeType =
                entryContext.FinalDirection == TradeDirection.Long
                    ? TradeType.Buy
                    : TradeType.Sell;

            // =========================
            // RISK POLICY
            // =========================
            double riskPercent = _riskSizer.GetRiskPercent(PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty));

            if (riskPercent <= 0)
                return;

            double slPriceDist = CalculateStopLossPriceDistance(PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty), entry.Type);

            if (slPriceDist <= 0)
                return;

            _riskSizer.GetTakeProfit(
                PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty),
                out double tp1R,
                out double tp1Ratio,
                out double tp2R,
                out double tp2Ratio);

            double slAtrMult = _riskSizer.GetStopLossAtrMultiplier(PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty), entry.Type);

            double lotCap = _riskSizer.GetLotCap(PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty));

            long volumeUnits = CalculateVolumeInUnits(riskPercent, slPriceDist, PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty));
            if (volumeUnits <= 0)
                return;

            // =========================
            // LOG – KÖTELEZŐ DEBUG
            // =========================
            GlobalLogger.Log(
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

            GlobalLogger.Log(TradeLogIdentity.WithTempId($"[EXEC][REQUEST] side={tradeType} volumeUnits={volumeUnits} slPips={slPips:0.#####} tpPips={tp2Pips:0.#####}", entryContext));

            var result = _bot.ExecuteMarketOrder(
                tradeType,
                _bot.SymbolName,
                volumeUnits,
                _botLabel,
                slPips,
                tp2Pips,
                entryContext.TempId);

            if (!result.IsSuccessful || result.Position == null)
            {
                GlobalLogger.Log(TradeLogIdentity.WithTempId($"[EXEC][FAIL] side={tradeType} volumeUnits={volumeUnits} error={(result == null ? "NULL_RESULT" : result.Error.ToString())}", entryContext));
                return;
            }

            long positionKey = Convert.ToInt64(result.Position.Id);
            GlobalLogger.Log($"[TRADE LINK] tempId={entryContext.TempId} posId={positionKey} symbol={result.Position.SymbolName}");
            GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[EXEC] order placed volume={volumeUnits}", result.Position.Id, entryContext.TempId));

            // =========================
            // CONTEXT – TELJES, R-ALAPÚ
            // =========================
            ctx = new PositionContext
            {
                PositionId = positionKey,
                Symbol = result.Position.SymbolName,
                Bot = _bot,
                TempId = entryContext.TempId,
                EntryType = entry.Type.ToString(),
                EntryReason = entry.Reason,
                FinalDirection = entryContext.FinalDirection,
                EntryScore = entry.Score,
                LogicConfidence = logicConfidence,

                EntryTime = _bot.Server.Time,
                EntryPrice = result.Position.EntryPrice,

                RiskPriceDistance = slPriceDist,
                PipSize = entryContext.PipSize,

                Tp1R = tp1R,
                Tp1Ratio = tp1Ratio,
                Tp2R = tp2R,
                Tp2Ratio = tp2Ratio,
                Tp2Price = tp2Price,

                MarketTrend = entryContext.FinalDirection != TradeDirection.None,
                Adx_M5 = entryContext.Adx_M5,

                BeOffsetR = 0.10,
                Tp1Hit = false,
                Tp1CloseFraction = tp1Ratio,

                TrailingMode =
                    PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty) >= 85 ? TrailingMode.Loose :
                    PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty) >= 75 ? TrailingMode.Normal :
                                                TrailingMode.Tight,

                EntryVolumeInUnits = volumeUnits,
                RemainingVolumeInUnits = volumeUnits
            };

            ctx.ComputeFinalConfidence();

            GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[EXEC][SUCCESS]\nvolumeUnits={ctx.EntryVolumeInUnits:0.##}\nentryPrice={ctx.EntryPrice:0.#####}\nsl={result.Position.StopLoss}\ntp={result.Position.TakeProfit ?? ctx.Tp2Price}", ctx, result.Position));
            GlobalLogger.Log(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildContextCreate(ctx), ctx, result.Position));
            GlobalLogger.Log(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildDirectionSnapshot(ctx), ctx, result.Position));
            GlobalLogger.Log(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildOpenSnapshot(ctx, result.Position.StopLoss, result.Position.TakeProfit ?? ctx.Tp2Price, ctx.EntryVolumeInUnits), ctx, result.Position));

            _positionContexts[positionKey] = ctx;
            GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[DIR][SET] posId={ctx.PositionId} finalDir={ctx.FinalDirection}", ctx));
            _exitManager.RegisterContext(ctx);
            GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[OPEN] entryPrice={ctx.EntryPrice}", ctx));
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
            return IndexPositionSizer.Calculate(
                _bot,
                riskPercent,
                slPriceDist,
                _riskSizer.GetLotCap(score));
        }
    }
}
