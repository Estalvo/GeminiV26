# FX Over-Filtering Audit (Gemini V26)
Date: 2026-03-31
Scope symbols: EURUSD, GBPUSD, USDJPY, USDCHF, USDCAD, EURJPY, GBPJPY, AUDUSD, NZDUSD, AUDNZD

## Evidence quality
- Code coverage: full for FX entry pipeline and FX entry types.
- Runtime-log coverage: **no FX runtime logs found** in repository logs snapshot (`Logs/*/runtime_20260330.log` contains only US30/XAU/USTECH100).
- Therefore, frequency metrics are split into:
  - runtime-observed metrics (FX = unavailable)
  - static blocker inventory from source code.

---

## Phase 1 — Entry flow trace (FX)

1. **Entry candidate creation**
   - TradeCore builds context, then runs `EntryRouter.Evaluate(new[] { _ctx })` per bar/pass.
   - For FX symbols, configured entry types are:
     - `FX_Flag`, `FX_FlagContinuation`, `FX_MicroStructure`, `FX_MicroContinuation`, `FX_ImpulseContinuation`, `FX_Pullback`, `FX_RangeBreakout`, `FX_Reversal`.

2. **Evaluation stage (Entry types)**
   - Each FX entry type creates an `EntryEvaluation` and can hard-invalidate (`IsValid=false`) with explicit reason tokens (e.g., NO_LOGIC_BIAS, NO_PULLBACK_STRUCTURE, SESSION_MATRIX_*).

3. **Router stage (EntryRouter)**
   - Router normalizes scores/state via `EntryDecisionPolicy.Normalize`.
   - Router itself does not drop by score directly; it forwards all evaluations.

4. **Matrix/HTF/penalty stage (TradeCore + TradeRouter)**
   - TradeCore applies HTF score-only bias: +5 aligned, -10 misaligned.
   - TradeCore applies restart protection and early-break penalties.
   - TradeRouter applies FX acceptance filters:
     - HTF mismatch penalty -10 (plus optional extra soft penalty)
     - threshold gate against `MinScoreThreshold=55` (with minor timing bias)
     - trigger-required gate
     - minimum quality gate (`decisionScore >= 45`)

5. **Final selection**
   - `TradeRouter.SelectEntry` chooses highest-score executable candidate (`IsValid && TriggerConfirmed`).
   - If none survives, TradeCore logs final reason `NO_SELECTED_ENTRY`.

---

## Phase 2 — Blocking analysis

### Runtime (from available logs)
- FX symbols in logs: **0 occurrences** (no FX runtime lines in provided `Logs/*/runtime_20260330.log`).
- FX rejection frequencies from runtime logs: **not measurable**.

### Static blocker inventory (code-derived)
Most frequent explicit invalidation reasons in FX entry files:
- `NO_LOGIC_BIAS` (16 call sites)
- `CTX_NOT_READY` (8)
- `NO_FX_PROFILE` (5)
- `TIMING_LATE_NEEDS_STRONG_STRUCTURE` (4)
- `TIMING_LATE_NEEDS_STRONG_TRIGGER` (4)
- `PB_TOO_DEEP` (4)

Additional high-impact FX global blockers:
- `FX_SCORE_BELOW_THRESHOLD`
- `FX_TRIGGER_REQUIRED`
- `FX_EARLY_BLOCK`
- `AUTH_TRIGGER_BYPASS_BLOCK`
- `FX_MIN_QUALITY_BLOCK`

---

## Phase 3 — Matrix strictness

### Threshold reachability
- Global minimum threshold is `55`.
- Reachability is plausible in ideal setups, but stacked penalties can quickly push borderline-valid setups below 55.

### Score/penalty pressure points
- HTF score-only in TradeCore: misalignment = `-10`.
- FX acceptance in TradeRouter applies another HTF mismatch penalty `-10` (plus optional extra `-4/-8` soft penalty when HTF confidence is high and logic confidence low).
- Early-break protection in TradeCore applies up to `-15` before trigger/state processing.
- Restart soft phase can apply `-20`.

**Interpretation:** multiple independent penalty layers can combine to -20/-35 (or more) before final acceptance, making threshold 55 practically hard to maintain in live-noise conditions.

---

## Phase 4 — Structure validation

Structure is validated at multiple layers:
- Entry type internal structure checks (flag/pullback/micro requirements).
- Trigger diagnostics require at least one of breakout close, M5 structure break, or M1 break.
- Early continuation rejection if impulse-phase early continuation has no minimal pullback structure and no strong trigger (`EARLY_NO_STRUCTURE`).

Assessment:
- Logic is coherent (not random), but **multi-layered** structure rejection increases false negatives risk in noisy FX sessions.
- Particularly strict when combined with trigger-required + score penalties.

---

## Phase 5 — Regime filtering

Regime constraints that can block/degrade FX entries:
- Session matrix (Asia): `AllowFlag=false`, `AllowBreakout=false`, higher ATR/ADX constraints.
- Continuation entries often require trend/impulse/pullback timing coherence.
- FX instrument matrix often sets `RequireHtfAlignmentForContinuation=true`.

Assessment:
- Regime logic is not inherently wrong, but Asia + HTF mismatch + trigger requirement is a strict conjunction likely to suppress many valid-but-imperfect continuations.

---

## Phase 6 — Entry-type analysis (code-level viability)

- `FX_Flag`: active, but heavily penalized by many micro-conditions.
- `FX_FlagContinuation`: active, but tight pullback band and timing gates.
- `FX_MicroStructure`: active, but multiple hard filters (compression/vol/range limits).
- `FX_MicroContinuation`: active, trend-slope/trigger constrained.
- `FX_ImpulseContinuation`: active but can self-block in expansion (`VolExpanding`) and trend checks.
- `FX_Pullback`: active, but many structure/ADX/depth/HTF penalties + hard stops.
- `FX_RangeBreakout`: active, but depends on range regime + low-slope requirement + session matrix breakout allowance.
- `FX_Reversal`: active but profile- and evidence-dependent.

No entry type is hard-disabled in code for FX. Over-filtering risk comes from cumulative gating, not explicit deactivation.

---

## Phase 7 — Log validation

- Total FX entry attempts: **not available** from provided runtime logs (no FX symbols present).
- Executed FX trades: **0 in provided reconstructed trade CSV**.
- Gap analysis (attempts vs executions): inconclusive from this repository snapshot due to missing FX runtime logs.

---

## Phase 8 — Top 3 root causes (code-proven)

1. **Penalty stacking across layers** (HTF in TradeCore + HTF in TradeRouter + early-break/restart penalties) can push candidates below threshold even when base setup is acceptable.
2. **Trigger-required execution policy** (`IsValid && TriggerConfirmed`) rejects score-valid setups that are still in ARMED/setup state.
3. **Strict session/regime conjunctions** (especially Asia matrix restrictions + continuation alignment requirements) reduce survivability in realistic FX noise.

---

## Phase 9 — Decision table

| Component | Status | Too Strict? | Action |
|---|---|---|---|
| Matrix threshold (55) | moderate-high | **conditionally yes** (with stacked penalties) | keep threshold; reduce stacked penalty overlap |
| Structure filter | strict | **borderline** | keep core logic; avoid duplicate hard structure-style blocks |
| Regime filter | strict in Asia/continuation | **yes in conjunction** | soften conjunctions, not regime model |
| Entry types | active | mixed | keep all; tune only outlier constraints |

---

## Phase 10 — Safe adjustments (only where justified)

### Recommended minimal changes (small, targeted)
1. **Avoid double HTF punishment**
   - Keep one -10 layer, reduce the second layer to -4/-6 for FX.
   - Rationale: preserves directional discipline but improves threshold reachability.

2. **Cap cumulative pre-router penalty budget for FX continuations**
   - Example: cap combined early-break + restart + HTF penalties to a bounded max impact.
   - Rationale: avoids unrealistic over-penalization from overlapping protections.

3. **Fix likely outlier matrix value before behavioral tuning**
   - GBPUSD Asia `MaxPullbackAtr = 0.08` appears abnormally tight vs other pairs/sessions.
   - Validate intent; if typo, normalize to instrument-consistent range.

### Not recommended
- Removing trigger requirement entirely.
- Removing regime/HTF systems.
- Lowering global threshold broadly.

---

## Phase 11 — Final verdict

- **Over-filtering risk is present in code design via cumulative gating/penalty overlap.**
- **Runtime proof for FX frequency is currently missing** in repository logs.
- Therefore: **NO BEHAVIOR CHANGE SHOULD BE APPLIED UNTIL FX runtime logs are collected and quantified**.
- If immediate action is required, only apply the three minimal adjustments above behind a feature flag.

