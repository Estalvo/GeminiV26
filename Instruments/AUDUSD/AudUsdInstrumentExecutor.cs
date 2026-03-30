using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Execution;
using GeminiV26.Core.Logging;
using GeminiV26.Instruments.FX;
using GeminiV26.Core.Risk.PositionSizing;

namespace GeminiV26.Instruments.AUDUSD
{
    /// <summary>
    /// AUDUSD Instrument Executor – Phase 3.7.4
    /// - Nincs néma abort
    /// - Context teljes feltöltése
    /// - FX risk sizer használata
    /// - DEBUG log minden kritikus ponton
    /// </summary>
    public class AudUsdInstrumentExecutor
    {
        private readonly Robot _bot;
        private readonly AudUsdInstrumentRiskSizer _riskSizer;
        private readonly AudUsdExitManager _exitManager;
        private readonly Dictionary<long, PositionContext> _positionContexts;
        private readonly string _botLabel;
        private readonly FxMarketStateDetector _marketStateDetector;
        private readonly AudUsdEntryLogic _entryLogic;

        public AudUsdInstrumentExecutor(
            Robot bot,
            AudUsdEntryLogic entryLogic,                 // ← ÚJ
            AudUsdInstrumentRiskSizer riskSizer,
            AudUsdExitManager exitManager,
            FxMarketStateDetector marketStateDetector,
            Dictionary<long, PositionContext> positionContexts,
            string botLabel)
        {
            _bot = bot;
            _entryLogic = entryLogic;                    // ← ÚJ
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
            var ms = _marketStateDetector.Evaluate();

            /*if (_marketStateDetector == null)
            {
                GlobalLogger.Log(_bot, "[EUR EXEC] SKIP: MarketStateDetector NULL");
                return; // vagy continue, attól függ hol vagy
            }

            if (ms == null)
            {
                GlobalLogger.Log(_bot, "[EUR EXEC] BLOCKED: MarketState NULL");
                return;
            }

            if (ms.IsLowVol)
            {
                GlobalLogger.Log(_bot, "[EUR EXEC] BLOCKED: Low volatility");
                return;
            }

            if (!ms.IsTrend)
            {
                GlobalLogger.Log(_bot, "[EUR EXEC] BLOCKED: No trend");
                return;
            }
            */

            // =========================
            // MARKET STATE – SOFT (FX)
            // =========================
            int statePenalty = 0;

            if (ms != null)
            {
                if (ms.IsLowVol)
                    statePenalty -= 10;

                if (!ms.IsTrend)
                    statePenalty -= 10;
            }

            GlobalLogger.Log(_bot, "[AUDUSD EXEC] ExecuteEntry START");

            var tradeType =
                entryContext.FinalDirection == TradeDirection.Long
                    ? TradeType.Buy
                    : TradeType.Sell;

            // =========================================================
            // ENTRY LOGIC – PRE-EXEC CONFIDENCE (EURUSD)
            // =========================================================
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

            var quality = ExecutionQualityPolicy.Decide(entryContext, entry);

            double riskPercent = _riskSizer.GetRiskPercent(adjustedRiskConfidence);
            riskPercent *= quality.RiskMultiplier;

            if (riskPercent <= 0)
            {
                GlobalLogger.Log(_bot, "[AUDUSD EXEC] BLOCKED: riskPercent <= 0");
                return;
            }

            double slPriceDist = CalculateStopLossPriceDistance(adjustedRiskConfidence, entry.Type);

            if (slPriceDist <= 0)
            {
                GlobalLogger.Log(_bot, "[AUDUSD EXEC] BLOCKED: SL distance invalid");
                return;
            }

            _riskSizer.GetTakeProfit(
                adjustedRiskConfidence,
                out double tp1R,
                out double tp1Ratio,
                out double tp2R,
                out double tp2Ratio);

            long volumeUnits = CalculateVolumeInUnits(riskPercent, slPriceDist, adjustedRiskConfidence);

            if (volumeUnits <= 0)
            {
                GlobalLogger.Log(_bot, "[AUDUSD EXEC] BLOCKED: volume invalid");
                return;
            }

            double entryPrice =
                tradeType == TradeType.Buy
                    ? _bot.Symbol.Ask
                    : _bot.Symbol.Bid;

            double tp2Price =
                tradeType == TradeType.Buy
                    ? entryPrice + slPriceDist * tp2R
                    : entryPrice - slPriceDist * tp2R;

            double slPips = slPriceDist / _bot.Symbol.PipSize;
            double tp2Pips = Math.Abs(tp2Price - entryPrice) / _bot.Symbol.PipSize;

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
                GlobalLogger.Log(_bot, "[AUDUSD EXEC] Order execution FAILED");
                return;
            }

            GlobalLogger.Log(_bot, $"[TRADE LINK] tempId={entryContext.TempId} posId={result.Position.Id} symbol={result.Position.SymbolName}");
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[EXEC] order placed volume={volumeUnits}", result.Position.Id, entryContext.TempId));

            ctx = new PositionContext
            {
                PositionId = result.Position.Id,
                Symbol = result.Position.SymbolName,
                Bot = _bot,
                TempId = entryContext.TempId,
                EntryType = entry.Type.ToString(),
                EntryReason = entry.Reason,
                FinalDirection = entryContext.FinalDirection,

                // =========================
                // RULEBOOK PIPELINE
                // =========================
                EntryScore = entry.Score,
                LogicConfidence = logicConfidence,
                QualityTier = quality.Tier,
                ForceFastBE = quality.ForceFastBE,
                ForceTightTrailing = quality.ForceTightTrailing,
                // FinalConfidence-t ComputeFinalConfidence tölti

                EntryTime = _bot.Server.Time,
                EntryPrice = result.Position.EntryPrice,
                RiskPriceDistance = slPriceDist,
                PipSize = entryContext.PipSize,

                Tp1R = tp1R,
                Tp1Ratio = tp1Ratio,
                Tp2R = tp2R,
                Tp2Ratio = tp2Ratio,
                BeOffsetR = 0.10,

                Tp1Price = tradeType == TradeType.Buy
                    ? entryPrice + slPriceDist * tp1R
                    : entryPrice - slPriceDist * tp1R,

                Tp1Hit = false,
                Tp1CloseFraction = tp1Ratio,
                BeMode = BeMode.AfterTp1,

                // ⚠️ Trailing marad adjustedRiskConfidence alapján
                TrailingMode =
                    adjustedRiskConfidence >= 85 ? TrailingMode.Loose :
                    adjustedRiskConfidence >= 75 ? TrailingMode.Normal :
                                                TrailingMode.Tight,

                EntryVolumeInUnits = result.Position.VolumeInUnits,
                RemainingVolumeInUnits = result.Position.VolumeInUnits,
                Tp2Price = tp2Price
            };

            double slPriceActual = result.Position.StopLoss ??
                (tradeType == TradeType.Buy ? ctx.EntryPrice - slPriceDist : ctx.EntryPrice + slPriceDist);
            ctx.InitialStopLossPrice = slPriceActual;
            ctx.RiskPriceDistance = Math.Abs(ctx.EntryPrice - slPriceActual);
            ctx.LastStopLossPrice = slPriceActual;
            _bot.Print($"[SL_SNAPSHOT] symbol={_bot.SymbolName} entry={ctx.EntryPrice} initialSL={slPriceActual}");

            // ✅ Kanonikus 70/30 FinalConfidence
            ctx.ComputeFinalConfidence();

            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(
                $"[ENTRY][EXEC][SUCCESS] symbol={ctx.Symbol ?? result.Position.SymbolName ?? _bot.SymbolName} entryType={ctx.EntryType} positionId={result.Position.Id} pipelineId={(ctx.PositionId > 0 ? ctx.PositionId.ToString() : (ctx.TempId ?? entryContext.TempId))}\n" +
                $"volumeUnits={ctx.EntryVolumeInUnits:0.##}\n" +
                $"entryPrice={ctx.EntryPrice:0.#####}\n" +
                $"sl={result.Position.StopLoss}\n" +
                $"tp={result.Position.TakeProfit ?? ctx.Tp2Price}", ctx, result.Position));
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildContextCreate(ctx), ctx, result.Position));
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildDirectionSnapshot(ctx), ctx, result.Position));
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildOpenSnapshot(ctx, result.Position.StopLoss, result.Position.TakeProfit ?? ctx.Tp2Price, ctx.EntryVolumeInUnits), ctx, result.Position));

            _positionContexts[ctx.PositionId] = ctx;
            ctx.AdjustedRiskConfidence = adjustedRiskConfidence;
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[DIR][SET] posId={ctx.PositionId} finalDir={ctx.FinalDirection}", ctx));
            _exitManager.RegisterContext(ctx);

            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[POSITION][OPEN] symbol={ctx.Symbol ?? _bot.SymbolName} entryType={ctx.EntryType} positionId={ctx.PositionId} pipelineId={(ctx.PositionId > 0 ? ctx.PositionId.ToString() : ctx.TempId)} entryPrice={ctx.EntryPrice}", ctx));

            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(

                $"[POSITION][CONTEXT] symbol={ctx.Symbol ?? _bot.SymbolName} positionId={ctx.PositionId} pipelineId={(ctx.PositionId > 0 ? ctx.PositionId.ToString() : ctx.TempId)} " +

                $"entryType={ctx.EntryType ?? "NA"} side={(result?.Position != null ? result.Position.TradeType.ToString() : "NA")} entryPrice={ctx.EntryPrice:0.#####} " +

                $"sl={(result?.Position?.StopLoss ?? 0):0.#####} tp1={(ctx.Tp1Price ?? 0):0.#####} tp2={(ctx.Tp2Price ?? 0):0.#####} " +

                $"riskPct={riskPercent:F2} confidence={ctx.FinalConfidence:F2} " +

                $"htfState={(entryContext != null ? entryContext.ActiveHtfDirection.ToString() : "NA")}", ctx));
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(
                $"[AUDUSD EXEC] OPEN {tradeType} vol={ctx.EntryVolumeInUnits} " +
                $"score={entry.Score} SLpips={slPips:F1} TP2={tp2Price:F5}", ctx));
        }

        private double CalculateStopLossPriceDistance(int score, EntryType entryType)
        {
            var atr = _bot.Indicators.AverageTrueRange(14, MovingAverageType.Exponential);
            double atrValue = atr.Result.LastValue;
            if (atrValue <= 0)
                return 0;

            double atrMult = _riskSizer.GetStopLossAtrMultiplier(score, entryType);
            return atrValue * atrMult;
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
