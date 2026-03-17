using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
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
                _bot.Print("[DIR][EXEC_ABORT] Missing entry");
                return;
            }

            if (entryContext == null || entryContext.FinalDirection == TradeDirection.None)
            {
                _bot.Print("[DIR][EXEC_ABORT] Missing FinalDirection");
                return;
            }

            _bot.Print($"[DIR][EXEC_FINAL] symbol={_bot.SymbolName} finalDir={entryContext.FinalDirection}");

            if (entry.Direction != entryContext.FinalDirection)
            {
                _bot.Print($"[DIR][EXEC_MISMATCH] entryDir={entry.Direction} finalDir={entryContext.FinalDirection}");
                // DO NOT TRUST entry.Direction
            }
            // =====================================================
            // FX MARKET STATE – SOFT GATE (USDJPY / FX)
            // =====================================================
            int statePenalty = 0;

            if (_marketStateDetector == null)
            {
                _bot.Print("[USDJPY EXEC] WARN: MarketStateDetector NULL");
            }
            else
            {
                var ms = _marketStateDetector.Evaluate();

                if (ms == null)
                {
                    _bot.Print("[USDJPY EXEC] WARN: MarketState NULL");
                }
                else
                {
                    if (ms.IsLowVol)
                    {
                        statePenalty -= 10;
                        _bot.Print("[USDJPY EXEC] MarketState: LowVol → penalty -10");
                    }

                    if (!ms.IsTrend)
                    {
                        statePenalty -= 10;
                        _bot.Print("[USDJPY EXEC] MarketState: NoTrend → penalty -10");
                    }
                }
            }

            // =====================================================
            // ENTRY LOGIC – PRE-EXEC CONFIDENCE (USDJPY)
            // =====================================================
            _entryLogic.Evaluate();
            int logicConfidence = _entryLogic.LastLogicConfidence;

            // =========================================================
            // FINAL CONFIDENCE (entry + logic + market state)
            // =========================================================
            int finalConfidence = PositionContext.ComputeFinalConfidenceValue(entry.Score, logicConfidence);
            int riskConfidence = PositionContext.ClampRiskConfidence(finalConfidence + statePenalty);

            var tradeType =
                entryContext.FinalDirection == TradeDirection.Long
                    ? TradeType.Buy
                    : TradeType.Sell;

            // === STOP LOSS DISTANCE ===
            double slPriceDist = CalculateStopLossPriceDistance(riskConfidence, entry.Type);

            if (slPriceDist <= 0)
                return;

            // === TP CONFIG ===
            _riskSizer.GetTakeProfit(
                riskConfidence,
                out double tp1R,
                out double tp1Ratio,
                out double tp2R,
                out double tp2Ratio);

            // === RISK ===
            double riskPercent = _riskSizer.GetRiskPercent(riskConfidence);

            if (riskPercent <= 0)
                return;

            long volumeUnits = CalculateVolumeInUnits(riskPercent, slPriceDist, riskConfidence);

            if (volumeUnits <= 0)
                return;

            _bot.Print($"[USDJPY EXEC] risk={riskPercent:F2}% slDist={slPriceDist:F5} vol={volumeUnits}");

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

            var result = _bot.ExecuteMarketOrder(
                tradeType,
                _bot.SymbolName,
                volumeUnits,
                _botLabel,
                slPips,
                tp2Pips);

            if (!result.IsSuccessful || result.Position == null)
                return;

            var ctx = new PositionContext
            {
                PositionId = result.Position.Id,
                Symbol = result.Position.SymbolName,
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

                // ⚠️ Trailing marad riskConfidence alapján
                TrailingMode =
                    riskConfidence >= 85 ? TrailingMode.Loose :
                    riskConfidence >= 75 ? TrailingMode.Normal :
                                                TrailingMode.Tight,

                EntryVolumeInUnits = result.Position.VolumeInUnits,
                RemainingVolumeInUnits = result.Position.VolumeInUnits,
                Tp2Price = tp2Price,

                MarketTrend = entryContext.FinalDirection != TradeDirection.None
            };

            // ✅ Kanonikus 70/30 FinalConfidence
            ctx.ComputeFinalConfidence();

            _positionContexts[ctx.PositionId] = ctx;
            _exitManager.RegisterContext(ctx);
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
