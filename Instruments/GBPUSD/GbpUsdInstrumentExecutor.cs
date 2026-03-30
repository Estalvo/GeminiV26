using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Logging;
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
            // =====================================================
            // FX MARKET STATE – SOFT GATE (GBPUSD / FX)
            // =====================================================
            int statePenalty = 0;

            if (_marketStateDetector == null)
            {
                GlobalLogger.Log(_bot, "[GBP EXEC] WARN: MarketStateDetector NULL");
            }
            else
            {
                var ms = _marketStateDetector.Evaluate();

                if (ms == null)
                {
                    GlobalLogger.Log(_bot, "[GBP EXEC] WARN: MarketState NULL");
                }
                else
                {
                    if (ms.IsLowVol)
                    {
                        statePenalty -= 10;
                        GlobalLogger.Log(_bot, "[GBP EXEC] MarketState: LowVol → penalty -10");
                    }

                    if (!ms.IsTrend)
                    {
                        statePenalty -= 10;
                        GlobalLogger.Log(_bot, "[GBP EXEC] MarketState: NoTrend → penalty -10");
                    }
                }
            }

            // =====================================================
            // ENTRY LOGIC – PRE-EXEC CONFIDENCE (GBPUSD)
            // =====================================================
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

            // =====================================================
            // HARD ATR GATE (GBPUSD) – NO MICRO VOL
            // =====================================================
            double atr = _atr14.Result.LastValue;
            if (atr <= 0)
            {
                GlobalLogger.Log(_bot, "[GBP EXEC] ATR_NOT_READY");
                return;
            }

            double atrPips = atr / _bot.Symbol.PipSize;
            if (atrPips < MinAtrPips)
            {
                GlobalLogger.Log(_bot, $"[GBP EXEC] ATR_GATE block atrPips={atrPips:F2} < {MinAtrPips:F2}");
                return;
            }

            // =====================================================
            // EXECUTION LOGIC
            // =====================================================
            var tradeType =
                entryContext.FinalDirection == TradeDirection.Long
                    ? TradeType.Buy
                    : TradeType.Sell;

            double riskPercent = _riskSizer.GetRiskPercent(adjustedRiskConfidence);
            if (riskPercent <= 0)
                return;

            double slPriceDist = CalculateStopLossPriceDistance(adjustedRiskConfidence, entry.Type);
            if (slPriceDist <= 0)
                return;

            // =====================================================
            // HARD SL FLOOR (GBPUSD) – keep TP/BE/trailing consistent
            // =====================================================
            double slPipsRaw = slPriceDist / _bot.Symbol.PipSize;
            if (slPipsRaw < MinSlPips)
            {
                GlobalLogger.Log(_bot, $"[GBP EXEC] SL_FLOOR applied slPips {slPipsRaw:F1} -> {MinSlPips:F1}");
                slPriceDist = MinSlPips * _bot.Symbol.PipSize;
            }

            _riskSizer.GetTakeProfit(
                adjustedRiskConfidence,
                out double tp1R,
                out double tp1Ratio,
                out double tp2R,
                out double tp2Ratio);

            // sizing MUST use the same slPriceDist we will actually place
            long volumeUnits = CalculateVolumeInUnits(riskPercent, slPriceDist, adjustedRiskConfidence);
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
                string errorCode = result?.Error.ToString() ?? "NA";
                string normalizedError = errorCode.ToUpperInvariant();
                string reason =
                    normalizedError.Contains("VOLUME") ? "volume_invalid" :
                    (normalizedError.Contains("SL") || normalizedError.Contains("TP") || normalizedError.Contains("STOP")) ? "sl_tp_invalid" :
                    normalizedError.Contains("MARKET") ? "market_closed" :
                    normalizedError.Contains("LIQUID") ? "no_liquidity" :
                    normalizedError.Contains("TIMEOUT") ? "timeout" :
                    (errorCode == "NA" || normalizedError.Contains("REJECT") || normalizedError.Contains("DENIED")) ? "broker_reject" :
                    "unknown";
                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[ENTRY][EXEC][FAIL] symbol={entry.Symbol ?? entryContext.Symbol ?? _bot.SymbolName} entryType={entry.Type} pipelineId={entryContext.TempId} reason={reason} errorCode={errorCode}", entryContext));
                return;
            }

            long positionKey = result.Position.Id;

            GlobalLogger.Log(_bot, $"[TRADE LINK] tempId={entryContext.TempId} posId={result.Position.Id} symbol={result.Position.SymbolName}");
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[EXEC] order placed volume={volumeUnits}", result.Position.Id, entryContext.TempId));

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

                RiskPriceDistance = slPriceDist, // now consistent with sizing & SL & TP
                PipSize = entryContext.PipSize,

                Tp1R = tp1R,
                Tp1Hit = false,
                Tp1CloseFraction = tp1Ratio,
                MarketTrend = entryContext.FinalDirection != TradeDirection.None,
                Adx_M5 = entryContext.Adx_M5,

                BeMode = BeMode.AfterTp1,

                TrailingMode =
                    adjustedRiskConfidence >= 85 ? TrailingMode.Loose :
                    adjustedRiskConfidence >= 75 ? TrailingMode.Normal :
                                                TrailingMode.Tight
            };

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

            _positionContexts[positionKey] = ctx;
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
