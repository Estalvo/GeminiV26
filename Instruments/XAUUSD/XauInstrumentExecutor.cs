// =========================================================
// GEMINI V26 – XAUUSD Instrument Executor
// Phase 3.7.x – RULEBOOK 1.0 COMPLIANT
//
// SZEREP:
// - XAUUSD instrument-specifikus végrehajtó
// - Mechanikus order execution (NEM dönt, NEM gate-el score alapján)
//
// ALAPELVEK:
// - Entry döntés: TradeRouter
// - Irány / confidence: XauEntryLogic
// - Környezet: XAU_MarketStateDetector
// - Risk / SL / TP policy: XauInstrumentRiskSizer
// - Trade menedzsment: XauExitManager
//
// FONTOS:
// - FinalConfidence = Single Source of Truth
// - MarketState tiltás XAU-specifikus, hard abort
// - Fix lot (Phase 3.7.x): statisztikai validálás miatt
// =========================================================

using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Logging;
using GeminiV26.Core.Risk.PositionSizing;
using GeminiV26.Instruments.METAL;

namespace GeminiV26.Instruments.XAUUSD
{
    public class XauInstrumentExecutor
    {
        private readonly Robot _bot;
        private readonly XauInstrumentRiskSizer _riskSizer;
        private readonly XauExitManager _exitManager;
        private readonly Dictionary<long, PositionContext> _positionContexts;
        private readonly string _botLabel;

        // -----------------------------------------------------
        // MarketState detector (XAU-specifikus környezetfigyelés)
        // -----------------------------------------------------
        private readonly XauMarketStateDetector _marketStateDetector;

        public XauInstrumentExecutor(
            Robot bot,
            XauInstrumentRiskSizer riskSizer,
            XauExitManager exitManager,
            Dictionary<long, PositionContext> positionContexts,
            string botLabel)
        {
            _bot = bot;
            _riskSizer = riskSizer;
            _exitManager = exitManager;
            _positionContexts = positionContexts;
            _botLabel = botLabel;

            // MarketStateDetector inicializálása
            // NEM globális, csak XAU executor használja
            _marketStateDetector = new XauMarketStateDetector(bot);
        }

        // =========================================================
        // EXECUTION – XAUUSD
        // =========================================================
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
            // =====================================================
            // 1️⃣ MARKET STATE CHECK (XAU-SPECIFIC HARD GATE)
            // -----------------------------------------------------
            // LowVol vagy HardRange esetén:
            // - NINCS score-módosítás
            // - NINCS router hack
            // - egyszerűen nem nyitunk
            // =====================================================
            if (_marketStateDetector == null)
            {
                _bot.Print("[XAU EXEC] SKIP: MarketStateDetector NULL");
                return;
            }

            var ms = _marketStateDetector.Evaluate();

            // =========================
            // MARKET STATE – SOFT (XAU)
            // =========================
            int statePenalty = 0;

            if (ms.IsLowVol)
                statePenalty -= 15;     // XAU low vol veszélyes

            if (ms.IsHardRange)
                statePenalty -= 20;     // range-ben nem szeretjük

            if (ms.IsTrend)
                statePenalty += 5;      // pici bónusz

            // =====================================================
            // 2️⃣ TRADE TYPE (router döntése alapján)
            // =====================================================
            var tradeType =
                entryContext.FinalDirection == TradeDirection.Long
                    ? TradeType.Buy
                    : TradeType.Sell;

            // =====================================================
            // 3️⃣ POSITION CONTEXT – SSOT
            // -----------------------------------------------------
            // A context minden adatot tartalmaz,
            // amire az ExitManagernek szüksége lesz.
            // =====================================================
            var ctx = new PositionContext
            {
                Symbol = _bot.SymbolName,
                TempId = entryContext.TempId,
                EntryType = entry.Type.ToString(),
                EntryReason = entry.Reason,
                FinalDirection = entryContext.FinalDirection,

                EntryScore = entry.Score,

                LogicConfidence = entry.LogicConfidence > 0
                    ? entry.LogicConfidence
                    : entry.Score,

                EntryTime = _bot.Server.Time,

                // Pre-exec becslés (fill után pontosítjuk)
                EntryPrice = tradeType == TradeType.Buy
                    ? _bot.Symbol.Ask
                    : _bot.Symbol.Bid,
                PipSize = entryContext.PipSize > 0 ? entryContext.PipSize : _bot.Symbol.PipSize,

                // TP1 policy (Phase 3.7.x – determinisztikus XAU)
                Tp1Hit = false,
                Tp1CloseFraction = 0.40,
                Tp1R = 0.25,

                MarketTrend = entryContext.FinalDirection != TradeDirection.None,
                Adx_M5 = entryContext.Adx_M5,

                InitialStopLossR = 1.0,
                BeMode = BeMode.AfterTp1
            };

            // -----------------------------------------------------
            // FinalConfidence összeállítása (Rulebook 1.0)
            // -----------------------------------------------------
            ctx.ComputeFinalConfidence();

                        _bot.Print(TradeLogIdentity.WithTempId(TradeAuditLog.BuildEntrySnapshot(_bot, entryContext, entry, ctx.LogicConfidence, ctx.FinalConfidence, statePenalty, PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty)), entryContext));
            _bot.Print(TradeLogIdentity.WithTempId(TradeAuditLog.BuildDirectionSnapshot(entryContext, entry), entryContext));
            if (statePenalty != 0)
                _bot.Print(TradeLogIdentity.WithTempId($"[SOFT_PENALTY] value={statePenalty} riskFinal={PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty)}", entryContext));

            // Trailing mód PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty) alapján (FinalConfidence + statePenalty)
            ctx.TrailingMode =
                PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty) >= 85 ? TrailingMode.Loose :
                PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty) >= 75 ? TrailingMode.Normal :
                                             TrailingMode.Tight;

            // =====================================================
            // 4️⃣ SL / TP POLICY (PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty))
            // =====================================================
            double slPriceDist = _riskSizer.CalculateStopLossPriceDistance(
                _bot,
                PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty),
                entry.Type);

            if (slPriceDist <= 0)
            {
                _bot.Print("[XAU EXEC] SL distance invalid → abort");
                return;
            }

            double tp2Price = _riskSizer.CalculateTp2PriceFromSlDistance(
                _bot,
                tradeType,
                PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty),
                slPriceDist);

            if (tp2Price <= 0)
            {
                _bot.Print("[XAU EXEC] TP2 price invalid → abort");
                return;
            }

            // =====================================================
            // 5️⃣ TP / R VALUES (EURUSD MINTA SZERINT)
            // =====================================================
            _riskSizer.GetTakeProfit(
                PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty),
                out double tp1R,
                out double tp1Ratio,
                out double tp2R,
                out double tp2Ratio
            );

            // Context feltöltés – NINCS újraszámolás
            ctx.Tp1R = tp1R;
            ctx.Tp1Ratio = tp1Ratio;
            ctx.Tp2R = tp2R;
            ctx.Tp2Ratio = tp2Ratio;

            // =====================================================
            // 6️⃣ VOLUME POLICY – METAL POSITION SIZER (XAU)
            // =====================================================
            double riskPercent = _riskSizer.GetRiskPercent(PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty));
            long volumeUnits = MetalPositionSizer.Calculate(
                _bot,
                riskPercent,
                slPriceDist,
                _riskSizer.GetLotCap(PositionContext.ClampRiskConfidence(ctx.FinalConfidence + statePenalty))
            );

            if (volumeUnits <= 0)
            {
                _bot.Print("[XAU EXEC] Volume invalid after MetalPositionSizer → abort");
                return;
            }
            // 🔎 DEBUG (EXECUTOR SZINT)
            _bot.Print($"[XAU EXEC RISK] FC={ctx.FinalConfidence} slDist={slPriceDist:F2} units={volumeUnits} lot={(double)volumeUnits/_bot.Symbol.LotSize:F2}");
/*
            // =====================================================
            // 6 VOLUME POLICY – FIX LOT (Phase 3.7.x)
            // -----------------------------------------------------
            // Cél: tiszta statisztika TP1 / trailing validálásához.
            // Risk-alapú sizing később visszakapcsolható.
            // =====================================================
            double fixedLot = 0.10;
            long volumeUnits = (long)_bot.Symbol.QuantityToVolumeInUnits(fixedLot);

            if (volumeUnits <= 0)
            {
                _bot.Print("[XAU EXEC] Volume invalid → abort");
                return;
            }
*/
            // =====================================================
            // 7 SL / TP → pips (pre-exec)
            // =====================================================
            double slPips = slPriceDist / _bot.Symbol.PipSize;
            double tp2Pips = Math.Abs(tp2Price - ctx.EntryPrice) / _bot.Symbol.PipSize;

            // =====================================================
            // 8 EXECUTE ORDER
            // =====================================================
            _bot.Print(TradeLogIdentity.WithTempId($"[EXEC][REQUEST] side={tradeType} volumeUnits={volumeUnits} slPips={slPips:0.#####} tpPips={tp2Pips:0.#####}", entryContext));

            var result = _bot.ExecuteMarketOrder(
                tradeType,
                _bot.SymbolName,
                volumeUnits,
                _botLabel,
                slPips,
                tp2Pips,
                entryContext.TempId
            );

            if (!result.IsSuccessful || result.Position == null)
            {
                _bot.Print(TradeLogIdentity.WithTempId($"[EXEC][FAIL] side={tradeType} volumeUnits={volumeUnits} error={(result == null ? "NULL_RESULT" : result.Error.ToString())}", entryContext));
                _bot.Print("[XAU EXEC] Order execution failed");
                return;
            }

            _bot.Print($"[TRADE LINK] tempId={entryContext.TempId} posId={result.Position.Id} symbol={result.Position.SymbolName}");
            _bot.Print(TradeLogIdentity.WithPositionIds($"[EXEC] order placed volume={volumeUnits}", result.Position.Id, entryContext.TempId));

            // =====================================================
            // 9 CONTEXT FINALIZÁLÁS (FILL UTÁN)
            // =====================================================
            ctx.PositionId = result.Position.Id;
            ctx.EntryPrice = result.Position.EntryPrice;
            ctx.PipSize = entryContext.PipSize > 0 ? entryContext.PipSize : _bot.Symbol.PipSize;

            ctx.EntryVolumeInUnits = result.Position.VolumeInUnits;
            ctx.RemainingVolumeInUnits = result.Position.VolumeInUnits;
            ctx.InitialVolumeInUnits = ctx.EntryVolumeInUnits;

            // --- SL (kanonikus ár)
            double slPriceActual = result.Position.StopLoss ??
                (tradeType == TradeType.Buy
                    ? ctx.EntryPrice - slPriceDist
                    : ctx.EntryPrice + slPriceDist);

            double rDist = Math.Abs(ctx.EntryPrice - slPriceActual);

            // --- TP1 (fix R)
            ctx.Tp1Price = tradeType == TradeType.Buy
                ? ctx.EntryPrice + rDist * ctx.Tp1R
                : ctx.EntryPrice - rDist * ctx.Tp1R;

            // --- TP2
            ctx.Tp2Price = result.Position.TakeProfit.HasValue
                ? Convert.ToDouble(result.Position.TakeProfit.Value)
                : Convert.ToDouble(tp2Price);

            // =====================================================
            // 10 REGISTER CONTEXT
            // =====================================================
            _bot.Print(TradeLogIdentity.WithPositionIds($"[EXEC][SUCCESS]\nvolumeUnits={ctx.EntryVolumeInUnits:0.##}\nentryPrice={ctx.EntryPrice:0.#####}\nsl={result.Position.StopLoss}\ntp={result.Position.TakeProfit ?? ctx.Tp2Price}", ctx, result.Position));
            _bot.Print(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildContextCreate(ctx), ctx, result.Position));
            _bot.Print(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildDirectionSnapshot(ctx), ctx, result.Position));
            _bot.Print(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildOpenSnapshot(ctx, result.Position.StopLoss, result.Position.TakeProfit ?? ctx.Tp2Price, ctx.EntryVolumeInUnits), ctx, result.Position));

            _positionContexts[ctx.PositionId] = ctx;
            _bot.Print(TradeLogIdentity.WithPositionIds($"[DIR][SET] posId={ctx.PositionId} finalDir={ctx.FinalDirection}", ctx));
            _exitManager.RegisterContext(ctx);

            _bot.Print(TradeLogIdentity.WithPositionIds($"[OPEN] entryPrice={ctx.EntryPrice}", ctx));
            _bot.Print(TradeLogIdentity.WithPositionIds(
                $"[XAU EXEC] OPEN {tradeType} vol={ctx.EntryVolumeInUnits} " +
                $"FC={ctx.FinalConfidence} fill={ctx.EntryPrice:F2} " +
                $"SL={slPriceActual:F2} R={rDist:F2} " +
                $"TP1={ctx.Tp1Price:F2} TP2={ctx.Tp2Price:F2}", ctx));
        }
    }
}
