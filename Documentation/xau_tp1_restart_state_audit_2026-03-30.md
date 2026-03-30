# XAUUSD TP1 Restart/State-Loss Audit (2026-03-30)

## 1) Where original SL is set

### Entry-time SL creation
- XAU executor computes SL distance pre-order, sends SL in pips to broker, then reads actual SL from the filled position:
  - `slPriceActual = result.Position.StopLoss ?? (entry ± slPriceDist)`
  - `rDist = abs(entry - slPriceActual)`
  - `tp1Price = entry ± (rDist * tp1R)`

### What is persisted in `PositionContext`
- `PositionContext` has `RiskPriceDistance` and `LastStopLossPrice`, but **no immutable `InitialStopLossPrice` field**.
- In XAU executor finalize block, `ctx.Tp1Price` is stored, but there is no explicit assignment of `ctx.RiskPriceDistance = rDist` and no `ctx.LastStopLossPrice = slPriceActual`.

### Critical status
- **CRITICAL:** there is no immutable original-SL snapshot field (`InitialStopLossPrice`) in `PositionContext`.

## 2) Where TP1 SL comes from

TP1 path in XAU exit manager:
- Gets `rDist = GetRiskDistance(pos, ctx)`.
- `GetRiskDistance` source priority:
  1. `ctx.RiskPriceDistance`
  2. `abs(ctx.EntryPrice - ctx.LastStopLossPrice)`
  3. `abs(pos.EntryPrice - pos.StopLoss)` (live broker position)
- If `ctx.Tp1Price` already exists, that stored price is used directly.
- If missing, TP1 is recomputed as `entry ± rDist * tp1R`.

So TP1 SL source is context/broker stop-based distance, **not ATR recomputation** in exit path.

## 3) Restart path behavior

Startup flow:
- `GeminiV26Bot.OnStart()` calls `_core.RehydrateOpenPositions()`.
- `TradeCore.RehydrateOpenPositions()` runs `RehydrateService.Run()`.
- `RehydrateService.TryRebuild(...)` rebuilds context for open positions.

SL restoration on rehydrate:
- Reads `stopLoss = position.StopLoss` from broker position.
- Computes `riskDistance = abs(entryPrice - stopLoss)`.
- Stores both:
  - `ctx.RiskPriceDistance = riskDistance`
  - `ctx.LastStopLossPrice = stopLoss`

No ATR-based SL recomputation is done in rehydrate.

## 4) Mismatch evidence (A vs B)

### Definitions
- A = original SL at entry execution time.
- B = SL basis used for TP1 distance after restart/missing context.

### Code-level evidence
- A is available transiently as `slPriceActual` in executor at entry.
- A is not persisted immutably as `InitialStopLossPrice` in context.
- After restart, B is rebuilt from **current broker stop** (`position.StopLoss`) via rehydrate.

Therefore, if current broker stop differs from the original entry SL, then `A != B` and TP1 distance can shift.

Important nuance:
- For normal lifecycle, BE/trailing are TP1-gated, so pre-TP1 SL usually remains original.
- But code does not enforce immutable original-SL usage for TP1 reconstruction; it relies on mutable stop snapshots.

## 5) Root cause classification

**E) Combination of above**
- A) Missing initial SL snapshot (immutable field absent).
- B) Context loss/restart rebuild path exists and repopulates from live position state.
- D) TP1 reconstruction can use non-original SL source (`position.StopLoss` fallback chain).

Not confirmed:
- C) No evidence that SL is recomputed from ATR during restart.

## 6) Minimal fix location (no refactor)

1. Add immutable entry SL snapshot field to `PositionContext`:
- File: `Core/PositionContext.cs`
- Add `InitialStopLossPrice` (double?) near other SL/risk fields.

2. Set immutable snapshot at entry fill:
- File: `Instruments/XAUUSD/XauInstrumentExecutor.cs`
- Where `slPriceActual` and `rDist` are computed, persist:
  - `ctx.InitialStopLossPrice = slPriceActual`
  - `ctx.RiskPriceDistance = rDist`

3. TP1 source hardening:
- File: `Instruments/XAUUSD/XauExitManager.cs`
- In `GetRiskDistance`, prioritize `InitialStopLossPrice` before mutable fields.

4. Restart-safe restoration:
- File: `Core/Runtime/RehydrateService.cs`
- If no stored immutable snapshot exists, restore from broker position SL (already done), and mark source in log.

## 7) Required logging to add (currently missing)

At entry fill:
- `[SL_SNAPSHOT] symbol=XAUUSD entry=... initialSL=...`

At rehydrate rebuild:
- `[SL_REBUILD] symbol=XAUUSD source=broker|context|recomputed sl=...`

At TP1 computation:
- `[TP1_SOURCE] symbol=XAUUSD slUsed=... source=initial_snapshot|risk_distance|last_sl|position_sl`

