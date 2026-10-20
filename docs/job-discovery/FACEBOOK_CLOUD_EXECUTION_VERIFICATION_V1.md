# Facebook Discovery Cloud Execution Verification V1

Ngày kiểm chứng: **2026-10-21**, Asia/Saigon. Repository: [CoderPL1005/portfolio](https://github.com/CoderPL1005/portfolio).

## 1. Kết luận

**CLOUD_WORKFLOW_NOT_RUN — CLOUD_EXECUTION_UNVERIFIED.** Workflow discovery chưa tồn tại trên remote nên không dispatch, không retry. Local tests/YAML validation không được tính cloud PASS. Hai adapters vẫn chỉ offline/mock; không Facebook live collection.

Đã hoàn thiện cấu hình local. Bước cần người dùng thực hiện tiếp theo là review/commit/push workflow cùng tool đầy đủ; task này không thực hiện các Git writes đó.

## 2. Bằng chứng remote thực tế

Đọc public GitHub REST API bằng GET, không credential:

- [Repository metadata](https://api.github.com/repos/CoderPL1005/portfolio): `private=false`, `visibility=public`, default branch `main`, archived/disabled=false.
- [Remote main ref](https://api.github.com/repos/CoderPL1005/portfolio/git/ref/heads/main): `922e575df327d643ee9b20d62ab13239d402b6bc`, khớp local HEAD tại thời điểm kiểm tra.
- [Workflow list](https://api.github.com/repos/CoderPL1005/portfolio/actions/workflows?per_page=100): total_count=1, chỉ workflow CI `.github/workflows/ci.yml`, ID `347627421`, active. Đây không phải discovery run.
- [Target file tại commit đã kiểm chứng](https://api.github.com/repos/CoderPL1005/portfolio/contents/.github/workflows/browser-discovery-offline.yml?ref=922e575df327d643ee9b20d62ab13239d402b6bc): **HTTP 404**.
- Local `.github/workflows/browser-discovery-offline.yml`, `tools/`, `docs/job-discovery/` đang untracked từ các task trước. Không stage/reset/clean hoặc ghi đè các thay đổi đó.

Đây là kiểm tra remote trực tiếp, không suy luận từ cached origin/main. GitHub CLI không được tìm thấy trong PATH. Quyền dispatch của tài khoản và account billing không được xác minh; workflow chưa tồn tại nên chưa cần authentication/dispatch.

Lần GET đầu trong sandbox bị chặn network; sau đó read-only public API checks được thực hiện thành công bằng quyền network cho phép. Không có POST workflow dispatch.

## 3. Workflow local đã hoàn thiện

File: `.github/workflows/browser-discovery-offline.yml`.

| Thuộc tính | Giá trị |
|---|---|
| Trigger | Chỉ workflow_dispatch |
| Runner | ubuntu-latest, standard GitHub-hosted label |
| Permissions | contents: read |
| Timeout | 10 phút |
| Concurrency | Global group browser-discovery-offline, cancel-in-progress=false: tối đa một running run; GitHub quản lý pending runs |
| Node | 22.12.0, khớp engines >=22.12.0 |
| Dependencies | npm ci --ignore-scripts --no-audit --no-fund |
| Browser | npx playwright install --with-deps chromium |
| Tests | npm test; npm run test:browser |
| Simulation | npm run simulate |
| Report | Validate rồi in output/report.json vào stdout/log; không upload artifact/cache |
| Facebook/provider secrets | Không dùng |

Actions refs đã được GET xác minh tồn tại: checkout@v7 → `3d3c42e5aac5ba805825da76410c181273ba90b1`; setup-node@v7 → `949feb2413d6458794dcd2491c4babbbce0c15c1`. Đây là snapshot tag hiện tại, không bảo đảm tag bất biến hoặc action execute thành công trên cloud.

Bổ sung step kiểm tra JSON hợp lệ, evidenceKind=OFFLINE_FIXTURE_ONLY, allowed=false, liveDiscovery=LIVE_UNSEEDED_DISCOVERY_NOT_RUN, stopCode=null và offline PASS. Quét deny-pattern credentials trước khi in. Report chỉ dữ liệu fixture, không profile/cookies/traces/screenshots. Không đưa env/token vào JSON.

Chỉ thay đổi trong task này:

1. Workflow local: thêm JSON validation/log output.
2. `tools/browser-job-discovery/test/workflow.test.mjs`: tăng kiểm tra concurrency, single job, Node/engines, commands và report step. Nhận diện cấu hình storageState bị cấm nhưng cho phép nhắc tên nó trong deny-pattern; không bỏ assertion bảo mật.
3. Tài liệu này.

Không sửa production services hoặc source adapters/runtime.

## 4. Local verification — không phải cloud evidence

Môi trường: Windows, Node 22.12.0, Playwright 1.64.1, Chrome đã có sẵn. Không cài dependency/browser mới trong task.

| Kiểm tra | Kết quả |
|---|---|
| Unit/config/regression | **95 PASS**, 0 fail/skip; lần xác nhận cuối khoảng 1.19 giây |
| Chromium loopback tests | **8 PASS**, 0 fail/skip; khoảng 15.31 giây |
| Offline simulation | **PASS**, exit 0, stopCode=null |
| Exact workflow JSON validator | **PASS**, chạy Node body trích trực tiếp từ YAML, đọc report vừa sinh |
| JSON report | 5 unique fixture posts, 1 duplicate, 1 synthetic target match, 1 image requiring extraction; 5,423 stdout bytes từ validator |
| Live/provider data calls | **0** |

Một assertion config ban đầu false-positive do deny-pattern mới có chữ storageState; đã sửa matcher để phân biệt cấu hình/export session bị cấm với phép kiểm tra chống leak và chạy lại toàn bộ unit/config suite PASS. Không bỏ/skip test.

Browser session chỉ cho GET đến loopback fixture allowlist, chặn external requests/WebSockets/service workers. V2 A đọc synthetic anchors trên leased Page; V2 B chỉ canned mock transport, không có live fetch. Simulation cũ không đại diện hai adapter live; adapter behavior được kiểm tra riêng trong các suites.

Report nằm trong `tools/browser-job-discovery/output/report.json` (gitignored); không commit report. Giá trị cloud gate bên trong report vẫn UNVERIFIED — không sửa thành PASS từ local execution.

## 5. Run record

| Trường | Giá trị |
|---|---|
| Commit SHA của workflow được chạy | **N/A — không có cloud run** |
| Baseline local/remote main SHA | 922e575df327d643ee9b20d62ab13239d402b6bc |
| Workflow run ID / URL | **N/A** |
| Cloud conclusion | **NOT_RUN** |
| Cloud duration | **N/A** |
| Runner thực tế đã execute | **N/A** |
| Runner cấu hình | ubuntu-latest, standard |
| Dispatch/retry count | **0 / 0** |
| Provider usage / fee trong task | **0 calls / $0** |

Baseline SHA không được gọi là SHA của discovery workflow: file discovery chưa được commit ở SHA đó.

## 6. Zero-cost safety

[GitHub Actions billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions) và [runner reference](https://docs.github.com/en/actions/reference/runners/github-hosted-runners) xác nhận standard hosted runners trong public repository được miễn phí; larger runners không thuộc ngoại lệ miễn phí này.

Cấu hình dùng đúng standard ubuntu-latest, không runner group/larger runner/self-hosted machine, không provider data API hoặc paid resource. Không upload artifact hoặc tạo cache tường minh. Chưa kiểm tra billing dashboard, payment method, spending limits, organization policy hoặc account entitlement. Không tuyên bố đã audit toàn bộ tài khoản billing; chưa có run phát sinh trong task.

GitHub/npm/Chromium download ở cloud sẽ là dependency setup, không phải Facebook collection. Không cần laptop/iPhone hoạt động sau khi dispatch trên hosted runner, nhưng tuyên bố device independence vẫn **UNVERIFIED** cho tới khi workflow thực sự PASS ở cloud.

## 7. Acceptance gates

| Gate | Kết quả và phạm vi |
|---|---|
| PUBLIC_REPO_VERIFIED | **YES**, public GitHub API |
| STANDARD_RUNNER_VERIFIED | **YES ở cấu hình**; chưa có runner execution |
| CLOUD_WORKFLOW_PASS/FAIL/NOT_RUN | **CLOUD_WORKFLOW_NOT_RUN** |
| CLOUD_EXECUTION_VERIFIED | **NO / UNVERIFIED** |
| CLOUD_DEVICE_INDEPENDENCE_VERIFIED/UNVERIFIED | **CLOUD_DEVICE_INDEPENDENCE_UNVERIFIED** |
| ZERO_PROVIDER_COST_VERIFIED/UNVERIFIED | **ZERO_PROVIDER_COST_VERIFIED cho task local**, 0 calls; cloud usage N/A |
| FACEBOOK_LIVE_DISCOVERY_NOT_RUN | **YES** |
| Private billing settings | **UNVERIFIED** |

## 8. Hướng dẫn bước tiếp theo — người dùng tự thực hiện

Sau review source/license và secret scan riêng, từ repository root kiểm tra scope trước khi tự stage. Không dùng git add . vì còn nhiều tài liệu untracked từ các task trước.

```powershell
git status --short
git diff --check
git add -- .github/workflows/browser-discovery-offline.yml tools/browser-job-discovery
git diff --cached --stat
git diff --cached --check
# Chỉ tiếp tục nếu staged content đúng workflow + toàn bộ isolated tool đã review,
# không có node_modules/output/.browser-cache/credential artifacts.
git commit -m "test: add offline discovery cloud verification"
git push origin main
```

Các lệnh trên là hướng dẫn, **không được agent thực hiện**. Tool fixtures/src/tests/package-lock phải được đưa lên cùng workflow để runner không thiếu file. Tài liệu có thể stage bằng đường dẫn tường minh sau review riêng. Kiểm tra automation hiện hữu trước khi push vì push có thể kích hoạt CI/auto-deploy khác; task này không thực hiện push hoặc deployment.

Sau khi workflow xuất hiện trên default branch và xác minh tài khoản có quyền dispatch, vào GitHub → Actions → Offline browser discovery PoC → Run workflow → main **đúng một lần**. Không tự retry nếu fail; lấy run ID/URL, head_sha, job logs, duration, test counts và JSON report log để điều tra. Không thêm secret/token Facebook/provider để chạy offline suite.

Chỉ sau workflow success mới cập nhật CLOUD_EXECUTION_VERIFIED/device independence. Cloud PASS vẫn không chứng minh Facebook search authorization, provider schema, target coverage hoặc FACEBOOK_POST_DISCOVERY_VERIFIED.

## 9. Scope cuối

Không commit/push/schedule, production DB, Gmail/Gemini, R2, Facebook/SocialAPIsHub data endpoint, tài nguyên trả phí hoặc production service change. Blocker duy nhất cho cloud execution trong task: workflow/tool chưa có trên remote và user không cho agent commit/push. Verdict: **LOCAL WORKFLOW READY — CLOUD NOT RUN**.
