# GEMINI V26 – Full Dead Code Audit (strict, no code changes)
Date: 2026-04-01 (UTC)
Mode: Read-only audit

## 1) OVERALL CLEANLINESS SCORE
**6.5 / 10**

Rationale (high level):
- Positive: no `gate == null || ...` legacy null-gate pattern found in scoped code.
- Negative: multiple high-confidence dead/legacy artifacts still present (unused factory, unused helpers, unreachable branches, duplicated TP1/exit blocks across instrument managers, legacy compatibility bridge/fallback confidence path).

## 2) DEAD CODE SUMMARY

| Típus | Darab |
|---|---:|
| Unused | 4 |
| Shadowed | 0 (high-confidence explicit shadow not found) |
| Unreachable | 2 |
| Legacy fallback | 4 |
| Duplicate logic | 3 |
| Unused helpers | 5 |
| Partial feature | 2 |

## 3) CRITICAL FINDINGS

1. **File:** `Core/PositionContext.cs`  
   **Type:** LEGACY FALLBACK (veszélyes)  
   **Issue:** `AdjustedRiskConfidence <= 0` esetben fallback a `FinalConfidence`-re SAFE MODE-n kívül; implicit behavior branch.

2. **File:** `Core/Runtime/RehydrateService.cs`  
   **Type:** UNREACHABLE  
   **Issue:** `ctx.FinalDirection == TradeDirection.None` check gyakorlatilag elérhetetlen, mert közvetlenül előtte ugyanabból a source-ból Long/Short-ra állítódik.

3. **File:** `Instruments/*/*ExitManager.cs` (US30/NAS100/EURUSD/... family pattern)  
   **Type:** DUPLICATED LOGIC  
   **Issue:** TP1 execution + TP1 smart-exit + fallback SL source logging több managerben szinte byte-szintű másolat.

4. **File:** `Risk/RiskSizerFactoryShadow.cs`  
   **Type:** UNUSED / LEGACY  
   **Issue:** explicit archive class, semmi runtime szerep.

## 4) FULL LIST

### A) UNUSED (soha nem használt)

- **File:** `Risk/RiskSizerFactoryShadow.cs`  
  **Method/Item:** `RiskSizerFactoryShadow` class  
  **Snippet:** `ARCHIVED ... NOT USED since Phase 3.6` + empty body  
  **Category:** UNUSED  
  **Why dead:** nincs hívás, nincs implementáció, explicit archival marker.

- **File:** `Core/TradeCore.cs`  
  **Method/Item:** `_lastTickLogTime` field  
  **Snippet:** `private DateTime _lastTickLogTime = DateTime.MinValue;`  
  **Category:** UNUSED  
  **Why dead:** nincs olvasás/írás a deklaráción kívül.

- **File:** `Instruments/XAUUSD/XauExitManager.cs`  
  **Method/Item:** `Debug(string msg)`  
  **Category:** UNUSED HELPER  
  **Why dead:** nincs hívási hely.

- **File:** `Core/Entry/TransitionDetector.cs`  
  **Method/Item:** `ApplyLegacyProjection(...)`  
  **Category:** UNUSED HELPER / PARTIAL FEATURE  
  **Why dead:** nincs hívó, csak legacy-disabled projection scaffold.

### B) SHADOWED

- **High-confidence explicit field-vs-local shadowing nem találtam** a kritikus scope-ban.

### C) UNREACHABLE

- **File:** `Core/Runtime/RehydrateService.cs`  
  **Method:** `TryRebuild(...)`  
  **Snippet logic:** `direction` és `liveDirection` is `position.TradeType`-ból képzett, ezért a mismatch branch tipikusan nem teljesül.  
  **Category:** UNREACHABLE  
  **Why dead:** determinisztikus azonos forrásból képzett érték-összehasonlítás.

- **File:** `Core/Runtime/RehydrateService.cs`  
  **Method:** `TryRebuild(...)`  
  **Snippet logic:** `if (ctx.FinalDirection == TradeDirection.None)` közvetlenül Long/Short assignment után.  
  **Category:** UNREACHABLE  
  **Why dead:** előfeltétel alapján nem jöhet létre `None` (csak Buy/Sell mapping).

### D) LEGACY FALLBACK (veszélyes)

- **File:** `Core/PositionContext.cs`  
  **Method:** `ComputeFinalConfidence()`  
  **Snippet:** `else if (AdjustedRiskConfidence <= 0) AdjustedRiskConfidence = FinalConfidence;`  
  **Category:** LEGACY FALLBACK  
  **Why dead/risky:** implicit confidence helyreállítás, rejtett viselkedést okozhat, audit-higiénia szempontból kerülendő.

- **File:** `Instruments/XAUUSD/XauExitManager.cs`  
  **Method:** TP1 source / SL fallback path  
  **Snippet:** `source=(... ? "initial" : "fallback")` és `fallback path active` logika  
  **Category:** LEGACY FALLBACK  
  **Why risky:** kimeneti logika SL fallback útra támaszkodhat.

- **File:** `Core/TradeRouter.cs`  
  **Method:** `GetTypePriority`  
  **Snippet:** `// FX (fallback)` switch default routing  
  **Category:** LEGACY FALLBACK  
  **Why risky:** instrument-class branch után fallback ág fenntartja régi viselkedés lehetőségét.

- **File:** `Core/PositionContext.cs`  
  **Method/Item:** `[Obsolete] public int Confidence => FinalConfidence;`  
  **Category:** LEGACY COMPAT BRIDGE  
  **Why risky:** régi API felület fenntartása, új SSOT mellett félrevezető lehet.

### E) DUPLICATED LOGIC

- **File group:** `Instruments/*/*ExitManager.cs` (US30/NAS100/EURUSD/GBPUSD/USDJPY/...)
  **Method blocks:** TP1 execute + post-TP1 checks + smart-exit logging  
  **Category:** DUPLICATED LOGIC  
  **Why dead-ish:** ugyanazon üzleti döntési logika sokszorosítva → inkonzisztencia és rejtett eltérés kockázat.

- **File group:** Instrument executors (`Instruments/*/*InstrumentExecutor.cs`)  
  **Method blocks:** direction mismatch logging (`DO NOT TRUST entry.Direction`) + repeated risk/tp formatting logs  
  **Category:** DUPLICATED LOGIC

- **File group:** Exit managers  
  **Method blocks:** duplicate `[TP1][EXECUTED]` plain + structured log ugyanarra az eseményre  
  **Category:** DUPLICATED LOGGING

### F) UNUSED HELPERS

- **File:** `Instruments/BTCUSD/BtcUsdExitManager.cs` – `ApplyTrailing(Position, PositionContext)` (unused).  
- **File:** `Instruments/XAUUSD/XauExitManager.cs` – `ApplyTrailing(Position, PositionContext)` (unused).  
- **File:** `Instruments/XAUUSD/XauExitManager.cs` – `Debug(string)` (unused).  
- **File:** `Core/Entry/TransitionDetector.cs` – `ApplyLegacyProjection(...)` (unused).  
- **File:** `Core/TradeCore.cs` – `ResolveEntrySnapshotRegime(...)` appears orphan helper (no local call sites).

### G) PARTIAL FEATURE

- **File:** `Core/Entry/TransitionDetector.cs`  
  **Item:** legacy projection method kept but disconnected  
  **Category:** PARTIAL FEATURE  
  **Why:** scaffold present, integration removed.

- **File:** `Risk/RiskSizerFactoryShadow.cs`  
  **Item:** archived shadow factory stub  
  **Category:** PARTIAL FEATURE / LEGACY REMNANT.

## 5) SAFE vs RISKY

| Elem | Törlés kockázat |
|---|---|
| Unused field (`_lastTickLogTime`) | SAFE |
| Unused helper (`Debug`, unused `ApplyTrailing`) | SAFE |
| Legacy compat alias (`Confidence`) | ⚠️ VALIDATE |
| Fallback confidence assignment | ⚠️ HIGH RISK |
| Rehydrate direction guard branches | ⚠️ VALIDATE |
| Exit duplicated TP1 branches | ⚠️ HIGH RISK |

## 6) CLEANUP PRIORITY

| Priority | Terület |
|---|---|
| 🔴 HIGH | RiskSizer / fallback confidence + legacy bridges |
| 🔴 HIGH | ExitManager TP1/trailing duplicated branches |
| 🟡 MEDIUM | Rehydrate unreachable guards and fallback traces |
| 🟢 LOW | Logging de-dup + archival stubs |

## 7) PATCH SUGGESTION (NEM IMPLEMENTÁLVA)

1. Remove clearly unused artifacts first (safe set):
   - `RiskSizerFactoryShadow` class
   - `_lastTickLogTime`
   - XAU/BTC unused helper methods
2. Convert unreachable rehydrate checks into assert-style invariants (or remove once proven by tests).
3. Freeze SAFE MODE contract:
   - explicit denylist for confidence-dependent branches
   - eliminate implicit fallback confidence path where possible.
4. Consolidate duplicated ExitManager TP1/trailing blocks behind shared base/service.

---

## SAFE MODE specific check result
- `RehydratedWithoutConfidence` path is present and explicitly logged in rehydrate + exit managers.
- **Risk flag:** legacy confidence fallback still exists in `ComputeFinalConfidence()` (outside rehydrate path), which can mask missing confidence wiring.

## Direction SSOT check result
- Execution managers consistently verify `entryContext.FinalDirection` and log mismatch against `entry.Direction`.
- `entry.Direction` is still present mostly as diagnostics/mismatch input; risk of legacy read-path persistence is medium.

## Gate audit result
- `gate == null || ...` exact legacy pattern: **not found** in audited scope.
