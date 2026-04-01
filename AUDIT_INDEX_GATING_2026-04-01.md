# Audit: INDEX qualification / gating regression (US30, US100, GER40)

Date: 2026-04-01

## Executive conclusion

The INDEX stack is currently **over-filtering** continuation setups.

Primary blockers observed in live runtime logs:

1. `NO_MOMENTUM_INDEX` hard block in `ApplyContinuationTransitionNoMomentumFilter`.
2. `INVALID_FLAG` hard block when `flagBars < 2` in `ApplyContinuationWeakStructureFilter`.
3. Additional qualification hard blocks (`TOO_EARLY`, `TRANSITION_COLLAPSE`, `PULLBACK_TOO_SHALLOW`) from `ContinuationPolicy`.

These are additive and run in sequence, creating a strong AND-chain effect.

## Dominant rejection evidence from runtime logs (2026-04-01)

Parsed runtime counts:

- US30:
  - `NO_SELECTED_ENTRY`: 53
  - `NO_MOMENTUM_INDEX`: 42
  - `TRANSITION_NO_MOMENTUM action=block`: 42
  - `INVALID_FLAG`: 11
- US100:
  - `NO_SELECTED_ENTRY`: 49
  - `NO_MOMENTUM_INDEX`: 46
  - `TRANSITION_NO_MOMENTUM action=block`: 46
  - `INVALID_FLAG`: 7
  - qualification blocks (`TOO_EARLY`): 4
- GER40:
  - `NO_SELECTED_ENTRY`: 51
  - `NO_MOMENTUM_INDEX`: 46
  - `TRANSITION_NO_MOMENTUM action=block`: 46
  - `INVALID_FLAG`: 7
  - qualification blocks (`TRANSITION_COLLAPSE`, `PULLBACK_TOO_SHALLOW`)

## Regression timeline (git history)

Relevant commits introducing stricter behavior:

- `550e9e7` — Harden continuation momentum and flag structure filters
  - Introduced INDEX-special hard block path: `[NO_MOMENTUM_INDEX_BLOCK]`.
  - Added hard block on `flagBars < 2` with reason `[INVALID_FLAG]`.
- `c73688d` — Tighten entry qualification trend/momentum and dead-market guards
  - Tightened trend/momentum definitions and dead-market strict blocking.
- `b23ced7` — Centralized `EntryStateEvaluator`
  - Structured shared strict thresholds for transition/trend/momentum.
- `447159c` — Qualification engine integration into TradeCore
  - Added qualification pass after router winner selection.

## Key finding

The strongest single regression trigger is:

- For INDEX continuation momentum types (`Index_Flag`, `Index_Breakout`),
- if `ctx.IsTransition_M5 == true` and `ctx.MarketState?.IsMomentum == false`,
- candidate is hard-invalidated with `[NO_MOMENTUM_INDEX_BLOCK]`.

This happens before qualification phase and before instrument executor.

## Accepted-but-not-executed note

Runtime logs contain winner entries for some INDEX attempts, but no corresponding execution traces in the same attempt windows.

Given code flow, this implies late-stage drop can occur after winner selection (qualification/final acceptance/session/impulse/risk), and the dominant system-wide no-trade condition is still pre-winner over-filtering (`NO_MOMENTUM_INDEX`, `INVALID_FLAG`, strict qualification chain).
