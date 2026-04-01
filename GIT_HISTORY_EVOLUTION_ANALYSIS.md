# Git History Evolution Analysis (Trade Suppression Focus)

Repository: `GeminiV26`  
Analyzed on: 2026-04-01  
Total commits scanned: 1216

## Method

- Scanned full history with `git log` (reverse and normal) for chronology and thematic commit clusters.
- Tagged commit subjects containing suppression-related terms (`gate`, `filter`, `block`, `reject`, `timing`, `HTF`, `structure`, `consistency`, `integrity`).
- Computed weekly signal density (suppression-term commits / total commits).
- Reviewed diffs/statistics for likely turning-point commits (especially `Core/TradeCore.cs`, gate/qualification modules, and entry types).

## Phase Model

### Phase 1 — Initial working phase (2026-01-30 → ~2026-03-10)

Characteristics:
- Base system import and broad strategy scaffolding.
- Mostly feature build-out and instrument-level tuning.
- Relatively low suppression density in commit subjects.

Evidence:
- First commit: `7f8cd0b` (2026-01-30) `Initial full project import`.
- First explicit gate mention appears early but not in dense clusters: `227a114` (2026-02-18) `Update GlobalSessionGate.cs`.
- Weekly suppression-density remained low: W08 = 5%, W09 = 0%, W10 = 6%.

Interpretation:
- System likely still permissive enough to generate entries while architecture and per-asset logic matured.

### Phase 2 — Stability / controlled hardening phase (~2026-03-11 → 2026-03-26)

Characteristics:
- Quality-oriented filtering introduced progressively.
- HTF/structure checks increased, but still comparatively moderate.

Evidence:
- Example commits:
  - `6cc3dc6` (2026-03-11) `Add BTC pullback quality filters for dead setup rejection`.
  - `7647a27` (2026-03-10) `Refine transition detection with impulse and pullback quality filters`.
  - `cc98a5f` (2026-03-05) `Update FX_MicroStructureEntry.cs` (structure emphasis).
- Weekly suppression-density rose but stayed moderate: W11 = 14%, W12 = 16%.

Interpretation:
- Filtering increased to improve quality, but no single-day gate explosion yet.

### Phase 3 — Over-filtering / suppression surge (2026-03-27 → 2026-04-01)

Characteristics:
- Rapid addition of hard rejects, veto layers, timing guards, session hard blocks, and continuation integrity filters.
- High frequency of explicit block/reject/filter language.
- Followed by partial rollback/softening commits.

Evidence:
- Weekly suppression-density jumped sharply: W13 = 32%, W14 = 28%.
- Highest daily concentrations:
  - 2026-03-27: 40 suppression-term hits.
  - 2026-03-29: 32 hits.
  - 2026-03-30: 25 hits.
  - 2026-03-31: 18 hits.
  - 2026-04-01: 23 hits.

Interpretation:
- This is the strongest candidate window for entry-frequency collapse / overblocking regressions.

## Introduction Points by Constraint Type

### New gates

Key introductions:
- `3f4d9c3` (2026-03-27): **final veto-only acceptance gate** in `TradeCore`.
- `30f5ad2` (2026-03-31): **global hard session block gate** (19:30–02:00 UTC).
- `b0ad2f6` (2026-04-01): **block execution on direction consistency mismatches**.

### Stricter filters

Key introductions/intensifications:
- `ef2a7b5` (2026-03-31): continuation integrity filters + trigger invalid blocking.
- `550e9e7` (2026-03-31): hardened continuation momentum + flag structure filters.
- `bcbcab9` (2026-03-30): disabled XAU impulse + tightened XAU environment filters.

### Timing / HTF / structure constraints

Key introductions/intensifications:
- `14bb853` (2026-03-27): context-aware early/late continuation timing gate.
- `9668bab` (2026-03-30): tuned final timing gate threshold + override logic.
- `b9ce7d7` (2026-03-27): early continuation no-structure hard reject guard.
- `cfd2d5e` (2026-03-27): stale trigger age filter after trigger evaluation.
- `3865851` / `55744cb` (2026-03-30): XAU counter-HTF protection/filtering in final acceptance path.

## Commits Most Likely to Drop Entry Frequency

High-confidence candidates:

1. `3f4d9c3` (2026-03-27)  
   Adds final acceptance veto layer in `TradeCore` with multiple new false-return paths.

2. `14bb853` (2026-03-27)  
   Introduces early/late timing gate across multiple entry types.

3. `b9ce7d7` (2026-03-27)  
   Adds hard reject for early continuation without structure.

4. `cfd2d5e` (2026-03-27)  
   Adds stale-trigger age filter after trigger eval (extra drop-off stage).

5. `30f5ad2` (2026-03-31)  
   Introduces global hard session block; likely removes entire time windows of entries.

6. `ef2a7b5` (2026-03-31)  
   Adds broad continuation integrity blocking in `TradeCore`.

7. `550e9e7` (2026-03-31)  
   Tightens momentum/structure thresholds and blocking behavior.

8. `bcbcab9` (2026-03-30)  
   Disables one XAU entry mode (impulse) and tightens environment checks.

## Added Conditions vs. Strictness Correlation

Observed pattern:

- As the number of layered conditions increased (timing + HTF + structure + session + integrity + direction-consistency), commit language shifted from “score/penalty/tune” to “hard reject/block/veto.”
- Suppression-term density nearly doubled from W12 (16%) to W13 (32%).
- After strong hardening (Mar 27–31), several commits explicitly mention **over-filtering** and **softening**, indicating realized strictness/regression pressure:
  - `81dfb31` (2026-03-29): remove HTF hard rejects, soft penalties.
  - `825db1a` (2026-03-29): relax final acceptance filters.
  - `ee93068` (2026-03-31): tune continuation filters to reduce over-blocking.
  - `973eace` (2026-04-01): soften index continuation over-filtering.
  - `bed44c5` (2026-04-01): adjust final timing gate to soft-first.

Conclusion:
- The repository exhibits a clear hardening wave followed by corrective softening, strongly suggesting an overblocking regression during late March.

## Key Turning Points

1. **2026-03-27** — Multi-gate expansion day (timing + veto + structure + stale trigger + integrity): primary inflection toward suppression.
2. **2026-03-30 to 2026-03-31** — Hardening deepens (XAU disablement, final timing threshold tuning, continuation/session hard blocks).
3. **2026-03-29 and 2026-04-01** — Corrective softening wave (remove hard HTF rejects, relax final acceptance, soften over-filtered index/timing paths).

## Suspected Regression Commits (Overblocking)

Primary suspected regressions:
- `3f4d9c3`
- `14bb853`
- `b9ce7d7`
- `cfd2d5e`
- `9668bab`
- `bcbcab9`
- `30f5ad2`
- `ef2a7b5`
- `550e9e7`
- `b0ad2f6` (direction mismatch hard block)

Likely mitigations / rollback indicators:
- `81dfb31`
- `825db1a`
- `ee93068`
- `973eace`
- `bed44c5`

## Practical Readout

If you are investigating “why entries dropped,” start replay analysis around:
- **Regression window:** 2026-03-27 → 2026-03-31.
- **Most likely causal chain:**
  1) final acceptance veto + timing gate,
  2) no-structure/stale-trigger rejects,
  3) continuation integrity hard blocks,
  4) global session hard block,
  5) direction consistency hard block.

This sequence is the strongest history-based explanation for trade suppression increase.
