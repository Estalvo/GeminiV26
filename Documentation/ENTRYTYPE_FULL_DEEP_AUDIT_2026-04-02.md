# Gemini V26 – Full Deep Audit of Active EntryTypes (2026-04-02)

## Scope + execution reach map

Active set is determined from `TradeCore` runtime registration by instrument class, then checked against `EntryRouter` hard skips and EntryType self-disables.

- **FX registered:** `FX_Flag`, `FX_FlagContinuation`, `FX_MicroStructure`, `FX_MicroContinuation`, `FX_ImpulseContinuation`, `FX_Pullback`, `FX_RangeBreakout`, `FX_Reversal`.
- **INDEX registered:** `Index_Pullback`, `Index_Breakout`, `Index_Flag`.
- **METAL registered:** `XAU_Flag`, `XAU_Pullback`, `XAU_Reversal`, `XAU_Impulse`.
- **CRYPTO registered:** `Crypto_Pullback` (`BTC_PullbackEntry`), `Crypto_Flag` (`BTC_FlagEntry`), `Crypto_RangeBreakout` (`BTC_RangeBreakoutEntry`).
- **Fallback/legacy (unknown symbol route):** `TC_Pullback`, `TC_Flag`, `BR_RangeBreakout`, `TR_Reversal`.

Runtime caveats:
- `XAU_Impulse` is hard-skipped in `EntryRouter`.
- `FX_Flag` is compiled as a stub that always returns invalid (`"FX_FlagEntry disabled"`).

---

## Full execution path trace (shared)

`EntryType.Evaluate(ctx)` → `EntryRouter.Evaluate(...)` normalization/snapshot → `TradeCore` score adjustments + execution state machine (`UpdateExecutionStateMachine`) → `TradeRouter.SelectEntry` ranking/executability checks → `PassFinalAcceptance` (direction/restart/overextended block) → instrument executor.

### Where bad trades should be filtered but can leak

1. **Entry-local structural weakness not always terminal**
   - Some entries rely on large additive scoring instead of hard structure invalidation, so weak structure can still survive when trigger or score is inflated.
2. **Direction handling is uneven**
   - HTF is often score-only/soft mismatch in many entries; only some paths hard-block misalignment.
3. **Managed trigger family is partial**
   - Trigger management is enabled only for selected EntryTypes; others are effectively “always trigger-eligible” once valid.
4. **FinalAcceptance is intentionally minimal**
   - Final acceptance only blocks direction-none, restart hard protection, and overextension; no quality/structure enforcement here.

---

## EntryType-by-EntryType audit

### EntryType: FX_Flag

STRUCTURE QUALITY:
Weak

TIMING QUALITY:
Random (non-executable)

DIRECTION RELIABILITY:
Often wrong (forced `None`)

OVERTRADING RISK:
Low (disabled)

MAIN FAILURE MODES:
- Hard-disabled stub always returns invalid.
- Still present in registration, creating pipeline noise.

ROOT CAUSE:
- Implementation replaced by stub; production logic commented out.

RECOMMENDATION:
4) DISABLE (remove from active registration to reduce noise and confusion)

---

### EntryType: FX_FlagContinuation

STRUCTURE QUALITY:
Medium

TIMING QUALITY:
Mid

DIRECTION RELIABILITY:
Aligned

OVERTRADING RISK:
Medium

MAIN FAILURE MODES:
- Late continuations can pass with score support, not always with strong post-break follow-through.
- Pullback-depth windows are rigid; can miss good trades then allow mediocre ones in tolerated band.
- Still score-sensitive when setupScore is barely positive.

ROOT CAUSE:
- Mixed hard gates + score accumulation; continuation timing discipline exists but quality still partially score-mediated.

RECOMMENDATION:
3) RESTRICT (require stronger follow-through when `timing.IsLate`; keep only clean breakout + structure pair)

---

### EntryType: FX_MicroStructure

STRUCTURE QUALITY:
Medium

TIMING QUALITY:
Mid

DIRECTION RELIABILITY:
Mixed

OVERTRADING RISK:
Medium

MAIN FAILURE MODES:
- Micro-conditions can fit noise bursts (short-lived compression breaks).
- Trigger-dominance logic can rescue otherwise weak setupScore.
- Sensitive to short-term volatility regime flips.

ROOT CAUSE:
- Built for micro continuation windows; granular trigger math increases noise-reactivity.

RECOMMENDATION:
3) RESTRICT (keep only when both structural break and directional follow-through are present; remove trigger-only rescue behavior)

---

### EntryType: FX_MicroContinuation

STRUCTURE QUALITY:
Medium

TIMING QUALITY:
Mid

DIRECTION RELIABILITY:
Aligned

OVERTRADING RISK:
Medium

MAIN FAILURE MODES:
- Pullback maturity/depth hard gates are narrow and can become regime-misaligned.
- Good in trend, but brittle in transition bars.
- Can rely on score lift once inside valid band.

ROOT CAUSE:
- ContinuationTimingGate helps, but local tuning remains heavily parameter-bound.

RECOMMENDATION:
2) FIX (tighten structural requirement so depth/maturity are necessary but not sufficient without clean directional structure break)

---

### EntryType: FX_ImpulseContinuation

STRUCTURE QUALITY:
Medium

TIMING QUALITY:
Mid

DIRECTION RELIABILITY:
Aligned

OVERTRADING RISK:
Low-Medium

MAIN FAILURE MODES:
- Can reject too much in expansion regimes (`VolExpanding` block), then accept later weaker continuation.
- Strong dependence on pullback band validity.
- Setup still score-additive beyond core structure.

ROOT CAUSE:
- Rule set is conservative but occasionally mis-timed around volatility transitions.

RECOMMENDATION:
2) FIX (remove contradictory vol gating behavior that blocks healthy early continuation then allows later degraded setups)

---

### EntryType: FX_Pullback

STRUCTURE QUALITY:
Medium

TIMING QUALITY:
Mid-Late

DIRECTION RELIABILITY:
Mixed

OVERTRADING RISK:
High

MAIN FAILURE MODES:
- Historically broad acceptance surface (many soft penalties instead of hard invalidation).
- Pullback + flag coexistence can permit continuation-chase entries.
- Can re-fire repeatedly in noisy trend pauses.

ROOT CAUSE:
- Pullback family blends many contextual soft conditions; insufficient hard no-trade zoning.

RECOMMENDATION:
3) RESTRICT (add strict no-trade in chop/compression + one-attempt-per-impulse behavior)

---

### EntryType: FX_RangeBreakout

STRUCTURE QUALITY:
Medium

TIMING QUALITY:
Mid

DIRECTION RELIABILITY:
Mixed

OVERTRADING RISK:
Medium

MAIN FAILURE MODES:
- Range detection + breakout confirmation can trigger on weak edge pokes.
- Follow-through may be shallow but still pass when score sufficient.
- Susceptible to fake breaks at range boundaries.

ROOT CAUSE:
- Range breakout logic is structurally sensible, but breakout quality threshold is not consistently strict.

RECOMMENDATION:
2) FIX (require post-break close persistence / immediate continuation bar quality)

---

### EntryType: FX_Reversal

STRUCTURE QUALITY:
Medium

TIMING QUALITY:
Late

DIRECTION RELIABILITY:
Mixed

OVERTRADING RISK:
Medium

MAIN FAILURE MODES:
- Reversal evidence threshold can pass local counter-move noise.
- In trend days, reversal attempts can fire against dominant impulse.
- Trigger is quality signal, not always hard requirement.

ROOT CAUSE:
- Reversal model uses evidence scoring but not enough hard structural invalidation in anti-trend context.

RECOMMENDATION:
3) RESTRICT (only allow in confirmed range/mean-reversion context with hard anti-trend protections)

---

### EntryType: Index_Pullback

STRUCTURE QUALITY:
Strong

TIMING QUALITY:
Early-Mid

DIRECTION RELIABILITY:
Aligned

OVERTRADING RISK:
Low-Medium

MAIN FAILURE MODES:
- Can still pass on weak continuation authority in borderline fatigue conditions.
- Pullback depth/bars constraints differ by profile, causing cross-index inconsistency.

ROOT CAUSE:
- Good structural skeleton with profile-dependent variability.

RECOMMENDATION:
1) KEEP (minor tuning only)

---

### EntryType: Index_Breakout

STRUCTURE QUALITY:
Strong

TIMING QUALITY:
Mid

DIRECTION RELIABILITY:
Aligned

OVERTRADING RISK:
Low-Medium

MAIN FAILURE MODES:
- Early breakout allowance can admit marginal impulse context.
- SetupScore can be recovered by breakout events even when trend fatigue is rising.

ROOT CAUSE:
- Strong safeguards exist, but breakout urgency logic can still overvalue single-bar strength.

RECOMMENDATION:
2) FIX (require stronger continuation authority for fatigue>=high zones)

---

### EntryType: Index_Flag

STRUCTURE QUALITY:
Strong

TIMING QUALITY:
Early-Mid

DIRECTION RELIABILITY:
Aligned

OVERTRADING RISK:
Low

MAIN FAILURE MODES:
- Rare false positives around wide but technically valid flag ranges.
- Trigger-score additive model can mildly overrate one-bar spikes.

ROOT CAUSE:
- Mostly robust structure checks; minor sensitivity to momentum candle outliers.

RECOMMENDATION:
1) KEEP

---

### EntryType: XAU_Flag

STRUCTURE QUALITY:
Strong

TIMING QUALITY:
Early-Mid

DIRECTION RELIABILITY:
Aligned

OVERTRADING RISK:
Low-Medium

MAIN FAILURE MODES:
- Can become selective to the point of delayed entries, then chase breakout tail.
- HTF mismatch handling is partly soft unless LTF strength missing.

ROOT CAUSE:
- Good structure-first design, but conflict resolution still mixes soft/hard behavior.

RECOMMENDATION:
2) FIX (promote HTF conflict from soft to hard unless explicit high-quality local break)

---

### EntryType: XAU_Pullback

STRUCTURE QUALITY:
Strong

TIMING QUALITY:
Mid

DIRECTION RELIABILITY:
Aligned

OVERTRADING RISK:
Medium

MAIN FAILURE MODES:
- Pullback windows can open repeatedly inside chop-like retrace clusters.
- Some high-quality opportunities are blocked, then lower-quality later entries pass.

ROOT CAUSE:
- Pullback policy is robust but may be over-constrained early and looser later.

RECOMMENDATION:
2) FIX (prefer first qualified pullback window; reduce repeated re-entry tolerance)

---

### EntryType: XAU_Reversal

STRUCTURE QUALITY:
Medium

TIMING QUALITY:
Late

DIRECTION RELIABILITY:
Mixed

OVERTRADING RISK:
Medium

MAIN FAILURE MODES:
- Reversal in non-clean ranges can still appear valid by score composition.
- Vulnerable to trend-resumption whipsaw after entry.

ROOT CAUSE:
- Reversal scoring in metal regime remains sensitive to local counter-move noise.

RECOMMENDATION:
3) RESTRICT (hard range-context requirement + stricter breakout-against-entry invalidation)

---

### EntryType: XAU_Impulse

STRUCTURE QUALITY:
Weak (runtime inactive)

TIMING QUALITY:
Random (not routed)

DIRECTION RELIABILITY:
Often wrong (effectively none)

OVERTRADING RISK:
Low (blocked)

MAIN FAILURE MODES:
- Registered but blocked in `EntryRouter`.
- Dead code path increases maintenance risk.

ROOT CAUSE:
- Entry is intentionally disabled by router-level skip.

RECOMMENDATION:
4) DISABLE (remove from registration and enum if unused)

---

### EntryType: Crypto_Pullback (BTC_PullbackEntry)

STRUCTURE QUALITY:
Medium

TIMING QUALITY:
Mid-Late

DIRECTION RELIABILITY:
Mixed

OVERTRADING RISK:
High

MAIN FAILURE MODES:
- Large multi-factor score composition can mask weak structure.
- Pullback + breakout + flag boosts can stack in noisy compression.
- Higher chance of repeated firing in unstable volatility transitions.

ROOT CAUSE:
- Over-reliance on composite scoring; hard structural invalidation not dominant enough.

RECOMMENDATION:
3) RESTRICT (require clear pullback completion + directional continuation break before validity)

---

### EntryType: Crypto_Flag (BTC_FlagEntry)

STRUCTURE QUALITY:
Medium-Strong

TIMING QUALITY:
Mid

DIRECTION RELIABILITY:
Aligned

OVERTRADING RISK:
Medium

MAIN FAILURE MODES:
- Late-flag and stale-impulse windows still regime-sensitive in crypto volatility spikes.
- Trigger model can overrate abrupt breakout candles without durable continuation.

ROOT CAUSE:
- Good structural basis, but crypto volatility amplifies one-bar trigger noise.

RECOMMENDATION:
2) FIX (increase anti-fake-break requirement: continuation persistence over first post-break bars)

---

### EntryType: Crypto_RangeBreakout (BTC_RangeBreakoutEntry)

STRUCTURE QUALITY:
Medium

TIMING QUALITY:
Mid

DIRECTION RELIABILITY:
Mixed

OVERTRADING RISK:
High

MAIN FAILURE MODES:
- Range breakout in crypto frequently fakes out; current logic can still pass weak continuation.
- Strong dependence on setupScore stacking.
- Can enter at range-edge noise during volatility regime shifts.

ROOT CAUSE:
- Range classification + breakout confirmation not strict enough for crypto microstructure.

RECOMMENDATION:
4) DISABLE (current quality too unstable vs false breakout frequency)

---

### EntryType: TC_Pullback (legacy fallback)

STRUCTURE QUALITY:
Medium

TIMING QUALITY:
Mid

DIRECTION RELIABILITY:
Mixed

OVERTRADING RISK:
Medium

MAIN FAILURE MODES:
- Generic multi-asset heuristics not tightly fit to modern instrument-specific behavior.
- Mixed hard/soft logic increases ambiguity.

ROOT CAUSE:
- Legacy generalized entry competing with specialized modules.

RECOMMENDATION:
4) DISABLE (keep only for test harness, not production execution)

---

### EntryType: TC_Flag (legacy fallback)

STRUCTURE QUALITY:
Medium

TIMING QUALITY:
Mid

DIRECTION RELIABILITY:
Mixed

OVERTRADING RISK:
Medium

MAIN FAILURE MODES:
- Generic continuation logic may conflict with newer trigger/state policies.
- Can pass with scoring even when structure is only marginal.

ROOT CAUSE:
- Legacy design overlaps with stronger dedicated entries.

RECOMMENDATION:
4) DISABLE

---

### EntryType: BR_RangeBreakout (legacy fallback)

STRUCTURE QUALITY:
Medium

TIMING QUALITY:
Mid-Late

DIRECTION RELIABILITY:
Mixed

OVERTRADING RISK:
High

MAIN FAILURE MODES:
- Classic range breakout weakness: fake breaks in low-quality continuation.
- Score layering can keep weak breakouts alive.

ROOT CAUSE:
- Legacy breakout rules are less robust than instrument-specific families.

RECOMMENDATION:
4) DISABLE

---

### EntryType: TR_Reversal (legacy fallback)

STRUCTURE QUALITY:
Medium

TIMING QUALITY:
Late

DIRECTION RELIABILITY:
Often wrong (in trend regimes)

OVERTRADING RISK:
Medium-High

MAIN FAILURE MODES:
- Reversal evidence can be present in transient pullback noise.
- Against-trend reversals remain vulnerable to immediate continuation failure.

ROOT CAUSE:
- Legacy reversal conditions too permissive for modern trend/transition context.

RECOMMENDATION:
4) DISABLE

---

## Global summary

## 1) Ranking BEST → WORST

1. Index_Flag
2. Index_Pullback
3. Index_Breakout
4. XAU_Flag
5. XAU_Pullback
6. Crypto_Flag
7. FX_ImpulseContinuation
8. FX_FlagContinuation
9. FX_MicroContinuation
10. FX_MicroStructure
11. FX_RangeBreakout
12. XAU_Reversal
13. FX_Reversal
14. FX_Pullback
15. Crypto_Pullback
16. Crypto_RangeBreakout
17. TC_Flag
18. TC_Pullback
19. BR_RangeBreakout
20. TR_Reversal
21. FX_Flag (stub disabled)
22. XAU_Impulse (router blocked)

## 2) Top 2 reliable EntryTypes
- `Index_Flag`
- `Index_Pullback`

Worst (must disable)
- `Crypto_RangeBreakout`
- all legacy fallback entries (`TC_Flag`, `TC_Pullback`, `BR_RangeBreakout`, `TR_Reversal`)
- already-inactive dead paths (`FX_Flag`, `XAU_Impulse`) should be removed from active registration

## 3) Systemic shared flaws
- Overuse of additive score rescue after weak structure.
- Uneven hard/soft HTF enforcement across entries.
- Inconsistent late-continuation handling (some use shared timing gate, others custom logic).
- Repeated re-fire risk in pullback families during compression/chop.
- Range breakout families are most exposed to fake breakout noise.

## 4) Is current EntryType system viable as a base?

**YES, but only after reduction.**

As-is, the system is too broad and includes disabled/dead/legacy paths that dilute quality. The base is viable if reduced to a smaller core centered on structurally robust entries (Index + selected XAU/FX continuation) and if weak breakout/reversal variants are restricted or removed.

---

## Final objective alignment (fewer + higher quality)

Recommended production core (minimal set):
- `Index_Flag`
- `Index_Pullback`
- `Index_Breakout`
- `XAU_Flag`
- `XAU_Pullback`
- `FX_ImpulseContinuation` (after fix)
- `FX_FlagContinuation` (restricted)

Disable / decommission:
- `FX_Flag` (stub)
- `XAU_Impulse` (blocked)
- `Crypto_RangeBreakout`
- legacy fallback quartet (`TC_*`, `BR_*`, `TR_*`)
