using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Logging;
using GeminiV26.Instruments.FX;
using GeminiV26.Core.Risk.PositionSizing;

namespace GeminiV26.Instruments.NZDUSD
{
    /// <summary>
    /// NZDUSD Instrument Executor – Phase 3.7.4
    /// - Nincs néma abort
    /// - Context teljes feltöltése
    /// - FX risk sizer használata
    /// - DEBUG log minden kritikus ponton
    /// </summary>
    public class NzdUsdInstrumentExecutor
    {
        private readonly Robot _bot;
        private readonly NzdUsdInstrumentRiskSizer _riskSizer;
        private readonly NzdUsdExitManager _exitManager;
        private readonly Dictionary<long, PositionContext> _positionContexts;
        private readonly string _botLabel;
        private readonly FxMarketStateDetector _marketStateDetector;
        private readonly NzdUsdEntryLogic _entryLogic;

        public NzdUsdInstrumentExecutor(
            Robot bot,
            NzdUsdEntryLogic entryLogic,                 // ← ÚJ
            NzdUsdInstrumentRiskSizer riskSizer,
            NzdUsdExitManager exitManager,
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
            var ms = _marketStateDetector.Evaluate();

            /*if (_marketStateDetector == null)
            {
                _bot.Print("[NZDUSD EXEC] SKIP: MarketStateDetector NULL");
                return; // vagy continue, attól függ hol vagy
            }

            if (ms == null)
            {
                _bot.Print("[NZDUSD EXEC] BLOCKED: MarketState NULL");
                return;
            }

            if (ms.IsLowVol)
            {
                _bot.Print("[NZDUSD EXEC] BLOCKED: Low volatility");
                return;
            }

            if (!ms.IsTrend)
            {
                _bot.Print("[NZDUSD EXEC] BLOCKED: No trend");
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

            _bot.Print("[NZDUSD EXEC] ExecuteEntry START");

            var tradeType =
                entryContext.FinalDirection == TradeDirection.Long
                    ? TradeType.Buy
                    : TradeType.Sell;

            // =========================================================
            // ENTRY LOGIC – PRE-EXEC CONFIDENCE (NZDUSD)
            // =========================================================
            _entryLogic.Evaluate();
            int logicConfidence = _entryLogic.LastLogicConfidence;
            var ctx = new PositionContext
            {
                Symbol = _bot.SymbolName,
                TempId = entryContext.TempId,
                EntryType = entry.Type.ToString(),
                EntryReason = entry.Reason,
                FinalDirection = entryContext.FinalDirection,
                EntryScore = entry.Score,
                LogicConfidence = logicConfidence
            };
            ctx.ComputeFinalConfidence();

            _bot.Print(TradeLogIdentity.WithTempId(TradeAuditLog.BuildEntrySnapshot(_bot, entryContext, entry, logicConfidence, ctx.FinalConfidence, statePenalty, PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty)), entryContext));

            _bot.Print(TradeLogIdentity.WithTempId(TradeAuditLog.BuildDirectionSnapshot(entryContext, entry), entryContext));

            if (statePenalty != 0)

                _bot.Print(TradeLogIdentity.WithTempId($"[SOFT_PENALTY] value={statePenalty} riskFinal={PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty)}", entryContext));

            double riskPercent = _riskSizer.GetRiskPercent(PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty));

            if (riskPercent <= 0)
            {
                _bot.Print("[NZDUSD EXEC] BLOCKED: riskPercent <= 0");
                return;
            }

            double slPriceDist = CalculateStopLossPriceDistance(PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty), entry.Type);

            if (slPriceDist <= 0)
            {
                _bot.Print("[NZDUSD EXEC] BLOCKED: SL distance invalid");
                return;
            }

            _riskSizer.GetTakeProfit(
                PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty),
                out double tp1R,
                out double tp1Ratio,
                out double tp2R,
                out double tp2Ratio);

            long volumeUnits = CalculateVolumeInUnits(riskPercent, slPriceDist, PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty));

            if (volumeUnits <= 0)
            {
                _bot.Print("[NZDUSD EXEC] BLOCKED: volume invalid");
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
                _bot.Print("[NZDUSD EXEC] Order execution FAILED");
                return;
            }

            _bot.Print($"[TRADE LINK] tempId={entryContext.TempId} posId={result.Position.Id} symbol={result.Position.SymbolName}");
            _bot.Print(TradeLogIdentity.WithPositionIds($"[EXEC] order placed volume={volumeUnits}", result.Position.Id, entryContext.TempId));

            ctx = new PositionContext
            {
                PositionId = result.Position.Id,
                Symbol = result.Position.SymbolName,
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

                // ⚠️ Trailing marad PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty) alapján
                TrailingMode =
                    PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty) >= 85 ? TrailingMode.Loose :
                    PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty) >= 75 ? TrailingMode.Normal :
                                                TrailingMode.Tight,

                EntryVolumeInUnits = result.Position.VolumeInUnits,
                RemainingVolumeInUnits = result.Position.VolumeInUnits,
                Tp2Price = tp2Price
            };

            // ✅ Kanonikus 70/30 FinalConfidence
            ctx.ComputeFinalConfidence();

            _bot.Print(TradeLogIdentity.WithPositionIds($"[EXEC][SUCCESS]\nvolumeUnits={ctx.EntryVolumeInUnits:0.##}\nentryPrice={ctx.EntryPrice:0.#####}\nsl={result.Position.StopLoss}\ntp={result.Position.TakeProfit ?? ctx.Tp2Price}", ctx, result.Position));
            _bot.Print(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildContextCreate(ctx), ctx, result.Position));
            _bot.Print(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildDirectionSnapshot(ctx), ctx, result.Position));
            _bot.Print(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildOpenSnapshot(ctx, result.Position.StopLoss, result.Position.TakeProfit ?? ctx.Tp2Price, ctx.EntryVolumeInUnits), ctx, result.Position));

            _positionContexts[ctx.PositionId] = ctx;
            _bot.Print(TradeLogIdentity.WithPositionIds($"[DIR][SET] posId={ctx.PositionId} finalDir={ctx.FinalDirection}", ctx));
            _exitManager.RegisterContext(ctx);

            _bot.Print(TradeLogIdentity.WithPositionIds($"[OPEN] entryPrice={ctx.EntryPrice}", ctx));
            _bot.Print(TradeLogIdentity.WithPositionIds(
                $"[NZDUSD EXEC] OPEN {tradeType} vol={ctx.EntryVolumeInUnits} " +
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
