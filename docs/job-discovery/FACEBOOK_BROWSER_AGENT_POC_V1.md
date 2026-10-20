# Facebook Browser Agent PoC V1

Ngày kiểm chứng: 2026-10-20. Phạm vi: isolated core, offline browser simulator và cloud feasibility.

## 1. Verdict chính xác

| Gate | Verdict | Bằng chứng |
|---|---|---|
| Offline pipeline | **OFFLINE_BROWSER_AGENT_PASS** | Unit/config tests và Chromium thật trên HTML local đều pass. |
| Technical browser | **BROWSER_TECHNICALLY_READY** | Chrome/Chromium headless chạy navigation, form fill, DOM read và URL extraction trong fixture. |
| Cloud runtime | **CLOUD_BROWSER_RUNTIME_UNVERIFIED** | Workflow manual đã tạo và test config; chưa chạy GitHub-hosted runner. |
| Persistent login | **PERSISTENT_SESSION_UNVERIFIED** | Context mới bị xóa khi đóng; không có Facebook authenticated context. |
| Facebook authorization | **FACEBOOK_ACCESS_UNVERIFIED** | Không có bằng chứng cho automated collection; live gate luôn đóng. |
| Live discovery | **LIVE_UNSEEDED_DISCOVERY_NOT_RUN** | Không truy cập/tìm bài Facebook thật bằng browser agent. |
| Guaranteed $0 cloud | **ZERO_COST_UNVERIFIED** | Không phát sinh provider fee trong local test; chưa xác minh account billing/cloud runtime. |

Không kết luận PoC end-to-end production PASS. Gate offline đã qua, các gate Facebook/cloud/session còn độc lập.

## 2. Audit repository thực tế

| Thành phần hiện có | Source | Reuse boundary |
|---|---|---|
| RawJobPosting | `src/Portfolio.Domain/Entities/RawJobPosting.cs` | Source/external ID/URL/hash/raw text/metadata/discovered time/status/version; có thể làm ingress model về sau. |
| JobPosting | `src/Portfolio.Domain/Entities/JobPosting.cs` | Company/title/location/description/technology stack; yêu cầu dữ liệu thực, không map unknown thành placeholder. |
| Dedup persistence | `RawJobPostingConfiguration.cs` | Unique `(Source, SourceExternalId)`, IngestionKey, `(Source, SourceUrlHash)`; content hash/fingerprint là indexes, không phải mọi hash đều unique. |
| Hash conventions | `JobPostingFeatures.cs`, `JobHashing` | NFC + newline normalization + SHA-256; URL normalization và company/title fingerprint. |
| Screenshot extraction | `JobAnalysisFeatures.cs` | Cần private image attachments và `IJobAnalysisService`; có storage/Gemini calls nên không nối vào PoC offline. |
| Target matching | `docs/job-discovery/TARGET_MATCHING_V2.md` | Chỉ là specification trước task; giờ implement rule subset cho fixture trong isolated tool, không sửa production scoring. |
| Provenance | RawJobPosting fields + metadata | PoC dùng namespace OFFLINE_FIXTURE và marker USER_PROVIDED/SYNTHETIC; không ghi Facebook production rows. |
| CI | `.github/workflows/ci.yml` | CI backend/frontend hiện tại giữ nguyên. Workflow fixture mới độc lập. |

Các tài liệu DTMED/unseeded/matching trước được đọc và giữ nguyên. Không có production browser discovery service/session vault để tái sử dụng. Frontend hiện không có Playwright dependency; package/lockfile mới nằm riêng trong `tools/browser-job-discovery`.

## 3. Core và interfaces

Port definitions nằm trong `src/contracts.d.ts` của tool:

- `IBrowserSessionProvider`: mở/đóng một page/context; implementation hiện tại chỉ ephemeral loopback.
- `ISearchPlanner`: tạo tối đa 5 queries từ technology/location/seniority, không nhận company/URL seed qua orchestration.
- `IBrowserDiscoveryAdapter`: search form, navigation, candidate URLs và accessible post read.
- `IActivityBudget`: limits, counters, session lease và monotonic deadline.
- `IJobPostExtractor`: text evidence + image-required marker, không provider call.
- `IDiscoveryReporter`: JSON với gates, metrics, safe stopCode và provenance.

Orchestration nhận ports thay vì phụ thuộc Facebook DOM. Không thêm production DI, DbContext, repository, controller hoặc API contract.

```text
Target inputs
  -> SearchPlanner
  -> ActivityBudget + FixtureBrowserSessionProvider
  -> FixtureBrowserDiscoveryAdapter (form input/navigation)
  -> JobPostExtractor
  -> Target Matching V2
  -> identity/content dedup
  -> JSON report (OFFLINE_FIXTURE_ONLY)
```

Facebook live mode bị từ chối trước browser launch. Chưa có implementation nào đọc cookie/session từ máy, repository, prompt hoặc cloud vault.

## 4. Fixture evidence

| Fixture | Nội dung và mục đích |
|---|---|
| search.html | Form search + fixed candidate result set; query được submit thật, không mô phỏng Facebook ranking. |
| entry.html | Synthetic Group JD: Junior .NET/Angular Hanoi, 0–2 years; đây là target match duy nhất. |
| dtmed.html | USER_PROVIDED_FIXTURE từ task: DTMED/full-stack/.NET/Angular/min1, location trống; không chép bài Facebook live. |
| image.html + recruitment.svg | Synthetic Page post có hình tuyển dụng; marker IMAGE_REQUIRES_EXTRACTION, không OCR. |
| unrelated.html | Cooking post, không phải JD. |
| unknown.html | Thiếu location và experience; không tự suy diễn. |
| /posts/duplicate | Cùng identity/content với entry, đếm duplicate. |
| login.html / denied.html | Login/checkpoint/access restriction; phải stop ngay. |

Giới hạn classifier: parser bảo thủ trên fixture requirements, chưa phải general NLP. Nhiều mandatory experience bounds khác nhau được coi conflict thay vì chọn số thấp; optional “nice to have/ưu tiên” không tự trở thành hard minimum. Title Senior/Middle-only bị loại; AngularJS không đồng nghĩa Angular. Numerical lower bound 1+ giữ max=null và phân loại stretch; missing location fail closed. Publication không có thì freshness UNKNOWN.

## 5. Kết quả chạy thực tế

Môi trường: Windows, Node `22.12.0`, Playwright `1.64.1`, Chrome có sẵn qua `BROWSER_POC_EXECUTABLE`. Không tải browser cloud hoặc đăng ký provider.

| Command | Kết quả |
|---|---|
| `npm.cmd test` | **52 passed, 0 failed, 0 skipped** |
| `npm.cmd run test:browser` | **7 passed, 0 failed, 0 skipped** |
| `npm.cmd run simulate` | Exit 0, JSON report được tạo trong output gitignored |

Simulation metrics:

- 5 searches; 6 candidate reads; peak 1 session; cuối run 0 active session.
- 5 unique posts; 1 duplicate; 1 synthetic fullstack target match.
- 1 image-only item yêu cầu extraction; không biến chữ trong SVG thành extracted data.
- DTMED fixture có reason LOCATION_UNVERIFIED, min=1/max=null và không được tính target.
- 0 live JD; 0 real Facebook posts; không claim unseeded discovery ngoài fixture.

Report không upload artifact. `output/report.json` chứa dữ liệu fixture, query, hash và provenance; không chứa session state. Console chỉ in safe metrics/status. Không ghi raw Playwright exception/call log.

## 6. Budget và stops đã test

Counters tăng trước thao tác, kể cả request thất bại. Không có retry loop. Invalid/larger-than-default budget bị reject.

- Search cap 5; candidate cap 20.
- Monotonic 10-minute activity deadline; timer đóng browser khi operation đang chờ. Graceful shutdown có thể thêm thời gian ngắn.
- One session lease trong process; workflow concurrency group giới hạn workflow run.
- Detect login form/password, login path/text, CAPTCHA marker, checkpoint/access-denied; stop không chuyển candidate/query tiếp theo.
- Navigation failure có safe code NAVIGATION_FAILED.
- Browser requests chỉ đến exact loopback origin và fixture routes; external fetch/navigation bị abort; service workers/WebSocket bị block.
- No likes/comments/follows/messages/apply; fixture server chỉ GET, không application POST.

Đây là simulator restriction detection; chưa được kiểm chứng với Facebook DOM/ngôn ngữ/markup thực tế. Không dùng word matching này như bảo đảm nhận diện mọi Facebook checkpoint.

## 7. Cloud verification chưa chạy

Workflow local mới chưa commit/push, không có trên remote để dispatch. User cấm commit/push nên không thể chạy chính workflow hiện tại trên GitHub mà vẫn giữ constraint. Không dùng workflow cũ hoặc code remote khác để báo PASS cho bản hiện tại.

Workflow manual-only, permission contents:read, timeout 10 phút, checkout không persist credentials, một concurrency group, không artifacts/secrets/session cache. Workflow tests parse YAML thật để xác nhận các thuộc tính này.

Playwright chính thức hỗ trợ GitHub Actions với browser dependency installation ([CI guide](https://playwright.dev/docs/ci)). Đây là technical support evidence, không phải một Actions run đã execute.

## 8. Blockers

1. Chưa có authorization automated collection từ Facebook; user login không mở được gate này.
2. Chưa có secure persistent session provider/human login flow cloud đã kiểm chứng.
3. Chưa verify GitHub runtime/billing và browser startup trên runner thực.
4. Chưa có Facebook DOM adapter; selector fixture không phải selector live.
5. Image-only JD chưa có OCR/extraction offline; cần phase riêng nếu được cho phép.

Không sửa Gmail, CV package, application lifecycle, production code/database. Không commit/push hoặc tạo paid resource.
