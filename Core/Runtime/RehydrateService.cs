using System;
using System.Collections.Generic;
using cAlgo.API;
using Gemini.Memory;
using GeminiV26.Core.Context;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Logging;

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
        private readonly RuntimeSymbolResolver _runtimeSymbols;
        private readonly Dictionary<long, PositionContext> _registry;
        private readonly ContextRegistry _contextRegistry;
        private readonly string _botLabel;
        private readonly Func<PositionContext, bool> _registerExitContext;
        private readonly MarketMemoryEngine _memoryEngine;

        public RehydrateService(
            Robot bot,
            Dictionary<long, PositionContext> registry,
            ContextRegistry contextRegistry,
            string botLabel,
            Func<PositionContext, bool> registerExitContext,
            MarketMemoryEngine memoryEngine)
        {
            _bot = bot ?? throw new ArgumentNullException(nameof(bot));
            _runtimeSymbols = new RuntimeSymbolResolver(_bot);
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _contextRegistry = contextRegistry ?? throw new ArgumentNullException(nameof(contextRegistry));
            _botLabel = botLabel ?? string.Empty;
            _registerExitContext = registerExitContext ?? throw new ArgumentNullException(nameof(registerExitContext));
            _memoryEngine = memoryEngine ?? throw new ArgumentNullException(nameof(memoryEngine));
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
                    $"[REHYDRATE_WARN] geminiCandidates={summary.GeminiManagedCandidates} reason=zero_successful_rehydrates");
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

            long positionKey = Convert.ToInt64(position.Id);
            summary.GeminiManagedCandidates++;
            _bot.Print(TradeLogIdentity.WithPositionIds(
                $"[REHYDRATE][DISCOVERED]\npositionId={positionKey}\nsymbol={position.SymbolName}\nlabel={position.Label}",
                positionKey,
                position.Comment,
                position.SymbolName));

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
            _bot.Print(TradeLogIdentity.WithPositionIds(
                $"[REHYDRATE][ATTACH]\npositionId={positionKey}\nfinalDirection={rebuild.Context.FinalDirection}\ntp1Hit={rebuild.Context.Tp1Hit}\nbeMoved={rebuild.Context.BeActivated}\ntrailActive={rebuild.Context.TrailingActivated}",
                rebuild.Context,
                position));

            if (rebuild.DefaultedLifecycleFields)
            {
                _bot.Print(
                    $"[REHYDRATE_FALLBACK] pos={positionKey} symbol={position.SymbolName} reason=lifecycle_fields_defaulted");
                _bot.Print(TradeLogIdentity.WithPositionIds(
                    "[REHYDRATE][FALLBACK]\nreason=lifecycle_fields_defaulted",
                    rebuild.Context,
                    position));
            }
        }

        private RebuildResult TryRebuild(Position position, RehydrateSummary summary)
        {
            long positionKey = Convert.ToInt64(position.Id);
            var result = new RebuildResult();

            if (string.IsNullOrWhiteSpace(position.SymbolName))
            {
                _bot.Print($"[REHYDRATE_WARN] pos={positionKey} reason=missing_symbol_name");
                _bot.Print(TradeLogIdentity.WithPositionIds("[REHYDRATE][WARN]\nreason=missing_symbol_name", positionKey, position?.Comment, position?.SymbolName));
                return result;
            }

            if (!_runtimeSymbols.TryGetSymbolMeta(position.SymbolName, out var symbol))
            {
                _bot.Print($"[REHYDRATE][RESOLVER_FAIL] symbol={position.SymbolName} positionId={positionKey}");
                _bot.Print($"[REHYDRATE_WARN] pos={positionKey} symbol={position.SymbolName} reason=missing_symbol_metadata");
                _bot.Print(TradeLogIdentity.WithPositionIds("[REHYDRATE][WARN]\nreason=missing_symbol_metadata", positionKey, position.Comment, position.SymbolName));

                var unresolvedContext = new PositionContext
                {
                    PositionId = positionKey,
                    Symbol = position.SymbolName,
                    TempId = position.Comment ?? string.Empty,
                    EntryType = "REHYDRATED",
                    EntryReason = "Startup rehydrate from live open position",
                    FinalDirection = position.TradeType == TradeType.Buy ? TradeDirection.Long : TradeDirection.Short,
                    EntryScore = 50,
                    LogicConfidence = 50,
                    EntryTime = position.EntryTime,
                    EntryTimeUtc = position.EntryTime.ToUniversalTime(),
                    EntryPrice = position.EntryPrice,
                    RemainingVolumeInUnits = position.VolumeInUnits,
                    InitialVolumeInUnits = position.VolumeInUnits,
                    EntryVolumeInUnits = position.VolumeInUnits,
                    IsRehydrated = true,
                    RehydratedAtUtc = _bot.Server.Time.ToUniversalTime(),
                    RehydrateSource = "LiveOpenPosition",
                    ContextTrust = MemoryTrustLevel.Low,
                    RuntimeSymbolAvailable = false,
                    MarketTrend = true,
                    Adx_M5 = 0
                };
                unresolvedContext.ComputeFinalConfidence();
                result.Context = unresolvedContext;
                result.UsedFallback = true;
                result.DefaultedLifecycleFields = true;
                return result;
            }

            _bot.Print($"[REHYDRATE][RESOLVER_OK] symbol={position.SymbolName} positionId={positionKey}");

            double entryPrice = position.EntryPrice;
            if (!IsFinitePositive(entryPrice))
            {
                _bot.Print($"[REHYDRATE_WARN] pos={positionKey} symbol={position.SymbolName} reason=invalid_entry_price value={entryPrice}");
                _bot.Print(TradeLogIdentity.WithPositionIds("[REHYDRATE][WARN]\nreason=invalid_entry_price", positionKey, position.Comment, position.SymbolName));
                return result;
            }

            double currentVolumeInUnits = position.VolumeInUnits;
            if (!IsFinitePositive(currentVolumeInUnits))
            {
                _bot.Print($"[REHYDRATE_WARN] pos={positionKey} symbol={position.SymbolName} reason=invalid_volume_units value={currentVolumeInUnits}");
                _bot.Print(TradeLogIdentity.WithPositionIds("[REHYDRATE][WARN]\nreason=invalid_volume_units", positionKey, position.Comment, position.SymbolName));
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
                TempId = position.Comment ?? string.Empty,
                EntryType = "REHYDRATED",
                EntryReason = "Startup rehydrate from live open position",
                FinalDirection = direction,
                // Ambiguity note:
                // original entry evaluation is runtime-only and cannot be recovered safely after restart,
                // so we restore with neutral placeholders and keep FinalConfidence deterministic via ComputeFinalConfidence().
                EntryScore = 50,
                LogicConfidence = 50,
                EntryTime = position.EntryTime,
                EntryTimeUtc = position.EntryTime.ToUniversalTime(),
                EntryPrice = entryPrice,
                RiskPriceDistance = riskDistance,
                PipSize = symbol.PipSize,
                Tp1Hit = tp1Hit,
                Tp1Executed = tp1Hit,
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
                Adx_M5 = 0,
                RuntimeSymbolAvailable = true
            };

            // AGENTS rule: PositionContext létrehozás után azonnal számoljuk a FinalConfidence-t.
            ctx.ComputeFinalConfidence();
            if (ctx.RuntimeSymbolAvailable)
                AttachMemoryState(ctx, position.SymbolName, ref defaultedLifecycleFields);
            RebuildTradeExcursions(ctx, position);
            _bot.Print(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildContextCreate(ctx), ctx, position));
            _bot.Print(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildDirectionSnapshot(ctx), ctx, position));

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

        private void AttachMemoryState(PositionContext ctx, string symbolName, ref bool defaultedLifecycleFields)
        {
            string normalizedSymbol = SymbolRouting.NormalizeSymbol(symbolName);
            SymbolMemoryState memoryState = _memoryEngine.GetState(normalizedSymbol);

            if (memoryState != null && memoryState.BuildMode != MemoryBuildMode.Default)
            {
                ctx.MovePhase = memoryState.MovePhase;
                ctx.MoveAge = memoryState.MoveAgeBars;
                ctx.PullbackCount = memoryState.PullbackCount;
                ctx.ContextTrust = memoryState.TrustLevel;
                _bot.Print(
                    $"[REHYDRATE][MEMORY_ATTACH] phase={ctx.MovePhase} age={ctx.MoveAge} pullbacks={ctx.PullbackCount}");
                defaultedLifecycleFields = false;
                return;
            }

            ctx.ContextTrust = MemoryTrustLevel.Low;
            defaultedLifecycleFields = true;
            _bot.Print($"[REHYDRATE][NO_MEMORY] symbol={normalizedSymbol}");
        }

        private void RebuildTradeExcursions(PositionContext ctx, Position position)
        {
            if (ctx == null || position == null)
                return;

            if (!TryComputeBarsSinceEntry(ctx, position.SymbolName, TimeFrame.Minute5))
            {
                if (!TryComputeBarsSinceEntry(ctx, position.SymbolName, TimeFrame.Minute))
                {
                    _bot.Print($"[MFE][REBUILD_FAIL] pos={ctx.PositionId} symbol={position.SymbolName} reason=missing_entry_history");
                    return;
                }
            }

            if (TryRebuildExcursions(ctx, position.SymbolName, TimeFrame.Minute) ||
                TryRebuildExcursions(ctx, position.SymbolName, TimeFrame.Minute5))
            {
                _bot.Print(
                    $"[MFE][REBUILD_OK] pos={ctx.PositionId} symbol={position.SymbolName} mfe={ctx.MfeR:0.##} mae={ctx.MaeR:0.##}");
                return;
            }

            _bot.Print($"[MFE][REBUILD_FAIL] pos={ctx.PositionId} symbol={position.SymbolName} reason=missing_price_history");
        }

        private bool TryComputeBarsSinceEntry(PositionContext ctx, string symbolName, TimeFrame timeFrame)
        {
            if (!_runtimeSymbols.TryGetBars(timeFrame, symbolName, out Bars bars))
            {
                _bot.Print($"[REHYDRATE][RESOLVER_FAIL] symbol={symbolName} positionId={ctx.PositionId}");
                return false;
            }

            if (bars == null || bars.Count == 0)
                return false;

            int startIndex = FindStartBarIndex(bars, ctx.EntryTime);
            if (startIndex < 0)
                return false;

            int lastClosedIndex = Math.Max(0, bars.Count - 2);
            ctx.BarsSinceEntryM5 = Math.Max(0, lastClosedIndex - startIndex);
            return true;
        }

        private bool TryRebuildExcursions(PositionContext ctx, string symbolName, TimeFrame timeFrame)
        {
            if (ctx.RiskPriceDistance <= 0 || ctx.FinalDirection == TradeDirection.None)
                return false;

            if (!_runtimeSymbols.TryGetBars(timeFrame, symbolName, out Bars bars))
            {
                _bot.Print($"[REHYDRATE][RESOLVER_FAIL] symbol={symbolName} positionId={ctx.PositionId}");
                return false;
            }

            if (bars == null || bars.Count == 0)
                return false;

            int startIndex = FindStartBarIndex(bars, ctx.EntryTime);
            int lastClosedIndex = Math.Max(0, bars.Count - 2);
            if (startIndex < 0 || startIndex > lastClosedIndex)
                return false;

            double bestFavorablePrice = ctx.EntryPrice;
            double worstAdversePrice = ctx.EntryPrice;

            for (int i = startIndex; i <= lastClosedIndex; i++)
            {
                Bar bar = bars[i];
                if (ctx.FinalDirection == TradeDirection.Long)
                {
                    if (bar.High > bestFavorablePrice)
                        bestFavorablePrice = bar.High;

                    if (bar.Low < worstAdversePrice)
                        worstAdversePrice = bar.Low;
                }
                else
                {
                    if (bar.Low < bestFavorablePrice)
                        bestFavorablePrice = bar.Low;

                    if (bar.High > worstAdversePrice)
                        worstAdversePrice = bar.High;
                }
            }

            ctx.BestFavorablePrice = bestFavorablePrice;
            ctx.WorstAdversePrice = worstAdversePrice;

            if (ctx.FinalDirection == TradeDirection.Long)
            {
                ctx.MfeR = Math.Max(0, (bestFavorablePrice - ctx.EntryPrice) / ctx.RiskPriceDistance);
                ctx.MaeR = Math.Max(0, (ctx.EntryPrice - worstAdversePrice) / ctx.RiskPriceDistance);
            }
            else
            {
                ctx.MfeR = Math.Max(0, (ctx.EntryPrice - bestFavorablePrice) / ctx.RiskPriceDistance);
                ctx.MaeR = Math.Max(0, (worstAdversePrice - ctx.EntryPrice) / ctx.RiskPriceDistance);
            }

            return true;
        }

        private static int FindStartBarIndex(Bars bars, DateTime entryTime)
        {
            if (bars == null || bars.Count == 0)
                return -1;

            int startIndex = -1;
            for (int i = 0; i < bars.Count; i++)
            {
                DateTime barOpenTime = bars.OpenTimes[i];
                if (barOpenTime > entryTime)
                    break;

                startIndex = i;
            }

            return startIndex >= 0 ? startIndex : 0;
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
