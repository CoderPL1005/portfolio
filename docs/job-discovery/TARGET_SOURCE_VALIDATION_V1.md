# Job Discovery — Target Source Validation V1

Ngày xác minh: 2026-10-20 (Asia/Saigon)

## 1. Kết luận

**GO_TARGET_SOURCE_VALIDATED** cho một adapter có phạm vi hẹp đối với `career.ncc.plus`.

Nguồn này được tìm mà không cung cấp trước công ty, URL hoặc board identifier; chủ nguồn đang công khai WordPress REST API và RSS, không yêu cầu đăng nhập, robots không chặn, và request live trả về một JD đang ở trạng thái `publish` phù hợp target:

- Công ty: NCCPLUS Vietnam Joint Stock Company.
- Vị trí: `Internship Programs: .NET`.
- Địa điểm có cấu trúc: `Ha Noi 3`.
- Công nghệ: `.NET`, RESTful API, ASP.NET Boilerplate, OOP, database.
- Cấp độ: Internship.
- URL gốc: <https://career.ncc.plus/internship-programs-net/>.

JD có ngày publish gốc `2021-05-11`, sửa lần cuối `2026-09-17`, và vẫn xuất hiện trong danh sách tuyển dụng/REST API tại ngày audit. Vì vậy nguồn đạt target về technology/seniority/location nhưng **không đạt ưu tiên published trong 7 ngày**. Không dùng `modified` để giả thành ngày đăng.

Kết luận này chỉ xác minh **một target-bearing source có thể polling**. Nó không chứng minh source discovery toàn web đã được tự động hóa hoặc coverage đủ rộng.

## 2. Tiêu chuẩn trạng thái

| Trạng thái | Điều kiện |
|---|---|
| `DISCOVERED` | Tìm được domain/endpoint mà không có seed company/board ID. |
| `ACCESSIBLE` | HTTP read thành công, không cần session cá nhân. |
| `AUTHORIZED` | Có API/feed public được thiết kế cho anonymous read và không thấy hạn chế site-specific trái ngược trong bounded audit. |
| `VERIFIED_LIVE` | Đã gọi endpoint thật trong spike và xác nhận payload. |
| `TARGET_MATCH` | JD thật khớp technology + seniority + location theo tiêu chí. |
| `BLOCKED` | Không có đường tự động hợp lệ hoặc dữ liệu cần thiết không thể lấy qua đường được phép. |

`AUTHORIZED` trong tài liệu này chỉ áp dụng cho GET public, bounded, lưu URL/provenance và dữ liệu tối thiểu phục vụ job discovery. Nó không cấp quyền tái xuất bản toàn bộ nội dung hoặc POST ứng tuyển.

## 3. Nguồn đạt nghiệm thu: NCC Career

### 3.1 Discovery và endpoints

Nguồn được phát hiện từ public search index bằng query target, không dùng danh sách công ty. Các endpoint live:

| Endpoint | Kết quả |
|---|---|
| `GET https://career.ncc.plus/wp-json/` | REST index hoạt động, namespace `wp/v2` hiện diện. |
| `GET https://career.ncc.plus/wp-json/wp/v2/types` | Public content types gồm `post`, `page`; không cần authentication. |
| `GET https://career.ncc.plus/wp-json/wp/v2/search?search=.Net&per_page=20&type=post` | 7 kết quả; chứa post ID `256`, title `Internship Programs: .NET`. |
| `GET https://career.ncc.plus/wp-json/wp/v2/posts/256?context=view` | Trả JD, status, date, modified, canonical link, content và metadata. |
| `GET https://career.ncc.plus/feed/` | RSS 2.0 hoạt động, 5 items, chứa target JD. |
| `GET https://career.ncc.plus/robots.txt` | `User-agent: *`, `Disallow:` rỗng, có sitemap. |

WordPress mô tả REST API là API phân tán cung cấp public data cho client anonymous; endpoint `GET /wp/v2/posts` và `GET /wp/v2/posts/<id>` là contract chính thức ([WordPress REST reference](https://developer.wordpress.org/rest-api/reference/), [Posts endpoint](https://developer.wordpress.org/rest-api/reference/posts/)). Site NCC không bật authentication requirement cho các GET đã thử và REST index tự công bố capabilities.

### 3.2 JD evidence

| Trường | Giá trị live |
|---|---|
| API ID | `256` |
| Status | `publish` |
| Title | `Internship Programs: .NET` |
| Company | NCCPLUS Vietnam Joint Stock Company |
| Address field | `Ha Noi 3` |
| Employment metadata | `Full time/ Part time` |
| Technology evidence | `.Net`, RESTful API, ASP.NET Boilerplate, OOP, database |
| Published | `2021-05-11T14:39:41` site timezone |
| Modified | `2026-09-17T09:28:48` site timezone |
| Canonical URL | <https://career.ncc.plus/internship-programs-net/> |
| RSS pubDate | `Tue, 11 May 2021 07:39:41 +0000` |

Trang [NCC Careers](https://career.ncc.plus/) vẫn liệt kê `Internship Programs - .Net`, `Full-time or Part-time`, địa điểm `Ha Noi 3`. Việc hiện diện trong current list, API status `publish` và canonical page hoạt động là bằng chứng live; nó không làm mới published date.

### 3.3 Authorization, quota và cost

- Anonymous GET là use case công khai của WordPress REST API; site có thể yêu cầu auth nếu muốn nhưng hiện không làm vậy ([WordPress REST FAQ](https://developer.wordpress.org/rest-api/frequently-asked-questions/)).
- `robots.txt` không disallow path nào. Đây là guard bổ sung, không phải nguồn authorization duy nhất.
- Không tìm thấy site-specific terms cấm API polling trong bounded search. Nếu terms thay đổi, adapter phải fail closed.
- Không có provider-published request quota cho site được tìm thấy. Vì vậy không được hiểu là unlimited.
- PoC đề xuất tối đa 1 RSS GET + 1 REST detail GET cho item mới/đổi mỗi run, tối đa 2 run/ngày, timeout 30 giây, không retry vô hạn.
- Endpoint không cần API key, login hoặc payment; request spike có chi phí thực tế `$0`.

### 3.4 Cloud feasibility

Endpoint là HTTPS public, không cookie và không IP allowlist quan sát được. Do đó có thể gọi từ GitHub-hosted runner bằng standard HTTP client. Không cần laptop/iPhone hoặc secret.

Khả năng này chưa được chạy bên trong GitHub Actions vì spike không được phép tạo/kích hoạt workflow. Trạng thái chính xác: **cloud-compatible by contract**, chưa `VERIFIED_FROM_GITHUB_ACTIONS`.

## 4. Các nguồn ứng viên khác

### 4.1 Softdreams public WordPress REST

| Thuộc tính | Kết quả |
|---|---|
| Discovery | Không có seed; public search tìm được target page. |
| REST index | `https://softdreams.vn/wp-json/` — live, `wp/v2`. |
| Search | `GET /wp-json/wp/v2/search?search=.Net&per_page=20&type=post` tìm được ID `1712`. |
| Record | `GET /wp-json/wp/v2/tuyen-dung/1712?context=view`. |
| Title | `.Net/C# Developer (Fresher – Junior)`. |
| API date | `2024-04-02T16:16:12`. |
| API modified | `2026-10-16T10:34:19` — trong 7 ngày, nhưng không phải published date. |
| Content | .NET MVC 5, Web API, Entity Framework, 6 tháng–3 năm kinh nghiệm. |
| Canonical URL | <https://softdreams.vn/tuyen-dung/fresher-junior-dotnet/>. |

Trang canonical hiển thị địa điểm Hà Nội và hạn nộp 31/10/2026, nhưng các trường này không xuất hiện trong public REST record đã gọi. Vì adapter API-only không thể chứng minh location từ cùng payload, nguồn được xếp `DISCOVERED + ACCESSIBLE + AUTHORIZED + VERIFIED_LIVE`, nhưng **chưa được dùng làm acceptance source độc lập** nếu không có review riêng cho HTML/structured-data retrieval.

### 4.2 AvePoint Greenhouse board

Greenhouse chính thức xác nhận Job Board GET endpoints chứa public job-board data và không cần authentication ([Job Board API](https://docs.greenhouse.io/job-board)).

Live endpoint:

`GET https://boards-api.greenhouse.io/v1/boards/avepoint/jobs?content=true`

Kết quả:

- 291 published job records.
- 0 live record có title đồng thời chứa `.NET`, `C#` hoặc `Software Developer` và location Hà Nội.
- Candidate indexed `Junior Software Developer - Hanoi`, Greenhouse ID `6200673`, trả HTTP `404` khi gọi trực tiếp và không còn trong current board.

Trạng thái: `DISCOVERED + ACCESSIBLE + AUTHORIZED + VERIFIED_LIVE`, **không TARGET_MATCH tại thời điểm audit**.

### 4.3 Portals và community sources

| Nguồn | Evidence | Trạng thái |
|---|---|---|
| LinkedIn | Search index cho thấy AvePoint/NCC/Pixelz target gần đây | `BLOCKED`: không dùng search/browser page làm automated ingestion; không có public `$0` job API được xác minh |
| ITviec | Có listing .NET Hà Nội thật | `BLOCKED`: terms giới hạn retrieval/copy ngoài reasonable personal use nếu chưa có prior written consent ([terms](https://itviec.com/blog/terms-and-conditions/)) |
| Joboko/TopCV | Có mirror/listing target | `BLOCKED`: không có API/feed và quyền automated retrieval được xác minh trong spike |
| University/community pages | Có PDF/post, phần lớn cũ hoặc expired | `BLOCKED`: không có stable current feed + authorization evidence |
| Pixelz career page | Có .NET Hanoi role nhưng yêu cầu 3+ năm | Không đúng entry-level target; API/feed permission chưa xác minh |

Không lặp lại Remotive, Jobicy và Arbeitnow vì Spike V1 trước đã đo coverage target bằng 0.

## 5. Matching result

| Source | Technology | Seniority | Location | Published ≤7d | Acceptance |
|---|---:|---:|---:|---:|---|
| NCC REST/RSS | PASS | PASS — Internship | PASS — Ha Noi 3 | FAIL | **PASS/TARGET_MATCH**, freshness caveat |
| Softdreams REST | PASS | PASS — Fresher/Junior | Không có trong API payload | FAIL; modified ≤7d | Không dùng làm acceptance source |
| AvePoint Greenhouse | Không có current matching record | — | — | — | FAIL target coverage |

## 6. GO / NO-GO

- **GO**: tạo PoC adapter read-only cho NCC RSS + REST, có request budget và provenance đầy đủ.
- **GO có điều kiện**: dùng Softdreams khi location/deadline được expose qua API/feed hoặc HTML retrieval được policy review riêng chấp thuận.
- **NO-GO**: quảng bá rằng đã có nguồn target mới trong 7 ngày. Không có bằng chứng đó.
- **NO-GO**: LinkedIn, ITviec, TopCV, Joboko scraping.
- **NO-GO**: scheduled production workflow ở phase này.

## 7. Phạm vi không thay đổi

Không sửa production code, migration, Gmail submission, CV package, application lifecycle, database hoặc cloud resource. Không bật workflow và không gửi application.
