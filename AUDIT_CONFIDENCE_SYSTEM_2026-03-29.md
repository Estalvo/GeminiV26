# Confidence System Audit (Gemini V26)
Date: 2026-03-29
Auditor mode: production-risk review

## Scope Reviewed
- TradeCore confidence snapshot construction
- All instrument executors confidence source, fallback, and risk shaping usage
- ExitManager confidence usage
- Rehydrate confidence initialization paths

## High-Risk Findings
1. **XAU executor violates specified neutral fallback policy**
   - Uses `EntryScoreFallback` (entry score) instead of neutral `50` when both routed and entry logic confidence are unavailable.
2. **Crypto executors use canonical final confidence for trailing, not adjusted risk confidence**
   - BTCUSD/ETHUSD trailing mode is derived from `ctx.FinalConfidence` after order placement.
3. **Executor-side logic recomputation present in index executors**
   - US30/NAS100/GER40 call `_entryLogic.Evaluate()` and overwrite `entry` fields via `ApplyToEntryEvaluation(entry)` during execution flow.
4. **TradeCore snapshot and executor risk confidence can diverge without explicit reconciliation log**
   - TradeCore logs `FC/RC` from `_ctx`; executors recompute local `ctx.FinalConfidence` and `adjustedRiskConfidence`.
5. **Exit behavior still keyed on FinalConfidence thresholds in ExitManagers**
   - TP1 fallback bucket logic uses `ctx.FinalConfidence` across managers.

## Notes
- `PositionContext.FinalConfidence` appears immutable by API design (`private set`, idempotent compute guard).
- Rehydrate paths (generic service and XAU custom) initialize neutral confidence values before calling `ComputeFinalConfidence()`.
