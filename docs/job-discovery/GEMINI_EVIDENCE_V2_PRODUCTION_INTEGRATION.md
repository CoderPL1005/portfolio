# Gemini evidence V2 integration review

Baseline: `bff4527`. Existing PEWF task: `SEMI_AUTOMATED_JOB_DISCOVERY_JD_INGESTION_V1`.
Verification date: 2026-11-01. Verdict: **READY_FOR_HUMAN_REVIEW**.
This is a local implementation review, not production acceptance or deployment authorization.

## Interoperability issue

The Node V2 validator can replace incorrect model offsets with canonical UTF-16
offsets when the original quotation occurs exactly once in the original post.
Backend `GeneratedFact()` previously required the entire submitted evidence array,
including offsets, to equal the stored original generation. A legitimate correction
therefore failed provenance verification. The production worker also omitted the
evidence-policy option, so its validation remained STRICT.

## Changed files

- `tools/browser-job-discovery/src/ai-extraction-evidence.mjs`: clarify production opt-in comment; existing `EVIDENCE_POLICY_V2` rules reused unchanged.
- `tools/browser-job-discovery/src/ai-job-extraction.mjs`: retain the pre-existing explicit validated policy option; add validation version/fingerprint separate from generation cache identity. This file was already modified before this task; its existing formatting and policy implementation were preserved.
- `tools/browser-job-discovery/src/discovery-execution-worker.mjs`: use backend snapshot policy, transmit it with processing, reject saved-policy mismatch, skip UNKNOWN/CLAIMED semantic requests.
- `src/Portfolio.Application/Features/JobHunting/DiscoveryExecutionContracts.cs`: server policy option and processing policy contract.
- `src/Portfolio.Application/Features/JobHunting/DiscoveryExecutionService.cs`: freeze validated server policy in new execution configurations.
- `src/Portfolio.Application/Features/JobHunting/DiscoveryExecutionEvidence.cs`: backend provenance verification of original claims, exact quotations, canonical spans, source boundaries and contextual evidence.
- `src/Portfolio.Application/Features/JobHunting/DiscoveryExecutionIngestion.cs`: policy-aware verification for final ingestion; verify outer field value equals the submitted evidence claim; V2 vacancy fingerprint.
- `src/Portfolio.Application/Features/JobHunting/DiscoveryExecutionProcessing.cs`: enforce snapshot policy and persisted policy consistency; bind processing fingerprints to policy, validator version and generation hash.
- `frontend/src/app/features/admin/job-hunting/discovery-observation-view.ts`: evidence checks for technologies, evidence origins, and prevention of classifier fallback over explicitly rejected persisted fields.
- `frontend/src/app/features/admin/job-hunting/discovery-review-page.component.ts`: requirements and evidence origins on existing unified cards, including linked recovery cards; explicit post-mentioned/unverified wording.
- `frontend/src/app/features/admin/job-hunting/discovery-enrichment.spec.ts`: rejected/unrelated evidence and display regressions.
- `tools/browser-job-discovery/test/fixtures/evidence-production-v2.mjs`: synthetic persisted generations and real replay worker output, with network forbidden.
- `tools/browser-job-discovery/test/evidence-production-v2.test.mjs`: policy, worker and generation/validation cache-identity checks.
- `tests/Portfolio.IntegrationTests/PortfolioContent/DiscoveryEvidenceV2Tests.cs`: real Node output through backend processing, persistence and unified projection; forgery, cache, idempotence and conflict checks.
- This report.

`DiscoveryExecutionSemantic.cs` and `DiscoveryUnifiedRead.cs` were inspected and
reused without modification. No new extraction provider, migration, dependency,
model, Gemini prompt version or schema version was introduced.

## Policy selection

`DiscoveryExecution:EvidencePolicy` defaults to `STRICT`. An operator may configure
`DiscoveryExecution__EvidencePolicy=V2` on the backend for **new executions**.
Only `STRICT` and `V2` are accepted. The server-created execution snapshot is the
authority, not a processing request flag. A missing historical snapshot property
means STRICT. Changing current settings does not change a stored snapshot.

Workers validate the snapshot value and pass it to `extractRecruitmentPost()`.
The backend rejects a submitted policy that differs from the snapshot, or a
processing layer recorded under a different policy. No historical V2 conversion
or migration is performed. This change does not create a historical policy-upgrade
endpoint or mutate historical execution configuration.

## Provenance and correction security

- Only a COMPLETED observation's stored original generation can authorize Gemini facts.
- Submitted claim values must exactly equal original JSON claim values. The outer ingestion value must also equal the evidence envelope's value.
- Evidence count, order and quotation text must match the original model evidence.
- Submitted spans must match the unchanged source exactly, using UTF-16 offsets with valid surrogate boundaries.
- Any altered span must independently resolve to exactly one ordinal source occurrence, counting overlapping occurrences. Repeated ambiguous quotes, invented quotes and arbitrary offsets fail.
- Claims bind to the original model vacancy title and source role boundaries, including omitted roles and inline hiring statements. Role-local evidence cannot be borrowed from another vacancy. Shared facts require post-level attribution.
- Literal support excludes identity inferred from URLs/email. Skill obligations and publication claims receive contextual checks. Unsupported conversions abstain; an enum/number cannot acquire a fabricated string value through correction.
- STRICT retains its original exact-evidence comparison and compatibility behavior. All source/authorization/version checks still apply.

The backend verifier is deliberately conservative: it can abstain when title
evidence uses multiple spans, the deterministic title differs from the model title,
or a nonliteral representation would require conversion. Rejected fields remain
NEEDS_REVIEW/null; this is not a promise of identical recall to the Node validator.
It verifies provenance and does not generate a second AI extraction.

## Persistence and UI

Original Gemini generation stays in `SemanticOutputJson`; original raw text and
provider identity remain unchanged. Validated/corrected spans live in the separate
processing layer. That layer records `evidencePolicy`, `validationVersion` and an
opaque `generationHash`, linking the two representations without exposing raw
model output through the unified read model.

Provider-generation cache identity remains policy-independent; validation identity
includes the policy and version. Reusing a COMPLETED generation consumes no new
attempt even with all three attempts used. UNKNOWN/CLAIMED are not retried by the
worker. Existing backend reservation and budget guards are unchanged.

Repeated identical processing is idempotent. Changed requests require the existing
version guard for Admin replay. Processing creates no duplicate vacancy entities,
JobPostings, provider attempts or completed receipts. Deterministic facts win
conflicts and retain their origin/conflict marker. SocialAPIs accounting is unchanged.

Existing unified cards show persisted supported company, title, location, stack,
requirements, Gemini status, evidence origins and missing fields. Explicitly
rejected fields do not fall back to classifier values. Corrected evidence is
labelled as post-mentioned and not externally verified. Observation detail routes
and human review remain available.

## Offline verification

| Check | Result |
| --- | --- |
| Relevant Node extraction/evidence/worker and original smoke-validator regressions | 162 passed |
| Backend unit suite | 554 passed |
| Relevant backend execution/unified integration suite | 167 passed, 12 PostgreSQL cases skipped |
| Final focused Node → backend persistence → unified projection/forgery/cache suite | 23 passed |
| Angular affected Discovery tests | 58 passed in 7 files |
| Angular full suite, earlier in this task | 319 passed in 37 files |
| Backend Release build | Passed, zero warnings/errors |
| Final Angular production build | Passed; two existing unrelated stylesheet budget warnings |
| `git diff --check` | Passed |
| Existing PEWF procedural gate | Passed; not a correctness or production-acceptance assertion |

The focused suite covers exact offsets, unique incorrect offsets, UTF-8 versus
UTF-16, Vietnamese/emoji, inline titles, distinct vacancies, ambiguous quotes,
fabricated values, cross-vacancy claims, missing facts, deterministic conflicts,
COMPLETED valid/rejected enrichment, UNKNOWN without retry, forged corrected
evidence, STRICT compatibility, default policy, persisted read fields, idempotence,
version conflicts and generation reuse at the exhausted attempt limit.

These backend tests use EF InMemory. They exercise actual processing and read
handlers, rather than asserting only Node output, but **do not validate PostgreSQL
transactions, unique constraints, rollback or cross-process concurrency**.
PostgreSQL opt-in tests were skipped because local test database configuration was
not enabled. No PostgreSQL runtime verification or Neon write was performed.

Local TRX and Angular logs are under `.tmp/evidence-v2/`. PEWF verification logs
and generated framework report remain in the existing private task workspace.
A PowerShell `Tee-Object` build-log wrapper treated warning stderr as an error;
the subsequent direct Angular CLI production build exited 0.

## Historical safety and production prerequisites

No live Gemini, SocialAPIs, dispatch, production API, Neon write, database reset,
commit, push or deployment was performed. Execution
`d20d3b13-ae44-492f-b4aa-590af73dd78a` was not accessed or modified by this task.
Its supplied INGESTING/7 observations/3 UNKNOWN/3 of 3 attempts/1 request/1 credit
state was not rechecked live. No attempts, limits, ActiveKey or receipts were changed.

Before live use, human review must approve this change, isolated PostgreSQL
transaction/concurrency checks must run, and the operator must deliberately enable
V2 for a new execution under the existing consent/provider controls. The existing
historical active execution may still block new executions; this task does not
release it. Any historical recovery needs its own explicit authorized procedure.
Conservative backend abstentions should be reviewed against production requirements.

The earlier HTTP 200 Gemini smoke output is unavailable and failed evidence
validation. This integration makes **no claim that V2 would recover that output**.
