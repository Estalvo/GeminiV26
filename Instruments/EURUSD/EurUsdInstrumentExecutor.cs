using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Instruments.FX;
using GeminiV26.Core.Risk.PositionSizing;

namespace GeminiV26.Instruments.EURUSD
{
    /// <summary>
    /// EURUSD Instrument Executor – Phase 3.9
    /// - Nincs néma abort
    /// - Context teljes feltöltése
    /// - FX risk sizer használata
    /// - DEBUG log minden kritikus ponton
    /// </summary>
    public class EurUsdInstrumentExecutor
    {
        private readonly Robot _bot;
        private readonly EurUsdInstrumentRiskSizer _riskSizer;
        private readonly EurUsdExitManager _exitManager;
        private readonly Dictionary<long, PositionContext> _positionContexts;
        private readonly string _botLabel;
        private readonly FxMarketStateDetector _marketStateDetector;
        private readonly EurUsdEntryLogic _entryLogic;

        public EurUsdInstrumentExecutor(
            Robot bot,
            EurUsdEntryLogic entryLogic,                 // ← ÚJ
            EurUsdInstrumentRiskSizer riskSizer,
            EurUsdExitManager exitManager,
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

            _bot.Print($"[DIR][EXEC_FINAL] symbol={_bot.SymbolName} finalDir={entryContext.FinalDirection}");

            if (entry.Direction != entryContext.FinalDirection)
            {
                _bot.Print($"[DIR][EXEC_MISMATCH] entryDir={entry.Direction} finalDir={entryContext.FinalDirection}");
                // DO NOT TRUST entry.Direction
            }
            var ms = _marketStateDetector?.Evaluate();

            _bot.Print(
                $"[EUR EXEC] ENTRY RECEIVED type={entry.Type} finalDir={entryContext.FinalDirection} score={entry.Score} reason={entry.Reason}"
            );
            
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

            _bot.Print("[EUR EXEC] ExecuteEntry START");

            var tradeType =
                entryContext.FinalDirection == TradeDirection.Long
                    ? TradeType.Buy
                    : TradeType.Sell;

            // =========================================================
            // ENTRY LOGIC – PRE-EXEC CONFIDENCE (EURUSD)
            // =========================================================
            _entryLogic.Evaluate();
            int logicConfidence = _entryLogic.LastLogicConfidence;
            int finalConfidence = PositionContext.ComputeFinalConfidenceValue(entry.Score, logicConfidence);
            int riskConfidence = PositionContext.ClampRiskConfidence(finalConfidence + statePenalty);

            _bot.Print(
                $"[EUR EXEC] CONF entryScore={entry.Score} logic={logicConfidence} final={finalConfidence} statePenalty={statePenalty} riskConf={riskConfidence}"
            );

            double riskPercent = _riskSizer.GetRiskPercent(riskConfidence);

            if (riskPercent <= 0)
            {
                _bot.Print("[EUR EXEC] BLOCKED: riskPercent <= 0");
                return;
            }

            double slPriceDist = CalculateStopLossPriceDistance(riskConfidence, entry.Type);

            if (slPriceDist <= 0)
            {
                _bot.Print("[EUR EXEC] BLOCKED: SL distance invalid");
                return;
            }

            _riskSizer.GetTakeProfit(
                riskConfidence,
                out double tp1R,
                out double tp1Ratio,
                out double tp2R,
                out double tp2Ratio);

            long volumeUnits = CalculateVolumeInUnits(riskPercent, slPriceDist, riskConfidence);

            _bot.Print(
                $"[EUR EXEC] RISK risk%={riskPercent:F3} slDist={slPriceDist:F5} volume={volumeUnits}"
            );
            
            if (volumeUnits <= 0)
            {
                _bot.Print("[EUR EXEC] BLOCKED: volume invalid");
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

            _bot.Print(
                $"[EUR EXEC] SEND ORDER type={tradeType} vol={volumeUnits} slPips={slPips:F1} tp2Pips={tp2Pips:F1}"
            );
                
            var result = _bot.ExecuteMarketOrder(
                tradeType,
                _bot.SymbolName,
                volumeUnits,
                _botLabel,
                slPips,
                tp2Pips);
                    
            if (!result.IsSuccessful || result.Position == null)
            {
                _bot.Print("[EUR EXEC] Order execution FAILED");
                return;
            }

            var ctx = new PositionContext
            {
                PositionId = result.Position.Id,
                Symbol = result.Position.SymbolName,
                EntryType = entry.Type.ToString(),
                EntryReason = entry.Reason,
                FinalDirection = entryContext.FinalDirection,

                // ✅ Rulebook mezők rendbetéve (viselkedést nem változtat)
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

                // maradhat így, hogy ne változzon a működés
                TrailingMode =
                    riskConfidence >= 85 ? TrailingMode.Loose :
                    riskConfidence >= 75 ? TrailingMode.Normal :
                                                TrailingMode.Tight,

                EntryVolumeInUnits = result.Position.VolumeInUnits,
                RemainingVolumeInUnits = result.Position.VolumeInUnits,
                Tp2Price = tp2Price,

                MarketTrend = entryContext.FinalDirection != TradeDirection.None
            };

            // ✅ 1 sor, safe: kanonikus FinalConfidence kiszámolása (CSV/analytics)
            ctx.ComputeFinalConfidence();

            _positionContexts[ctx.PositionId] = ctx;
            _exitManager.RegisterContext(ctx);

            _bot.Print(
                $"[EUR EXEC] OPEN {tradeType} vol={ctx.EntryVolumeInUnits} " +
                $"score={entry.Score} SLpips={slPips:F1} TP2={tp2Price:F5}"
            );            
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
