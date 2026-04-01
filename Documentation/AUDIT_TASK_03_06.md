# Gemini V26 Architectural & Feature Audit (Tasks 03–06)

## Scope
Strict implementation audit against requested Tasks 03–06.
No refactor proposal, no feature expansion beyond task compliance checks.

## TASK-03 — Early vs Late continuation split

### Status
**PARTIAL**

### Implemented
- A dedicated timing classifier exists (`ContinuationTimingGate`) with explicit outcomes: `Early`, `LateValid`, `LateReject`, `OverextendedReject`, `SideInactiveReject`.
- Early path receives positive adjustment (`+6`, lower min score by `-4`) and late-valid path receives penalty (`-6`) plus stricter requirements (`+4` min score, strong trigger/structure flags).
- Hard rejection exists for overextended and late-missed-chase cases.
- Timing snapshots are injected in `EntryContextBuilder` (`HasEarlyContinuation*`, `HasLateContinuation*`, `IsOverextended*`, freshness/attempt counters).
- Multiple continuation-family entries call the timing gate and apply `RequireStrongTrigger` / `RequireStrongStructure` checks.

### Missing / Incomplete
- Split is **not uniformly applied** across all required continuation-family setups listed in the task.
  - `Index_FlagEntry`, `Index_PullbackEntry`, `Index_BreakoutEntry`, and `FX_PullbackEntry` do not use `ContinuationTimingGate`.
- “Higher priority” for early continuation is only indirectly represented via score boost; there is no explicit, centralized priority classing (e.g., explicit early-vs-late precedence policy at router level).
- Late continuation suppression is fragmented per-entry and absent in several setups, so behavior is inconsistent by entry type.

### Architectural alignment
- Good: timing state comes via `EntryContext` (aligned with context-driven pipeline).
- Risk: enforcement is entry-local, not orchestration-level; this weakens consistency and auditability across setup families.

### Risk impact
- Late/exhausted continuation entries can still pass in setup families that never invoke timing split logic.
- Symbol/setup-dependent behavior drift: same market phase can be treated as “early/late” in one setup but ignored in another.

### Actions
1. Add explicit task-level coverage map and bind every required setup family (Flag/Pullback/Breakout/MicroContinuation/MicroStructure continuation) to timing split enforcement.
2. Enforce a single, shared early/late clamp contract for all continuation families before router winner selection.
3. Add per-entry audit tags (`EARLY_CONTINUATION_APPLIED`, `LATE_CONTINUATION_CLAMPED`) for deterministic post-trade attribution.

---

## TASK-04 — Final acceptance hardening

### Status
**PARTIAL**

### Implemented
- Global minimum score threshold (`MinScoreThreshold = 55`) is active and used in routing decisions.
- FX-specific acceptance filter in `TradeRouter` includes score gates and trigger requirements.
- Overextension / late-chase rejection exists where `ContinuationTimingGate` is used.
- Rich decision logging exists (`[ENTRY DECISION]`, reject detail logs), supporting no-silent-decision intent.

### Missing / Incorrect vs requirement
- Hardening is **not centralized as a pre-execution final gate**; it is spread across entry evaluators + FX-only router filter.
- “Timing validity” and “late continuation clamp” are not uniformly enforced for non-FX/non-timing-gated setups.
- Requirement says **HTF hard blocking not allowed**, but hard HTF blocks are present:
  - In router FX filter (`HTF_STRONG_OPPOSITE_LTF_WEAK` -> reject).
  - In several entry types (e.g., index entries rejecting on HTF mismatch when continuation authority absent).
- “Structure + state combined validation” is not enforced as a single final acceptance contract before execution.

### Architectural alignment
- TradeCore orchestration: partially aligned (router + logs), but acceptance policy is fragmented.
- PositionContext SSOT / FinalDirection SSOT: generally respected downstream at executor handoff.
- No silent decision: mostly respected through logs, but policy fragmentation reduces explainability consistency.

### Risk impact
- Weak setups can pass/deny inconsistently depending on entry type path.
- HTF hard-block behavior can silently violate intended policy and suppress otherwise valid LTF opportunities.
- Audit difficulty: acceptance rationale differs by module instead of one canonical gate.

### Actions
1. Define one final pre-execution acceptance contract in orchestration path (after scoring, before executor dispatch).
2. Remove HTF hard-block outcomes from final acceptance path and convert to non-blocking penalty/weighting where required by task policy.
3. Require all entries to publish timing/state/structure verdict fields consumed by that final gate.

---

## TASK-05 — Exit timing retune (instrument level)

### Status
**PARTIAL**

### Implemented
- Post-TP1 trailing is instrument-class aware via `TrailingProfiles` (`Fx`, `Index`, `Crypto`, `Metal`).
- `ForceVolatilityTrailAfterBars` exists in profile and is used by `AdaptiveTrailingEngine` to force fallback when structure trail cannot progress.
- TP1 -> BE -> trailing lifecycle ordering exists in exit managers.
- TP1 event/state is written into `PositionContext` and consumed by post-TP1 logic.

### Missing / Incorrect vs requirement
- BE offset is not consistently instrument-specific:
  - Many executors set `BeOffsetR = 0.10` directly.
  - Some paths (e.g., BTC) do not set `BeOffsetR`, relying on defaults.
- Trailing activation timing differentiation is limited; `ForceVolatilityTrailAfterBars` is currently `6` for all instrument classes in profile.
- Trailing aggressiveness parameters differ by class, but volatility fallback distance contains a critical override bug:
  - `BuildVolatilityStop` computes regime multiplier, then overwrites it with `slAtrMultiplier`, nullifying low/normal/high ATR profile multipliers.
- XAU profile type exposes `BeOffsetR` fields, but matrix construction shown does not populate TP/BE/trail values in the builder fragment; runtime fallback may dominate.

### Architectural alignment
- PositionContext usage is coherent for TP1/BE/trailing states.
- Class-level profile routing exists, but some runtime knobs remain hardcoded in executors, weakening instrument-level policy purity.

### Risk impact
- Profit protection behavior can converge across instruments instead of truly diverging by class.
- Fallback trail distance may not react to volatility regime as intended, creating too-loose/too-tight stops.
- TP1 lock quality varies by instrument implementation path.

### Actions
1. Make BE offset source deterministic per instrument class (no mixed hardcoded/default behavior).
2. Differentiate `ForceVolatilityTrailAfterBars` and related timing knobs per class according to intended retune.
3. Fix volatility fallback multiplier path so regime multipliers are actually applied (and auditable in logs).

---

## TASK-06 — Central MFE/MAE lifecycle tracker

### Status
**PARTIAL**

### Implemented
- `PositionContext` contains lifecycle fields: `MfeR`, `MaeR`, `BestFavorablePrice`, `WorstAdversePrice`, TP1 flags.
- `TradeViabilityMonitor` centrally updates `MfeR`/`MaeR` when invoked.
- Rehydrate service rebuilds excursion stats and best/worst prices for restored positions.
- CSV/trade logs consume `MfeR`/`MaeR` from `PositionContext`.

### Missing / Incorrect vs requirement
- Runtime excursion tracking is still effectively coupled to TVM invocation (`ShouldEarlyExit` calls `UpdateMfeMae`).
- TVM short-circuits after TP1 (`if (ctx.Tp1Hit) return false`), so ongoing post-TP1 lifecycle excursion updates are not guaranteed.
- `BestFavorablePrice` / `WorstAdversePrice` are not maintained in the normal live loop by central tracker (primarily set in rehydrate path).
- No explicit centralized “TP1 before/after lifecycle segments” tracker exists.

### Architectural alignment
- PositionContext as SSOT is conceptually in place, but lifecycle writes are incomplete and phase-dependent.
- No-silent-decision principle is less relevant here; issue is data lifecycle completeness.

### Risk impact
- MFE/MAE can be underreported after TP1, degrading CSV quality and post-trade analytics.
- Lifecycle mismatch between logs, context state, and true price path (especially for runners).
- Strategy tuning based on CSV can drift due to missing post-TP1 excursion capture.

### Actions
1. Introduce a dedicated lifecycle updater called on every managed tick (independent from TVM exit decision path).
2. Persist and update `BestFavorablePrice`/`WorstAdversePrice` continuously pre- and post-TP1.
3. Add explicit lifecycle markers for TP1-before and TP1-after excursion segments into `PositionContext` and CSV outputs.

