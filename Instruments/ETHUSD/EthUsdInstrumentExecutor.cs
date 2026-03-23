// =========================================================
// GEMINI V26 – ETHUSD InstrumentExecutor
// Phase 3.7.3 – RULEBOOK 1.0 COMPLIANT
// =========================================================

using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Logging;
using GeminiV26.Core.Risk.PositionSizing;
using GeminiV26.Instruments.CRYPTO;
using GeminiV26.Instruments.ETHUSD;

namespace GeminiV26.Instruments.ETHUSD
{
    public class EthUsdInstrumentExecutor
    {
        private readonly Robot _bot;
        private readonly EthUsdInstrumentRiskSizer _riskSizer;
        private readonly EthUsdExitManager _exitManager;
        private readonly Dictionary<long, PositionContext> _positionContexts;
        private readonly string _botLabel;

        // 🔑 ENTRY LOGIC (ÚJ – ténylegesen használt)
        private readonly EthUsdEntryLogic _entryLogic;

        private readonly CryptoMarketStateDetector _marketStateDetector;

        public EthUsdInstrumentExecutor(
            Robot bot,
            EthUsdEntryLogic entryLogic,
            EthUsdInstrumentRiskSizer riskSizer,
            EthUsdExitManager exitManager,
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
            _bot.Print("[ETHUSD][EXEC] ExecuteEntry");

            // =========================
            // MARKET STATE (OBSERVE ONLY)
            // =========================
            _marketStateDetector?.Evaluate();

            var tradeType =
                entryContext.FinalDirection == TradeDirection.Long
                    ? TradeType.Buy
                    : TradeType.Sell;

            // =========================================================
            // 🔑 ENTRY LOGIC – PRE-EXEC CONFIDENCE
            // =========================================================
            _entryLogic.Evaluate(out _, out int logicConfidence);
            int statePenalty = 0;

            int finalConfidence = PositionContext.ComputeFinalConfidenceValue(entry.Score, logicConfidence);
            int riskConfidence = PositionContext.ClampRiskConfidence(finalConfidence);

            _bot.Print(TradeLogIdentity.WithTempId(TradeAuditLog.BuildEntrySnapshot(_bot, entryContext, entry, logicConfidence, finalConfidence, statePenalty, riskConfidence), entryContext));

            _bot.Print(TradeLogIdentity.WithTempId(TradeAuditLog.BuildDirectionSnapshot(entryContext, entry), entryContext));

            if (statePenalty != 0)

                _bot.Print(TradeLogIdentity.WithTempId($"[SOFT_PENALTY] value={statePenalty} riskFinal={riskConfidence}", entryContext));

            // =========================================================
            // SL DISTANCE (ATR)
            // =========================================================
            double slPriceDist =
                CalculateStopLossPriceDistance(riskConfidence, entry.Type);

            if (slPriceDist <= 0)
                return;

            double slPips = slPriceDist / _bot.Symbol.PipSize;
            if (slPips <= 0)
                return;

            // =========================================================
            // RISK-BASED VOLUME (CRYPTO POSITION SIZER)
            // =========================================================
            double riskPercent = _riskSizer.GetRiskPercent(riskConfidence);
            long volumeUnits = CryptoPositionSizer.Calculate(
                _bot,
                riskPercent,
                slPriceDist,
                _riskSizer.GetLotCap(riskConfidence));

            if (volumeUnits < _bot.Symbol.VolumeInUnitsMin)
            {
                _bot.Print(
                    $"[ETHUSD][EXEC] abort: volume < min " +
                    $"(vol={volumeUnits}, min={_bot.Symbol.VolumeInUnitsMin})"
                );
                return;
            }

            // =========================================================
            // TP POLICY
            // =========================================================
            _riskSizer.GetTakeProfit(
                riskConfidence,
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
                $"[ETH RISK] score={entry.Score} logicConf={logicConfidence} FC={riskConfidence} " +
                $"risk%={riskPercent:F2} slDist={slPriceDist:F2} slPips={slPips:F1} " +
                $"volUnits={volumeUnits}"
            );

            // =========================================================
            // SEND ORDER
            // =========================================================
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
                _bot.Print("[ETHUSD][EXEC] ORDER FAILED (TradeResult unsuccessful or Position null)");
                _bot.Print($"[ETHUSD][EXEC] ORDER FAILED isSuccessful={result.IsSuccessful}");
                return;
            }


            long posId = result.Position.Id;
            _bot.Print($"[TRADE LINK] tempId={entryContext.TempId} posId={posId} symbol={result.Position.SymbolName}");
            _bot.Print(TradeLogIdentity.WithPositionIds($"[EXEC] order placed volume={volumeUnits}", result.Position.Id, entryContext.TempId));

            // =========================================================
            // POSITION CONTEXT (SSOT)
            // =========================================================
            var ctx = new PositionContext
            {
                PositionId = posId,
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
                PipSize = entryContext.PipSize,

                EntryVolumeInUnits = result.Position.VolumeInUnits,
                RemainingVolumeInUnits = result.Position.VolumeInUnits,

                Tp1R = tp1R,
                Tp1CloseFraction = tp1Ratio,
                Tp1Hit = false,

                BeMode = BeMode.AfterTp1,
                Tp2Price = tp2Price,

                MarketTrend = entryContext.FinalDirection != TradeDirection.None,
                Adx_M5 = entryContext.Adx_M5
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

            _bot.Print(TradeLogIdentity.WithPositionIds($"[EXEC][SUCCESS]\nvolumeUnits={ctx.EntryVolumeInUnits:0.##}\nentryPrice={ctx.EntryPrice:0.#####}\nsl={result.Position.StopLoss}\ntp={result.Position.TakeProfit ?? ctx.Tp2Price}", ctx, result.Position));
            _bot.Print(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildContextCreate(ctx), ctx, result.Position));
            _bot.Print(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildDirectionSnapshot(ctx), ctx, result.Position));
            _bot.Print(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildOpenSnapshot(ctx, result.Position.StopLoss, result.Position.TakeProfit ?? ctx.Tp2Price, ctx.EntryVolumeInUnits), ctx, result.Position));

            _positionContexts[posId] = ctx;
            _bot.Print(TradeLogIdentity.WithPositionIds($"[DIR][SET] posId={ctx.PositionId} finalDir={ctx.FinalDirection}", ctx));
            _exitManager.RegisterContext(ctx);

            _bot.Print(TradeLogIdentity.WithPositionIds($"[OPEN] entryPrice={ctx.EntryPrice}", ctx));
            _bot.Print(TradeLogIdentity.WithPositionIds(
                $"[ETHUSD][EXEC] OPEN {tradeType} " +
                $"vol={ctx.EntryVolumeInUnits} FC={ctx.FinalConfidence}", ctx));
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
