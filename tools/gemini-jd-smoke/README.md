# Gemini JD isolated smoke

Local-only .NET 8 harness; calls the **actual** `GeminiDiscoveryTextExtractor` from `Portfolio.Infrastructure`, including its production prompt, configured model and projected schema. It does not start the API, connect a database, dispatch a worker or change execution attempts. Node runs the existing canonical schema/evidence validators offline over stdin. No additional packages.

## Offline (default)

From PowerShell at the repository root, using existing .NET 8 / Node >=22.12 and cached dependencies:

```powershell
Set-Location C:\ProjectCaNhan
dotnet restore tools/gemini-jd-smoke --source "$env:USERPROFILE\.nuget\packages" -p:NuGetAudit=false
dotnet build tools/gemini-jd-smoke -c Release --no-restore
dotnet run --project tools/gemini-jd-smoke -c Release --no-build --no-restore
node --test tools/gemini-jd-smoke/test/validation.test.mjs
dotnet test tests/Portfolio.UnitTests --no-restore --filter FullyQualifiedName~GeminiJdSmokeTests
```

The restore source above is the **existing local cache**, not an external feed. Stop if packages are missing; do not automatically install/pull packages. Unit test project also needs its normal existing restore metadata after the new project reference.

Offline mode needs no key. It uses only an in-memory HTTP handler, never a network transport. Model is the configured `Gemini__JobExtractionModel`, or the explicitly synthetic `synthetic-offline-model` when absent. `Mode=OFFLINE_MOCK`, `ProviderRequests=0`, `MockRequests=1`; status/tokens are synthetic fixture values, not Google evidence.

## Exactly one consented live attempt — operator only

First confirm the existing Google project's effective model, tier, billing and available quota. The harness cannot independently establish free-tier eligibility or remaining balance. Same key/project as existing integration; **do not create another key** and do not put a key in this repository, command arguments, chat or output.

Load `Gemini__ApiKey` and `Gemini__JobExtractionModel` into the local process using your already authorized secret-loading mechanism. No appsettings/Render secret retrieval is performed by this tool. Then:

```powershell
Set-Location C:\ProjectCaNhan
if (-not $env:Gemini__ApiKey -or -not $env:Gemini__JobExtractionModel) {
    throw 'Load the existing approved Gemini process configuration securely first; never paste keys into chat.'
}
dotnet run --project tools/gemini-jd-smoke -c Release --no-build --no-restore -- --live
```

The interactive console warns:

> One Gemini API request may consume quota or incur charges. Confirm project tier before use.

Only after independently verifying tier/billing, type exactly:

```text
CONFIRM ONE REQUEST - PROJECT TIER VERIFIED
```

Redirected/piped stdin is refused. `--live` alone does not authorize a call. At most **one** `generateContent` attempt; no retry, redirect following, fallback model, pagination, background processing or second call after timeout/error. Output ceiling is the production 8192 tokens; timeout ceiling 20 seconds. Local validator prerequisites are checked before sending. A timeout is potentially chargeable; do not automatically run again. No quota/CountTokens endpoint is called.

## Interpretation

Output includes mode, requested/returned model identity when available, attempt count, HTTP status, elapsed request milliseconds, allowlisted finish reason and non-negative token counts if supplied. Independent flags: `TRANSPORT_PASS`, `JSON_PARSE_PASS`, `SCHEMA_PASS`, and evidence outcome. Exit 0 / `END_TO_END_PASS` requires no rejected evidence and at least one grounded vacancy; it still does not prove a complete JD or employer/source verification. `EVIDENCE_PARTIAL` retains supported facts alongside rejected fields. `PARTIAL_EXTRACTION` (exit 2) also covers supported post facts without a grounded vacancy. An all-unknown response is `EVIDENCE_ABSTAINED` / `END_TO_END_INCOMPLETE` (exit 2), not a fabricated success. Validation/transport failure is exit 1. Always inspect `Coverage`, `ValidatedSyntheticFields`, `EvidenceDiagnostics` and `MissingFacts`.

### Evidence policy V2 (smoke opt-in)

Default mock now supplies evidence-backed ACME employer, Hà Nội, the explicit inline Intern Backend Developer role and its own stack/requirements. Salary, experience, application URL and publication date remain unknown. This remains mock data, never a recovered live response.

The harness explicitly opts into `EVIDENCE_POLICY_V2` in the **same** validator. Other worker callers retain strict default behavior, preserving the existing 67 AI hardening tests. V2 may realign incorrect offsets only when the supplied nonempty quote occurs exactly once in the unchanged source. No fuzzy matching, Unicode normalization, searching by claimed value or invented quotes. Corrections are reported as INFO and still undergo exact-value, negation/obligation and vacancy-scope checks. Repeated/overlapping ambiguous quotes and split surrogate pairs are rejected; correct explicit offsets may already disambiguate repeated quotes. A byte-compatible offset diagnostic is not proof of what units the model intended.

Inline role recognition requires an anchored explicit hiring phrase, narrow complete role title and unambiguous suffix. Narrative, negated and unsupported titles cannot create a vacancy. Hiring blocks independently cut neighbouring role scope even if omitted by the model or not themselves accepted. Multiline broad title quotations remain unsupported. Missing model fields/vacancies are reported, not inferred by the validator.

All three C#↔Node pipes explicitly use strict UTF-8. C# and Node canonical span indices remain UTF-16 code units (emoji can occupy two); the trusted source is never converted to byte-indexed text. Diagnostics expose only fixed enums and allowlisted field paths, capped at 16 entries with total counts/truncation; missing facts are a separate bounded summary.

No raw response from the earlier live call is available to this task. Its exact rejection cannot be replayed or inferred from employer/location/empty-vacancy metadata alone. **V2 work authorizes no additional live retry.** The live instructions above are not new permission to execute a call.

For failures: fixed error category, failure stage, response-received flag, status, duration and fixed diagnostic ID. HTTP 400 `INVALID_ARGUMENT` differs from 429 `RESOURCE_EXHAUSTED` and 400 `FAILED_PRECONDITION`. `ProviderErrorHint` distinguishes known schema vs other parameter fields **only if** Google's structured field violations identify them; otherwise `UNSPECIFIED`, not a guessed cause. Neither error.message nor arbitrary field names/descriptions are printed.

No request JSON, prompts, response body, keys, authentication headers, contacts or secret configuration is printed or written. Only validated synthetic values are shown. Response/validation sizes are bounded; validator child environment excludes API keys, database config and Node preload hooks. No persistent cache, files containing provider data, migration, JobPosting or Discovery execution is created. Existing execution `d20d3b13-ae44-492f-b4aa-590af73dd78a`, UNKNOWN attempts, ActiveKey and accounting remain untouched.

**No live request was executed during implementation.** Operator consent is for a separate, manual run and does not establish that a call is free.
