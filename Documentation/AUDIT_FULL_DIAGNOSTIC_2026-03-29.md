# Gemini V26 Full Diagnostic (2026-03-29)

## Scope
Code-level institutional diagnostic across architecture, execution pipeline, entry quality, risk/exit, logging, memory/rehydrate, edge detection, and failure modes.

## Executive Verdict
Gemini V26 is **close to institutional infrastructure quality**, but **not yet capable of consistent positive expectancy in production** without targeted remediation.

Primary blockers:
1. TradeCore still owns protective exit logic (`CheckHardLoss`) and entry confidence state shaping, violating strict orchestration purity.
2. Pipeline has multiple hard choke points (global session + router trigger requirements + final acceptance + per-symbol gates), creating trade starvation risk.
3. Analytics/edge pipeline is schema-misaligned (`ExpectancyEngine` requires `RMultiple`, while `CsvAnalyticsLogger` does not produce it), so edge learning is partially blind.
4. Risk/exit behavior has drift from rulebook target ranges (TP1 fallback can widen to `0.60R`).
5. Logging is rich but noisy and inconsistent enough to increase reconstruction cost at scale.

## 1) Architecture Audit

### Strengths
- Clear module boundaries exist (EntryRouter, TradeRouter, per-instrument executors/exit managers).
- Final confidence formula is canonically defined in `PositionContext` and immutably stored after compute.
- Direction SSOT handling is explicit in entry→execution handoff (`FinalDirection` checks and mismatch logs).

### Violations / Drift
- **TradeCore is not orchestration-only**:
  - It computes entry-layer confidence snapshot values (`EntryScore`, `LogicBiasConfidence`, `FinalConfidence`, `RiskConfidence`) inside orchestrator flow.
  - It contains and executes a monetary hard-loss close routine (`CheckHardLoss`) including PnL/risk math and forced close.
- **Responsibility duplication**:
  - Entry logic confidence is re-evaluated inside executors (`_entryLogic.Evaluate()`), potentially desyncing with earlier routed context.
  - TP1 logic is partly established in executor (`Tp1R`, `Tp1Price`) but exit manager can recalculate fallback TP1R.
- **Coupling risk**:
  - TradeCore contains a long per-symbol `if/else` execution matrix. This is operationally explicit, but high-maintenance and drift-prone for instrument expansion.

## 2) Pipeline Analysis

### Effective Pipeline Map (actual)
1. Context build + market state enrichment.
2. Global session gate decision.
3. Instrument entry logic bias probe.
4. Entry types evaluation via EntryRouter.
5. Routing/selection via TradeRouter.
6. Final acceptance and direction consistency checks.
7. Per-symbol SessionGate + ImpulseGate.
8. Executor sends order + builds PositionContext.
9. Exit manager TP1/BE/trailing lifecycle.
10. Analytics logging on close.

### Choke Points (where trades die)
- Global session gate blocks by calendar buckets and symbol filters.
- TradeRouter execution requires `TriggerConfirmed`; score-only acceptance is still blocked from execution.
- Final acceptance gate in TradeCore can drop winning candidates post-routing.
- Per-symbol gate combinations can block after all upstream work.
- Executor risk guards (`riskPercent <= 0`, invalid SL distance, invalid volume) can still abort at last stage.

### Always-Trade Compliance
- Current behavior is **not strict Always-Trade**; it is “always-evaluate, conditionally-execute”. This is likely intentional for safety, but contradicts strict always-trade doctrine.

## 3) Entry Engine Diagnostic

### Observations
- Router explicitly enforces executable state = valid + trigger confirmed.
- Multiple late-entry protections exist (timing penalties, late continuation rejection), but still coexist with strict trigger gate.
- Transition/flag quality is scored and routed, but downstream confidence/risk shaping may alter practical behavior per executor.

### Risks
- **Late/early balance skew**: strict trigger requirements + multi-gate stack can miss high-quality early continuation setups.
- **Overfitting pressure**: numerous reason-token penalties and soft/hard filters can cause regime-fragile behavior.
- **TransitionQuality underutilization**: captured in analytics, but end-to-end optimization is constrained by missing `RMultiple` in logger schema.

## 4) Risk & Exit Engine

### Strengths
- TP1 partial close, BE move, and post-TP1 adaptive management are implemented with explicit logs.
- Risk distance recovery logic attempts resilience after restart/state drift.

### Weaknesses
- TP1 fallback can resolve to `0.60R`, outside stated institutional target (`~0.4–0.5R`).
- BE is tied to TP1 path (correct), but if TP1 resolution drifts, BE timing drifts too.
- Exit behavior consistency across instruments is uncertain (pattern appears replicated; central contract not enforced).

## 5) Logging & Debug Trace Quality

### Strengths
- Extensive lifecycle and direction traces exist (ENTRY/DIR/HTF/TP1/BE/MFE/EXIT).
- Rehydrate has strong skip/attach/fallback summary logging.

### Gaps
- Log taxonomy is inconsistent (`[TC]`, `[BLOCK]`, `[EXIT]`, `[AUDIT]`, `[DIR]` mixed without strict hierarchy).
- MFE tracker logs every update with multiple redundant lines, which can become a performance and observability bottleneck.
- Some branches still return early with minimal semantic reason detail.

## 6) Memory & Rehydrate

### Strengths
- Memory engine tracks move phase/freshness/extension and exposes timing penalties.
- Rehydrate is safety-oriented, symbol-scoped, duplicate-aware, and context-register integrated.

### Risks
- Fallback rehydrate contexts are intentionally neutral placeholders; safe, but low-trust decision quality after restart is expected until memory/state converges.
- If runtime symbol resolution fails repeatedly, rehydrated positions remain operational but decision richness degrades.

## 7) Edge Detection Capability (Critical)

### Current State
- Expectancy engine can group by setup/regime and evaluate an explicit edge filter (TransitionQuality > 0.7, Flag, Trend).
- Statistical relevance threshold (`sample >= 30`) is encoded.

### Critical Data Contract Break
- `ExpectancyEngine` requires `RMultiple` column.
- `CsvAnalyticsLogger` does not write `RMultiple`; therefore a large part of edge analysis can silently produce empty/partial reports depending on CSV source.

## 8) Failure Modes

Most credible real-world failure modes:
1. **Overfiltering / no-trade regime** from stacked gate architecture.
2. **False safety starvation** (good setups rejected due trigger timing and session filters).
3. **TP1 miss clustering** when fallback TP1 resolution diverges from intended R-targets.
4. **Hard-loss orchestration bleed** (TradeCore-level protective closure can conflict with instrument lifecycle handling).
5. **Post-restart context quality dip** due fallback/low-trust rehydrate.
6. **Analytics blind spots** due schema mismatch, blocking true edge detection feedback loop.

## Prioritized Fix Plan

### P0 (must fix first)
1. Move `CheckHardLoss` out of `TradeCore` into a dedicated risk-protection module.
2. Enforce a single confidence source flow: compute in context owner layer only; TradeCore should consume, not shape.
3. Unify analytics schema: guarantee `RMultiple`, `Profit`, `Confidence`, `SetupType`, `MarketRegime`, `TransitionQuality`, `MFE`, `MAE` are emitted by the same pipeline consumed by expectancy.

### P1
4. Normalize TP1 contract to institutional target band (`0.4–0.5R`) unless explicit strategy override exists.
5. Replace long symbol `if/else` blocks with instrument registry + pluggable execution chain.
6. Harden “no silent decision” standard: every return-path should emit structured reason code.

### P2
7. Introduce gate telemetry counters (per gate: evaluated, blocked, passed, expectancy impact).
8. Add restart grace policy that lowers aggressiveness only while trust remains low; auto-expire policy by bars/time.
9. Trim per-tick log verbosity (especially MFE) and move to sampled or event-driven logging.

## Phase 4+ Upgrade Suggestions
- Central execution contract (`IInstrumentExecutionPipeline`) with declarative gate stack.
- Real-time edge scorer fed by aligned analytics schema and confidence-calibrated outcomes.
- Bayesian/online calibration of setup families by regime/session.
- “Kill-switch by anomaly” for sudden block-rate spikes or SL cluster bursts.

## Final Answer: Positive Expectancy?
**Not yet reliably.**

### Why not
- Architecture purity breach in orchestration layer + risk logic bleed.
- Trade starvation risk from stacked filters and trigger hard requirements.
- Edge-learning feedback loop is not fully trustworthy due analytics schema mismatch.

### What is missing
- Clean separation of orchestration vs protection/risk logic.
- End-to-end data contract integrity for expectancy learning.
- Unified, measurable gate governance tied to post-trade outcomes.

### What must be fixed first
1. Decouple hard-loss logic from TradeCore.
2. Repair analytics schema contract (`RMultiple` et al.) so edge detection is real, not nominal.
3. Instrument gate funnel metrics and reduce overfiltering before further strategy tuning.
