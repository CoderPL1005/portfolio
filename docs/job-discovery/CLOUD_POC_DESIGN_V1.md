# Job Discovery Cloud PoC V1 — Cloud Design

## 1. Mục tiêu và ngoài phạm vi

### Mục tiêu

- Chạy hoàn toàn trên cloud, không phụ thuộc laptop/iPhone.
- Poll ít nhất một public job-board API được phép và lấy JD thật kèm URL/provenance.
- Chuẩn hóa deterministic, chống trùng, ghi vào Job Hunting hiện hữu và gửi Web Push best-effort.
- Không có phí bắt buộc; không tự động ứng tuyển.

### Ngoài phạm vi

- Facebook/LinkedIn UI scraping, CAPTCHA bypass, browser stealth, proxy rotation, cookie cá nhân.
- Gmail submission, CV package/finalization, fit-scoring algorithm, application lifecycle.
- Gemini/OCR/RAG trong discovery path.
- Universal career-site crawler hoặc search-engine scraping.

## 2. Kiến trúc đề xuất

```text
GitHub Actions schedule + workflow_dispatch
  -> Discovery Worker (ephemeral, bounded)
      -> Allowlisted Source Adapters
           Greenhouse | Lever | Ashby
           Facebook Graph Page (disabled until authorized)
      -> Adapter DTO validation
      -> Deterministic JD normalization
      -> Batch ingest request (signed machine credential)
          -> Portfolio.Api internal discovery endpoint
              -> server-side validation + source policy
              -> canonical URL/text hashing
              -> database uniqueness/dedup transaction
              -> RawJobPosting + JobPosting/PENDING_ANALYSIS
              -> record notification candidates
          <- per-item Created | Duplicate | Rejected
      -> best-effort Web Push through backend
      -> redacted run summary
```

Luồng này giữ quyền ghi database trong backend. GitHub runner không nhận Neon connection string và không tái tạo EF/business rules.

## 3. Các boundary đề xuất cho Phase 2

### 3.1 Discovery Worker

Một executable .NET nhỏ dùng Application contracts chung, không host HTTP và không phụ thuộc frontend. Mỗi run:

1. đọc allowlist adapter/board từ cấu hình version-controlled không chứa secret;
2. fetch song song có giới hạn (ví dụ 2–4 requests), timeout 15–30 giây/source;
3. validate kích thước/content type/schema;
4. normalize deterministic;
5. gửi batch nhỏ tới internal ingest API;
6. xuất metrics đếm, không log full JD/token;
7. exit code khác 0 chỉ cho lỗi run-level; lỗi một source không làm mất kết quả source khác.

Không dùng headless browser ở V1. Không thêm Gemini. Một board API trả JSON được map trực tiếp vào `JobPosting` fields; trường không chắc chắn để `null`.

### 3.2 Source Adapter

```csharp
interface IJobDiscoverySourceAdapter
{
    string SourceKind { get; }
    Task<IReadOnlyList<DiscoveredJob>> DiscoverAsync(
        DiscoveryCursor? cursor,
        CancellationToken cancellationToken);
}
```

Adapter không ghi DB, không gửi notification và không biết application lifecycle. Mỗi adapter có:

- allowlisted base URL/board identifier;
- explicit timeout, bounded response size, retry `429/5xx` với jitter và giới hạn;
- pagination/cursor nếu API chính thức hỗ trợ;
- provenance + raw hash;
- test fixtures đã loại dữ liệu nhạy cảm;
- feature flag/kill switch.

### 3.3 Internal ingestion API

Không tái sử dụng admin access/refresh token. Thêm endpoint nội bộ hẹp, ví dụ:

```text
POST /api/v1/internal/job-discovery/batches
Authorization: HMAC/service token riêng
Idempotency-Key: <run-or-batch-id>
```

Ràng buộc:

- chỉ nhận source kinds và hosts allowlist;
- giới hạn batch/count/body;
- timestamp/nonce + constant-time signature comparison hoặc credential machine-to-machine tương đương;
- secret ở GitHub Environment và Render, không lưu trong repo;
- response không trả secret/private storage data;
- không gọi analyze/apply/fit/Gmail/R2;
- persistence dùng unique constraints hiện có và xử lý conflict như duplicate, không `check-then-insert` đơn thuần.

Nếu muốn zero-migration V1, map Greenhouse/Lever/Ashby thành `COMPANY_SITE`, đặt adapter identity trong `Metadata`, và dùng `SourceExternalId = <adapter>:<board>:<job-id>`. Nếu sản phẩm cần filter/report theo nguồn, tạo migration riêng ở Phase 2 để mở rộng source check constraint; không sửa migration lịch sử.

### 3.4 Normalization và deduplication

Thứ tự identity:

1. `IngestionKey = SHA256(adapter + board + externalId)` — idempotency mạnh nhất.
2. `(Source, SourceExternalId)` — uniqueness nguồn.
3. `(Source, SourceUrlHash)` — canonical URL duplicate.
4. `ContentHash` — exact normalized body duplicate/cross-source candidate.
5. `CompanyTitleFingerprint` — chỉ gắn cờ review; không tự merge vì có thể là nhiều location/requisition.

Chuẩn hóa:

- HTML → text an toàn; giữ raw payload bounded trong metadata/raw content nếu chính sách nguồn cho phép.
- normalize whitespace/Unicode; absolute HTTPS URL; source timestamp giữ offset/UTC.
- không bịa salary/location/date.
- record mới ở `VerificationStatus=PENDING`, `SelectionStatus=PENDING_ANALYSIS`; PoC không tự chọn/apply.

`AnalyzeRawJobPostingCommandHandler` screenshot không tham gia luồng này. JSON APIs có structured fields nên worker/backend mapper deterministic tạo `JobPosting`; nguồn thiếu dữ liệu bắt buộc thì lưu Raw `RECEIVED` hoặc reject có lý do, không gọi AI.

### 3.5 Web Push

- Sau commit, backend xác định IDs thực sự `Created`, rồi gửi một notification tổng hợp (`N công việc mới`) hoặc thông báo bounded.
- Push failure không rollback job data.
- Không gửi lại notification cho `Duplicate`.
- Deep link vào admin Job Hunting list/detail; không tự apply.
- PoC có thể gửi trực tiếp sau commit. Production hardening nên dùng durable outbox nếu yêu cầu exactly-once notification; không cần cho discovery correctness.

## 4. GitHub Actions design

Workflow mới tách khỏi CI, ví dụ `.github/workflows/job-discovery.yml`:

- `schedule`: 2–4 lần/ngày, tránh phút `00`; thêm `workflow_dispatch` để kiểm thử có kiểm soát.
- `permissions: contents: read` và không cấp write token.
- `timeout-minutes: 15`.
- `concurrency: job-discovery-production`, `cancel-in-progress: false`.
- restore/build/run đúng worker; không build Angular.
- environment `job-discovery-production` chứa `DISCOVERY_API_BASE_URL` và machine secret; không chứa Neon/Gmail/R2/Gemini secrets.
- exponential backoff hữu hạn cho Render cold start; không retry toàn batch với idempotency mới.
- upload artifact chỉ là sanitized summary, retention ngắn.

GitHub xác nhận scheduled workflows chạy trên default branch và interval tối thiểu 5 phút, nhưng có thể trễ/drop; public standard runners miễn phí, còn private repo phụ thuộc quota ([schedule](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax), [billing](https://docs.github.com/en/actions/concepts/billing-and-usage)). Vì vậy PoC dùng polling theo cửa sổ thời gian + idempotency, không dựa vào “chạy đúng phút”.

## 5. Security, policy và cost controls

1. **SSRF:** adapter không nhận arbitrary URL từ workflow input; base host cố định/allowlist, không follow redirect sang private IP/host khác.
2. **Secrets:** separate discovery secret; rotate; environment protection; mask; không echo curl verbose/headers.
3. **Content:** JD là untrusted input; sanitize khi render, length limit, không thực thi HTML/scripts.
4. **Rate limits:** per-adapter cadence, ETag/Last-Modified nếu có, bounded pagination; dừng khi 401/403 thay vì tìm cách né.
5. **Privacy:** chỉ public job data; không crawl profile/candidate/contact list. Email application trong JD chỉ là source data đã công khai, không kích hoạt send.
6. **Cost:** không AI/browser/proxy; cap runs/pages/items; GitHub budget `$0`; monitor Neon compute/storage; no artifact blobs.
7. **Kill switch:** workflow disable + adapter `Enabled=false`; lỗi policy là fail closed.

## 6. Acceptance criteria và test plan

### 6.1 PoC pass

- Một scheduled/manual cloud run trên GitHub-hosted runner hoàn thành khi máy cá nhân tắt.
- Lấy được ít nhất một JD thật từ Greenhouse/Lever/Ashby allowlist bằng API chính thức.
- Record có source adapter, external ID, canonical URL, published/fetched time phân biệt rõ và raw hash.
- Chạy lại cùng payload tạo `Duplicate/AlreadyExists`, không tạo JobPosting thứ hai.
- Record normalized có `PENDING`/`PENDING_ANALYSIS`; không tạo JobApplication/SubmissionAttempt.
- Web Push (nếu có subscription) là best-effort; không ảnh hưởng persistence.
- Không dùng API/service trả phí, AI, Gmail, R2 hoặc Facebook scraping.
- Báo cáo run liệt kê created/duplicate/rejected/error theo source, không có secrets/full sensitive payload.

### 6.2 Tests

- Unit: mapping fixtures Greenhouse/Lever/Ashby; URL/text/date normalization; invalid schema; oversized response; retry boundaries.
- Application: dedup priority, cross-run idempotency, source allowlist, invalid URL, no downstream application/scoring calls.
- Integration PostgreSQL: concurrent identical ingest → đúng một winner; unique indexes; batch partial results.
- API security: missing/bad/replayed credential rejected; admin JWT không tự cấp machine endpoint; body/rate limits.
- Workflow: lint/action dry-run bằng fake adapter/internal test server; verify secrets not printed.
- Web Push: only created IDs notify; sender failure leaves database committed.
- Policy regression: no browser packages/cookies/proxies/CAPTCHA handling; Facebook adapter disabled without required config.

## 7. Phase 2 implementation order

1. Chọn 3–5 company boards thật dùng Greenhouse/Lever/Ashby; ghi lại official endpoint, terms và board identifiers.
2. Chốt source representation: zero-migration `COMPANY_SITE` + metadata hay migration thêm source constants. Ưu tiên zero-migration cho PoC.
3. Thêm neutral `DiscoveredJob` contract và deterministic normalizer; fixtures trước adapter network code.
4. Tách/reuse một ingestion application service dùng uniqueness hiện hữu; thêm internal batch endpoint với machine auth, replay/body limits.
5. Thêm Greenhouse adapter đầu tiên và E2E test; sau đó Lever/Ashby.
6. Thêm worker executable và scheduled workflow với `workflow_dispatch`, concurrency, timeout và sanitized summary.
7. Nối Web Push best-effort sau persistence; xác nhận không có auto-apply/fit rerun.
8. Chạy PoC trên test/staging database trước, sau đó production với page/item caps thấp; theo dõi quota.
9. Facebook track riêng: tạo Meta app, xin đúng quyền, xác minh Page allowlist và live response. Chỉ đổi trạng thái từ `AUTH_REQUIRED` sau bằng chứng; không block PoC ATS.
10. Sau 1–2 tuần, đánh giá duplicate rate, stale jobs, quota, source terms và quyết định mở rộng domain adapters.

## 8. Rủi ro còn lại

- GitHub schedule không SLA; run có thể trễ/drop hoặc bị disable khi repo public không hoạt động 60 ngày.
- Free tiers và policies có thể thay đổi; phải re-audit trước production expansion.
- ATS board IDs phải được curate; các API public không cung cấp global search toàn bộ công ty.
- URL/content có thể đổi nhưng external ID giữ nguyên; Phase 2 cần policy update existing raw source thay vì tạo duplicate, không làm mất audit trail.
- Exactly-once external notification không được bảo đảm nếu chưa có outbox; persistence correctness vẫn phải exactly-once theo unique constraints.
- Facebook coverage có thể thấp hoặc bằng 0 nếu không được Meta duyệt; đây là kết quả feasibility hợp lệ, không được “khắc phục” bằng scraping.

## 9. Quyết định V1

**GO có điều kiện:** triển khai PoC bằng Greenhouse trước, GitHub Actions làm scheduler/worker, backend internal API làm trust/persistence boundary. Facebook giữ `AUTH_REQUIRED`; TopCV/ITviec/VietnamWorks giữ `UNVERIFIED`; LinkedIn/Facebook scraping giữ `BLOCKED`.
