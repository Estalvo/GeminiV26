# ChatGPT Project Instruction — Gemini Multi-Repository Router

Copy the text below into the ChatGPT Project instructions for the Gemini project.

---

The Gemini ChatGPT Project contains multiple independent repositories and instrument-class implementations, including FX, CRYPTO, METAL/XAU and possible HCA/other variants.

MANDATORY FIRST STEP: before any source audit, debugging, modification, optimization, or development task, identify the exact TARGET REPOSITORY or TARGET REPOSITORIES for the task.

Repository-local authority is mandatory.

For each target repository, use ONLY that repository's own:
1. root `AGENTS.md` or equivalent repository instructions;
2. `openspec/config.yaml`, if present;
3. `openspec/BASELINE_CURATION.md`, if present;
4. relevant `openspec/specs/*` capability specifications, if present;
5. current approved `openspec/changes/*` records, if present;
6. current source and repository-local contract/regression tests;
7. repository-local current documentation and evidence according to that repository's own precedence rules.

NEVER apply FX OpenSpec requirements, thresholds, authority semantics, lifecycle rules, TVM/TTM behavior, entry logic, or other implementation doctrine to CRYPTO, METAL/XAU, INDEX, HCA, or another repository merely because those rules exist in GeminiV26_FX.

Likewise, NEVER apply CRYPTO, METAL/XAU, INDEX, HCA, or another repository's behavior to FX unless the TARGET repository's own approved OpenSpec change explicitly adopts it.

Each repository is a separate authority domain.

Examples:
- If the task targets `Estalvo/GeminiV26_FX`, read and obey the FX repository's own AGENTS.md and FX OpenSpec.
- If the task targets `Estalvo/GeminiV26_CRYPTO`, read and obey only CRYPTO-local instructions/OpenSpec/contracts.
- If the task targets `Estalvo/GeminiV26_METAL`, read and obey the METAL repository's own AGENTS.md/OpenSpec/contracts.
- If the task targets an HCA repository, treat that HCA repository as its own authority domain unless its own documentation explicitly delegates authority to another repository.

If a target repository does NOT yet contain its own OpenSpec baseline:
- DO NOT borrow or reuse a sibling repository's OpenSpec as authority.
- DO NOT perform a behavioral trading-logic patch under another repository's rules.
- Before behavioral implementation, bootstrap a repository-local OpenSpec from that target repository's own current source, tests, current documentation, and approved doctrine.
- Clearly distinguish repository-local facts from reference evidence imported from another repository.
- Cross-repository code may be inspected as evidence only unless the target repository explicitly authorizes adoption.

For a task that intentionally spans multiple repositories:
- load each repository's local instructions/OpenSpec independently;
- maintain a separate intended/implemented/observed model for each repository;
- identify any cross-repository differences explicitly;
- never normalize differences away automatically;
- create separate OpenSpec change records in each repository whose behavior will change, unless a repository-local governance file explicitly defines a shared change authority.

For every target repository, keep these layers separate:
1. INTENDED behavior = that repository's current OpenSpec baseline + approved local OpenSpec changes;
2. IMPLEMENTED behavior = that repository's current source + local contract/regression tests;
3. OBSERVED behavior = runtime logs, bars, CSVs, broker/trade evidence supplied for that repository/runtime.

Never modify trading behavior from runtime-log conclusions alone. Runtime evidence identifies symptoms; repository-local source-path writer→SSOT→consumer audit establishes causality; the TARGET repository's OpenSpec defines intended behavior. Reconcile all three before a behavioral patch.

Any behavioral code change MUST have a TARGET-REPOSITORY-LOCAL OpenSpec change record before implementation. A bug fix restoring an existing local baseline requirement still requires a local change record and should be classified as IMPLEMENTATION_CONFORMANCE_FIX.

Use the TARGET repository's own OpenSpec workflow. Prefer minimal surgical patches. Do not silently broaden scope across repositories, asset classes, or trading layers.

Project-wide Gemini invariants may be treated as shared ONLY when the target repository's own current instructions/specifications explicitly confirm them. Repository-local instructions always determine how a shared invariant is implemented for that codebase.

Never infer chronology from numeric GHHMM labels. Use explicit date, Git ancestry/timestamp, repository-local baseline references, and current source.

Codex/repository-only agents do not see external runtime logs. Runtime conclusions needed by such agents MUST be committed as evidence into the TARGET repository, normally under its relevant OpenSpec change.

If a target repository's intended OpenSpec behavior and current source disagree, report the discrepancy and create/continue a local explicit change/audit. Never silently choose a sibling repository's behavior as the answer.

Before starting implementation, explicitly state which repository is the TARGET and which local instruction/OpenSpec files govern the work.

---
