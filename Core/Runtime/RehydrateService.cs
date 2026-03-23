using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core.Context;
using GeminiV26.Core.Entry;

namespace GeminiV26.Core.Runtime
{
    /// <summary>
    /// Startup-only recovery layer.
    /// Rebuilds the minimum safe PositionContext state for still-open Gemini positions.
    /// No entry, risk, SL/TP, or volume sizing logic belongs here.
    /// </summary>
    public sealed class RehydrateService
    {
        private readonly Robot _bot;
        private readonly Dictionary<long, PositionContext> _registry;
        private readonly ContextRegistry _contextRegistry;
        private readonly string _botLabel;
        private readonly Func<PositionContext, bool> _registerExitContext;
        private readonly string _currentSymbolCanonical;

        public RehydrateService(
            Robot bot,
            Dictionary<long, PositionContext> registry,
            ContextRegistry contextRegistry,
            string botLabel,
            Func<PositionContext, bool> registerExitContext)
        {
            _bot = bot ?? throw new ArgumentNullException(nameof(bot));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _contextRegistry = contextRegistry ?? throw new ArgumentNullException(nameof(contextRegistry));
            _botLabel = botLabel ?? string.Empty;
            _registerExitContext = registerExitContext ?? throw new ArgumentNullException(nameof(registerExitContext));
            _currentSymbolCanonical = SymbolRouting.NormalizeSymbol(_bot.SymbolName);
        }

        public RehydrateSummary Run()
        {
            var summary = new RehydrateSummary();

            foreach (var position in _bot.Positions)
            {
                summary.TotalOpenPositionsSeen++;

                try
                {
                    ProcessPosition(position, summary);
                }
                catch (Exception ex)
                {
                    summary.Failed++;
                    _bot.Print(
                        $"[REHYDRATE_WARN] pos={(position == null ? "NULL" : Convert.ToInt64(position.Id).ToString())} " +
                        $"symbol={position?.SymbolName ?? "UNKNOWN"} error={ex.GetType().Name} message={ex.Message}");
                    _bot.Print(
                        $"[REHYDRATE_SKIP] pos={(position == null ? "NULL" : Convert.ToInt64(position.Id).ToString())} " +
                        $"symbol={position?.SymbolName ?? "UNKNOWN"} reason=exception_during_rebuild unmanaged=true");
                }
            }

            if (summary.GeminiManagedCandidates > 0 && summary.SuccessfullyRehydrated == 0)
            {
                _bot.Print(
                    $"[REHYDRATE_WARN] currentSymbol={_currentSymbolCanonical} " +
                    $"geminiCandidates={summary.GeminiManagedCandidates} reason=zero_successful_rehydrates");
            }

            _bot.Print(
                $"[REHYDRATE_SUMMARY] seen={summary.TotalOpenPositionsSeen} " +
                $"candidates={summary.GeminiManagedCandidates} ok={summary.SuccessfullyRehydrated} " +
                $"skipped={summary.Skipped} failed={summary.Failed} duplicates={summary.Duplicates} " +
                $"fallbacks={summary.FallbackReconstructions} ambiguousTp1={summary.AmbiguousTp1Cases} " +
                $"directionMismatches={summary.DirectionMismatchWarnings}");

            return summary;
        }

        private void ProcessPosition(Position position, RehydrateSummary summary)
        {
            if (position == null)
            {
                summary.Skipped++;
                _bot.Print("[REHYDRATE_SKIP] pos=NULL reason=null_position");
                return;
            }

            string normalizedSymbol = SymbolRouting.NormalizeSymbol(position.SymbolName);
            bool exactOwner = string.Equals(position.Label, _botLabel, StringComparison.Ordinal);
            bool ambiguousOwner =
                !exactOwner &&
                !string.IsNullOrWhiteSpace(position.Label) &&
                position.Label.IndexOf("gemini", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!exactOwner)
            {
                if (ambiguousOwner)
                {
                    summary.Skipped++;
                    _bot.Print(
                        $"[REHYDRATE_SKIP] pos={Convert.ToInt64(position.Id)} symbol={position.SymbolName} " +
                        $"label={position.Label} reason=ownership_ambiguous");
                }

                return;
            }

            summary.GeminiManagedCandidates++;

            if (!string.Equals(normalizedSymbol, _currentSymbolCanonical, StringComparison.Ordinal))
            {
                summary.Skipped++;
                _bot.Print(
                    $"[REHYDRATE_SKIP] pos={Convert.ToInt64(position.Id)} symbol={position.SymbolName} " +
                    $"reason=symbol_scope_mismatch currentSymbol={_currentSymbolCanonical}");
                return;
            }

            long positionKey = Convert.ToInt64(position.Id);
            if (_registry.ContainsKey(positionKey))
            {
                summary.Duplicates++;
                _bot.Print(
                    $"[REHYDRATE_DUP] pos={positionKey} symbol={position.SymbolName} reason=context_already_exists");
                return;
            }

            RebuildResult rebuild = TryRebuild(position, summary);
            if (rebuild.Context == null)
            {
                summary.Failed++;
                _bot.Print(
                    $"[REHYDRATE_SKIP] pos={positionKey} symbol={position.SymbolName} reason=rebuild_failed unmanaged=true");
                return;
            }

            try
            {
                _registry.Add(positionKey, rebuild.Context);
            }
            catch (ArgumentException)
            {
                summary.Duplicates++;
                _bot.Print(
                    $"[REHYDRATE_DUP] pos={positionKey} symbol={position.SymbolName} reason=duplicate_registry_key_attempt");
                return;
            }

            _contextRegistry.RegisterPosition(rebuild.Context);

            if (!_registerExitContext(rebuild.Context))
            {
                _registry.Remove(positionKey);
                _contextRegistry.RemovePosition(positionKey);
                summary.Failed++;
                _bot.Print(
                    $"[REHYDRATE_SKIP] pos={positionKey} symbol={position.SymbolName} reason=exit_manager_registration_failed unmanaged=true");
                return;
            }

            if (rebuild.UsedFallback)
                summary.FallbackReconstructions++;

            if (rebuild.AmbiguousTp1)
                summary.AmbiguousTp1Cases++;

            if (rebuild.DirectionMismatch)
                summary.DirectionMismatchWarnings++;

            summary.SuccessfullyRehydrated++;

            _bot.Print(
                $"[REHYDRATE] pos={positionKey} symbol={position.SymbolName} dir={rebuild.Context.FinalDirection} " +
                $"tp1Hit={rebuild.Context.Tp1Hit} be={rebuild.Context.BeActivated} trailing={rebuild.Context.TrailingActivated} " +
                $"remainingUnits={rebuild.Context.RemainingVolumeInUnits} source={rebuild.Context.RehydrateSource}");

            if (rebuild.DefaultedLifecycleFields)
            {
                _bot.Print(
                    $"[REHYDRATE_FALLBACK] pos={positionKey} symbol={position.SymbolName} reason=lifecycle_fields_defaulted");
            }
        }

        private RebuildResult TryRebuild(Position position, RehydrateSummary summary)
        {
            long positionKey = Convert.ToInt64(position.Id);
            var result = new RebuildResult();

            if (string.IsNullOrWhiteSpace(position.SymbolName))
            {
                _bot.Print($"[REHYDRATE_WARN] pos={positionKey} reason=missing_symbol_name");
                return result;
            }

            var symbol = _bot.Symbols.GetSymbol(position.SymbolName);
            if (symbol == null)
            {
                _bot.Print($"[REHYDRATE_WARN] pos={positionKey} symbol={position.SymbolName} reason=missing_symbol_metadata");
                return result;
            }

            double entryPrice = position.EntryPrice;
            if (!IsFinitePositive(entryPrice))
            {
                _bot.Print($"[REHYDRATE_WARN] pos={positionKey} symbol={position.SymbolName} reason=invalid_entry_price value={entryPrice}");
                return result;
            }

            double currentVolumeInUnits = position.VolumeInUnits;
            if (!IsFinitePositive(currentVolumeInUnits))
            {
                _bot.Print($"[REHYDRATE_WARN] pos={positionKey} symbol={position.SymbolName} reason=invalid_volume_units value={currentVolumeInUnits}");
                return result;
            }

            if (currentVolumeInUnits < symbol.VolumeInUnitsMin)
            {
                _bot.Print(
                    $"[REHYDRATE_WARN] pos={positionKey} symbol={position.SymbolName} reason=volume_below_min " +
                    $"volume={currentVolumeInUnits} min={symbol.VolumeInUnitsMin}");
            }

            if (!IsVolumeStepAligned(currentVolumeInUnits, symbol.VolumeInUnitsStep))
            {
                _bot.Print(
                    $"[REHYDRATE_WARN] pos={positionKey} symbol={position.SymbolName} reason=volume_step_misaligned " +
                    $"volume={currentVolumeInUnits} step={symbol.VolumeInUnitsStep}");
            }

            TradeDirection direction = position.TradeType == TradeType.Buy
                ? TradeDirection.Long
                : TradeDirection.Short;

            double? stopLoss = position.StopLoss;
            double? takeProfit = position.TakeProfit;
            double riskDistance = 0;
            bool usedFallback = false;
            bool defaultedLifecycleFields = false;
            bool directionMismatch = false;
            bool ambiguousTp1 = false;

            if (!stopLoss.HasValue)
            {
                usedFallback = true;
                defaultedLifecycleFields = true;
                _bot.Print(
                    $"[REHYDRATE_WARN] pos={positionKey} symbol={position.SymbolName} reason=missing_stop_loss_exit_manager_expectation");
            }
            else if (!IsFinitePositive(stopLoss.Value))
            {
                usedFallback = true;
                defaultedLifecycleFields = true;
                _bot.Print(
                    $"[REHYDRATE_WARN] pos={positionKey} symbol={position.SymbolName} reason=invalid_stop_loss value={stopLoss.Value}");
                stopLoss = null;
            }
            else
            {
                riskDistance = Math.Abs(entryPrice - stopLoss.Value);
                if (!IsFinitePositive(riskDistance))
                {
                    usedFallback = true;
                    defaultedLifecycleFields = true;
                    _bot.Print(
                        $"[REHYDRATE_WARN] pos={positionKey} symbol={position.SymbolName} reason=invalid_risk_distance value={riskDistance}");
                    riskDistance = 0;
                }
            }

            if (takeProfit.HasValue && !IsFinitePositive(takeProfit.Value))
            {
                usedFallback = true;
                _bot.Print(
                    $"[REHYDRATE_WARN] pos={positionKey} symbol={position.SymbolName} reason=invalid_take_profit value={takeProfit.Value}");
                takeProfit = null;
            }

            bool protectedStop = stopLoss.HasValue && IsProtectedStop(direction, stopLoss.Value, entryPrice, symbol.TickSize);
            bool trailingEvident = stopLoss.HasValue && IsTrailingBeyondEntry(direction, stopLoss.Value, entryPrice, symbol.TickSize);

            if (stopLoss.HasValue && IsSuspiciousStop(direction, stopLoss.Value, entryPrice, symbol.TickSize))
            {
                _bot.Print(
                    $"[REHYDRATE_WARN] pos={positionKey} symbol={position.SymbolName} reason=suspicious_sl_location " +
                    $"entry={entryPrice} sl={stopLoss.Value} side={position.TradeType}");
            }

            bool tp1Hit = false;
            double tp1ClosedVolumeInUnits = 0;
            double entryVolumeInUnits = currentVolumeInUnits;
            double initialVolumeInUnits = currentVolumeInUnits;

            if (protectedStop)
            {
                tp1Hit = true;
                usedFallback = true;
                defaultedLifecycleFields = true;
                // Ambiguity note:
                // the live position proves protective-stop phase, but not the original pre-TP1 volume.
                // We therefore restore the fact of TP1/BE progression, while keeping remaining volume as the only hard volume fact.
                _bot.Print(
                    $"[REHYDRATE_TP1] pos={positionKey} symbol={position.SymbolName} reason=protected_stop_infers_tp1 sl={stopLoss.Value} entry={entryPrice}");
                _bot.Print(
                    $"[REHYDRATE_FALLBACK] pos={positionKey} symbol={position.SymbolName} reason=original_volume_unknown tp1ClosedUnits=0 remainingUnits={currentVolumeInUnits}");
            }
            else
            {
                ambiguousTp1 = true;
                usedFallback = true;
                defaultedLifecycleFields = true;
                _bot.Print(
                    $"[REHYDRATE_TP1] pos={positionKey} symbol={position.SymbolName} reason=tp1_ambiguous default=false " +
                    $"remainingUnits={currentVolumeInUnits} originalVolumeRecoverable=false");
            }

            if (tp1Hit && tp1ClosedVolumeInUnits > 0 && entryVolumeInUnits <= currentVolumeInUnits)
            {
                _bot.Print(
                    $"[REHYDRATE_WARN] pos={positionKey} symbol={position.SymbolName} reason=invalid_tp1_inference " +
                    $"entryUnits={entryVolumeInUnits} remainingUnits={currentVolumeInUnits} closedUnits={tp1ClosedVolumeInUnits}");
            }

            var ctx = new PositionContext
            {
                PositionId = positionKey,
                Symbol = position.SymbolName,
                EntryType = "REHYDRATED",
                EntryReason = "Startup rehydrate from live open position",
                FinalDirection = direction,
                // Ambiguity note:
                // original entry evaluation is runtime-only and cannot be recovered safely after restart,
                // so we restore with neutral placeholders and keep FinalConfidence deterministic via ComputeFinalConfidence().
                EntryScore = 50,
                LogicConfidence = 50,
                EntryTime = _bot.Server.Time,
                EntryTimeUtc = _bot.Server.Time.ToUniversalTime(),
                EntryPrice = entryPrice,
                RiskPriceDistance = riskDistance,
                Tp1Hit = tp1Hit,
                Tp1CloseFraction = 0,
                Tp1ClosedVolumeInUnits = tp1ClosedVolumeInUnits,
                EntryVolumeInUnits = entryVolumeInUnits,
                RemainingVolumeInUnits = currentVolumeInUnits,
                InitialVolumeInUnits = initialVolumeInUnits,
                BePrice = protectedStop && stopLoss.HasValue ? stopLoss.Value : 0,
                BeMode = tp1Hit ? BeMode.AfterTp1 : BeMode.None,
                BeActivated = protectedStop,
                TrailingMode = tp1Hit ? TrailingMode.Normal : TrailingMode.None,
                TrailingActivated = tp1Hit && trailingEvident,
                LastStopLossPrice = stopLoss,
                LastTrailingStopTarget = tp1Hit && trailingEvident && stopLoss.HasValue ? stopLoss.Value : null,
                Tp2Price = takeProfit,
                IsRehydrated = true,
                RehydratedAtUtc = _bot.Server.Time.ToUniversalTime(),
                RehydrateSource = "LiveOpenPosition",
                MarketTrend = direction != TradeDirection.None,
                Adx_M5 = 0
            };

            // AGENTS rule: PositionContext létrehozás után azonnal számoljuk a FinalConfidence-t.
            ctx.ComputeFinalConfidence();

            TradeDirection liveDirection = position.TradeType == TradeType.Buy
                ? TradeDirection.Long
                : TradeDirection.Short;

            if (ctx.FinalDirection != liveDirection)
            {
                directionMismatch = true;
                ctx.FinalDirection = liveDirection;
                _bot.Print(
                    $"[REHYDRATE_DIR] pos={positionKey} symbol={position.SymbolName} reason=direction_mismatch " +
                    $"restored={ctx.FinalDirection} live={liveDirection}");
            }

            if (ctx.FinalDirection == TradeDirection.None)
            {
                _bot.Print(
                    $"[REHYDRATE_DIR] pos={positionKey} symbol={position.SymbolName} reason=direction_missing_after_restore");
                return result;
            }

            result.Context = ctx;
            result.UsedFallback = usedFallback;
            result.DefaultedLifecycleFields = defaultedLifecycleFields;
            result.AmbiguousTp1 = ambiguousTp1;
            result.DirectionMismatch = directionMismatch;
            return result;
        }

        private static bool IsFinitePositive(double value)
        {
            return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsProtectedStop(TradeDirection direction, double stopLoss, double entryPrice, double epsilon)
        {
            double tolerance = Math.Max(epsilon, 1e-8);

            return direction == TradeDirection.Long
                ? stopLoss >= entryPrice - tolerance
                : stopLoss <= entryPrice + tolerance;
        }

        private static bool IsTrailingBeyondEntry(TradeDirection direction, double stopLoss, double entryPrice, double epsilon)
        {
            double tolerance = Math.Max(epsilon * 2.0, 1e-8);

            return direction == TradeDirection.Long
                ? stopLoss > entryPrice + tolerance
                : stopLoss < entryPrice - tolerance;
        }

        private static bool IsSuspiciousStop(TradeDirection direction, double stopLoss, double entryPrice, double epsilon)
        {
            double tolerance = Math.Max(epsilon, 1e-8);

            if (direction == TradeDirection.Long)
                return Math.Abs(stopLoss - entryPrice) < tolerance;

            return Math.Abs(stopLoss - entryPrice) < tolerance;
        }

        private static bool IsVolumeStepAligned(double volumeInUnits, double step)
        {
            if (step <= 0)
                return true;

            double ratio = volumeInUnits / step;
            double nearest = Math.Round(ratio);
            return Math.Abs(ratio - nearest) < 1e-6;
        }

        private sealed class RebuildResult
        {
            public PositionContext Context { get; set; }
            public bool UsedFallback { get; set; }
            public bool DefaultedLifecycleFields { get; set; }
            public bool AmbiguousTp1 { get; set; }
            public bool DirectionMismatch { get; set; }
        }
    }

    public sealed class RehydrateSummary
    {
        public int TotalOpenPositionsSeen { get; set; }
        public int GeminiManagedCandidates { get; set; }
        public int SuccessfullyRehydrated { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public int Duplicates { get; set; }
        public int FallbackReconstructions { get; set; }
        public int AmbiguousTp1Cases { get; set; }
        public int DirectionMismatchWarnings { get; set; }
    }
}
