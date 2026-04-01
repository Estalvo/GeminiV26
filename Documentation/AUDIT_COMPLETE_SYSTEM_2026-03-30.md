# GEMINI V26 — RE-AUDIT (Context-Aware, Current-State)

## 1. Executive correction summary
- This re-audit re-validated prior claims against the **current codebase state** and reclassified findings into: actual defects, intentional design debt, stale/outdated, and refactor-later items.
- Prior audit was too harsh on some intentional phase behavior (e.g., mismatch log-only policy, TVM early exits, late-entry blocking intent).
- The most material current defects are concentrated in **direction fallback remnants** and **analytics/export observability gaps**.
- Production behavior appears operationally stable in pipeline/exit flow, but analytics trustworthiness remains below production-learning standard due to silent failure patterns.

## 2. Invalidated findings from previous audit

### 2.1 "Late-stage block is a weakness" → **INVALID / OUTDATED**
- Current context explicitly treats anti-late-entry blocking as intended.
- Code shows active final acceptance gating with explicit logs and deterministic reject reasons, not silent behavior.
- Why invalidated: without current expectancy evidence proving false negatives on strong setups, this is not a defect.

### 2.2 "TVM closes before TP1 is inherently weak" → **INVALID / OUTDATED**
- TVM is explicitly protective by design and logs skip/hold/allow reasons.
- Why invalidated: early close pre-TP1 is expected under defensive policy when viability conditions fail.

## 3. Still-valid findings

### 3.1 Direction SSOT violation risk (residual) → **PARTIALLY VALID**
**Evidence**
- TradeCore position-open path still contains fallback assignment from broker trade type:
  `pctx.FinalDirection = ... ? _ctx.FinalDirection : FromTradeType(pos.TradeType);`.

**Current verdict**
- Execution/exit largely consume `FinalDirection` (good), but upstream fallback path still exists.

**Why it matters now**
- This can hide missing upstream direction state and weakens strict SSOT guarantees.

### 3.2 Router fallback direction mutation → **VALID**
**Evidence**
- EntryRouter still mutates direction to logic bias when raw direction is None and marks `FallbackDirectionUsed=true`.

**Current verdict**
- Contradicts expected “router does not mutate direction” model in current target policy.

**Why it matters now**
- Can convert directional uncertainty into executable direction, reducing auditability and increasing wrong-side risk.

### 3.3 PositionContext fallback away from FinalDirection → **PARTIALLY VALID**
**Evidence**
- Position-open fallback from `FromTradeType` remains.

**Current verdict**
- Limited scope (rehydration/open binding path), but still a real fallback path.

**Why it matters now**
- Creates non-SSOT recovery semantics if entry context is missing.

### 3.4 Silent analytics handling / disabled CSV paths → **VALID (HIGH PRIORITY)**
**Evidence**
- `UnifiedAnalyticsWriter` catches all exceptions silently.
- `CsvAnalyticsLogger.OnTradeClosed()` returns immediately (disabled).
- `CsvTradeLogger.OnTradeClosed()` returns immediately (disabled).
- `Data/CsvExporter.cs` remains skeleton.

**Current verdict**
- Observability and export trust are still incomplete.

**Why it matters now**
- Hidden analytics write failure directly harms expectancy learning and post-trade governance.

## 4. Intentional design debts (not bugs)

### 4.1 Split risk authority → **INTENTIONAL DESIGN DEBT**
- Current split (RiskSizer/Executor/TradeCore) is known phase state.
- Reclassified as non-bug **unless divergence appears**.
- Current scan found no concrete evidence of contradictory risk outputs causing immediate live behavior break.

### 4.2 TradeCore doing too much → **INTENTIONAL DESIGN DEBT**
- TradeCore still owns policy logic beyond orchestration target.
- Known transitional architecture; not a defect by itself in this phase.

### 4.3 Mismatch policy log-only → **INTENTIONAL PHASE POLICY**
- `ValidateDirectionConsistency` logs mismatch and returns true by design in current phase.
- Keep as planned policy until strict mode rollout.

## 5. True current defects

### DEFECT-1 — Router mutates direction (live-risk logic defect)
- Category: logic inconsistency / rule intent mismatch.
- Impact: can allow synthetic direction in absence of raw directional signal.
- Priority: high.

### DEFECT-2 — Position-open direction fallback from broker side
- Category: SSOT breach in lifecycle binding path.
- Impact: hides missing direction state; can contaminate post-open context semantics.
- Priority: high.

### DEFECT-3 — Silent analytics write failure
- Category: observability failure.
- Impact: analytics data loss can occur without alerting.
- Priority: critical for learning pipeline.

### DEFECT-4 — Analytics/CSV writers intentionally disabled without health substitute
- Category: broken export readiness / governance gap.
- Impact: reduced redundancy and poor diagnosability if unified writer fails.
- Priority: high.

## 6. Analytics/export verdict

### High-priority verification answers
1) Are CSV exports actually active?
- **Partially active**: unified analytics writer is active; legacy CSV loggers are disabled.

2) Are analytics writers active?
- **Yes, one primary path active** (`UnifiedAnalyticsWriter` via stats tracker). Two legacy paths are hard-disabled.

3) Are exceptions swallowed silently?
- **Yes** in unified writer (`catch {}` with no error log).

4) Is MFE/MAE/RMultiple reliably persisted?
- **Partially**: values are computed and passed into trade close snapshots / memory records.
- Reliability concern remains because write failures can be silent.

5) Can exported data be trusted for expectancy work?
- **Conditional / partial trust**: schema supports expectancy slices, but silent write-failure risk prevents full trust guarantee.

## 7. Revised scores

Re-scored for **current healthy state**, not theoretical end-state purity:

1) Architecture alignment (current-state): **7.0 / 10**
2) Pipeline health: **8.0 / 10**
3) Risk/exit reliability: **8.0 / 10**
4) Entry quality: **7.5 / 10**
5) Logging/observability: **8.0 / 10**
6) Analytics readiness: **5.0 / 10**
7) Overall production readiness: **7.4 / 10**

## 8. Top 5 next actions

1) **Remove router direction mutation fallback**  
Label: **BUGFIX NOW**  
- Replace fallback mutation with explicit invalid-state handling and hard trace event.

2) **Remove TradeCore position-open fallback to `FromTradeType` for FinalDirection**  
Label: **BUGFIX NOW**  
- Preserve strict direction provenance (entry-context / rehydrate-record only).

3) **Make analytics writer fail-loud (without breaking trading flow)**  
Label: **BUGFIX NOW**  
- Keep non-throwing behavior, but emit structured error logs + counters/heartbeat.

4) **Add analytics-path health telemetry + exporter status flagging**  
Label: **SAFE TO POSTPONE**  
- Introduce periodic “analytics OK/degraded” runtime diagnostics.

5) **Consolidate risk authority gradually into planned boundary**  
Label: **REFACTOR LATER**  
- Move toward target architecture only where behavior equivalence is provably maintained.
