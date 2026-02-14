// =========================================================
// GEMINI V26 – BTCUSD InstrumentExecutor
// Phase 3.7.3 – RULEBOOK 1.0 COMPLIANT
// =========================================================

using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
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

        public void ExecuteEntry(EntryEvaluation entry)
        {
            _bot.Print("[BTCUSD][EXEC] ExecuteEntry");

            // =========================
            // MARKET STATE (OBSERVE ONLY)
            // =========================
            _marketStateDetector?.Evaluate();

            var tradeType =
                entry.Direction == TradeDirection.Long
                    ? TradeType.Buy
                    : TradeType.Sell;

            // =========================================================
            // 🔑 ENTRY LOGIC – PRE-EXEC CONFIDENCE
            // =========================================================
            _entryLogic.Evaluate(out _, out int logicConfidence);

            // 🔒 safety clamp – ONLY for BTC executor
            logicConfidence = Math.Max(-20, Math.Min(20, logicConfidence));

            int tempFinalConfidence = Clamp01to100(entry.Score + logicConfidence);

            // =========================================================
            // SL DISTANCE (ATR)
            // =========================================================
            double slPriceDist =
                CalculateStopLossPriceDistance(tempFinalConfidence, entry.Type);

            if (slPriceDist <= 0)
                return;

            double slPips = slPriceDist / _bot.Symbol.PipSize;
            if (slPips <= 0)
                return;

            // =========================================================
            // RISK-BASED VOLUME (BTC – price-value aware)
            // =========================================================
            double riskPercent = _riskSizer.GetRiskPercent(tempFinalConfidence);
            double balance = _bot.Account.Balance;
            double riskMoney = balance * (riskPercent / 100.0);

            // value of 1 price unit move per 1 volume unit
            double valuePerUnitPerPrice = 1.0;


            if (valuePerUnitPerPrice <= 0 || double.IsNaN(valuePerUnitPerPrice))
            {
                _bot.Print("[BTCUSD][EXEC] abort: invalid valuePerUnitPerPrice");
                return;
            }

            double lossPerUnit = slPriceDist * valuePerUnitPerPrice;
            if (lossPerUnit <= 0 || double.IsNaN(lossPerUnit))
            {
                _bot.Print("[BTCUSD][EXEC] abort: invalid lossPerUnit");
                return;
            }
                        
            double rawUnits = riskMoney / lossPerUnit;

            // =========================================================
            // SCORE-BASED RISK MONOTONICITY GUARD (CRYPTO)
            // =========================================================
            double score = tempFinalConfidence;

            double scoreRiskMult =                
                score < 45 ? 0.70 :
                score < 55 ? 0.85 :
                score < 65 ? 0.95 :
                             1.00;

            rawUnits *= scoreRiskMult;

            // =========================================================
            // CRYPTO SCORE SAFETY GUARD (HARD CAP)
            // low score MUST NOT produce large size
            // =========================================================
            double minUnits = _bot.Symbol.VolumeInUnitsMin;
            double maxLowScoreUnits = minUnits * 3;

            if (tempFinalConfidence < 45 &&
                rawUnits > maxLowScoreUnits)
            {
                _bot.Print(
                    $"[BTCUSD][RISK] score={tempFinalConfidence} rawUnits capped " +
                    $"from {rawUnits:F6} to {maxLowScoreUnits:F6}"
                );

                rawUnits = maxLowScoreUnits;
            }

            if (rawUnits < _bot.Symbol.VolumeInUnitsMin)
            {
                _bot.Print(
                    $"[BTCUSD][EXEC] rawUnits too small → abort " +
                    $"raw={rawUnits:F6} min={_bot.Symbol.VolumeInUnitsMin}"
                );
                return;
            }

            // ⚠️ IMPORTANT: lotCap MUST be in units
            double lotCapUnits = _riskSizer.GetLotCap(tempFinalConfidence);
            double cappedUnits = Math.Min(rawUnits, lotCapUnits);

            double volumeUnits =
                _bot.Symbol.NormalizeVolumeInUnits(cappedUnits);

            if (volumeUnits < _bot.Symbol.VolumeInUnitsMin)
            {
                _bot.Print(
                    $"[BTCUSD][EXEC] abort: volume < min " +
                    $"(vol={volumeUnits}, min={_bot.Symbol.VolumeInUnitsMin})"
                );
                return;
            }

            // =========================================================
            // TP POLICY
            // =========================================================
            _riskSizer.GetTakeProfit(
                tempFinalConfidence,
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
                return;

            _bot.Print(
                $"[BTC RISK] score={entry.Score} logicConf={logicConfidence} FC={tempFinalConfidence} " +
                $"risk%={riskPercent:F2} slDist={slPriceDist:F2} slPips={slPips:F1} " +
                $"rawUnits={rawUnits:F0} cap={lotCapUnits:F0} volUnits={volumeUnits}"
            );

            // =========================================================
            // SEND ORDER
            // =========================================================
            var result = _bot.ExecuteMarketOrder(
                tradeType,
                _bot.SymbolName,
                volumeUnits,
                _botLabel,
                slPips,
                tp2Pips);

            if (!result.IsSuccessful || result.Position == null)
            {
                _bot.Print("[BTCUSD][EXEC] ORDER FAILED (TradeResult unsuccessful or Position null)");
                _bot.Print($"[BTCUSD][EXEC] ORDER FAILED isSuccessful={result.IsSuccessful}");
                return;
            }


            long posId = result.Position.Id;

            // =========================================================
            // POSITION CONTEXT (SSOT)
            // =========================================================
            var ctx = new PositionContext
            {
                PositionId = posId,
                Symbol = result.Position.SymbolName,

                EntryType = entry.Type.ToString(),
                EntryReason = entry.Reason,

                EntryScore = entry.Score,
                LogicConfidence = logicConfidence,

                EntryTime = _bot.Server.Time,
                EntryPrice = result.Position.EntryPrice,
                RiskPriceDistance = slPriceDist,

                EntryVolumeInUnits = result.Position.VolumeInUnits,
                RemainingVolumeInUnits = result.Position.VolumeInUnits,

                Tp1R = tp1R,
                Tp1CloseFraction = tp1Ratio,
                Tp1Hit = false,

                BeMode = BeMode.AfterTp1,
                Tp2Price = tp2Price
            };

            ctx.Tp1Price =
                tradeType == TradeType.Buy
                    ? ctx.EntryPrice + slPriceDist * ctx.Tp1R
                    : ctx.EntryPrice - slPriceDist * ctx.Tp1R;

            // 🔒 FINAL CONFIDENCE
            ctx.ComputeFinalConfidence();

            ctx.TrailingMode =
                ctx.FinalConfidence >= 85 ? TrailingMode.Loose :
                ctx.FinalConfidence >= 75 ? TrailingMode.Normal :
                                             TrailingMode.Tight;

            _positionContexts[posId] = ctx;
            _exitManager.RegisterContext(ctx);

            _bot.Print(
                $"[BTCUSD][EXEC] OPEN {tradeType} " +
                $"vol={ctx.EntryVolumeInUnits} FC={ctx.FinalConfidence}"
            );
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
