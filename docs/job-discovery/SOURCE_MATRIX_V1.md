# Job Discovery Cloud PoC V1 — Source Matrix

Ngày đánh giá: 2026-10-20. Mọi trạng thái là trạng thái của **use case tự động tìm JD trên cloud**, không phải đánh giá khả năng con người mở website.

## 1. Định nghĩa trạng thái

- `SUPPORTED`: có tài liệu chính thức và đường đọc public/được phép đủ để xây PoC; vẫn phải tôn trọng rate limit/terms.
- `AUTH_REQUIRED`: có đường API chính thức nhưng cần token, app review, partnership hoặc hợp đồng chưa có.
- `BLOCKED`: đường thu thập phù hợp bị điều khoản/chính sách chặn hoặc không có API discovery khả dụng cho use case.
- `UNVERIFIED`: trang/dữ liệu có thể truy cập nhưng quyền automation, API, độ ổn định hoặc chi phí chưa được chứng minh.
- `UNSUPPORTED`: sản phẩm/API đã dừng hoặc không cung cấp chức năng cần thiết.

`SUPPORTED` chỉ được dùng khi có tài liệu chính thức. Một trang xuất hiện trên Google hoặc `robots.txt` cho phép crawl không đủ để nhận trạng thái này.

## 2. Ma trận nguồn

| Nguồn | Trạng thái | Truy cập được phép / auth | Cloud automation, dữ liệu nhận được | Giới hạn và quyết định PoC |
|---|---|---|---|---|
| Facebook Page qua Graph API | `AUTH_REQUIRED` | Meta app + access token + quyền/app review tương ứng; Page public content access phải được xác minh cho app cụ thể | Nếu được cấp: post ID, text, permalink, created time; cloud-capable | Chưa có app/token/review trong repo. Không đánh dấu supported trước live proof. Adapter phải disabled-by-default |
| Facebook public posts/profile search | `BLOCKED` | Không tìm thấy official general-search API cho use case | Không dùng HTML/browser automation | Meta cấm automation thu thập không được phép; không session cookie, CAPTCHA bypass, stealth/proxy ([Meta Help](https://www.facebook.com/help/463983701520800)) |
| Facebook Groups | `BLOCKED` | Không có đường general group discovery được xác minh; quyền/group API lịch sử không phải cơ sở cho PoC mới | Không cloud-scrape Group UI | Chỉ xem xét lại nếu Meta cung cấp product/permission hiện hành và app được duyệt |
| Facebook URL qua search index | `UNVERIFIED` | Phụ thuộc search API hợp lệ | Có thể chỉ nhận URL/snippet/time không tin cậy | Không bảo đảm full JD; Google API đóng với khách hàng mới, Bing API đã dừng |
| LinkedIn | `BLOCKED` | Job Posting API yêu cầu partner/OAuth và là API **đăng** job, không phải public job-search feed | Không dùng crawler/browser automation/session cookie | LinkedIn cấm crawlers/bots/scraping; chương trình Job Posting không nhận partnership mới và không giải quyết discovery ([prohibited software](https://www.linkedin.com/help/linkedin/answer/a1341387/prohibition-of-scraping-software), [API overview](https://learn.microsoft.com/en-us/linkedin/talent/job-postings/api/overview?view=li-lts-2026-03)) |
| TopCV public pages | `UNVERIFIED` | Trang JD public; API partner được tài liệu hóa hướng tới employer/ATS, cần token/NDA/hợp đồng | URL/JD/date có thể thấy bằng browser/search, nhưng automation permission và stable read API chưa được chứng minh | Không crawl trong PoC. Liên hệ TopCV/xin feed/API trước; [điều kiện giao dịch](https://www.topcv.vn/dieu-kien-giao-dich-chung) mô tả API là dịch vụ tích hợp theo hợp đồng |
| ITviec public pages | `UNVERIFIED` | Public pages và robots hiện cho phép crawl ngoài một route; không tìm thấy official public discovery API/explicit reuse grant | Có URL/JD/posted age trên page, nhưng contract/rate limit/HTML stability chưa xác minh | Robots không phải giấy phép. Chỉ thêm sau permission hoặc official feed; [robots.txt](https://itviec.com/robots.txt) |
| VietnamWorks | `UNVERIFIED` | Public product tồn tại nhưng audit không xác minh được discovery API/permission | Khả năng cloud automation chưa chứng minh | Không crawl trước khi có terms/feed/API bằng văn bản |
| Career page tùy ý | `UNVERIFIED` | Theo từng domain: cần robots + terms + permission/API/feed | Có thể có URL, JD, `datePosted` qua JSON-LD | Không viết “universal scraper”. Chỉ adapter allowlist, rate limit, ETag/Last-Modified và kill switch |
| Greenhouse Job Board API | `SUPPORTED` | Public board token; GET list/detail, không cần user login cho public board | JSON có ID, title, location, content, absolute URL, first-published/updated fields | Nguồn PoC ưu tiên. Poll allowlist board token; không dùng application POST. [Official Job Board API](https://docs.greenhouse.io/job-board) |
| Lever Postings API | `SUPPORTED` | Public site name; GET public published postings | JSON/HTML, job ID, text/description, hosted/apply URL; cloud-capable | Nguồn PoC ưu tiên; API không phải full-text search toàn Lever, cần allowlist site. [Official Postings API](https://github.com/lever/postings-api/blob/master/README.md) |
| Ashby public Job Postings API | `SUPPORTED` | Public job-board name; GET published listings | JSON có title, location, description, publishedAt, jobUrl/applyUrl | Chỉ nhận `isListed=true`; nguồn PoC ưu tiên. [Official public API](https://developers.ashbyhq.com/docs/public-job-posting-api) |
| Company JSON-LD `JobPosting` | `UNVERIFIED` | Format công khai nhưng quyền crawl theo từng domain | Có thể có title, description, datePosted, validThrough, org/location/application URL | Schema giúp parse, không cấp quyền truy cập. Chỉ dùng domain allowlist đã duyệt; [Google JobPosting docs](https://developers.google.com/search/docs/appearance/structured-data/job-posting) |
| Google Custom Search JSON API | `UNSUPPORTED` cho tài khoản mới | API key + Programmable Search Engine; đóng với khách hàng mới | Existing customers: JSON results, 100 free queries/day | Existing service dừng 2027-01-01; không phải nền tảng PoC mới ([official overview](https://developers.google.com/custom-search/v1/overview)) |
| Bing Search APIs | `UNSUPPORTED` | Sản phẩm đã dừng | Không còn service/API signup | Retired 2025-08-11; Azure Grounding là sản phẩm khác, không chứng minh `$0` ([Microsoft lifecycle](https://learn.microsoft.com/en-us/lifecycle/announcements/bing-search-api-retirement)) |

## 3. Thứ tự ưu tiên triển khai

1. **Greenhouse**: schema rõ, URL và timestamps trực tiếp, không auth cho public board.
2. **Lever**: public postings API, nhưng phải cấu hình site allowlist.
3. **Ashby**: public job-board API, lọc `isListed=true`.
4. **Company JSON-LD/feed được duyệt**: adapter theo domain, không generic crawler.
5. **Facebook Page Graph API**: chỉ sau app review/access proof.
6. TopCV/ITviec/VietnamWorks: chờ permission/API/feed chính thức.
7. LinkedIn/Facebook UI scraping: không triển khai.

## 4. Contract dữ liệu tối thiểu của adapter

Mỗi adapter chỉ được phát ra record có provenance đầy đủ:

```text
SourceKind
SourceExternalId
CanonicalUrl
Title
Company
Location (nullable)
DescriptionText
PublishedAt (nullable + confidence)
ExpiresAt (nullable)
ApplicationUrl (nullable)
FetchedAt
RawPayloadHash
Evidence { adapter, endpoint/board, source URL }
```

Quy tắc:

- Không suy đoán thời gian đăng khi nguồn không cung cấp; lưu `null`, không dùng thời gian crawl làm `PublishedAt`.
- Chỉ nhận URL `https`; canonicalize trước khi hash.
- Không lấy CV/candidate data hoặc dữ liệu private.
- Không tự động apply; application URL chỉ là metadata.
- Raw payload cần giới hạn kích thước, sanitize HTML khi hiển thị và không log toàn bộ nếu chứa dữ liệu không cần thiết.
- Adapter phải có rate limit, timeout, retry hữu hạn, `User-Agent` minh bạch, kill switch và allowlist.

## 5. Facebook feasibility rõ ràng

| Mức | Có thể làm | Không được làm |
|---|---|---|
| V1 | Viết contract/config cho Graph Page adapter nhưng để tắt; tài liệu hóa app review; lưu URL/post ID nếu có input hợp lệ | Không lấy cookie Facebook cá nhân; không Playwright/Selenium login; không scrape HTML |
| Sau khi có quyền | Poll đúng Page IDs được allowlist bằng Graph API, theo scopes/quota được duyệt; giữ post permalink/time | Không mở rộng thành global/group search ngoài permission |
| Không có quyền | Nhận URL do admin nhập thủ công vào inbox hiện hữu | Không né CAPTCHA/rate limit hoặc thuê scraping proxy |

Vì chưa có bằng chứng access token/app review/live response, Facebook **không đáp ứng acceptance “nguồn thật được phép” ở V1**. Nó vẫn là nguồn ưu tiên nghiên cứu, nhưng PoC phải thành công độc lập bằng ATS API public.
