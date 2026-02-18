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
        public void ExecuteEntry(EntryEvaluation entry)
        {
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
                entry.Direction == TradeDirection.Long
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
                EntryType = entry.Type.ToString(),
                EntryReason = entry.Reason,

                EntryScore = entry.Score,
                LogicConfidence = 0,

                EntryTime = _bot.Server.Time,

                // Pre-exec becslés (fill után pontosítjuk)
                EntryPrice = tradeType == TradeType.Buy
                    ? _bot.Symbol.Ask
                    : _bot.Symbol.Bid,

                // TP1 policy (Phase 3.7.x – determinisztikus XAU)
                Tp1Hit = false,
                Tp1CloseFraction = 0.40,
                Tp1R = 0.25,

                InitialStopLossR = 1.0,
                BeMode = BeMode.AfterTp1
            };

            // -----------------------------------------------------
            // FinalConfidence összeállítása (Rulebook 1.0)
            // -----------------------------------------------------
            ctx.ComputeFinalConfidence();

            // ⬇️ XAU MarketState SOFT influence
            ctx.FinalConfidence += statePenalty;
            ctx.FinalConfidence = Math.Max(0, ctx.FinalConfidence);

            // Trailing mód kizárólag FinalConfidence alapján
            ctx.TrailingMode =
                ctx.FinalConfidence >= 85 ? TrailingMode.Loose :
                ctx.FinalConfidence >= 75 ? TrailingMode.Normal :
                                             TrailingMode.Tight;

            // =====================================================
            // 4️⃣ SL / TP POLICY (FinalConfidence)
            // =====================================================
            double slPriceDist = _riskSizer.CalculateStopLossPriceDistance(
                _bot,
                ctx.FinalConfidence,
                entry.Type);

            if (slPriceDist <= 0)
            {
                _bot.Print("[XAU EXEC] SL distance invalid → abort");
                return;
            }

            double tp2Price = _riskSizer.CalculateTp2PriceFromSlDistance(
                _bot,
                tradeType,
                ctx.FinalConfidence,
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
                ctx.FinalConfidence,
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
            // 6️⃣ VOLUME POLICY – RISK SIZER (XAU)
            // =====================================================
            long volumeUnits = _riskSizer.CalculateVolumeInUnits(
                _bot,
                ctx.FinalConfidence,
                slPriceDist
            );

            if (volumeUnits <= 0)
            {
                _bot.Print("[XAU EXEC] Volume invalid after RiskSizer → abort");
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
            var result = _bot.ExecuteMarketOrder(
                tradeType,
                _bot.SymbolName,
                volumeUnits,
                _botLabel,
                slPips,
                tp2Pips
            );

            if (!result.IsSuccessful || result.Position == null)
            {
                _bot.Print("[XAU EXEC] Order execution failed");
                return;
            }

            // =====================================================
            // 9 CONTEXT FINALIZÁLÁS (FILL UTÁN)
            // =====================================================
            ctx.PositionId = result.Position.Id;
            ctx.EntryPrice = result.Position.EntryPrice;

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
            _positionContexts[ctx.PositionId] = ctx;
            _exitManager.RegisterContext(ctx);

            _bot.Print(
                $"[XAU EXEC] OPEN {tradeType} vol={ctx.EntryVolumeInUnits} " +
                $"FC={ctx.FinalConfidence} fill={ctx.EntryPrice:F2} " +
                $"SL={slPriceActual:F2} R={rDist:F2} " +
                $"TP1={ctx.Tp1Price:F2} TP2={ctx.Tp2Price:F2}"
            );
        }
    }
}
