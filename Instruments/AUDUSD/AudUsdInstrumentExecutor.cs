using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Instruments.FX;

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

        public void ExecuteEntry(EntryEvaluation entry)
        {
            var ms = _marketStateDetector.Evaluate();
            
            /*if (_marketStateDetector == null)
            {
                _bot.Print("[EUR EXEC] SKIP: MarketStateDetector NULL");
                return; // vagy continue, attól függ hol vagy
            }
                        
            if (ms == null)
            {
                _bot.Print("[EUR EXEC] BLOCKED: MarketState NULL");
                return;
            }
            
            if (ms.IsLowVol)
            {
                _bot.Print("[EUR EXEC] BLOCKED: Low volatility");
                return;
            }

            if (!ms.IsTrend)
            {
                _bot.Print("[EUR EXEC] BLOCKED: No trend");
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

            _bot.Print("[AUDUSD EXEC] ExecuteEntry START");

            var tradeType =
                entry.Direction == TradeDirection.Long
                    ? TradeType.Buy
                    : TradeType.Sell;

            // =========================================================
            // ENTRY LOGIC – PRE-EXEC CONFIDENCE (EURUSD)
            // =========================================================
            _entryLogic.Evaluate();
            int logicConfidence = _entryLogic.LastLogicConfidence;

            //int tempFinalConfidence =
            //    Math.Max(0, Math.Min(100, entry.Score + logicConfidence));

            int tempFinalConfidence =
                Math.Max(0, Math.Min(100,
                    entry.Score +
                    logicConfidence +
                    statePenalty
                ));

            double riskPercent = _riskSizer.GetRiskPercent(tempFinalConfidence);

            if (riskPercent <= 0)
            {
                _bot.Print("[AUDUSD EXEC] BLOCKED: riskPercent <= 0");
                return;
            }

            double slPriceDist = CalculateStopLossPriceDistance(tempFinalConfidence, entry.Type);

            if (slPriceDist <= 0)
            {
                _bot.Print("[AUDUSD EXEC] BLOCKED: SL distance invalid");
                return;
            }

            _riskSizer.GetTakeProfit(
                tempFinalConfidence,
                out double tp1R,
                out double tp1Ratio,
                out double tp2R,
                out double tp2Ratio);

            long volumeUnits = CalculateVolumeInUnits(riskPercent, slPriceDist, tempFinalConfidence);

            if (volumeUnits <= 0)
            {
                _bot.Print("[AUDUSD EXEC] BLOCKED: volume invalid");
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
                tp2Pips);

            if (!result.IsSuccessful || result.Position == null)
            {
                _bot.Print("[AUDUSD EXEC] Order execution FAILED");
                return;
            }

            var ctx = new PositionContext
            {
                PositionId = result.Position.Id,
                Symbol = result.Position.SymbolName,
                EntryType = entry.Type.ToString(),
                EntryReason = entry.Reason,

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

                // ⚠️ Trailing marad tempFinalConfidence alapján
                TrailingMode =
                    tempFinalConfidence >= 85 ? TrailingMode.Loose :
                    tempFinalConfidence >= 75 ? TrailingMode.Normal :
                                                TrailingMode.Tight,

                EntryVolumeInUnits = result.Position.VolumeInUnits,
                RemainingVolumeInUnits = result.Position.VolumeInUnits,
                Tp2Price = tp2Price
            };

            // ✅ Kanonikus 70/30 FinalConfidence
            ctx.ComputeFinalConfidence();

            _positionContexts[ctx.PositionId] = ctx;
            _exitManager.RegisterContext(ctx);
            
            _bot.Print(
                $"[AUDUSD EXEC] OPEN {tradeType} vol={ctx.EntryVolumeInUnits} " +
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
            double balance = _bot.Account.Balance;
            double riskAmount = balance * (riskPercent / 100.0);

            double slPips = slPriceDist / _bot.Symbol.PipSize;

            // AUDUSD low-vol FX: 8 pip floor túl agresszív
            const double MinSlPips_AUDUSD = 6.0;
            if (slPips < MinSlPips_AUDUSD)
                slPips = MinSlPips_AUDUSD;

            // PipValue = USD per pip per MIN UNIT
            double pipValuePerUnit = _bot.Symbol.PipValue;

            if (pipValuePerUnit <= 0)
            {
                _bot.Print("[EUR RISK] ERROR: PipValuePerUnit <= 0");
                return 0;
            }

            // ✅ UNIT alapú számítás
            double rawUnits = riskAmount / (slPips * pipValuePerUnit);

            // Lot cap → UNIT cap
            double capLots = _riskSizer.GetLotCap(score);
            double capUnits = capLots * _bot.Symbol.LotSize;

            double finalUnits = Math.Min(rawUnits, capUnits);

            long normalized = (long)_bot.Symbol.NormalizeVolumeInUnits(
                finalUnits,
                RoundingMode.Down
            );

            _bot.Print(
                $"[AUDUSD RISK FIX] risk={riskPercent:F2}% " +
                $"slPips={slPips:F1} pipValUnit={pipValuePerUnit:E5} " +
                $"rawUnits={rawUnits:F0} capUnits={capUnits:F0} finalUnits={normalized}"
            );

            return normalized < _bot.Symbol.VolumeInUnitsMin ? 0 : normalized;
        }
    }
}
