# Phase A — Vacancy-aware matching và structured extraction

Baseline: `b8ec2d3`. Phạm vi chỉ `tools/browser-job-discovery`.

> Cập nhật hiện tại: **Phase A.1**, xem phần cuối. Các số liệu/quy tắc Phase A bên dưới ghi lại mốc trước quality fix; quy tắc employer, requirements, shared footer và phiên bản hiện tại được thay thế bởi phần A.1.

## Thay đổi và luồng tích hợp

- `src/vacancy-extraction.mjs`: module thuần, không I/O, dùng chung để tách vị trí, trích xuất và phân loại. Không thêm dependency.
- `src/core.mjs`: `JobPostExtractor` bổ sung extraction cho SocialAPIs; reporter bổ sung metrics. Matcher fixture/browser cũ không đổi.
- `src/socialapis-live-smoke.mjs`: summary dùng chung classifier, không đưa JD/contact vào diagnostics.
- `src/socialapis-adapter.mjs`: ưu tiên kết quả vacancy-aware từ extractor; capture và live vẫn dùng cùng adapter đọc post.
- `src/cli.mjs`: merge state theo sourceIdentity cũ; phân biệt cập nhật raw content, nâng extraction version và cập nhật metadata.
- `test/core.test.mjs`, `test/discovery-adapters.test.mjs`: regression A–H, state cũ, hash, metrics và parity giữa capture/live mock.

Luồng vẫn là `cli → adapter → runDiscovery → JobPostExtractor → deduplicate → DiscoveryReporter → merge state`.
Không thay đổi HTTP transport, giới hạn 1 request/1 credit, timeout, pagination, retry, rejection reasons hoặc PARTIAL_SUCCESS.

## Quy tắc

- Tách danh sách vị trí đánh số/heading; headline chung không trở thành một vị trí thứ ba khi đã có danh sách chi tiết.
- Stack/requirements/seniority/salary được xét theo block vị trí. Cấp Fresher chung chỉ kế thừa từ heading cha không có stack riêng.
- `.NET`, C#, ASP.NET/Core là tín hiệu backend; Angular được ghi nhận riêng và không đủ điều kiện một mình. Bare NET không phải công nghệ.
- Không dùng email/URL làm tín hiệu công nghệ, cấp độ hoặc địa điểm. Giữ nguyên dấu underscore trong địa chỉ email.
- PQA/QA/tester, bảo mật và người tìm việc không phải vacancy .NET mục tiêu.
- Cho phép danh sách chức danh có cấp độ mà không cần đúng từ “tuyển”. Câu hỏi/thảo luận không được xem là heading tuyển dụng.
- Hà Nội được nhận diện bằng tên thành phố/quận trong text. Post nhiều thành phố cần section/role evidence; mâu thuẫn để review.
- Có TARGET_MATCH, POTENTIAL_MATCH, NOT_MATCHED và INSUFFICIENT_DATA. Freshness độc lập.
- Salary giữ raw evidence, bound chỉ khi một biểu thức đủ rõ; không diễn giải up-to/broad-level salary thành mức riêng cho Fresher. `M` không tự suy ra currency.

## Regression trước/sau

Đối chiếu classifier thực sự tại baseline bằng cùng bộ text synthetic trong test, không phải một lần gọi live mới:

| Case | Trước | Sau | Kiểm tra chính |
| --- | --- | --- | --- |
| A Fresher .NET | NOT_MATCHED | TARGET_MATCH | Title đủ evidence dù không có “tuyển”; 5 tháng đến dưới 1 năm; địa điểm/email |
| B FSS | NOT_MATCHED | TARGET_MATCH | Tách SQL/C#.NET và Flutter; offer từ 10M không có upper bound |
| C BlueOC | NOT_MATCHED | TARGET_MATCH | QA/.NET/data labeling riêng; Fresher–Middle; up to 35M không là lương Fresher |
| D Cybersecurity/NET salary | NOT_MATCHED | NOT_MATCHED | Không nhận NET salary là .NET |
| E TDA | TARGET_MATCH | TARGET_MATCH | Fresher–Junior và Middle riêng; không lấy 3+ năm/Redis của Middle |
| F PQA | NOT_MATCHED | NOT_MATCHED | Email Markdown lấy được nhưng không biến PQA thành .NET |
| G Người tìm việc | NOT_MATCHED | NOT_MATCHED | Không phải employer ad |
| H Multi-city | INSUFFICIENT_DATA | INSUFFICIENT_DATA | HCM .NET không được ghép với Hà Nội; vacancy chưa rõ stack để review |

Các kết quả trên phụ thuộc đúng text synthetic đã test. Không thay thế bằng chứng live, không tuyên bố job còn mở.

## Fields và evidence

`jobs[]` vẫn là source posts; bổ sung `vacancies[]`, `extractionVersion` và `structuredMatch`.
Mỗi vacancy có title, employer, location, technologies, seniority, experience, requirements, salary, contacts, sourceUrl, publishedAt, match và evidence.
Post nhiều vacancy không chọn tùy tiện title/requirements của một role làm giá trị top-level. Top-level technology là union để báo cáo, không dùng union để match role.
Employer chỉ nhận từ nhãn rõ `Công ty:`, `Company:` hoặc `Employer:` và không suy ra từ domain email.
Contact của role khác không tự kế thừa; contact trong phần intro có thể dùng chung. Top-level contact là thông tin được trích xuất từ post, chưa xác minh.

## State và tương thích

- `contentHash` vẫn SHA-256 của title/employer/location/requirements/body **đầu vào raw**, với normalize/trim cũ; không hash field đã extraction.
- `extractionVersion = vacancy-aware-v1` tách khỏi hash.
- `storage.updated` giữ tổng số bản ghi thay đổi; bổ sung `contentUpdated`, `extractionRefreshed`, `metadataUpdated` để giải thích nguyên nhân.
- State cũ vẫn đọc được. Chỉ post gặp lại mới được extraction lại; post lịch sử khác giữ nguyên, không batch reclassify.
- `metrics` là CURRENT_RUN; `processedSourceIdentities` chỉ các post trong lượt này. `accumulatedMetrics` mô tả state đã lưu, không phải job mới.
- Metrics mới: relevant vacancies/posts (TARGET hoặc POTENTIAL), potential matches, unmatched posts, email/structured coverage, unknown freshness.
- `targetMatches` cũ không tự gộp POTENTIAL_MATCH mới. Consumer muốn cả hai phải dùng `relevantSourcePosts`.
- Không cần DB/migration. Consumer mới cần chấp nhận title/location/requirements nullable và đọc vacancy-level fields khi có nhiều role.

## Kiểm chứng offline

- `npm test`: 191/191 PASS, 0 skipped; tăng 24 tests so với baseline 167.
- `npm run test:browser`: 8/8 PASS, dùng Chrome local qua `BROWSER_POC_EXECUTABLE`, chỉ điều hướng fixture loopback.
- `node --check` toàn bộ 15 module src/test: PASS. Package không cấu hình lint/typecheck riêng.
- `git diff --check`: PASS.
- Đã sửa assertion flaky có sẵn trong test malformed-item: chuỗi ID `9999` vô tình trùng số thập phân elapsedMs. Chỉ chuẩn hóa trường đo thời gian trong assertion; vẫn kiểm tra toàn bộ dữ liệu provider không bị leak.
- Replay capture private 18.567 bytes qua CLI có socket/fetch guard: 8 accepted, 0 rejected; 4 TARGET, 1 INSUFFICIENT, 3 NOT_MATCHED; 4 relevant vacancies thuộc 4 posts.
- Replay lần hai: inserted 0, unchanged 8. Không duplicate. PublishedAt/published_at của cả 8 vẫn null; freshness UNKNOWN.
- Coverage trên capture: title 6/8, location 8/8, requirements 8/8, email 5/8. Đây là field coverage, không có nghĩa 8 posts đều là JD phù hợp hoặc 8 contact hợp lệ.
- Không chép capture/contact thật vào fixture hoặc tài liệu. Synthetic fixtures dùng domain `.invalid`.

## Giới hạn và rủi ro còn lại

Đây là parser deterministic có giới hạn, không phải general NLP. Layout không theo heading/list hoặc requirement dạng văn xuôi phức tạp có thể cần review. Employer không có nhãn rõ có thể vẫn null; không đoán tên công ty. Contact/salary chung không luôn gán được cho từng role, được giữ ở post hoặc để null. Nhấn mạnh Markdown bằng dấu sao/backtick được hỗ trợ; underscore trong email được ưu tiên bảo toàn, không tự sửa địa chỉ.

State tăng kích thước do giữ evidence và vacancy; giới hạn store 10 MiB cũ vẫn giữ nguyên. Dữ liệu private vẫn nằm trong ignored `.debug`, không đưa vào stdout.

Không có live SocialAPIs/Gemini/Gmail, không tiêu credits, không sửa production/workflow/config, không bật schedule và không stage/commit/push.

## Phase A.1 — Quality fix

Chỉ tiếp tục sửa `src/vacancy-extraction.mjs`, bổ sung regression trong `test/core.test.mjs` và cập nhật tài liệu này. Không thay đổi transport, workflow, credit accounting hoặc pipeline.

### Các lỗi và kết quả trước/sau

| Lỗi đã xác nhận ở Phase A | Sau A.1 |
| --- | --- |
| Java/React Developer có C# optional bị TARGET_MATCH | NOT_MATCHED; giữ C# trong mentioned technologies, không lấy làm primary stack |
| Footer requirements/offer/email FSS thuộc Flutter cuối danh sách | Tách shared footer; `sharedRequirements`, requirements và offer ở cấp post; cả hai role không sở hữu email/offer chung |
| Email TDA thuộc riêng Middle | Email ở post; cả Fresher–Junior và Middle không bị gán email riêng |
| Requirements lẫn intro/office/benefits/closing | Chỉ giữ requirement section hoặc câu yêu cầu có evidence; cho phép null |
| PQA/QA/Developer trong dòng yêu cầu/quyền lợi thành heading mới | Loại câu điều kiện/quyền lợi; case LUVINA PQA có đúng một vacancy |
| BlueOC/TDA/FSS không có nhãn `Company:` bị employer=null | Nhận self-identification, gắn `employerStatus=POST_MENTIONED_UNVERIFIED`; không xác minh doanh nghiệp hoặc suy ra qua email |
| BlueOC không có technical requirements nhưng bị gán footer vào requirements | Requirements của .NET vacancy là null; vẫn TARGET_MATCH từ chức danh Fresher–Middle .NET |

### Chi tiết phạm vi field

- `primaryTechnologies` lấy từ title và mandatory requirements. Optional skills và training/benefits không tự nâng một vai trò Java/React thành .NET.
- Title .NET/backend thật hoặc mandatory C#/ASP.NET Core vẫn được nhận diện; Angular một mình vẫn không đủ.
- Giữ `technologies` để ghi nhận các công nghệ được nhắc tới; không đồng nhất với primary stack.
- Tách intro, role blocks và shared footer (`postSections`). Requirements chung sau danh sách chỉ có headings được giữ ở post thay vì gán riêng role cuối.
- Footer chung có location/contact/benefits hoặc nhãn requirements chung được tách. Nếu phạm vi không rõ, giữ post-level thay vì khẳng định role ownership.
- Requirements parser loại địa điểm, contacts, salary, benefits, lời mở đầu/kết; không tự tạo yêu cầu kỹ thuật từ title.
- Giữ numbered/heading-based vacancies, bao gồm heading có khoảng năm kinh nghiệm. Không coi câu yêu cầu PQA/QA là chức danh mới.
- `extractionVersion=vacancy-aware-v1.1`; contentHash vẫn dùng raw algorithm cũ. State gặp lại được tính extraction refresh, không phải raw content update.

### Kiểm chứng A.1

- `npm test`: **202/202 PASS**, 0 skipped; thêm 11 tests so với Phase A, toàn bộ A–H vẫn PASS.
- `npm run test:browser`: **8/8 PASS**, chỉ fixture loopback trên Chromium local.
- Static syntax check và `git diff --check`: PASS.
- Capture thật 18.567 bytes: 8 accepted/0 rejected, 4 TARGET/1 INSUFFICIENT/3 NOT_MATCHED; 0 network calls.
- Source identity, raw body, source URL, raw contentHash của cả 8 posts không đổi. publishedAt/published_at vẫn null; freshness UNKNOWN.
- Role-requirements coverage từ 8/8 xuống **4/8**, vì đã loại nội dung không phải requirements. FSS còn có requirements chung riêng ở cấp post; metric cũ chỉ đếm requirements trong vacancies. Đây là sửa chất lượng, không phải mất raw data.
- Email coverage vẫn 5/8. Replay xác nhận tên FSS/TDA là post-mentioned/unverified; footer email của cả hai không gán riêng cho vacancy cuối.
- Capture hiện có chứa một LUVINA **Developer JP**, không phải PQA. Sau sửa vẫn một role, NOT_MATCHED, requirements chỉ còn điều kiện ứng viên. Case **PQA** kiểm chứng bằng text synthetic, không tuyên bố đã có live PQA evidence.
- BlueOC và Java/React optional C# được kiểm chứng bằng representative synthetic tests, không gọi live hoặc tạo synthetic live evidence.

### Giới hạn A.1

Phân đoạn vẫn là deterministic heuristic, không phải general NLP. Optional clause phức tạp hoặc footer không có dấu hiệu rõ có thể cần review. Requirements không có đủ evidence được để null; không cố tăng coverage bằng cách lấy toàn bộ body. Employer chỉ là tên trong post, không phải verified canonical company. Giữ nguyên body/evidence để người review đối chiếu. Không thêm database, dependencies hoặc network service.
