# Discovery Experiment Results V1

Ngày đo: 2026-10-20 (Asia/Saigon)
Loại thử nghiệm: read-only, bounded, không credential, không thay đổi production.

## 1. Phương pháp và tiêu chí

Target match nghiêm ngặt phải đồng thời có:

1. title/text chứa `.NET`, `dotnet` hoặc `ASP.NET`;
2. location chứa `Hà Nội`, `Ha Noi` hoặc `Hanoi`;
3. seniority/title chứa `Intern`, `Fresher` hoặc `Junior`;
4. ưu tiên published date trong 7 ngày gần ngày đo.

Không dùng dữ liệu mẫu làm kết quả. Bản ghi API/trang thật được giữ dưới dạng thống kê và URL bằng chứng; không lưu bản sao nội dung vào repository.

## 2. Source-discovery experiment

### 2.1 Truy vấn

Batch 1:

- `".NET Intern" "Hà Nội" tuyển dụng`
- `".NET Fresher" "Hà Nội" tuyển dụng`
- `"Junior .NET" "Hà Nội" tuyển dụng`
- `"ASP.NET Core Developer" "Hà Nội" tuyển dụng`

Batch 2 áp dụng freshness 7 ngày:

- `.NET Intern OR Fresher OR Junior ASP.NET Core Hà Nội tuyển dụng`
- `site:topcv.vn .NET Hà Nội Intern Fresher Junior`
- `site:itviec.com .NET Hà Nội Junior`
- `site:careers.* .NET Hanoi Junior developer`

### 2.2 Candidate domains

Trong sample đóng, tìm được **14 domain ứng viên mới**, không cấp trước company/domain/board ID:

| # | Domain | Loại tín hiệu | Trạng thái sau kiểm tra |
|---:|---|---|---|
| 1 | topcv.vn | Job portal | Chưa xác minh quyền API/automation |
| 2 | laptrinhchuyennghiep.com | Training/job content | Kết quả cũ/không phải canonical source |
| 3 | findglocal.com | Aggregator/social mirror | Không đủ provenance |
| 4 | 123job.vn | Job portal | Chưa xác minh quyền API/automation |
| 5 | haui.edu.vn | University PDF/page | Nhiều artifact cũ |
| 6 | hachinet.com | Company/career | Chưa xác minh automation |
| 7 | pitagon.vn | Company/career | Chưa xác minh automation |
| 8 | uneti.edu.vn | University page | Không phải feed tuyển dụng ổn định |
| 9 | itnavi.com.vn | Job portal | Chưa xác minh automation |
| 10 | fis.com.vn | Company/career | Chưa xác minh automation |
| 11 | falcongames.com | Company/career | Chưa xác minh automation |
| 12 | vnpt.vn | Company/career | Chưa xác minh automation |
| 13 | itviec.com | Job portal | Có dữ liệu thật; terms chặn retrieval ngoài personal use nếu chưa consent |
| 14 | pixelz.com | Official career, first-hop từ ITviec | JD thật; freshness và automation permission chưa xác minh |

Các domain xuất hiện sau cutoff không được cộng vào số đo để tránh tăng mẫu tùy ý.

### 2.3 Tỷ lệ source mới

- Candidate web domains: **14**.
- Public API products được tìm và gọi: **3**.
- Tổng bề mặt nguồn ứng viên trong sample: **17**.
- Nguồn API có endpoint thực thi live, không credential: **3/3**.
- Nguồn đã chứng minh vừa đúng target vừa có thể tự động thu thập theo ràng buộc: **0**.

## 3. Job-discovery experiments qua API được phép

### 3.1 Exact requests

Tổng cộng **7 HTTP GET** live, không có request ghi:

| # | Endpoint | Mục đích | HTTP/payload |
|---:|---|---|---|
| 1 | `https://jobicy.com/api/v2/remote-jobs?count=100&geo=apac&industry=engineering&tag=dotnet` | .NET APAC | 0 job |
| 2 | `https://jobicy.com/api/v2/remote-jobs?count=100&geo=apac&industry=engineering&tag=asp.net` | ASP.NET APAC | 0 job |
| 3 | `https://remotive.com/api/remote-jobs?search=.net&limit=50` | Query ban đầu | 10 job |
| 4 | cùng endpoint #3 | Thu các trường bằng chứng an toàn | cùng 10 job |
| 5 | `https://remotive.com/api/remote-jobs?search=dotnet&limit=50` | Kiểm tra semantics query | cùng 10 job |
| 6 | `https://remotive.com/api/remote-jobs?search=asp.net&limit=50` | Kiểm tra semantics query | cùng 10 job |
| 7 | `https://www.arbeitnow.com/api/job-board-api` | Page 1 catalogue | 300 job |

Ba lần query Remotive khác nhau trả đúng cùng tập 10 bản ghi. Đây là bằng chứng query precision thấp/caching hoặc semantics rộng; duplicate giữa các biến thể là **100%**. Không đếm lặp thành JD mới.

### 3.2 Real job sample

10 bản ghi Remotive thật, có ID/URL/company/location/published date:

| ID | Title | Company | Location | Publication date |
|---:|---|---|---|---|
| 2091158 | Software Engineer, AI Coding Tasks | Askable | Worldwide | 2026-10-17 |
| 2069746 | Tech Lead Full-Stack Rails Engineer | Mitre Media | USA/Canada | 2026-10-14 |
| 1680495 | Remote Office Assistant | Coalition Technologies | Worldwide | 2026-10-11 |
| 2086540 | Inside Sales Contractor | Credit Wellness | Worldwide | 2026-10-08 |
| 2091045 | Tier III Service Desk Engineer | Unio Digital | Worldwide | 2026-10-07 |
| 1185979 | Freelance Writer | IAPWE | Worldwide | 2026-10-04 |
| 1749306 | Freelance Copywriter | Coalition Technologies | Worldwide | 2026-10-02 |
| 2091133 | Senior React Full-stack Developer | Lemon.io | Europe/USA/Canada/APAC | 2026-10-02 |
| 2091132 | Senior back-end Engineer | Lemon.io | Multiple countries | 2026-09-30 |
| 2091144 | Content Reviewer US | TELUS | USA | 2026-09-21 |

Trong 10 bản ghi này, 2 có publication date trong 7 ngày nhưng không đúng technology/location/seniority. Arbeitnow trả 300 bản ghi thật; spike không tải thêm page và không lưu full payload.

### 3.3 Metrics

| Metric | Kết quả |
|---|---:|
| API response records thật có provider ID trong hai payload sample | **310** (10 Remotive + 300 Arbeitnow; không khẳng định semantic uniqueness giữa hai provider) |
| Strict target matches | **0** |
| Target precision trên sample API | **0/310 = 0%** |
| Target jobs mới trong 7 ngày | **0** |
| Cross-source target dedup có thể đo | **Không** — target set rỗng |
| Provider-query duplicate quan sát được | Remotive: **100%** giữa 3 query variants |

Con số 310 không ngụ ý 310 nguồn, không phải coverage toàn thị trường và chưa qua semantic cross-provider dedup; đó là 10 record Remotive + 300 record Arbeitnow trong page/sample đầu. Jobicy trả 0.

## 4. Manual browser evidence từ nguồn có tín hiệu tốt

### ITviec

Trang [11 .NET jobs in Ha Noi](https://itviec.com/it-jobs/.net/ha-noi) được mở live. Năm listing hiển thị gần nhất được xem:

1. Solution Architect (Java/.NET/Oracle/API), VPBank — 3 ngày — không đúng seniority.
2. .NET Developer (ASP.NET, C#, SQL), QIG — 4 ngày — title chung, không xác nhận Intern/Fresher/Junior.
3. Senior Fullstack, Caspeco — 4 ngày — không đúng seniority.
4. [Jr/Mid Software Engineer (.NET), Pixelz](https://itviec.com/it-jobs/junior-middle-software-engineer-net-pixelz-0347?lab_feature=preview_jd_page) — 6 ngày — title đúng target; JD yêu cầu 3+ năm nên candidate fit còn mâu thuẫn.
5. Bridge System Engineer — 4 ngày — không đúng target.

Strict title target trong 5 listing gần: **1/5 = 20%**. Đây là bằng chứng dữ liệu/trang thật, **không** phải bằng chứng cho phép automation. [Terms and Conditions của ITviec](https://itviec.com/blog/terms-and-conditions/) giới hạn retrieval/copy/download ngoài reasonable personal use nếu chưa có prior written consent. Vì vậy adapter tự động bị `BLOCKED` cho tới khi có consent.

### Pixelz official career

Từ listing trên, source discovery tìm được [.NET Developer – Hanoi](https://www.pixelz.com/jobs/hanoi/dotnet-developer/) trên domain công ty. Các trường title/company/location/JD có thể đọc, nhưng trang không cung cấp published date đủ rõ và chưa xác minh policy cho polling. Trạng thái: `UNVERIFIED`, không đưa vào automatable count.

## 5. Facebook experiment status

Không thực hiện Graph API request vì không có app/token/quyền đã duyệt. Kết quả là `BLOCKED`, không phải `BLOCKED_BY_ENVIRONMENT` và không phải PASS.

- Page/post/group search toàn cục: không có endpoint hiện hành được xác minh.
- Page content có quyền cụ thể: `TECHNICALLY_POSSIBLE`, `AUTH_REQUIRED`, chưa `VERIFIED_LIVE`.
- Search-index discovery của URL Facebook: chỉ là candidate discovery; không có deployable `$0` search API đã xác minh.
- Không dùng cookie, username/password, browser session, CAPTCHA bypass, stealth fingerprinting hoặc proxy rotation.

## 6. Chi phí

| Hạng mục | Thực tế spike | Dự kiến PoC hẹp |
|---|---:|---:|
| Public API requests | $0 | $0 nếu tuân thủ quota |
| Search nghiên cứu tương tác | $0 trong môi trường audit | Không thể coi là runtime API miễn phí |
| GitHub Actions | Không kích hoạt | $0 chỉ khi repo/plan dùng standard runner miễn phí và không vượt quota |
| Render/Neon | Không gọi production | Có thể trong free tier; phải giới hạn request/storage |
| Meta/Gmail/R2/AI | Không gọi | Không cần cho discovery PoC |

Không có credential, payment method hay dịch vụ có khả năng tự phát sinh phí được sử dụng.

## 7. Khả năng tái lập và giới hạn

- Kết quả search/index và listing thay đổi theo thời gian/địa lý; ngày đo là một phần của bằng chứng.
- Không benchmark exhaustive recall vì không có ground-truth toàn thị trường.
- Không claim API authorization vượt quá nội dung tài liệu công khai của từng API.
- Không có source target hợp lệ nên chưa đo được dedup precision/recall của pipeline với JD mục tiêu.
- Bước tiếp theo chỉ được chạy qua workflow thủ công (`workflow_dispatch`) trước khi cân nhắc schedule.

## 8. GO / NO-GO theo bằng chứng

- Public API adapter kỹ thuật: **GO**.
- Target coverage Hà Nội/.NET/entry-level từ ba API: **NO-GO**.
- General source discovery `$0` toàn web: **NO-GO**.
- ITviec automated adapter: **NO-GO** nếu chưa có written consent.
- Pixelz/career-page verifier: **GO cho nghiên cứu thủ công**, `UNVERIFIED` cho automation.
- Facebook: **NO-GO** trong V1.
