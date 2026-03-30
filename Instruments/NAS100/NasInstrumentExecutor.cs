using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Logging;
using GeminiV26.Instruments.INDEX;
using GeminiV26.Core.Risk.PositionSizing;

namespace GeminiV26.Instruments.NAS100
{
    public class NasInstrumentExecutor
    {
        private readonly Robot _bot;
        private readonly NasInstrumentRiskSizer _riskSizer;
        private readonly NasExitManager _exitManager;
        private readonly Dictionary<long, PositionContext> _positionContexts;
        private readonly string _botLabel;
        private readonly IndexMarketStateDetector _marketStateDetector;
        private readonly NasEntryLogic _entryLogic;

        public NasInstrumentExecutor(
            Robot bot,
            NasEntryLogic entryLogic,                 // ← ÚJ
            NasInstrumentRiskSizer riskSizer,
            NasExitManager exitManager,
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
                GlobalLogger.Log(_bot, "[DIR][EXEC_ABORT] Missing entry");
                return;
            }

            if (entryContext == null || entryContext.FinalDirection == TradeDirection.None)
            {
                GlobalLogger.Log(_bot, "[DIR][EXEC_ABORT] Missing FinalDirection");
                return;
            }

            TradeAuditLog.EnsureAttemptId(entryContext, _bot.Server.Time);
            DirectionGuard.Validate(entryContext, null, _bot.Print);


            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[DIR][EXEC_FINAL] symbol={_bot.SymbolName} finalDir={entryContext.FinalDirection}", entryContext));

            if (entry.Direction != entryContext.FinalDirection)
            {
                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[DIR][EXEC_MISMATCH] entryDir={entry.Direction} finalDir={entryContext.FinalDirection}", entryContext));
                // DO NOT TRUST entry.Direction
            }
            if (_marketStateDetector != null)
            {
                var ms = _marketStateDetector.Evaluate();
                if (ms != null)
                {
                    if (ms.IsLowVol)
                        GlobalLogger.Log(_bot, "[NAS][STATE] LOWVOL");

                    if (ms.IsTrend)
                        GlobalLogger.Log(_bot, "[NAS][STATE] TREND");
                }
            }

            // =========================
            // ENTRY LOGIC – NAS
            // =========================
            int logicConfidence = PositionContext.ClampRiskConfidence(entryContext.LogicBiasConfidence);
            string logicConfidenceSource = "EntryContext.LogicBiasConfidence";

            if (logicConfidence <= 0 && entry.LogicConfidence > 0)
            {
                logicConfidence = PositionContext.ClampRiskConfidence(entry.LogicConfidence);
                logicConfidenceSource = "EntryEvaluation.LogicConfidence";
                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[CONF][EXEC_FALLBACK] source={logicConfidenceSource} value={logicConfidence}", entryContext));
            }

            if (logicConfidence <= 0)
            {
                logicConfidence = 50;
                logicConfidenceSource = "NeutralDefault";
                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[CONF][EXEC_FALLBACK] source={logicConfidenceSource} value={logicConfidence}", entryContext));
            }

            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[CONF][EXEC_INPUT] entryScore={entry.Score} routedLogic={logicConfidence} source={logicConfidenceSource}", entryContext));

            int statePenalty = 0;

            if (_marketStateDetector != null)
            {
                var ms = _marketStateDetector.Evaluate();
                if (ms != null)
                {
                    if (ms.IsLowVol)
                        statePenalty -= 10;   // NAS low vol = gyengébb momentum

                    if (ms.IsTrend)
                        statePenalty += 5;    // kis bónusz, nem dönt
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
            int adjustedRiskConfidence = PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty);
            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[CONF][FINAL] final={ctx.FinalConfidence} adjustedRisk={adjustedRiskConfidence} statePenalty={statePenalty}", entryContext));

            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(TradeAuditLog.BuildEntrySnapshot(_bot, entryContext, entry), entryContext));

            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(TradeAuditLog.BuildDirectionSnapshot(entryContext, entry), entryContext));

            if (statePenalty != 0)

                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[SOFT_PENALTY] value={statePenalty} riskFinal={adjustedRiskConfidence}", entryContext));

            // === ORIGINAL LOGIC CONTINUES ===
            var tradeType =
                entryContext.FinalDirection == TradeDirection.Long
                    ? TradeType.Buy
                    : TradeType.Sell;

            // =========================
            // RISK POLICY
            // =========================
            double riskPercent = _riskSizer.GetRiskPercent(adjustedRiskConfidence);

            if (riskPercent <= 0)
                return;

            double slPriceDist = CalculateStopLossPriceDistance(adjustedRiskConfidence, entry.Type);
            if (slPriceDist <= 0)
                return;

            _riskSizer.GetTakeProfit(
                adjustedRiskConfidence,
                out double tp1R,
                out double tp1Ratio,
                out double tp2R,
                out double tp2Ratio);

            double slAtrMult = _riskSizer.GetStopLossAtrMultiplier(adjustedRiskConfidence, entry.Type);
            double lotCap = _riskSizer.GetLotCap(adjustedRiskConfidence);

            long volumeUnits = CalculateVolumeInUnits(riskPercent, slPriceDist, adjustedRiskConfidence);

            if (volumeUnits <= 0)
                return;

            // =========================
            // LOG – KÖTELEZŐ DEBUG
            // =========================
            GlobalLogger.Log(_bot, 
                $"[NAS RISK] score={entry.Score} " +
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

            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[ENTRY][EXEC][REQUEST] symbol={entry.Symbol ?? entryContext.Symbol ?? _bot.SymbolName} entryType={entry.Type} pipelineId={entryContext.TempId} side={tradeType} volumeUnits={volumeUnits} slPips={slPips:0.#####} tpPips={tp2Pips:0.#####}", entryContext));

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
                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[ENTRY][EXEC][FAIL] symbol={entry.Symbol ?? entryContext.Symbol ?? _bot.SymbolName} entryType={entry.Type} pipelineId={entryContext.TempId} side={tradeType} volumeUnits={volumeUnits} error={(result == null ? "NULL_RESULT" : result.Error.ToString())}", entryContext));
                return;
            }

            long positionKey = Convert.ToInt64(result.Position.Id);
            GlobalLogger.Log(_bot, $"[TRADE LINK] tempId={entryContext.TempId} posId={positionKey} symbol={result.Position.SymbolName}");
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[EXEC] order placed volume={volumeUnits}", result.Position.Id, entryContext.TempId));

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
                    adjustedRiskConfidence >= 85 ? TrailingMode.Loose :
                    adjustedRiskConfidence >= 75 ? TrailingMode.Normal :
                                                TrailingMode.Tight,

                EntryVolumeInUnits = volumeUnits,
                RemainingVolumeInUnits = volumeUnits
            };

            ctx.ComputeFinalConfidence();

            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[ENTRY][EXEC][SUCCESS] symbol={ctx.Symbol ?? result.Position.SymbolName ?? _bot.SymbolName} entryType={ctx.EntryType} positionId={result.Position.Id} pipelineId={ctx.PositionId > 0 ? ctx.PositionId.ToString() : (ctx.TempId ?? entryContext.TempId)}
volumeUnits={ctx.EntryVolumeInUnits:0.##}
entryPrice={ctx.EntryPrice:0.#####}
sl={result.Position.StopLoss}
tp={result.Position.TakeProfit ?? ctx.Tp2Price}", ctx, result.Position));
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildContextCreate(ctx), ctx, result.Position));
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildDirectionSnapshot(ctx), ctx, result.Position));
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildOpenSnapshot(ctx, result.Position.StopLoss, result.Position.TakeProfit ?? ctx.Tp2Price, ctx.EntryVolumeInUnits), ctx, result.Position));

            _positionContexts[positionKey] = ctx;
            ctx.AdjustedRiskConfidence = adjustedRiskConfidence;
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[DIR][SET] posId={ctx.PositionId} finalDir={ctx.FinalDirection}", ctx));
            _exitManager.RegisterContext(ctx);
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[POSITION][OPEN] symbol={ctx.Symbol ?? _bot.SymbolName} entryType={ctx.EntryType} positionId={ctx.PositionId} pipelineId={ctx.PositionId > 0 ? ctx.PositionId.ToString() : ctx.TempId} entryPrice={ctx.EntryPrice}", ctx));
        }

        private double CalculateStopLossPriceDistance(int score, EntryType entryType)
        {
            var atr = _bot.Indicators.AverageTrueRange(14, MovingAverageType.Exponential);
            double atrVal = atr.Result.LastValue;
            if (atrVal <= 0)
                return 0;

            double atrMult = _riskSizer.GetStopLossAtrMultiplier(score, entryType);
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
