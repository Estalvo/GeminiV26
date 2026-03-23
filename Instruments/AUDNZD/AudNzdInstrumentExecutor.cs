using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Logging;
using GeminiV26.Instruments.FX;
using GeminiV26.Core.Risk.PositionSizing;

namespace GeminiV26.Instruments.AUDNZD
{
    /// <summary>
    /// AUDNZD Instrument Executor – Phase 3.7.4
    /// - Nincs néma abort
    /// - Context teljes feltöltése
    /// - FX risk sizer használata
    /// - DEBUG log minden kritikus ponton
    /// </summary>
    public class AudNzdInstrumentExecutor
    {
        private readonly Robot _bot;
        private readonly AudNzdInstrumentRiskSizer _riskSizer;
        private readonly AudNzdExitManager _exitManager;
        private readonly Dictionary<long, PositionContext> _positionContexts;
        private readonly string _botLabel;
        private readonly FxMarketStateDetector _marketStateDetector;
        private readonly AudNzdEntryLogic _entryLogic;

        public AudNzdInstrumentExecutor(
            Robot bot,
            AudNzdEntryLogic entryLogic,                 // ← ÚJ
            AudNzdInstrumentRiskSizer riskSizer,
            AudNzdExitManager exitManager,
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
                _bot.Print("[AUDNZD EXEC] SKIP: MarketStateDetector NULL");
                return; // vagy continue, attól függ hol vagy
            }

            if (ms == null)
            {
                _bot.Print("[AUDNZD EXEC] BLOCKED: MarketState NULL");
                return;
            }

            if (ms.IsLowVol)
            {
                _bot.Print("[AUDNZD EXEC] BLOCKED: Low volatility");
                return;
            }

            if (!ms.IsTrend)
            {
                _bot.Print("[AUDNZD EXEC] BLOCKED: No trend");
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

            _bot.Print("[AUDNZD EXEC] ExecuteEntry START");

            var tradeType =
                entryContext.FinalDirection == TradeDirection.Long
                    ? TradeType.Buy
                    : TradeType.Sell;

            // =========================================================
            // ENTRY LOGIC – PRE-EXEC CONFIDENCE (AUDNZD)
            // =========================================================
            int logicConfidence = entryContext.LogicBiasConfidence;
            if (logicConfidence <= 0)
            {
                _entryLogic.Evaluate();
                logicConfidence = _entryLogic.LastLogicConfidence;
                _bot.Print($"[AUDNZD EXEC] logicConfidence fallback={logicConfidence}");
            }

            int finalConfidence = PositionContext.ComputeFinalConfidenceValue(entry.Score, logicConfidence);
            int riskConfidence = PositionContext.ClampRiskConfidence(finalConfidence + statePenalty);

            double riskPercent = _riskSizer.GetRiskPercent(riskConfidence);

            if (riskPercent <= 0)
            {
                _bot.Print("[AUDNZD EXEC] BLOCKED: riskPercent <= 0");
                return;
            }

            double slPriceDist = CalculateStopLossPriceDistance(riskConfidence, entry.Type);

            if (slPriceDist <= 0)
            {
                _bot.Print("[AUDNZD EXEC] BLOCKED: SL distance invalid");
                return;
            }

            _riskSizer.GetTakeProfit(
                riskConfidence,
                out double tp1R,
                out double tp1Ratio,
                out double tp2R,
                out double tp2Ratio);

            long volumeUnits = CalculateVolumeInUnits(riskPercent, slPriceDist, riskConfidence);

            if (volumeUnits <= 0)
            {
                _bot.Print("[AUDNZD EXEC] BLOCKED: volume invalid");
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
                _bot.Print("[AUDNZD EXEC] Order execution FAILED");
                return;
            }

            _bot.Print($"[TRADE LINK] tempId={entryContext.TempId} posId={result.Position.Id} symbol={result.Position.SymbolName}");

            var ctx = new PositionContext
            {
                PositionId = result.Position.Id,
                Symbol = result.Position.SymbolName,
                TempId = entryContext.TempId,
                EntryType = entry.Type.ToString(),
                EntryReason = entry.Reason,
                FinalDirection = entryContext.FinalDirection,

                // ✅ Rulebook-kompatibilis mezők
                EntryScore = entry.Score,
                LogicConfidence = logicConfidence,

                EntryTime = _bot.Server.Time,
                EntryPrice = result.Position.EntryPrice,
                RiskPriceDistance = slPriceDist,

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

                // ⚠️ Trailing marad riskConfidence alapján (nem változtatunk működésen)
                TrailingMode =
                    riskConfidence >= 85 ? TrailingMode.Loose :
                    riskConfidence >= 75 ? TrailingMode.Normal :
                                                TrailingMode.Tight,

                EntryVolumeInUnits = result.Position.VolumeInUnits,
                RemainingVolumeInUnits = result.Position.VolumeInUnits,
                Tp2Price = tp2Price
            };

            // ✅ Kanonikus FinalConfidence (70/30)
            ctx.ComputeFinalConfidence();

            _positionContexts[ctx.PositionId] = ctx;
            _bot.Print(TradeLogIdentity.WithPositionIds($"[DIR][SET] posId={ctx.PositionId} finalDir={ctx.FinalDirection}", ctx));
            _exitManager.RegisterContext(ctx);

            _bot.Print(TradeLogIdentity.WithPositionIds(
                $"[AUDNZD EXEC] OPEN {tradeType} vol={ctx.EntryVolumeInUnits} " +
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
