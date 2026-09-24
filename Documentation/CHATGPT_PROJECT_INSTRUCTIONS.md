# ChatGPT Project Instruction — Gemini

This Project contains multiple independent Gemini repositories (FX, CRYPTO, METAL/XAU, HCA and others).

For every technical task:
1. Identify the exact TARGET repository first.
2. Read that repository's root AGENTS.md and, if present, openspec/config.yaml, openspec/BASELINE_CURATION.md, relevant openspec/specs/*, current openspec/changes/*, source and local contract tests.
3. Treat each repository as a separate authority domain. Never apply one repository's thresholds, trading logic, TVM/TTM, lifecycle or fixes to another unless the target repository explicitly adopts them.
4. Behavioral changes are OpenSpec-driven and require a TARGET-repository-local OpenSpec change before implementation. If that repository has no OpenSpec yet, bootstrap one from its own current source/tests/docs; never borrow a sibling repo's OpenSpec as authority.
5. Keep INTENDED behavior (local OpenSpec), IMPLEMENTED behavior (local source/tests), and OBSERVED behavior (runtime logs/bars/CSV) separate. Runtime evidence identifies symptoms; source audit establishes causality; OpenSpec defines intended behavior.
6. Codex/repository-only agents do not see external runtime logs. Commit any required runtime conclusions as evidence into the TARGET repository.
7. The running bot may adapt at runtime but must never rewrite/compile/deploy its own source. Code evolution remains human-controlled, versioned, tested and auditable.
8. For cTrader code use VolumeInUnits APIs only; never restore Position.Volume, Symbol.VolumeMin, Symbol.VolumeStep or Symbol.VolumeMax.
9. Never infer chronology from numeric GHHMM labels; use date, Git ancestry/timestamps, baseline references and current source.

Before implementation, explicitly state the TARGET repository and the local governance/OpenSpec files being used.

Full project governance is stored in Estalvo/GeminiV26/Documentation/GEMINI_PROJECT_GOVERNANCE.md.
