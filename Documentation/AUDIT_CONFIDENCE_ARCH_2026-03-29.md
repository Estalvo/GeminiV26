# Gemini V26 Confidence Architecture Audit (Code-Level)
Date: 2026-03-29 (UTC)
Scope: runtime confidence flow, authority, recompute/override audit

## A. Confidence Inventory

### A1) Canonical confidence pipeline types

| Value | File | Owner | Kind | Proven role |
|---|---|---|---|---|
| `EntryScore` | `Core/PositionContext.cs` | `PositionContext` | input | Entry/setup quality input (0-100) to canonical formula. |
| `LogicConfidence` | `Core/PositionContext.cs` | `PositionContext` | input | Instrument logic confidence input (0-100) to canonical formula. |
| `FinalConfidence` | `Core/PositionContext.cs` | `PositionContext` | final | Canonical combined confidence, computed once and private-set. |
| `_isFinalConfidenceComputed` | `Core/PositionContext.cs` | `PositionContext` | guard | Immutability guard for `ComputeFinalConfidence()`. |
| `ComputeFinalConfidenceValue(entryScore, logicConfidence)` | `Core/PositionContext.cs` | static | formula | Implements `0.7*EntryScore + 0.3*LogicConfidence` with clamp+round. |
| `ComputeFinalConfidence()` | `Core/PositionContext.cs` | method | compute | Computes once; subsequent calls no-op. |
| `Confidence` (obsolete) | `Core/PositionContext.cs` | alias | legacy alias | Read-only alias to `FinalConfidence`. |

### A2) Entry-layer / candidate-layer confidence-like values

| Value | File | Owner | Kind | Proven role |
|---|---|---|---|---|
| `Score` | `Core/Entry/EntryEvaluation.cs` | `EntryEvaluation` | intermediate candidate | Candidate quality score used for router selection and gating, not authoritative runtime trade confidence. |
| `LogicConfidence` | `Core/Entry/EntryEvaluation.cs` | `EntryEvaluation` | intermediate | Instrument bias confidence copied into candidate object. |
| `RawLogicConfidence` | `Core/Entry/EntryEvaluation.cs` | `EntryEvaluation` | diagnostic/intermediate | Trace of source logic confidence before downstream normalization/fallback. |
| `BaseScore`, `AfterHtfScoreAdjustment`, `AfterPenaltyScore`, `FinalScoreSnapshot`, `ScoreThresholdSnapshot`, `PreQualityScore`, `PostQualityScore`, `PostCapScore` | `Core/Entry/EntryEvaluation.cs` | `EntryEvaluation` | diagnostic/intermediate | Score trace fields for observability; non-authoritative for post-entry lifecycle. |
| `LogicBiasConfidence` | `Core/Entry/EntryContext.cs` | `EntryContext` | intermediate | TradeCore-level logic confidence used during routing / selection phase. |
| `EntryScore`, `FinalConfidence`, `RiskConfidence` | `Core/Entry/EntryContext.cs` | `EntryContext` | snapshot/intermediate | Pre-exec snapshot fields; not immutable and not protected as SSOT. |
| `LogicConfidence` (obsolete alias) | `Core/Entry/EntryContext.cs` | `EntryContext` | legacy alias | Alias to `LogicBiasConfidence`. |
| `Htf*Confidence01`, `ActiveHtfConfidence` | `Core/Entry/EntryContext.cs` | `EntryContext` | context signal | HTF alignment confidence (0..1), not canonical trade confidence. |
| `MinimumFallbackConfidence`, `eval.RawLogicConfidence`, `eval.LogicConfidence` | `Core/Entry/CryptoDirectionFallback.cs` | fallback helper | derived candidate | Direction fallback assigns logic confidence for candidate-level continuity. |

### A3) TradeCore / metadata / analytics confidence-like values

| Value | File | Owner | Kind | Proven role |
|---|---|---|---|---|
| `_ctx.LogicBiasConfidence` | `Core/TradeCore.cs` | `EntryContext` in TradeCore | intermediate | Filled from instrument EntryLogic outputs before entry routing. |
| `_ctx.FinalConfidence` | `Core/TradeCore.cs` | `EntryContext` in TradeCore | derived snapshot | Computed from `selected.Score` + `_ctx.LogicBiasConfidence`; pre-exec snapshot only. |
| `_ctx.RiskConfidence` | `Core/TradeCore.cs` | `EntryContext` in TradeCore | derived snapshot | Clone/clamp of `_ctx.FinalConfidence` used for logs/snapshots. |
| `PendingEntryMeta.Confidence` | `Core/Trade/TradeMetaStore.cs` | pending metadata | legacy/misleading | Stores `selected.Score` under name `Confidence`. |
| `TradeAuditLog` output fields `entryScore`, `logicConfidence`, `finalConfidence`, `riskFinal` | `Core/Logging/TradeAuditLog.cs` | logging | diagnostics | Log payload uses EntryContext snapshot fields before executor computes PositionContext. |
| `TradeCloseSnapshot.Score` / `Confidence` | `Core/Analytics/TradeStatsTracker.cs` | analytics DTO | informational | Analytics-only close snapshot fields. |
| `TradeMemoryRecord.Confidence` | `core/memory/TradeMemoryRecord.cs` | memory DTO | informational | Historical memory feature; not runtime authority. |

### A4) Risk/management confidence-like values

| Value | File | Owner | Kind | Proven role |
|---|---|---|---|---|
| `finalConfidence` parameters in `IInstrumentRiskSizer` | `Risk/IInstrumentRiskSizer.cs` | risk interface | consumer input | Risk sizing interface contract says FinalConfidence-only input. |
| `score` params in risk sizers (e.g., EUR) | `Instruments/EURUSD/EurUsdInstrumentRiskSizer.cs` | instrument risk sizer impl | misleading name | Parameter name is `score` but receives post-penalty confidence from executors. |
| `statePenalty` in executors | `Instruments/*/*InstrumentExecutor.cs` | executors | derived risk-shaping | Soft market-state adjustment added to FinalConfidence before risk/trailing calls. |
| `riskFinal` (log token) | executor logs + `TradeAuditLog` | logs | derived/log alias | Label for penalty-adjusted or snapshot risk input; not canonical FinalConfidence. |
| `RiskProfile.Score` | `Risk/RiskProfile.cs` | legacy model | legacy | Legacy risk model class still names main input `Score`. |
| `ConfidenceTradeModel` method arg `confidence` | `Core/ConfidenceTradeModel.cs` | utility | consumer | Generic confidence-based TP/trailing/BE mapping utility. |

## B. Confidence Authority Map

Raw inputs (entry stage)
1. Entry-type candidate score: `EntryEvaluation.Score`.  
2. Instrument logic output: `IEntryLogic.LastLogicConfidence` (or instrument-specific evaluate out-param).  

Intermediate (selection stage)
3. TradeCore writes `_ctx.LogicBiasConfidence` from instrument logic.  
4. TradeCore selects candidate and sets `_ctx.EntryScore = selected.Score`.  
5. TradeCore computes `_ctx.FinalConfidence` and `_ctx.RiskConfidence` on `EntryContext` (snapshot only).

Authoritative (execution stage)
6. Executors create **new `PositionContext`** with `EntryScore` + (possibly re-evaluated) `LogicConfidence` and call `ctx.ComputeFinalConfidence()`.  
7. `PositionContext.FinalConfidence` becomes authoritative for that position object (private setter + compute guard).

Consumers
8. Risk sizing / SL-TP / trailing consume either:  
   - `ctx.FinalConfidence` (BTC/ETH style), or  
   - `ClampRiskConfidence(ctx.FinalConfidence + statePenalty)` (most executors).  
9. Exit managers consume `PositionContext` values; many branches read `ctx.FinalConfidence` thresholds.

## C. Proven Runtime Flow

1. `TradeCore` evaluates instrument entry logic and derives `logicConfidence` for `_ctx.LogicBiasConfidence`.  
2. Entry router evaluates entry types and candidates produce `EntryEvaluation.Score`.  
3. After routing, TradeCore sets `_ctx.EntryScore`, computes `_ctx.FinalConfidence = ComputeFinalConfidenceValue(_ctx.EntryScore, _ctx.LogicBiasConfidence)`, and sets `_ctx.RiskConfidence`.  
4. Executor receives `EntryEvaluation entry` + `EntryContext entryContext`.  
5. Executor usually **re-runs entry logic** (`_entryLogic.Evaluate(...)`) and obtains (possibly new) `logicConfidence`.  
6. Executor creates `PositionContext` with `EntryScore=entry.Score`, `LogicConfidence=<executor logic>`, calls `ctx.ComputeFinalConfidence()`.  
7. Executor applies risk with either `ctx.FinalConfidence` or penalty-shaped `ClampRiskConfidence(ctx.FinalConfidence + statePenalty)`.  
8. After order fill, executor often creates a second `PositionContext` again and calls `ctx.ComputeFinalConfidence()` again on that second instance before storing in `_positionContexts`.

## D. Violations / Drift

1. **FinalConfidence authority split (EntryContext vs PositionContext):** TradeCore computes `_ctx.FinalConfidence` on `EntryContext`, then executors recompute canonical value again on `PositionContext` (different object, potentially different `LogicConfidence`).
2. **Executor-side logic re-evaluation:** Most executors call `_entryLogic.Evaluate()` during execution, so LogicConfidence source can diverge from TradeCore-time value.
3. **Risk shaping in confidence space:** most executors compute risk/trailing using `FinalConfidence + statePenalty`; this is separate shaping but currently unnamed as separate risk variable.
4. **Misleading naming:**
   - `PendingEntryMeta.Confidence` stores `selected.Score`.
   - `RiskProfile.Score` legacy model and risk sizer parameter names still use `score` while semantically used as confidence.
   - `riskFinal` logs can refer to different concepts depending on stage.
5. **Rehydrate edge inconsistency:** `XauExitManager.RehydrateFromLivePositions` creates `PositionContext` without setting `EntryScore`/`LogicConfidence` and calls `ComputeFinalConfidence()` (result defaults to 0).

## E. Executor Findings

### Per-executor recompute/re-eval matrix

- Re-runs entry logic + computes FinalConfidence twice (pre-order + final stored context):
  - AUDNZD, AUDUSD, BTCUSD, ETHUSD, EURJPY, EURUSD, GBPJPY, GBPUSD, GER40, NAS100, NZDUSD, US30, USDCAD, USDCHF, USDJPY.
- XAU executor computes FinalConfidence once on PositionContext, but uses `entry.LogicConfidence` fallback to `entry.Score` (if logic missing) and applies state penalty on top for risk/trailing.

Classification:
- Re-running entry logic in executors: **divergence risk**.
- Recreating PositionContext and recomputing in same method: **duplication** (not a direct immutability breach because object instance changes).
- Using `FinalConfidence + statePenalty` for risk/trailing: **acceptable only if explicitly treated as separate derived risk input**; currently naming is partially ambiguous.

## F. Naming / Legacy Issues

- `PositionContext.Confidence` (obsolete alias): **acceptable but should be removed later**.
- `EntryContext.LogicConfidence` alias to `LogicBiasConfidence`: **confusing**.
- `PendingEntryMeta.Confidence = selected.Score`: **dangerous (semantic mismatch)**.
- `RiskProfile.Score`: **legacy / confusing**.
- `EurUsdInstrumentRiskSizer` etc parameter `score` for confidence input: **confusing**.
- `TradeAuditLog` `riskFinal` label and EntryContext snapshot `finalConfidence`: **confusing unless explicitly documented as pre-exec snapshot**.

## G. Final Verdict

1. **All confidence values and roles:** documented above; canonical runtime value is `PositionContext.FinalConfidence` after `ComputeFinalConfidence()`.
2. **Authoritative flow today:** TradeCore precomputes snapshot confidence in `EntryContext`, then executors recompute canonical confidence in `PositionContext` (often with fresh logic eval), then risk/management consume canonical or penalty-shaped variant.
3. **Formula compliance:** `PositionContext.ComputeFinalConfidenceValue` exactly implements `0.7*EntryScore + 0.3*LogicConfidence` with clamp and round.
4. **Immutability after compute:** Within a given `PositionContext` instance, yes (`private set` + `_isFinalConfidenceComputed`). At architecture level, there is duplication because new contexts are created and recomputed.
5. **Executor override/bypass:** No direct `FinalConfidence = ...` assignment in executors; however most executors recalculate inputs (`LogicConfidence`) by rerunning entry logic and then compute a fresh final value.
6. **Single SSOT vs competing sources:** Multiple confidence-bearing sources coexist (`EntryContext` snapshot, `PositionContext` canonical, metadata/analytics aliases). Canonical trade lifecycle source is `PositionContext`, but competing pre-exec/snapshot sources still exist.
7. **Fix first (priority):**
   1) Stop executor re-evaluation of entry logic; consume routed `entryContext.LogicBiasConfidence` (or a frozen value from TradeCore).
   2) Introduce explicit variable naming for penalty-shaped risk input (e.g., `AdjustedRiskConfidence`) and never label it `FinalConfidence`/`riskFinal` without context.
   3) Rename/remap misleading legacy fields (`PendingEntryMeta.Confidence`, risk sizer `score` params, `RiskProfile.Score`).
   4) Fix XAU rehydrate path to set neutral EntryScore/LogicConfidence before compute (as done in `RehydrateService`).

## H. Minimal Fix Plan

1. In executors, replace `_entryLogic.Evaluate()` usage with frozen routed logic confidence (`entryContext.LogicBiasConfidence`) unless explicitly unavailable; keep fallback only when null/missing.
2. In executors, create local `adjustedRiskConfidence = ClampRiskConfidence(ctx.FinalConfidence + statePenalty)` and use this name for risk/trailing/TP routing.
3. In `PendingEntryMeta`, rename `Confidence` to `EntryScore` (or add new field + deprecate old).
4. In XAU rehydrate path, set `EntryScore = 50`, `LogicConfidence = 50` before `ComputeFinalConfidence()`.
5. In risk sizers, rename method parameter identifiers from `score` to `finalConfidence` (no behavior change).
