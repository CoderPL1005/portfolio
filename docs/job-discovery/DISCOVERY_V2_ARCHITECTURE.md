# Discovery V2 Architecture

Trạng thái: đề xuất sau Spike V1; chưa triển khai.
Nguyên tắc: cloud-only, bounded, `$0` có guardrail, source-aware và fail closed.

## 1. Mục tiêu V2 thực tế

V2 không tuyên bố “crawl toàn Internet”. Nó tách hai hệ thống:

```text
Source Discovery
  -> Candidate Source Registry
  -> Policy/Endpoint Verification
  -> Verified Source Registry
  -> Job Discovery Adapters
  -> JD Normalization
  -> Existing Dedup/Ingestion Pipeline
  -> Review/Web Push
```

Mục tiêu PoC: tìm và ingest được ít nhất một JD thật từ nguồn được phép, giữ URL/provenance, không tự ứng tuyển. Với dữ liệu spike, acceptance phải phân biệt “adapter hoạt động” và “có target match Hà Nội”; nếu buộc target match thì Phase 2 chưa có source eligible và phải dừng.

## 2. Thành phần

### 2.1 Scheduler

GitHub Actions `workflow_dispatch` trước; chỉ thêm `schedule` sau khi manual run đạt tiêu chí. Workflow:

- standard hosted runner;
- `timeout-minutes: 10`;
- `concurrency` một run duy nhất;
- request/page/job budget hard-coded;
- không có DB connection string; gọi internal ingest endpoint scoped riêng;
- không tự retry vô hạn;
- summary ghi request count, source status, records và target matches.

Scheduled Actions chạy cloud độc lập laptop/iPhone nhưng best-effort, không SLA. Repo/plan phải được kiểm tra để khẳng định runner cost bằng $0.

### 2.2 Source Discovery Worker

Không tải JD ngay. Nó tạo `CandidateSource` logic với:

- normalized domain/endpoint/provider/board ID;
- discovery evidence URL;
- discovered/last-verified timestamps;
- access method (`API`, `RSS`, `JSON_LD`, `ATS_BOARD`);
- authorization evidence và policy URL;
- status: `CANDIDATE`, `VERIFIED`, `AUTH_REQUIRED`, `BLOCKED`, `STALE`;
- request rate/cap và schema fingerprint.

V1 adapters được phép tự đóng góp first-hop source candidate, ví dụ company URL xuất hiện trong một authorized aggregator. Không tự động promote candidate thành verified source.

Do không có general search API `$0` đã xác minh, V2 chỉ bật source discovery qua:

1. catalogue/source links trong public API đã được phép;
2. feed/ATS references xuất hiện trong verified payload;
3. link graph một hop khi terms/robots/policy được verifier chấp nhận;
4. manual approval cho candidate chưa rõ.

### 2.3 Source Verifier

Verifier chạy trước adapter và fail closed:

1. xác nhận endpoint tồn tại, TLS và response type;
2. kiểm tra official API/feed documentation;
3. ghi policy/terms snapshot metadata, không sao chép toàn văn;
4. xác nhận authentication/scopes;
5. test một request read-only;
6. xác nhận trường provenance/date/title/company/location;
7. đặt expiry cho verification;
8. không vượt qua CAPTCHA/login/anti-bot.

`robots.txt` chỉ là một input, không phải authorization.

### 2.4 Job Discovery Adapters

Interface logic tối thiểu:

```text
DiscoverAsync(VerifiedSource, Cursor, Budget)
  -> JobEnvelope[] + NextCursor + RequestCount
```

`JobEnvelope` gồm source, external ID, canonical URL, raw structured payload/text, title, company, location, published date, captured-at, content hash và evidence. Adapter không tự scoring/ứng tuyển.

Adapter ưu tiên:

1. Jobicy/Remotive/Arbeitnow API cho PoC mechanics;
2. RSS/Atom đã xác minh;
3. ATS public board có board identifier được discovery + verifier xác nhận;
4. JSON-LD/career page chỉ khi policy cho phép.

Facebook, LinkedIn và ITviec mặc định disabled.

### 2.5 Normalization và target filter

Pipeline deterministic, không AI:

- canonicalize URL/text/company/title/location;
- parse ISO/provider date, không bịa date khi thiếu;
- map technology `.NET`, `dotnet`, `ASP.NET Core`;
- map entry level từ explicit title/structured seniority;
- location aliases `Hà Nội`, `Ha Noi`, `Hanoi`;
- freshness = published date trong 7 ngày; unknown date là `UNVERIFIED_FRESHNESS`, không coi là fresh;
- lưu raw evidence bounded để admin kiểm tra.

### 2.6 Dedup và existing Job Hunting pipeline

Tái sử dụng các lớp identity đã có:

1. `(Source, SourceExternalId)`;
2. `IngestionKey`;
3. normalized source URL hash;
4. content hash;
5. company-title fingerprint.

Discovery không gọi screenshot/Gemini analyzer. Với payload có schema rõ, normalizer tạo raw/normalized candidate qua một ingestion boundary mới. Khi thiếu trường, giữ `RECEIVED`/reviewable thay vì bịa dữ liệu.

Fit scoring chỉ chạy theo workflow Job Hunting hiện hữu sau khi JD hợp lệ được tạo. Gmail submission, CV package, application lifecycle và external submission không thay đổi.

### 2.7 Web Push

Chỉ gửi sau commit và chỉ cho job mới vượt target filter. Push là best-effort; failure không rollback ingestion. Cần idempotency key/outbox hoặc event record để tránh push lặp, nhưng PoC có thể chỉ báo cáo workflow summary và hoãn push đến phase hardening.

## 3. Data flow chi tiết

```text
GitHub Actions (manual/bounded)
  -> read VerifiedSource registry through internal API
  -> adapter fetches allowed API/feed
  -> validate schema + normalize + target flag
  -> POST bounded JobEnvelope batch with workload credential
  -> backend owns transaction + existing hashes/unique indexes
  -> create/update RawJobPosting provenance
  -> deterministic normalization/review
  -> optional existing fit pipeline
  -> after-commit Web Push
```

Không cho runner truy cập Neon trực tiếp và không tái sử dụng admin refresh cookie. Internal endpoint cần secret riêng, scope ingest-only, replay protection, request/body cap và rotation.

## 4. Source states và vận hành

| State | Poll được? | Điều kiện chuyển |
|---|---:|---|
| `CANDIDATE` | Không | Mới phát hiện, chưa verify |
| `VERIFIED` | Có | Endpoint + policy + schema + live read pass |
| `AUTH_REQUIRED` | Không | Cần credential/app review chưa có |
| `BLOCKED` | Không | Terms/API/anti-bot không phù hợp |
| `STALE` | Không | Schema/policy/endpoint đổi hoặc quá hạn verification |

Mọi adapter phải có kill switch. Một 401/403/429/schema mismatch không được tự đổi sang HTML scraping.

## 5. Provider-call và cost budget

Mỗi run đề xuất:

- tối đa 3 verified sources;
- tối đa 2 pages/source;
- tối đa 100 records/source;
- tối đa 20 HTTP requests toàn run;
- timeout 10 phút;
- không AI/Gemini, không R2;
- payload/body size cap;
- no paid fallback.

Nếu repo private hoặc GitHub quota không đảm bảo, không bật schedule. Không thêm credit card để “bảo đảm uptime”; khi hết free quota, workflow phải dừng.

## 6. Facebook lane

Facebook là lane riêng, disabled mặc định:

```text
Meta app + approved permissions + managed token
  -> exact Graph API capability live test
  -> source-limited Page adapter
  -> bounded read
```

Không thiết kế public-post/group crawler. Chỉ bật khi có bằng chứng `AUTHORIZED` và `VERIFIED_LIVE`. Token phải nằm trong cloud secret manager/GitHub environment secret, rotation và least privilege; không nhận cookie cá nhân.

## 7. Phase 2 đề xuất

### Phase 2.0 — policy and acceptance gate

- Chốt PoC là mechanics-only hay bắt buộc target match.
- Xác minh GitHub repo public/private, included minutes và hard budget.
- Chọn một API có quyền rõ; Remotive phù hợp để chứng minh adapter nhưng coverage target bằng 0 trong spike.
- Không bật schedule.

### Phase 2.1 — isolated discovery CLI

- Tạo project/tool riêng trong solution hoặc command mode hiện hữu theo architecture review.
- Adapter Remotive/Jobicy với `HttpClient`, response cap, attribution và fixtures.
- Normalizer/filter deterministic và report JSON/Markdown.
- Chỉ `workflow_dispatch`, không production write.

### Phase 2.2 — candidate source registry

- Thiết kế source states/evidence/expiry.
- First-hop candidate extraction từ authorized payload.
- Human approval cho candidate; không auto-promote.
- Quyết định migration sau review; spike này không tạo migration.

### Phase 2.3 — secure ingestion boundary

- Workload credential riêng, ingest-only.
- Backend transaction/dedup bằng các unique index hiện có.
- Không expose admin cookie, Neon connection hay storage key.
- Audit metrics: request/source/raw/new/duplicate/target/rejected.

### Phase 2.4 — production-readiness gate

- Manual cloud run tìm được JD thật có URL/provenance.
- Nếu acceptance bắt buộc Hà Nội/.NET/entry-level, phải tìm được source được phép khác trước khi GO.
- Verify ToS/rate limit again; validate zero-cost guardrails.
- Chỉ sau đó mới đề xuất scheduled workflow và Web Push.

## 8. Deterministic tests cần có

- source state không poll khi chưa `VERIFIED`;
- pagination/request/runtime caps;
- schema drift fail closed;
- canonical URL/external ID/content hash dedup;
- same job qua hai source được flag duplicate/candidate merge theo policy;
- title/location/seniority/freshness filter, kể cả Unicode Hà Nội;
- missing date không bị coi là fresh;
- 401/403/429 không kích hoạt scraping fallback;
- credentials không xuất hiện trong logs/artifacts;
- ingestion retry idempotent;
- Web Push failure không đổi dữ liệu job;
- không có đường tự động ứng tuyển.

## 9. Acceptance và quyết định cuối

| Criterion | Trạng thái sau spike |
|---|---|
| Chạy cloud không cần thiết bị cá nhân | Khả thi qua GitHub Actions |
| Không cần payment | Khả thi có điều kiện theo repo/runner quota |
| Tìm JD thật từ ít nhất một nguồn được phép | Đạt về mechanics: 310 records từ public APIs |
| Tìm JD đúng target Hà Nội/.NET/entry-level | Chưa đạt: 0 records |
| Tự khám phá source mới toàn web | Chưa đạt theo strict `$0` |
| URL/provenance | Đạt ở API sample |
| Normalize/dedup | Architecture hiện có tái sử dụng được; chưa triển khai adapter |
| Không tự động ứng tuyển | Bảo toàn |
| Facebook feasibility rõ | NO-GO V1; AUTH_REQUIRED cho capability hẹp |

**Khuyến nghị:** GO cho Phase 2.0–2.1 như một cloud mechanics PoC có giới hạn. NO-GO cho production discovery agent hoặc bất kỳ claim coverage mục tiêu nào cho tới khi một nguồn target-bearing được phép được xác minh live. Không bật workflow có lịch trong giai đoạn này.
