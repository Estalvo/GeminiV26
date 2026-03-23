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
                _bot.Print("[DIR][EXEC_ABORT] Missing entry");
                return;
            }

            if (entryContext == null || entryContext.FinalDirection == TradeDirection.None)
            {
                _bot.Print("[DIR][EXEC_ABORT] Missing FinalDirection");
                return;
            }

            TradeAuditLog.EnsureAttemptId(entryContext, _bot.Server.Time);
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
                if (ms != null)
                {
                    if (ms.IsLowVol)
                        _bot.Print("[NAS][STATE] LOWVOL");

                    if (ms.IsTrend)
                        _bot.Print("[NAS][STATE] TREND");
                }
            }

            // =========================
            // ENTRY LOGIC – NAS
            // =========================
            _entryLogic.Evaluate();
            _entryLogic.ApplyToEntryEvaluation(entry);
            int logicConfidence = _entryLogic.LastLogicConfidence;

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

            int finalConfidence = PositionContext.ComputeFinalConfidenceValue(entry.Score, logicConfidence);
            int riskConfidence = PositionContext.ClampRiskConfidence(finalConfidence + statePenalty);

            _bot.Print(TradeLogIdentity.WithTempId(TradeAuditLog.BuildEntrySnapshot(_bot, entryContext, entry, logicConfidence, finalConfidence, statePenalty, riskConfidence), entryContext));

            _bot.Print(TradeLogIdentity.WithTempId(TradeAuditLog.BuildDirectionSnapshot(entryContext, entry), entryContext));

            if (statePenalty != 0)

                _bot.Print(TradeLogIdentity.WithTempId($"[SOFT_PENALTY] value={statePenalty} riskFinal={riskConfidence}", entryContext));

            // === ORIGINAL LOGIC CONTINUES ===
            var tradeType =
                entryContext.FinalDirection == TradeDirection.Long
                    ? TradeType.Buy
                    : TradeType.Sell;

            // =========================
            // RISK POLICY
            // =========================
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

            double slAtrMult = _riskSizer.GetStopLossAtrMultiplier(riskConfidence, entry.Type);
            double lotCap = _riskSizer.GetLotCap(riskConfidence);

            long volumeUnits = CalculateVolumeInUnits(riskPercent, slPriceDist, riskConfidence);

            if (volumeUnits <= 0)
                return;

            // =========================
            // LOG – KÖTELEZŐ DEBUG
            // =========================
            _bot.Print(
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

            _bot.Print(TradeLogIdentity.WithTempId($"[EXEC][REQUEST] side={tradeType} volumeUnits={volumeUnits} slPips={slPips:0.#####} tpPips={tp2Pips:0.#####}", entryContext));

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
                _bot.Print(TradeLogIdentity.WithTempId($"[EXEC][FAIL] side={tradeType} volumeUnits={volumeUnits} error={(result == null ? "NULL_RESULT" : result.Error.ToString())}", entryContext));
                return;
            }

            long positionKey = Convert.ToInt64(result.Position.Id);
            _bot.Print($"[TRADE LINK] tempId={entryContext.TempId} posId={positionKey} symbol={result.Position.SymbolName}");
            _bot.Print(TradeLogIdentity.WithPositionIds($"[EXEC] order placed volume={volumeUnits}", result.Position.Id, entryContext.TempId));

            // =========================
            // CONTEXT – TELJES, R-ALAPÚ
            // =========================
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

                RiskPriceDistance = slPriceDist,

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
                    riskConfidence >= 85 ? TrailingMode.Loose :
                    riskConfidence >= 75 ? TrailingMode.Normal :
                                                TrailingMode.Tight,

                EntryVolumeInUnits = volumeUnits,
                RemainingVolumeInUnits = volumeUnits
            };

            ctx.ComputeFinalConfidence();

            _bot.Print(TradeLogIdentity.WithPositionIds($"[EXEC][SUCCESS]\nvolumeUnits={ctx.EntryVolumeInUnits:0.##}\nentryPrice={ctx.EntryPrice:0.#####}\nsl={result.Position.StopLoss}\ntp={result.Position.TakeProfit ?? ctx.Tp2Price}", ctx, result.Position));
            _bot.Print(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildContextCreate(ctx), ctx, result.Position));
            _bot.Print(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildDirectionSnapshot(ctx), ctx, result.Position));
            _bot.Print(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildOpenSnapshot(ctx, result.Position.StopLoss, result.Position.TakeProfit ?? ctx.Tp2Price, ctx.EntryVolumeInUnits), ctx, result.Position));

            _positionContexts[positionKey] = ctx;
            _bot.Print(TradeLogIdentity.WithPositionIds($"[DIR][SET] posId={ctx.PositionId} finalDir={ctx.FinalDirection}", ctx));
            _exitManager.RegisterContext(ctx);
            _bot.Print(TradeLogIdentity.WithPositionIds($"[OPEN] entryPrice={ctx.EntryPrice}", ctx));
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
