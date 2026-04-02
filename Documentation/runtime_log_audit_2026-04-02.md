# Runtime log audit scratch (2026-04-02)

Trusted window: 2026-04-01 08:15:00 to 12:35:03 UTC from runtime_20260401.log files. 2026-04-02 logs contain startup/resolver-only lines.

## Active entry counts (from [TR] CAND + [ENTRY DECISION] + exec markers)
|Entry|Eval|Valid|Trigger True|Accept|Exec Request|Exec Success|Open|Close|
|---|---:|---:|---:|---:|---:|---:|---:|---:|
|FX_FlagContinuation|499|0|0|0|0|0|0|0|
|FX_ImpulseContinuation|499|16|1|1|0|0|0|0|
|Index_Pullback|159|7|6|6|0|0|0|0|
|Index_Flag|159|0|0|0|0|0|0|0|
|XAU_Flag|53|0|0|0|0|0|0|0|
|XAU_Pullback|53|0|0|0|0|0|0|0|
|Crypto_Flag|106|0|0|0|0|0|0|0|
|Crypto_Pullback|106|0|0|0|0|0|0|0|

## Example lines per active entry
- FX_FlagContinuation: {'cand': 'Logs/Logs/Runtime/EURJPY/runtime_20260401.log:154'}
- FX_ImpulseContinuation: {'cand': 'Logs/Logs/Runtime/EURJPY/runtime_20260401.log:160', 'accept': 'Logs/Logs/Runtime/EURJPY/runtime_20260401.log:470'}
- Index_Pullback: {'cand': 'Logs/Logs/Runtime/GERMANY 40/runtime_20260401.log:88', 'accept': 'Logs/Logs/Runtime/GERMANY 40/runtime_20260401.log:5713'}
- Index_Flag: {'cand': 'Logs/Logs/Runtime/GERMANY 40/runtime_20260401.log:92'}
- XAU_Flag: {'cand': 'Logs/Logs/Runtime/XAUUSD/runtime_20260401.log:79'}
- XAU_Pullback: {'cand': 'Logs/Logs/Runtime/XAUUSD/runtime_20260401.log:81'}
- Crypto_Flag: {'cand': 'Logs/Logs/Runtime/ETHUSD/runtime_20260401.log:84'}
- Crypto_Pullback: {'cand': 'Logs/Logs/Runtime/ETHUSD/runtime_20260401.log:82'}

## Unexpected executed non-active entry types
- FX_MicroContinuation @ Logs/Logs/Runtime/EURJPY/runtime_20260401.log:658
- FX_MicroStructure @ Logs/Logs/Runtime/GBPJPY/runtime_20260401.log:1138