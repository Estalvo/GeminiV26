# EntryType Timing Discipline Audit (2026-03-29)

Scope: static code audit of continuation timing enforcement across EntryTypes.

## 1) Timing signals inventory

### Core timing gate and side flags
- `ContinuationTimingGate.Evaluate(...)` consumes:
  - side-activation flags (`IsTimingLongActive`, `IsTimingShortActive`)
  - early/late continuation flags (`HasEarlyContinuation*`, `HasLateContinuation*`)
  - overextension flags (`IsOverextended*`)
  - continuation attempts (`ContinuationAttemptCount*`)
  - bars since impulse (`BarsSinceImpulse*`)
  - freshness (`ContinuationFreshness*`)
- Decisions: `Early`, `LateValid`, `LateReject`, `OverextendedReject`, `SideInactiveReject`.
- Output controls scoring and strictness (`ScoreAdjustment`, `MinScoreAdjustment`, `RequireStrongTrigger`, `RequireStrongStructure`).

### `barsSinceBreak`
- Router/runtime level:
  - computed via `GetBarsSinceBreak(...)` from `BarsSinceHighBreak_M5` / `BarsSinceLowBreak_M5`.
  - used in managed early-break penalties and trigger freshness/state transitions.
- Entry-level notable usage:
  - `FX_FlagEntry` has multiple direct `barsSinceBreak` checks.
  - `FX_MicroStructureEntry` uses side-local `barsSinceBreak` thresholds.

### Pullback-depth signals
- Present in pullback and continuation families (e.g., `pullbackDepth`, `pullbackDepthAtr`, `pullbackDepthR`), but enforcement thresholds differ per EntryType.

### Late-continuation flags
- Explicit late flag usage appears in:
  - `ContinuationTimingGate` (entry-internal only for gate adopters)
  - `PassFinalAcceptance(...)` (global final gate for all selected candidates)

## 2) EntryType mapping (requested families)

## Flag
- **Strict timing**:
  - `TC_Flag` (uses `ContinuationTimingGate`).
- **Partial/non-uniform timing**:
  - `FX_Flag` (custom `barsSinceBreak` rules/session-based checks, no shared timing gate).
  - `Index_Flag`, `XAU_Flag`, `Crypto_Flag` (no `ContinuationTimingGate` call).

## Pullback
- **Strict timing**:
  - `TC_Pullback` (uses `ContinuationTimingGate`).
- **Not strict / mainly structural-depth rules**:
  - `FX_Pullback`, `Index_Pullback`, `XAU_Pullback`, `Crypto_Pullback` (no shared timing gate; rely primarily on pullback/structure scoring).

## Breakout
- **Strict timing**:
  - `BR_RangeBreakout` (uses `ContinuationTimingGate`).
- **Not strict / custom rules**:
  - `FX_RangeBreakout`, `Index_Breakout`, `Crypto_RangeBreakout` (no shared timing gate).

## Reversal
- `FX_Reversal`, `XAU_Reversal`, `TR_Reversal` are not continuation-gate based (expectedly more reversal-structure driven), and do not enforce shared early-vs-late continuation policy.

## Microstructure entries
- **Strict timing**:
  - `FX_MicroStructure` (uses `ContinuationTimingGate`; additional bars-since-break checks).
  - `FX_MicroContinuation` and `FX_ImpulseContinuation` also use the shared timing gate.

## 3) Inconsistencies found

1. **Shared early-vs-late continuation policy is not uniformly applied**.
   - Some continuation-like families use `ContinuationTimingGate`, while many Flag/Pullback/Breakout families do not.

2. **Late continuation can be treated differently by EntryType before final gate**.
   - Gate-adopting entries can reject late/missed continuation early.
   - Non-gate entries can still proceed to router/final selection with only indirect timing pressure.

3. **`barsSinceBreak` discipline is fragmented**.
   - Strongly enforced in `FX_FlagEntry` and runtime trigger manager.
   - Absent or indirect in many other entries.

## 4) FinalAcceptance vs EntryType-level timing

- **Inside EntryType**:
  - Only entries calling `ContinuationTimingGate` get consistent early/late split + required strong trigger/structure escalation.

- **FinalAcceptance (global)**:
  - Applies global timing rejection via timing penalty (`recommendedTimingPenalty <= -10`).
  - Applies global late-continuation guard (`HasLateContinuation*`) and rejects weak/low-score late setups.

- **Conclusion**:
  - Timing exists at final acceptance for all selected entries, but **EntryType-level timing enforcement is uneven**.
  - This creates mixed behavior where some entries are filtered early, others only late in pipeline.

## 5) Temporary logging added

Added centralized per-candidate timing trace in execution-state validation loop:

`[TIMING][{EntryType}] barsSinceBreak={x} late={flag}`

This log now runs for each candidate EntryType in runtime validation.
