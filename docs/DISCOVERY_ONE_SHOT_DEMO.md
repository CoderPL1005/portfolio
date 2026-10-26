# Discovery one-shot demo — 2026-10-26

This is a deliberately **single-use**, **development-only** trigger. It does not enable a schedule or change the SocialAPIs provider, the production import API, classifier, or Admin review flow.

## Safe installation (zero provider requests)

Install the infrastructure first, **without** `.github/discovery/demo-once-2026-10-26.arm`:

- `.github/workflows/discovery-demo-once.yml`
- `.github/discovery/demo-once-2026-10-26.config.json`
- `tools/browser-job-discovery/src/demo-once-config.mjs`
- `tools/browser-job-discovery/test/demo-once-config.test.mjs`
- `tools/browser-job-discovery/package.json` test-script addition

Run `cd tools/browser-job-discovery && npm test`. Commit and push this infrastructure to `main`. This push cannot activate the new demo because the workflow only watches **addition of the arm marker**.

## One-time activation (may consume one FREE credit)

Before adding the arm marker, the operator must verify the SocialAPIs dashboard shows at least one available **free** credit and no cash/auto-top-up path. The workflow cannot independently prove provider billing settings. Ensure Render is Live and the `/api/v1/discovery-sync/ready` authenticated preflight returns `ready:true`; GitHub secrets `DISCOVERY_IMPORT_KEY`, `DISCOVERY_CHECKPOINT_KEY`, and `SOCIALAPIS_API_KEY` must be configured.

Commit and push `.github/discovery/demo-once-2026-10-26.arm` **once** to `main`. This push causes GitHub Actions to:

1. Check that the marker was **added**, not modified; reject reruns.
2. Validate the pinned query/date/profile and `maxRequests=maxCredits=1` offline.
3. Atomically create the claim tag `discovery-demo-2026-10-26-claimed` **before** dispatch. An existing tag prevents another dispatch.
4. Dispatch the existing `socialapis-job-discovery.yml` with `auto_admin_sync=true`, no public plaintext artifacts, no schedule and no retry.
5. In the existing workflow, validate prior checkpoints and M2M readiness before the provider request, then perform at most one HTTP request, classify and sync observations to Admin for human review.

The one-shot trigger itself never calls SocialAPIs. It only dispatches the existing safeguarded workflow. An ambiguous provider attempt is **not** retried. If claim creation succeeds but dispatch fails, the claim stays in place and the system fails closed. Inspect GitHub Actions logs and durable ledger rather than rerunning.

The date filter `2026-10-26` is sent to the provider; posts without verified publication timestamps remain `UNKNOWN` and must not be represented as verified same-day posts.

## Important limits

- This is **not** a daily schedule or a 4+1+1 deployment.
- One provider HTTP request is permitted; the provider's actual credit deduction and available free balance cannot be guaranteed by GitHub alone.
- The marker must not be pushed until the operator accepts this bounded live attempt.
- Do not delete the claim tag, manually rerun the workflow, or initialize/reset the existing discovery ledger.
