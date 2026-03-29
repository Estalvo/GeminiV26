using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Logging;
using GeminiV26.Instruments.FX;
using GeminiV26.Core.Risk.PositionSizing;

namespace GeminiV26.Instruments.USDJPY
{
    /// <summary>
    /// USDJPY Instrument Executor – Phase 3.7
    /// STRUKTÚRA: US30 klón (végrehajtás, nem gondolkodik)
    /// </summary>
    public class UsdJpyInstrumentExecutor
    {
        private readonly Robot _bot;
        private readonly UsdJpyInstrumentRiskSizer _riskSizer;
        private readonly UsdJpyExitManager _exitManager;
        private readonly Dictionary<long, PositionContext> _positionContexts;
        private readonly string _botLabel;
        private readonly FxMarketStateDetector _marketStateDetector;
        private readonly UsdJpyEntryLogic _entryLogic;

        public UsdJpyInstrumentExecutor(
            Robot bot,
            UsdJpyEntryLogic entryLogic,          // ← ÚJ
            UsdJpyInstrumentRiskSizer riskSizer,
            UsdJpyExitManager exitManager,
            FxMarketStateDetector marketStateDetector,
            Dictionary<long, PositionContext> positionContexts,
            string botLabel)
        {
            _bot = bot;
            _entryLogic = entryLogic;             // ← ÚJ
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
            // =====================================================
            // FX MARKET STATE – SOFT GATE (USDJPY / FX)
            // =====================================================
            int statePenalty = 0;

            if (_marketStateDetector == null)
            {
                GlobalLogger.Log(_bot, "[USDJPY EXEC] WARN: MarketStateDetector NULL");
            }
            else
            {
                var ms = _marketStateDetector.Evaluate();

                if (ms == null)
                {
                    GlobalLogger.Log(_bot, "[USDJPY EXEC] WARN: MarketState NULL");
                }
                else
                {
                    if (ms.IsLowVol)
                    {
                        statePenalty -= 10;
                        GlobalLogger.Log(_bot, "[USDJPY EXEC] MarketState: LowVol → penalty -10");
                    }

                    if (!ms.IsTrend)
                    {
                        statePenalty -= 10;
                        GlobalLogger.Log(_bot, "[USDJPY EXEC] MarketState: NoTrend → penalty -10");
                    }
                }
            }

            // =====================================================
            // ENTRY LOGIC – PRE-EXEC CONFIDENCE (USDJPY)
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

            // =========================================================
            // FINAL CONFIDENCE (entry + logic + market state)
            // =========================================================
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

            var tradeType =
                entryContext.FinalDirection == TradeDirection.Long
                    ? TradeType.Buy
                    : TradeType.Sell;

            // === STOP LOSS DISTANCE ===
            double slPriceDist = CalculateStopLossPriceDistance(adjustedRiskConfidence, entry.Type);

            if (slPriceDist <= 0)
                return;

            // === TP CONFIG ===
            _riskSizer.GetTakeProfit(
                adjustedRiskConfidence,
                out double tp1R,
                out double tp1Ratio,
                out double tp2R,
                out double tp2Ratio);

            // === RISK ===
            double riskPercent = _riskSizer.GetRiskPercent(adjustedRiskConfidence);

            if (riskPercent <= 0)
                return;

            long volumeUnits = CalculateVolumeInUnits(riskPercent, slPriceDist, adjustedRiskConfidence);

            if (volumeUnits <= 0)
                return;

            GlobalLogger.Log(_bot, $"[USDJPY EXEC] risk={riskPercent:F2}% slDist={slPriceDist:F5} vol={volumeUnits}");

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

            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[EXEC][REQUEST] side={tradeType} volumeUnits={volumeUnits} slPips={slPips:0.#####} tpPips={tp2Pips:0.#####}", entryContext));

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
                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[EXEC][FAIL] side={tradeType} volumeUnits={volumeUnits} error={(result == null ? "NULL_RESULT" : result.Error.ToString())}", entryContext));
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
                Tp2Price = tp2Price,

                MarketTrend = entryContext.FinalDirection != TradeDirection.None,
                Adx_M5 = entryContext.Adx_M5
            };

            // ✅ Kanonikus 70/30 FinalConfidence
            ctx.ComputeFinalConfidence();

            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[EXEC][SUCCESS]\nvolumeUnits={ctx.EntryVolumeInUnits:0.##}\nentryPrice={ctx.EntryPrice:0.#####}\nsl={result.Position.StopLoss}\ntp={result.Position.TakeProfit ?? ctx.Tp2Price}", ctx, result.Position));
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildContextCreate(ctx), ctx, result.Position));
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildDirectionSnapshot(ctx), ctx, result.Position));
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildOpenSnapshot(ctx, result.Position.StopLoss, result.Position.TakeProfit ?? ctx.Tp2Price, ctx.EntryVolumeInUnits), ctx, result.Position));

            _positionContexts[ctx.PositionId] = ctx;
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[DIR][SET] posId={ctx.PositionId} finalDir={ctx.FinalDirection}", ctx));
            _exitManager.RegisterContext(ctx);
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[OPEN] entryPrice={ctx.EntryPrice}", ctx));
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
            return FxPositionSizer.Calculate(
                _bot,
                riskPercent,
                slPriceDist,
                _riskSizer.GetLotCap(score));
        }
    }
}
