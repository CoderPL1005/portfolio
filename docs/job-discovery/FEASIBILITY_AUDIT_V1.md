# Job Discovery Cloud PoC V1 — Feasibility Audit

Ngày audit: 2026-10-20
Phạm vi: đọc mã nguồn và tài liệu công khai; không chạy crawler, không gọi nguồn tuyển dụng, không tạo tài nguyên cloud.

## 1. Quy ước bằng chứng

- **VERIFIED (repository):** được xác nhận trực tiếp từ mã nguồn hiện tại.
- **VERIFIED (official):** được xác nhận bằng tài liệu chính thức của nhà cung cấp tại thời điểm audit.
- **UNVERIFIED:** có dấu hiệu kỹ thuật nhưng chưa có bằng chứng đủ về quyền truy cập, điều khoản, dữ liệu thật hoặc vận hành ổn định.
- **BLOCKED:** không có đường truy cập được phép/phù hợp với ràng buộc của PoC.
- Các trạng thái nguồn (`SUPPORTED`, `BLOCKED`, `AUTH_REQUIRED`, `UNVERIFIED`, `UNSUPPORTED`) được định nghĩa chi tiết trong [SOURCE_MATRIX_V1.md](SOURCE_MATRIX_V1.md).

Audit này không phải tư vấn pháp lý. `robots.txt` cho phép crawl không tự động tạo quyền tái sử dụng dữ liệu; ngược lại, robots không thay thế điều khoản dịch vụ và quyền của chủ sở hữu nội dung.

## 2. Kiến trúc hiện tại đã xác minh

### 2.1 Nền tảng

| Thành phần | Kết quả | Bằng chứng |
|---|---|---|
| Backend | **VERIFIED** — ASP.NET Core .NET 8, kiến trúc Domain/Application/Infrastructure/API, custom request-dispatcher | `src/Portfolio.Api/Program.cs`, `Portfolio.sln` |
| Frontend | **VERIFIED** — Angular, admin Job Hunting và PWA/Web Push đã có | `frontend/src/app/features/admin/job-hunting`, `frontend/src/app/core/pwa` |
| Database | **VERIFIED** — EF Core + PostgreSQL; `RawJobPosting` và `JobPosting` là mô hình ingest/chuẩn hóa hiện hữu | `src/Portfolio.Infrastructure/Persistence`, `src/Portfolio.Domain/Entities` |
| CI | **VERIFIED** — chỉ chạy khi `push`/`pull_request` vào `main`; chưa có scheduled discovery workflow | `.github/workflows/ci.yml:3-9` |
| Background worker | **VERIFIED** — chỉ có `KnowledgeIndexingWorker`, phục vụ RAG và mặc định bị tắt; không có job-discovery scheduler/worker | `src/Portfolio.Infrastructure/AI/KnowledgeIndexingWorker.cs`, `src/Portfolio.Infrastructure/DependencyInjection.cs:68` |

### 2.2 Pipeline Job Hunting có thể tái sử dụng

1. **Raw inbox và provenance.** `RawJobPosting` đã có `Source`, `SourceExternalId`, `IngestionKey`, `SourceUrl`, `RawContent`, `Metadata`, `DiscoveredAt` và liên kết tới `JobPosting` (`RawJobPosting.cs:6-28`). Đây là nơi phù hợp để giữ URL/bằng chứng nguồn.
2. **Deduplication ở database.** EF áp dụng unique index cho `(Source, SourceExternalId)`, `IngestionKey`, và `(Source, SourceUrlHash)`; đồng thời có `ContentHash` và `CompanyTitleFingerprint` để phát hiện trùng theo tầng (`RawJobPostingConfiguration.cs:49-64`).
3. **Chuẩn hóa URL/text và hash.** `JobHashing` đã chuẩn hóa text/URL và tạo SHA-256; handler tạo job hiện tại dùng các giá trị đó (`JobPostingFeatures.cs:47-58`).
4. **Mô hình JD chuẩn hóa.** `JobPosting` đã có company, title, location, employment/workplace type, salary, description, technology stack, application email/URL, expiry, verification/selection status (`JobPosting.cs:6-33`).
5. **Fit scoring.** Deterministic fit analysis và candidate preferences đã tồn tại; discovery không cần thay đổi thuật toán hoặc tự động chạy scoring trong PoC.
6. **Web Push.** `IWebPushSender`, subscription persistence và sender đã tồn tại. Hạ tầng có thể tái sử dụng, nhưng hiện chưa có use case “new discovered jobs”; Phase 2 phải thêm orchestration riêng và failure phải best-effort.
7. **Admin UI/API.** Có danh sách/chi tiết CRUD Job Posting, raw screenshot inbox và analyze endpoint. Tất cả controller này yêu cầu admin auth (`AdminRawJobPostingsController.cs:10`, `AdminJobPostingsController.cs:11`).

### 2.3 Những phần chưa tồn tại hoặc chưa thể tái sử dụng trực tiếp

- **Không có source adapter/discovery worker/scheduled workflow.** Không có code polling Facebook, ATS hoặc career pages.
- **Không có machine-to-machine ingest API.** API hiện dùng admin JWT/cookie flow; không tìm thấy credential riêng, HMAC hoặc workload identity cho GitHub Actions. Không nên đưa refresh cookie của admin vào Actions.
- **Raw source enum chưa đủ.** Check constraint hiện chỉ chấp nhận `FACEBOOK`, `INSTAGRAM`, `TOPCV`, `VIETNAMWORKS`, `COMPANY_SITE`, `MANUAL`, `OTHER` (`RawJobPostingConfiguration.cs:14`). `LINKEDIN`, `ITVIEC`, `GREENHOUSE`, `LEVER`, `ASHBY` chưa phải source riêng. PoC có thể map ATS vào `COMPANY_SITE` + metadata mà không migration; nếu cần analytics theo adapter thì Phase 2 phải quyết định migration mới.
- **Analyzer hiện tại chỉ dành cho screenshot.** `AnalyzeRawJobPostingCommandHandler` yêu cầu 1–10 image attachment, đọc private storage rồi gọi Gemini multimodal (`JobAnalysisFeatures.cs:98-126`). Raw text/JSON-LD từ cloud discovery không đi qua handler này được.
- **Không có deterministic text/JSON normalizer.** `CreateJobPostingCommand` nhận sẵn cả raw và các trường chuẩn hóa; nó không tự parse JD. Vì vậy PoC $0 phải có normalizer deterministic cho adapter có schema rõ, hoặc lưu `RECEIVED` để admin duyệt. Không được gọi Gemini ngầm để đạt acceptance criteria $0.
- **Không có generic raw-text ingest endpoint.** Raw API chỉ list, analyze screenshot và upload screenshot (`AdminRawJobPostingsController.cs:15-56`). Endpoint tạo Job Posting hiện là admin CRUD, không phải batch ingestion contract cho worker.
- **Web Push chưa nối với discovery.** Chỉ hạ tầng gửi tồn tại; chưa có transaction/outbox hay command thông báo job mới.

## 3. Đánh giá cloud runtime

### 3.1 GitHub Actions scheduled workflow

**Kết luận: phù hợp nhất cho PoC V1, với điều kiện chấp nhận lịch chạy best-effort.**

- Scheduled workflow chạy trên commit mới nhất của default branch; cron ngắn nhất 5 phút. GitHub cảnh báo scheduled runs có thể trễ hoặc bị bỏ khi tải cao, đặc biệt đầu giờ; public repository không hoạt động 60 ngày có thể bị tự động tắt schedule ([workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax), [disable/enable workflow](https://docs.github.com/en/actions/managing-workflow-runs-and-deployments/managing-workflow-runs/disabling-and-enabling-a-workflow)). Vì vậy đây không phải scheduler có SLA.
- Standard GitHub-hosted runners miễn phí cho public repository. Private repository phụ thuộc quota của plan và có thể phát sinh phí khi vượt quota ([billing and usage](https://docs.github.com/en/actions/concepts/billing-and-usage)). Trạng thái repository/plan và budget thực tế phải được kiểm tra trước Phase 2; không coi `$0` là vĩnh viễn nếu repo chuyển private.
- GitHub-hosted job có timeout tối đa 6 giờ; PoC nên đặt timeout 10–15 phút và concurrency group để không chạy chồng ([GitHub Actions limits](https://docs.github.com/en/enterprise-cloud@latest/actions/reference/limits)).
- Secrets được mã hóa và chỉ đưa vào step được chỉ định, nhưng redaction không phải ranh giới tuyệt đối. Không log headers/body chứa secret; dùng environment secret, least privilege và rotate ([using secrets](https://docs.github.com/en/actions/security-guides/using-secrets-in-github-actions)).
- Runner chạy hoàn toàn trên cloud, không cần laptop/iPhone.

### 3.2 Render và Neon

- Render free web service sleep sau 15 phút không có inbound traffic và có thể mất khoảng một phút để khởi động lại. Worker phải có timeout/retry hữu hạn khi gọi API, không coi lần cold-start là thất bại dữ liệu ([Render free instances](https://render.com/docs/free)).
- Neon có thể scale-to-zero; hạn mức free thay đổi theo thời gian. Tài liệu chính thức gần thời điểm audit nêu 100 CU-hours/project/tháng và 1 GB/project, nhưng đây không phải cam kết bất biến ([Neon free plan update](https://neon.com/blog/neon-free-plan-1-gb-per-project)). Cần monitor storage/compute; không nhúng production connection string vào workflow nếu có thể tránh.
- Kiến trúc ưu tiên: Actions gọi một **internal ingestion API hẹp** trên Render bằng secret dành riêng cho discovery. API sở hữu transaction/dedup/persistence. Không cho runner quyền DB trực tiếp.

### 3.3 So sánh lựa chọn $0

| Runtime | Trạng thái | Nhận xét |
|---|---|---|
| GitHub Actions schedule | **VERIFIED / đề xuất** | Cloud-only, phù hợp batch, miễn phí trên public standard runners; không SLA, có thể trễ/drop |
| Render background worker/cron | **UNVERIFIED cho $0** | Free web service không phải durable scheduler; cron/background plan và giá phải kiểm tra tại thời điểm triển khai |
| Neon scheduled function | **UNVERIFIED** | Sản phẩm/functions và quota đang thay đổi; repo hiện không dùng, tăng bề mặt vận hành |
| Cloudflare Workers Cron | **UNVERIFIED** | Có free tier nhưng cần runtime/adapters mới và secrets mới; không tận dụng .NET pipeline trực tiếp |
| Laptop/iPhone automation | **UNSUPPORTED** | Vi phạm mục tiêu độc lập cloud và độ tin cậy |

## 4. Kết luận Facebook

### Được phép về mặt thiết kế

- **Graph API cho Page mà app được cấp quyền phù hợp:** `AUTH_REQUIRED`. Phải có Meta app/token, quyền cần thiết, app review/business verification tùy trường hợp và kiểm chứng bằng request thật. Chưa có bằng chứng repo hiện có những điều kiện này, nên chưa phải `SUPPORTED`.
- Nếu có quyền hợp lệ, adapter có thể lấy post ID, message, permalink và created time; dữ liệu vẫn phải đi qua normalizer/dedup và lưu provenance.

### Không chấp nhận cho PoC

- Browser scraping public posts/Pages/Groups, dùng session cookie cá nhân, CAPTCHA bypass, stealth fingerprinting hoặc proxy rotation: **BLOCKED**. Meta mô tả automation thu thập không được phép là unauthorized scraping và chủ động ngăn chặn ([Facebook Help Center](https://www.facebook.com/help/463983701520800), [Meta on scraping](https://about.fb.com/news/2021/04/facts-on-news-reports-about-facebook-data/)).
- General public-post search và Group discovery không có official path đã được xác minh cho use case này: **BLOCKED** cho PoC V1.
- Tìm URL Facebook qua search index chỉ là `UNVERIFIED`: snippet có thể thiếu/stale; Google Custom Search JSON API đã đóng với khách hàng mới và dừng cho khách hàng cũ ngày 2027-01-01, còn Bing Search APIs đã ngừng ngày 2025-08-11 ([Google](https://developers.google.com/custom-search/v1/overview), [Microsoft](https://learn.microsoft.com/en-us/lifecycle/announcements/bing-search-api-retirement)).

**Mức triển khai Facebook hiện tại:** chuẩn bị contract/adaptor disabled-by-default và quy trình xin quyền; không đưa Facebook vào tiêu chí “nguồn thật đầu tiên” của PoC. Chỉ bật sau khi Meta cấp quyền và một live-read test hợp lệ chứng minh scope, fields và quota.

## 5. Các giả định và việc chưa xác minh

1. Repository GitHub hiện có public hay private và budget Actions cụ thể — cần kiểm tra trên GitHub Billing trước Phase 2.
2. Render/Neon plan thực tế của tài khoản, quota còn lại và khả năng outbound/inbound trong production.
3. Meta app ownership, Page ownership, access token, App Review/Page Public Content Access.
4. Quyền automation/API của TopCV, ITviec, VietnamWorks; không có public discovery API/permission đủ rõ được xác minh trong audit.
5. Danh sách doanh nghiệp Việt Nam đang dùng Greenhouse/Lever/Ashby và board token/name hợp lệ. API chính thức đã được xác minh, nhưng target allowlist cần kiểm tra riêng.
6. Web Push keys/subscriptions production có hoạt động; audit chỉ xác nhận code tồn tại.
7. Volume thực tế, rate limits per board và retention cần thiết để giữ Neon trong free tier.

## 6. Kết luận khả thi

PoC cloud `$0` khả thi nhất là poll **allowlist Greenhouse + Lever + Ashby public job-board APIs** bằng GitHub Actions 2–4 lần/ngày, chuẩn hóa schema deterministic, gọi internal ingest API idempotent, lưu provenance và chỉ thông báo job mới. Điều này thỏa “ít nhất một nguồn được phép”, có URL/timestamp/JD thật, không cần browser/login/AI và không tự động ứng tuyển.

Facebook là mục tiêu nghiên cứu song song (`AUTH_REQUIRED`), không phải dependency của PoC acceptance.
