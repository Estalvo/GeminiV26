using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Instruments.FX;

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

        public void ExecuteEntry(EntryEvaluation entry)
        {
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
            int tempFinalConfidence =
                Math.Max(0, Math.Min(100,
                    entry.Score +
                    logicConfidence +
                    statePenalty
                ));

            var tradeType =
                entry.Direction == TradeDirection.Long
                    ? TradeType.Buy
                    : TradeType.Sell;

            // === STOP LOSS DISTANCE ===
            double slPriceDist = CalculateStopLossPriceDistance(tempFinalConfidence, entry.Type);

            if (slPriceDist <= 0)
                return;

            // === TP CONFIG ===
            _riskSizer.GetTakeProfit(
                entry.Score,
                out double tp1R,
                out double tp1Ratio,
                out double tp2R,
                out double tp2Ratio);

            // === RISK ===
            double riskPercent = _riskSizer.GetRiskPercent(tempFinalConfidence);

            if (riskPercent <= 0)
                return;

            long volumeUnits = CalculateVolumeInUnits(riskPercent, slPriceDist, tempFinalConfidence);

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
                EntryTime = _bot.Server.Time,
                EntryPrice = result.Position.EntryPrice,
                RiskPriceDistance = slPriceDist,   // 🔴 EZ HIÁNYZOTT
                Tp1R = tp1R,                       // 🔴 EZ IS
                Tp2R = tp2R,                       // 🔴 EZ IS
                Tp1Hit = false,
                Tp1CloseFraction = tp1Ratio,
                BeMode = BeMode.AfterTp1,
                TrailingMode =
                    tempFinalConfidence >= 85 ? TrailingMode.Loose :
                    tempFinalConfidence >= 75 ? TrailingMode.Normal :
                                                TrailingMode.Tight

            };

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
            double balance = _bot.Account.Balance;
            double riskAmount = balance * (riskPercent / 100.0);
            if (riskAmount <= 0)
                return 0;

            double slPips = slPriceDist / _bot.Symbol.PipSize;

            // USDJPY minimum SL
            // USDJPY mid-vol, smooth FX – 15 pip floor túl agresszív
            const double MinSlPips_USDJPY = 8.0;
            if (slPips < MinSlPips_USDJPY)
                slPips = MinSlPips_USDJPY;

            double pipValuePerLot =
                _bot.Symbol.TickValue / _bot.Symbol.TickSize * _bot.Symbol.PipSize;

            double rawLots = riskAmount / (slPips * pipValuePerLot);
            if (rawLots <= 0)
                return 0;

            double capLots = _riskSizer.GetLotCap(score);
            double finalLots = capLots > 0 ? Math.Min(rawLots, capLots) : rawLots;

            double rawUnits = finalLots * _bot.Symbol.LotSize;

            long units = (long)_bot.Symbol.NormalizeVolumeInUnits(
                rawUnits,
                RoundingMode.Down);

            if (units < _bot.Symbol.VolumeInUnitsMin)
                return 0;

            return units;
        }
    }
}
