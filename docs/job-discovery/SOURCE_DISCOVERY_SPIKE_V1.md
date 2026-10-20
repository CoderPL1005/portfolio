# Job Source Discovery Feasibility Spike V1

Ngày thực hiện: 2026-10-20
Phạm vi: thử nghiệm đọc có giới hạn và thiết kế; không triển khai crawler, không thay đổi production, không tạo tài nguyên cloud.

## 1. Kết luận ngắn

**Source Discovery tổng quát, không có danh sách công ty/URL/board ID và đồng thời phải `$0`, không cần phương thức thanh toán: NO-GO ở thời điểm spike.** Rào cản chính không phải scheduler mà là thiếu một web-search API tổng quát vừa được phép, còn nhận khách hàng mới, vừa miễn phí lâu dài. Bing Search APIs đã ngừng hoạt động ngày 2025-08-11; Google Custom Search JSON API không nhận khách hàng mới và dự kiến ngừng ngày 2027-01-01 ([Microsoft lifecycle](https://learn.microsoft.com/en-us/lifecycle/announcements/bing-search-api-retirement), [Google Custom Search overview](https://developers.google.com/custom-search/v1/overview)).

**PoC hẹp dùng các public job API/feed được phép: GO có điều kiện.** Ba API không cần credential đã được gọi thành công từ môi trường spike, hoàn toàn không dùng danh sách công ty có sẵn. Chúng trả về 310 bản ghi việc làm thật trong mẫu có giới hạn, nhưng không có bản ghi nào đồng thời khớp `.NET/ASP.NET`, Hà Nội và Intern/Fresher/Junior. Vì vậy đường này chứng minh cơ chế discovery, không chứng minh coverage cho mục tiêu tuyển dụng hiện tại.

**Facebook discovery tự động: NO-GO cho PoC V1.** Không có credential/app review và không xác minh được API hiện hành cho tìm kiếm public post/group toàn cục. Không sử dụng cookie cá nhân, mật khẩu, CAPTCHA bypass, stealth hay proxy rotation.

## 2. Quy ước bằng chứng

| Nhãn | Ý nghĩa trong tài liệu này |
|---|---|
| `VERIFIED_LIVE` | Đã thực hiện request đọc thật trong spike và quan sát payload/trang thật. |
| `VERIFIED_OFFICIAL` | Được tài liệu chính thức hiện hành xác nhận, nhưng chưa gọi live. |
| `TECHNICALLY_POSSIBLE` | Có khả năng kỹ thuật nhưng chưa đủ quyền/điều kiện để dùng. |
| `AUTHORIZED` | Có bằng chứng rõ rằng cách truy cập dự kiến được nhà cung cấp cho phép. |
| `UNVERIFIED` | Chưa đủ bằng chứng về quyền truy cập, độ ổn định hoặc dữ liệu. |
| `BLOCKED` | Không có đường truy cập phù hợp với các ràng buộc của spike. |
| `BLOCKED_BY_ENVIRONMENT` | Không chạy được vì giới hạn môi trường; **không** được coi là PASS. |

`robots.txt` không thay thế điều khoản dịch vụ hoặc quyền tái sử dụng nội dung. Việc một trang xem được bằng trình duyệt cũng không mặc nhiên cho phép thu thập tự động.

## 3. Source Discovery và Job Discovery

### Source Discovery

Đầu vào không chứa công ty, URL hay board identifier. Đầu ra là các ứng viên nguồn:

- domain/career page;
- ATS provider và board identifier;
- API/feed RSS/Atom;
- URL bằng chứng và trạng thái quyền truy cập;
- thời điểm xác minh, phương thức truy cập và giới hạn request.

Một nguồn chỉ được đưa vào registry hoạt động sau khi vượt qua bước verification. Search snippet chỉ là gợi ý nguồn, không phải JD chuẩn hóa.

### Job Discovery

Chỉ chạy trên nguồn đã xác minh. Adapter đọc JD, giữ URL/provenance, chuẩn hóa trường, lọc mục tiêu và đưa vào deduplication/pipeline hiện có.

Polling vài Greenhouse/Lever/Ashby board đã biết chỉ là **Job Discovery**. Nó không chứng minh khả năng tự tìm nguồn mới.

## 4. Thử nghiệm source discovery không dùng seed company

### 4.1 Search index tương tác

Spike dùng hai batch truy vấn có giới hạn cho `.NET Intern`, `.NET Fresher`, `Junior .NET`, `ASP.NET Core Developer` tại Hà Nội. Trong sample đóng này, 14 domain ứng viên được phát hiện mà không cấp trước danh sách công ty:

`topcv.vn`, `laptrinhchuyennghiep.com`, `findglocal.com`, `123job.vn`, `haui.edu.vn`, `hachinet.com`, `pitagon.vn`, `uneti.edu.vn`, `itnavi.com.vn`, `fis.com.vn`, `falcongames.com`, `vnpt.vn`, `itviec.com`, `pixelz.com`.

`pixelz.com` là source mới ở bước first-hop: từ listing ITviec tìm được Pixelz, sau đó xác minh được [career page chính thức](https://www.pixelz.com/jobs/) và [.NET Developer tại Hà Nội](https://www.pixelz.com/jobs/hanoi/dotnet-developer/). Trang này có JD thật nhưng không hiển thị published date đủ để chứng minh “mới trong 7 ngày”; quyền tự động thu thập cũng chưa được xác minh. Trạng thái: `UNVERIFIED`, không đưa vào polling production.

Search index tương tác hữu ích để tìm candidate domain, nhưng không phải API có thể đưa thẳng vào GitHub Actions. Các kết quả cũng có nhiều PDF cũ, việc hết hạn và trang tổng hợp lệch mục tiêu.

### 4.2 API công khai không cần danh sách công ty

Ba sản phẩm API được tìm thấy rồi gọi live:

| Nguồn | Access | Kết quả live | Quyền/giới hạn | Quyết định |
|---|---|---:|---|---|
| [Jobicy Remote Jobs API](https://github.com/Jobicy/remote-jobs-api) | Không API key | 2 query, 0 job | Tài liệu công khai; dữ liệu remote, last 7 days | `SUPPORTED`, nhưng `NO-GO` cho coverage Hà Nội |
| [Remotive Jobs API](https://github.com/remotive-com/remote-jobs-api) | Không API key | 10 job thật; query biến thể trả cùng tập | Cho phép redistribution khi attribution/link-back; khuyến nghị tối đa 4 lần/ngày | `SUPPORTED`, nhưng `NO-GO` cho target hiện tại |
| [Arbeitnow Job Board API](https://www.arbeitnow.com/api/job-board-api) | Không authentication | 300 job thật ở page 1 | Public API; phạm vi chủ yếu quốc tế/châu Âu | `SUPPORTED`, nhưng `NO-GO` cho target hiện tại |

Không có source nào trong ba API cho ra target match. Chi tiết request và số liệu nằm trong [DISCOVERY_EXPERIMENT_RESULTS_V1.md](DISCOVERY_EXPERIMENT_RESULTS_V1.md).

### 4.3 Career pages, ATS và feeds

- Career page có thể chứa JSON-LD `JobPosting`, HTML hoặc feed, nhưng quyền và cấu trúc phải xác minh theo từng domain. Pixelz chứng minh source discovery có thể dẫn tới trang chính thức, chưa chứng minh khả năng polling được phép.
- Không có Greenhouse/Lever/Ashby board phù hợp được xác minh live trong sample. Không có board identifier nào được đoán hoặc brute-force.
- Không tìm thấy RSS/Atom tuyển dụng Việt Nam phù hợp đã được xác minh trong sample.
- [VietJobs](https://github.com/VinNLP/VietJobs) là dataset MIT gồm 48.092 bản ghi từ 07–10/2025. Hữu ích cho test parser/evaluation offline, nhưng là snapshot cũ nên `NO-GO` cho phát hiện tin mới 7 ngày.

## 5. Feasibility theo hướng nguồn

| Hướng | Khả năng tìm source mới | Khả năng đọc JD | Trạng thái | GO/NO-GO |
|---|---|---|---|---|
| Public job API không key | Có, nhưng chỉ trong catalogue của API | Có schema/URL/date rõ | `VERIFIED_LIVE`, `AUTHORIZED` theo tài liệu nguồn | **GO có điều kiện** cho PoC kỹ thuật; **NO-GO** cho coverage mục tiêu đã đo |
| General web-search API | Rộng | Chỉ tìm candidate; vẫn cần verifier | Không còn lựa chọn `$0` tổng quát đã xác minh | **NO-GO** theo ràng buộc hiện tại |
| Company career page | Có thể tìm qua index/link graph | Có thể đọc HTML/JSON-LD | `UNVERIFIED` theo từng site | **GO chỉ sau allowlist/permission review** |
| Greenhouse/Lever/Ashby public board | Không tự tìm board nếu thiếu discovery layer | Endpoint công khai có thể dễ parse | Chưa có board target được xác minh live | **GO cho job discovery**, **NO-GO làm source discovery độc lập** |
| RSS/Atom | Có thể qua feed directory/link discovery | Rất phù hợp polling | Không có feed target đã xác minh | **UNVERIFIED** |
| Website việc làm Việt Nam | Index có tín hiệu tốt | Điều khoản/anti-bot khác nhau | ITviec bị chặn cho automation nếu chưa có consent | **NO-GO mặc định**, review từng nguồn |
| Facebook | Discovery tổng quát không có API đã xác minh | Graph API phụ thuộc quyền cụ thể | `BLOCKED`/`AUTH_REQUIRED` | **NO-GO V1** |
| LinkedIn | Có tín hiệu dữ liệu tốt | Không có public `$0` API ingestion được xác minh | `BLOCKED` | **NO-GO** |

## 6. Facebook feasibility riêng

| Khả năng | TECHNICALLY_POSSIBLE | AUTHORIZED | VERIFIED_LIVE | Kết luận |
|---|---:|---:|---:|---|
| Đọc Page do app quản lý/có quyền phù hợp qua Graph API | Có | Chưa | Không | `AUTH_REQUIRED`; cần Meta app, token, quyền, app review/business verification tùy trường hợp |
| Page discovery toàn cục | Chưa có đường API hiện hành được xác minh | Không | Không | `BLOCKED` |
| Public post search toàn cục | Không có endpoint hiện hành được xác minh | Không | Không | `BLOCKED` |
| Group discovery/read | Quyền rất hạn chế và phụ thuộc app/group | Không | Không | `BLOCKED` |
| Tìm URL Facebook qua search index hợp lệ | Có thể tìm candidate URL | Chưa có search API `$0` deployable | Không live qua API | `UNVERIFIED` |

Meta phân biệt automation được cấp phép và scraping không được cấp phép; spike không suy diễn rằng giới hạn tần suất biến scraping thành hợp lệ ([Meta Help về automated data collection](https://www.facebook.com/help/463983701520800), [Meta về scraping](https://about.fb.com/news/2021/04/facts-on-news-reports-about-facebook-data/)). Không thực hiện request Graph API vì thiếu credential/quyền; không dùng session cookie người dùng.

## 7. Cloud và chi phí

GitHub Actions scheduled workflow đáp ứng yêu cầu không cần laptop/iPhone. Standard GitHub-hosted runners miễn phí cho public repository; private repository phụ thuộc quota/plan và có thể phát sinh phí ([billing](https://docs.github.com/en/actions/concepts/billing-and-usage)). Schedule là best-effort, có thể delay/drop, chạy trên default branch và có thể bị disable sau thời gian dài không hoạt động ở public repo ([scheduled events](https://docs.github.com/en/actions/using-workflows/events-that-trigger-workflows#schedule), [workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax)).

Guardrail bắt buộc nếu triển khai V2:

- chỉ dùng public standard runner sau khi xác minh repo/plan;
- `timeout-minutes`, request budget, pagination cap và `concurrency` chống chạy chồng;
- source allowlist đã xác minh; không fallback sang scraping;
- secrets ở GitHub Actions secrets, không commit; worker không kết nối DB production trực tiếp;
- hard fail khi nguồn đổi điều khoản/schema;
- không bật schedule trong spike này.

Chi phí thực tế của spike: **$0**. Không dùng credential, provider trả phí, AI, R2, production DB hay workflow runner. Chi phí dự kiến PoC hẹp: **$0** nếu repo/runners thực sự thuộc quota miễn phí và chỉ gọi ba public API trong giới hạn. Source discovery web tổng quát `$0`: chưa có phương án đã xác minh.

## 8. Quyết định

1. **GO** cho V2 dạng controlled discovery: public no-auth APIs + registry/verifier + adapters + dedup hiện có, với tiêu chí thành công là cơ chế chứ không cam kết coverage Hà Nội.
2. **NO-GO** cho tuyên bố “agent tự tìm toàn web, không seed, luôn `$0`” cho tới khi có một search/feed discovery API hợp lệ được xác minh.
3. **NO-GO** cho Facebook ingestion V1; chỉ mở lại khi người dùng chủ động cung cấp Meta app/quyền đã được duyệt và phê duyệt một spike riêng.
4. **NO-GO** cho ITviec automated collection nếu chưa có prior written consent; manual evidence không được biến thành production adapter.
