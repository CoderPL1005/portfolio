# DTMED Job Discovery Test Case V1

Ngày thực hiện: 2026-10-20, Asia/Saigon.
Phạm vi: nghiên cứu, GET read-only bounded và tài liệu; không viết adapter/workflow.

## 1. Verdict

| Gate | Kết quả | Bằng chứng quyết định |
|---|---|---|
| Unseeded | **UNSEEDED_DISCOVERY_FAIL** | 8 public API searches, không JD/candidate company nào được trả về. |
| Seeded | **SEEDED_VERIFICATION_FAIL** | Có JD public nhưng chưa xác minh website thuộc DTMED, chưa đối chiếu Facebook trực tiếp và chưa có authorization cho polling. |
| Cloud | **CLOUD_AUTOMATION_UNVERIFIED** | HTTP API thực thi từ môi trường audit; không chạy GitHub Actions. |
| Matching | **TARGET_MATCHING_DESIGN_READY** | Quy tắc deterministic, provenance, unknown và cases trong TARGET_MATCHING_V2.md; chưa có implementation/tests chạy. |
| Tổng thể | **NO_GO** | Chưa có discovery result đáp ứng đồng thời target + authorization + unseeded input. |

FAIL ở seeded không có nghĩa hai nguồn chắc chắn khác nhau. Nó có nghĩa chưa chứng minh đủ điều kiện PASS.

## 2. Tách hai phép thử

Kết quả unseeded khóa lúc `2026-10-20T21:28:30.1093159+07:00` và được ghi vào [UNSEEDED_DISCOVERY_RESULTS_V1.md](UNSEEDED_DISCOVERY_RESULTS_V1.md) trước khi truy cập các URL seeded.

Input query unseeded chỉ có technology, seniority và location được yêu cầu. Tên/domain/URL DTMED không xuất hiện trong API queries. Người thực hiện đã đọc ground truth trong task nên không gọi đây là blind evaluation; đây là kiểm tra cơ chế với input được kiểm soát.

Search tool tương tác dùng để đọc tài liệu provider; không phải thuật toán chạy trên Actions. Sau khi khóa mới dùng search seeded về employer identity. Không đưa kết quả seeded vào recall/discovery metrics.

## 3. Facebook reference

Reference: <https://www.facebook.com/groups/tuyendungdev/permalink/1866770044694609/>.

Một lần mở bằng công cụ web không lấy được nội dung (`Cache miss`). Không dùng cookie/session, không thử Graph API khi chưa có quyền, không scrape Group và không thử endpoint thay thế để vượt chặn.

Các dữ kiện sau là **USER_PROVIDED**, không phải ảnh đã được công cụ trực tiếp kiểm chứng:

- Employer DTMED; Full-stack Developer.
- Backend C#/.NET; frontend Angular/TypeScript.
- Kinh nghiệm tối thiểu 1 năm; maximum không được cung cấp.
- Location chưa xác minh.
- Thời gian Facebook tương đối, không có absolute publication timestamp đáng tin cậy.

Không có file ảnh trong task hiện tại để xem trực tiếp. Không chuyển địa chỉ từ candidate website sang Facebook record khi chưa xác nhận identity của JD.

## 4. Candidate website — kết quả thật

[Candidate JD](https://phanmembenhvien.net/jobs/tuyen-dung-lap-trinh-vien-web-net-12) trả HTTP 200 trong GET trực tiếp.

| Thuộc tính | Website quan sát được | So với ground truth |
|---|---|---|
| Employer hiển thị | PhanmemBenhvien.net | Không có literal DTMED trong HTML lấy được |
| Title | Web Developer (.NET), Fullstack | Cùng nhóm vai trò, không chứng minh cùng vacancy |
| Backend | C#, ASP.NET/MVC, .NET Framework/Core | Khớp family; không suy ra ASP.NET Core từ .NET Core |
| Frontend | HTML/CSS/JavaScript/jQuery | Không thấy Angular/TypeScript |
| Experience | Tối thiểu 1 năm | min=1; max=null, không phải khoảng 1–2 |
| Location | Hà Nội, 117 Xuân Thủy hoặc theo dự án | Xác minh cho website record; không cho Facebook record |
| Published | Không tìm thấy ngày đăng rõ | Không xác nhận freshness 7 ngày |

Không coi crawl date của search engine, HTTP retrieval time, sitemap lastmod hoặc relative Facebook time là ngày đăng JD.

## 5. Employer identity

Trang công ty [DTMED trên LinkedIn](https://vn.linkedin.com/company/dtmed) tự khai website `https://dtmed.vn` và địa chỉ 117 Xuân Thủy. Đây là dấu hiệu liên quan về địa chỉ/ngành, không xác nhận quyền sở hữu domain candidate.

GET `https://dtmed.vn/` trả HTTP 200, title có DTMED. Không tìm thấy `phanmembenhvien.net` trong HTML đó. Website candidate không có literal DTMED trong JD đã đọc. Không tìm thấy cross-link chính thức đủ mạnh để nối hai domain.

Verdict identity: **UNVERIFIED**. Cần liên kết/statement rõ từ chủ thể chính thức hoặc xác nhận của employer; không suy ra chỉ từ cùng địa chỉ. Không liên hệ employer trong spike.

## 6. API, RSS, JSON-LD và policy

Raw HTML candidate có generator `Odoo` và canonical URL khớp URL cung cấp. Không tìm thấy `application/ld+json`, alternate/feed link hoặc publication meta trong JD HTML đã lấy. Đây là kết quả giới hạn trên trang đã đọc, không phải chứng minh toàn site không có API/RSS.

Không gọi Odoo JSON-RPC/search_read, không truy vấn model nội bộ và không thử endpoint ghi. Việc chạy Odoo không phải bằng chứng public job API được cho phép.

Robots trực tiếp trả HTTP 200:

```text
User-agent: *
Sitemap: https://phanmembenhvien.net/sitemap.xml
```

Không có Disallow. Robots chỉ mô tả crawl preferences; không cấp license/API permission. Chưa tải sitemap hàng loạt.

Footer link mang tên điều khoản trong rendered page không dẫn tới một policy usable khi mở. Probe `/terms` trả HTTP 200 nhưng title là `HTTP Error`; không coi 200 này là policy được xác minh. `/privacy` không lấy được nội dung bằng web tool. Không tìm được numeric request quota, giá hoặc authorization statement cho automated job polling.

Kết quả: `ACCESSIBLE`; `AUTHORIZED=UNVERIFIED`; **BLOCKED cho recurring adapter**, dù read-only verification của URL do người dùng đưa ra thành công.

## 7. Request ledger và chi phí

### Vòng tự động unseeded

- 8 GET API thành công, 1 lần kết nối sandbox thất bại, tổng 9 client attempts.
- Không auto-retry khi đã có HTTP failure; không pagination sau tập rỗng.
- Không DTMED seed; không provider key/payment; $0 provider fee.

### Vòng seeded direct HTTP riêng

| Request | URL | Kết quả |
|---:|---|---|
| S1 | `https://phanmembenhvien.net/robots.txt` | HTTP 200 |
| S2 | Candidate JD URL | HTTP 200; Odoo/canonical metadata |
| S3 | `https://dtmed.vn/` | HTTP 200; không có cross-link cần tìm |
| S4 | `https://phanmembenhvien.net/terms` | HTTP 200 với trang lỗi |

Tổng direct shell requests/client attempts của cả hai vòng: 13 (12 response + 1 failed connection attempt), không vượt 20. Web research tool calls được ghi riêng: đọc tài liệu provider, mở references/site/policy và search identity; số request nội bộ của tool không quan sát được. Không claim toàn bộ browsing của audit có tổng HTTP ≤20. Cap 20 áp dụng vòng tự động có ledger, không giả lập internal web-search traffic là Actions traffic.

Không trả phí API, không signup, không gửi email, không gọi Gemini/R2 hoặc production backend/database.

## 8. Cloud design gate

Luồng khả thi về kỹ thuật:

```text
Actions manual run
  -> authorized searchable API (query không company seed)
  -> candidate links từ payload
  -> source policy/endpoint verifier
  -> deterministic normalizer + target matcher
  -> report có provenance/attribution
```

Đề xuất budget tương lai: 8 discovery GET + tối đa 8 verification GET + 4 slots dự phòng, tổng attempts ≤20. Tính cả redirect/retry nếu có; tốt nhất tắt automatic redirects, tự theo Location trong budget. Stop khi 429/403/auth required, cap response bytes và thời gian. Không truy cập mạng riêng/localhost hoặc endpoint thay đổi state khi đi theo candidate link.

Himalayas là đường API free/no-key thực tế đã thử nhưng coverage Hà Nội bằng 0. ATS board/company RSS có thể dùng sau khi tìm được domain/token; không tự giải quyết source discovery. Chưa có general web-search discovery API miễn phí/no-credential đủ coverage được xác minh trong task.

| Cơ chế khảo sát | Vai trò và trạng thái trong phép thử |
|---|---|
| Documented job aggregator search API | Himalayas được chạy live chỉ với target inputs; 0 kết quả. |
| Company career RSS/REST | Cần discover domain trước. Các nguồn đã biết ở vòng cũ không được dùng làm seed cho phép thử này. Exposed endpoint không tự chứng minh permission tái sử dụng nội dung. |
| ATS public API | Cần board identifier tìm được từ payload/search channel hợp lệ; 0 identifier mới trong mẫu. Không đoán/brute-force board names. |
| Links trong authorized payload | 0 candidate vì không có records; không chạy verification giả định. |
| General web-search API | Google CSE đóng với khách hàng mới; Bing Search API retired. Không thử dịch vụ cần credentials hoặc payment. |

Các hạn chế Google/Bing được kiểm tra lại qua tài liệu chính thức sau khi khóa kết quả; không phải search JD bổ sung: [Google Custom Search](https://developers.google.com/custom-search/v1/overview), [Microsoft retirement](https://learn.microsoft.com/en-us/lifecycle/announcements/bing-search-api-retirement). Không kết luận rằng mọi search API trên thị trường đều bất khả thi; chỉ chưa xác minh được phương án phù hợp các constraints của phép thử này.

GitHub Actions standard public runners có thể miễn phí; private runner minutes/billing cần kiểm tra account trước khi bảo đảm $0. Schedule là best-effort và spike chưa chạy workflow ([billing](https://docs.github.com/en/actions/concepts/billing-and-usage), [scheduled events](https://docs.github.com/en/actions/using-workflows/events-that-trigger-workflows#schedule)).

## 9. Blocker và bước tiếp theo

1. Discovery coverage: cần một API/feed tìm được JD phù hợp bằng query target; không tạo DTMED-only adapter rồi coi là discovery pass.
2. Seeded identity: cần bằng chứng chính thức nối candidate domain với DTMED và cùng vacancy.
3. Angular: cần nguồn của chính vacancy xác nhận; không thêm vào website record từ Facebook chưa đối chiếu.
4. Authorization: cần API/feed/documented grant hoặc sự đồng ý của chủ nguồn cho polling. Không tự liên hệ trong task này.
5. Cloud validation: khi có source đạt gate mới đề nghị manual Actions run; task này không sửa workflow.

Không sửa production, migration, Gmail submission, CV package hoặc lifecycle. Tám tài liệu cũ được giữ nguyên.
