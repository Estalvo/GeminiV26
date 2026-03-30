// =========================================================
// GEMINI V26 – BTCUSD InstrumentExecutor
// Phase 3.7.3 – RULEBOOK 1.0 COMPLIANT
// =========================================================

using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Logging;
using GeminiV26.Core.Risk.PositionSizing;
using GeminiV26.Instruments.CRYPTO;
using GeminiV26.Instruments.BTCUSD;

namespace GeminiV26.Instruments.BTCUSD
{
    public class BtcUsdInstrumentExecutor
    {
        private readonly Robot _bot;
        private readonly BtcUsdInstrumentRiskSizer _riskSizer;
        private readonly BtcUsdExitManager _exitManager;
        private readonly Dictionary<long, PositionContext> _positionContexts;
        private readonly string _botLabel;

        // 🔑 ENTRY LOGIC (ÚJ – ténylegesen használt)
        private readonly BtcUsdEntryLogic _entryLogic;

        private readonly CryptoMarketStateDetector _marketStateDetector;

        public BtcUsdInstrumentExecutor(
            Robot bot,
            BtcUsdEntryLogic entryLogic,
            BtcUsdInstrumentRiskSizer riskSizer,
            BtcUsdExitManager exitManager,
            CryptoMarketStateDetector marketStateDetector,
            Dictionary<long, PositionContext> positionContexts,
            string botLabel)
        {
            _bot = bot;
            _entryLogic = entryLogic;                 // 🔑
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
                GlobalLogger.Log(_bot, "[ENTRY][EXEC][ABORT] symbol=BTCUSD entryType=UNKNOWN reason=missing_entry");
                return;
            }

            if (entryContext == null || entryContext.FinalDirection == TradeDirection.None)
            {
                GlobalLogger.Log(_bot, "[DIR][EXEC_ABORT] Missing FinalDirection");
                GlobalLogger.Log(_bot, $"[ENTRY][EXEC][ABORT] symbol={_bot.SymbolName} entryType={entry.Type} reason=missing_final_direction");
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
            GlobalLogger.Log(_bot, "[BTCUSD][EXEC] ExecuteEntry");

            // =========================
            // MARKET STATE (OBSERVE ONLY)
            // =========================
            _marketStateDetector?.Evaluate();

            var tradeType =
                entryContext.FinalDirection == TradeDirection.Long
                    ? TradeType.Buy
                    : TradeType.Sell;

            // =========================================================
            // 🔑 ENTRY LOGIC – PRE-EXEC CONFIDENCE
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
            int statePenalty = 0;

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

            // =========================================================
            // SL DISTANCE (ATR)
            // =========================================================
            double slPriceDist =
                CalculateStopLossPriceDistance(adjustedRiskConfidence, entry.Type);

            if (slPriceDist <= 0)
            {
                GlobalLogger.Log(_bot, $"[ENTRY][EXEC][ABORT] symbol={_bot.SymbolName} entryType={entry.Type} reason=invalid_sl_distance");
                return;
            }

            double slPips = slPriceDist / _bot.Symbol.PipSize;
            if (slPips <= 0)
            {
                GlobalLogger.Log(_bot, $"[ENTRY][EXEC][ABORT] symbol={_bot.SymbolName} entryType={entry.Type} reason=invalid_sl_pips");
                return;
            }

            // =========================================================
            // RISK-BASED VOLUME (CRYPTO POSITION SIZER)
            // =========================================================
            double riskPercent = _riskSizer.GetRiskPercent(adjustedRiskConfidence);
            long volumeUnits = CryptoPositionSizer.Calculate(
                _bot,
                riskPercent,
                slPriceDist,
                _riskSizer.GetLotCap(adjustedRiskConfidence),
                isExecutionContext: true);

            if (volumeUnits < _bot.Symbol.VolumeInUnitsMin)
            {
                GlobalLogger.Log(_bot, $"[BTCUSD][EXEC][ABORT] symbol={_bot.SymbolName} entryType={entry.Type} reason=volume_below_min volume={volumeUnits} minVolume={_bot.Symbol.VolumeInUnitsMin} riskPercent={riskPercent:0.##} slDistance={slPriceDist:0.########}");
                GlobalLogger.Log(_bot, $"[ENTRY][EXEC][ABORT] symbol={_bot.SymbolName} entryType={entry.Type} reason=volume_below_min");
                return;
            }

            // =========================================================
            // TP POLICY
            // =========================================================
            _riskSizer.GetTakeProfit(
                adjustedRiskConfidence,
                out double tp1R,
                out double tp1Ratio,
                out double tp2R,
                out double tp2Ratio);

            double entryPrice =
                tradeType == TradeType.Buy
                    ? _bot.Symbol.Ask
                    : _bot.Symbol.Bid;

            double tp2Price =
                tradeType == TradeType.Buy
                    ? entryPrice + slPriceDist * tp2R
                    : entryPrice - slPriceDist * tp2R;

            double tp2Pips =
                Math.Abs(tp2Price - entryPrice) / _bot.Symbol.PipSize;

            if (tp2Pips <= 0)
            {
                GlobalLogger.Log(_bot, $"[ENTRY][EXEC][ABORT] symbol={_bot.SymbolName} entryType={entry.Type} reason=invalid_tp_pips");
                return;
            }

            GlobalLogger.Log(_bot, 
                $"[BTC RISK] score={entry.Score} logicConf={logicConfidence} RC={adjustedRiskConfidence} FC={ctx.FinalConfidence} " +
                $"risk%={riskPercent:F2} slDist={slPriceDist:F2} slPips={slPips:F1} " +
                $"volUnits={volumeUnits}"
            );

            // =========================================================
            // SEND ORDER
            // =========================================================
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
                GlobalLogger.Log(_bot, "[BTCUSD][EXEC] ORDER FAILED (TradeResult unsuccessful or Position null)");
                GlobalLogger.Log(_bot, $"[BTCUSD][EXEC] ORDER FAILED isSuccessful={result.IsSuccessful}");
                string error = result?.Error.ToString() ?? "unknown";
                GlobalLogger.Log(_bot, $"[ENTRY][EXEC][FAIL] symbol={_bot.SymbolName} entryType={entry.Type} positionId=0 pipelineId={entryContext.TempId} reason={error}");
                return;
            }


            long posId = result.Position.Id;
                        GlobalLogger.Log(_bot, $"[TRADE LINK] tempId={entryContext.TempId} posId={posId} symbol={result.Position.SymbolName}");
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[EXEC] order placed volume={volumeUnits}", result.Position.Id, entryContext.TempId));

            // =========================================================
            // POSITION CONTEXT (SSOT)
            // =========================================================
            ctx = new PositionContext
            {
                PositionId = posId,
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

                EntryVolumeInUnits = result.Position.VolumeInUnits,
                RemainingVolumeInUnits = result.Position.VolumeInUnits,

                Tp1R = tp1R,
                Tp1CloseFraction = tp1Ratio,
                Tp1Hit = false,

                BeMode = BeMode.AfterTp1,
                Tp2Price = tp2Price,

                // 🔑 MARKET STATE SNAPSHOT
                MarketTrend = entryContext.FinalDirection != TradeDirection.None,
                Adx_M5 = entryContext.Adx_M5
            };

            ctx.Tp1Price =
                tradeType == TradeType.Buy
                    ? ctx.EntryPrice + slPriceDist * ctx.Tp1R
                    : ctx.EntryPrice - slPriceDist * ctx.Tp1R;

            // 🔒 FINAL CONFIDENCE
            ctx.ComputeFinalConfidence();

            ctx.TrailingMode =
                adjustedRiskConfidence >= 85 ? TrailingMode.Loose :
                adjustedRiskConfidence >= 75 ? TrailingMode.Normal :
                                             TrailingMode.Tight;

            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(
                $"[ENTRY][EXEC][SUCCESS] symbol={ctx.Symbol ?? result.Position.SymbolName ?? _bot.SymbolName} entryType={ctx.EntryType} positionId={result.Position.Id} pipelineId={(ctx.PositionId > 0 ? ctx.PositionId.ToString() : (ctx.TempId ?? entryContext.TempId))}\n" +
                $"volumeUnits={ctx.EntryVolumeInUnits:0.##}\n" +
                $"entryPrice={ctx.EntryPrice:0.#####}\n" +
                $"sl={result.Position.StopLoss}\n" +
                $"tp={result.Position.TakeProfit ?? ctx.Tp2Price}", ctx, result.Position));
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildContextCreate(ctx), ctx, result.Position));
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildDirectionSnapshot(ctx), ctx, result.Position));
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildOpenSnapshot(ctx, result.Position.StopLoss, result.Position.TakeProfit ?? ctx.Tp2Price, ctx.EntryVolumeInUnits), ctx, result.Position));

            _positionContexts[posId] = ctx;
            ctx.AdjustedRiskConfidence = adjustedRiskConfidence;
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[DIR][SET] posId={ctx.PositionId} finalDir={ctx.FinalDirection}", ctx));
            _exitManager.RegisterContext(ctx);

            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[POSITION][OPEN] symbol={ctx.Symbol ?? _bot.SymbolName} entryType={ctx.EntryType} positionId={ctx.PositionId} pipelineId={(ctx.PositionId > 0 ? ctx.PositionId.ToString() : ctx.TempId)} entryPrice={ctx.EntryPrice}", ctx));
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(
                $"[BTCUSD][EXEC] OPEN {tradeType} " +
                $"vol={ctx.EntryVolumeInUnits} FC={ctx.FinalConfidence}", ctx));
        }

        // =========================================================
        // HELPERS
        // =========================================================
        private double CalculateStopLossPriceDistance(
            int confidence,
            EntryType entryType)
        {
            var atr = _bot.Indicators
                .AverageTrueRange(14, MovingAverageType.Simple)
                .Result.LastValue;

            if (atr <= 0)
                return 0;

            double atrMult =
                _riskSizer.GetStopLossAtrMultiplier(confidence, entryType);

            return atr * atrMult;
        }

        private static int Clamp01to100(int v)
        {
            if (v < 0) return 0;
            if (v > 100) return 100;
            return v;
        }
    }
}
