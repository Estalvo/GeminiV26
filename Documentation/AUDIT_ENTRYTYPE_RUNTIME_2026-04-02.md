# EntryType Runtime Audit Notes (2026-04-02)

This file records that a static runtime-reach + gating audit was performed for the active EntryType system after XAU/BTC/ETH rewrites.

Primary audited files:
- `Core/TradeCore.cs`
- `Core/TradeRouter.cs`
- `Core/Entry/EntryRouter.cs`
- `Core/Entry/EntryContextBuilder.cs`
- Active `EntryTypes/*` for FX/INDEX/METAL/CRYPTO

Key finding snapshot:
- Runtime active registration set is reduced to 8 executable families (FX:2, INDEX:2, METAL:2, CRYPTO:2 symbol-specific wrappers).
- `XAU_Impulse` remains globally hard-skipped in `EntryRouter`.
- Legacy `TC_* / BR_* / TR_*` types remain compiled but are not runtime-registered in current `TradeCore` setup.
- Selection is `IsValid && TriggerConfirmed` executable-only in `TradeRouter`, with score rank + deterministic type tie-break.
- Final acceptance still has hard vetoes: direction none, restart hard protection, overextended.
- Confidence pipeline formula remains `0.7*EntryScore + 0.3*LogicConfidence`, but computed in `TradeCore` entry path rather than `PositionContext.ComputeFinalConfidence()` at selection point.

