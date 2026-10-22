# B.4.1 — Backend-focused Facebook Job Discovery

Baseline: `ce0c286`. Phạm vi: `tools/browser-job-discovery`. Triển khai local,
kiểm chứng offline; **không** SocialAPIs, Actions dispatch, production API/DB,
AI, Gmail, thay đổi billing/secrets, scheduler, stage/commit/push.

## 1. Tách mục tiêu công việc khỏi bằng chứng kỹ năng

`config/backend-target-preferences.json` là **cấu hình mục tiêu hiện tại**, không
phải profile mặc định áp đặt cho mọi người. Truyền nội dung file vào
`policy.targetJobPreferences` của entrypoint/workflow hiện có.

- Primary role BACKEND; cho phép Full-stack khi có bằng chứng trách nhiệm Backend.
- Nhóm PREFERRED: .NET, ASP.NET Core, C#.
- Nhóm ACCEPTED: Python, FastAPI. Nhãn ACCEPTED không làm giảm điểm matching
  Python so với .NET khi đều có demonstrated evidence.
- Intern/Fresher, Hà Nội; minimum required experience ưu tiên <=1 năm,
  fallback <=2 năm; >2 năm hoặc strict lower bound >2 bị loại.
- Frontend-only, Junior/Senior không đạt target của cấu hình này.

Các mức kinh nghiệm, seniorities, locations, nhóm stack và allowFullStack đều
được cấu hình/validate. Module mới hỗ trợ target BACKEND; target khác vẫn dùng
đường legacy khi không truyền preferences, không giả định đã có general NLP.

Technology Profile vẫn xây từ Portfolio snapshot: technologies + linked skills
USED/LEARNING/EXPLORING + published Experience/Project technology links. Giữ
nguyên builder B.4, không dùng CV, không thêm catalogue hoặc bảng mới. Project
không PLANNED có link Python/FastAPI nâng effective usage thành USED, vẫn giữ
declared learning level và provenance. Có Python không tự chứng minh FastAPI.

Backend orchestration yêu cầu `profile.technologyProfile`, không dùng mảng tên
technology do operator tự khai làm bằng chứng Portfolio. Snapshot local vẫn có
nhãn `OPERATOR_SUPPLIED_PUBLIC_EXPORT_NOT_LIVE_VERIFIED`; không tự fetch production
và không xác minh được độ mới/đầy đủ của export chỉ từ JSON.

## 2. Query planner

Planner lấy **ý định stack** từ preferences, tra lại technology evidence để ghi
USED/developing/UNVERIFIED; không biến preference thành proficiency. Không chọn
Angular/SQL/Docker/Git/AWS chỉ vì chúng USED.

Round-robin giữa các stack groups trước khi giới hạn pool 32 queries; reserve
một generic Backend query. Với cấu hình hiện tại, pool 11 queries bắt đầu:

1. `.NET Backend Fresher Hà Nội`
2. `Python Backend Fresher Hà Nội`
3. `ASP.NET Core Backend Fresher Hà Nội`
4. `FastAPI Backend Fresher Hà Nội`
5. `C# Backend Fresher Hà Nội`

Sau đó là các biến thể Intern và `Backend Developer Fresher Hà Nội`.
Không sinh Junior hoặc Frontend-only query. Nhóm Python không bị catalogue
supporting tools lấn hết pool. Pool có coverage không đồng nghĩa mọi query đã
được gửi: pagination/allocation/budget hiện tại vẫn quyết định HTTP requests.

Ranking giữ measured new relevant opportunities / verified deducted credits.
Khi chưa có số đo, exploration prior 0.25 + round-robin order cho cả hai nhóm.
Fingerprint preferences mới tách lịch sử yield khỏi target cũ; không đổi hoặc
xóa lịch sử. Adaptive shortlist backend tối đa 16 theo contract hiện có.

## 3. Matching và ranking trên tập kết quả

| Kết quả | Điều kiện |
|---|---|
| TARGET_MATCH | Backend rõ ràng, stack được chứng minh, explicit target level, location phù hợp, minimum required years <=1 hoặc UNKNOWN |
| POTENTIAL_MATCH | Generic Backend chưa rõ stack, developing-only, hoặc chưa có đủ evidence cho framework bắt buộc |
| FALLBACK_MATCH | Backend/stack/level/location phù hợp, minimum required experience trên mức ưu tiên nhưng không quá fallback maximum (hiện tại 2 năm) |
| INSUFFICIENT_DATA | Thiếu role/Backend responsibility, location, level hoặc có experience conflict |
| NOT_MATCHED | Frontend-only, primary Backend stack không phù hợp, level không phù hợp, closed recruitment, hoặc required experience vượt trần |

Title và mandatory requirements là role-stack evidence; optional clauses không
được dùng cứu role Java/React bằng một lần nhắc C#. SQL/EF Core/Docker/Git/AWS/
Azure/TypeScript/Angular/React là supporting signals trong matcher Backend,
không tự xác nhận role stack. Có lexical detection cho một số primary stack
phổ biến; đây không phải technology catalogue hoặc hệ thống hiểu mọi JD.

Full-stack cần responsibility Backend trong requirements (ví dụ xây REST API),
không chỉ có .NET/Angular trong một footer. Role chỉ ghi `.NET Developer` mà
thiếu evidence Backend không tự được nâng thành Backend đã xác minh.

Tái sử dụng mandatory-experience parser hiện có; bare `2 years`, `2 năm`,
range/lower bounds và optional experience được phân biệt. UNKNOWN vẫn lưu riêng
trong `vacancy.match.requiredExperience.status/minimum`, không biến thành 0.
Explicit Intern/Fresher với years UNKNOWN có thể TARGET nếu đủ các điều kiện khác.
Range 1–2 có minimum 1, không đồng nghĩa bắt buộc 2; minimum và maximum vẫn riêng.
Conflict không được tự nâng thành TARGET.

`state.rankedOpportunities` đánh giá trên **tập opportunities đã dedup của registry**
theo preferences hiện tại, không sửa raw jobs lịch sử. Thứ tự tier:

1. TARGET_MATCH.
2. FALLBACK_MATCH chỉ khi số distinct TARGET_MATCH < `config.target`.
3. POTENTIAL_MATCH, rồi INSUFFICIENT_DATA.
4. FALLBACK_MATCH bị defer nếu primary đã đủ, rồi NOT_MATCHED.

Mỗi row giữ classification, opportunityId/sourceIdentity/vacancyIndex,
fallbackEligible và recommendationTier. Repost không làm tăng số primary.
Fallback không bao giờ trở thành TARGET và không tăng primary yield/stop-target
counters. Page report có `fallbackVacancies` riêng. Ranking là derived view của
accumulated registry, **không phải chỉ các JD mới hoặc đã xác minh còn tuyển/gần đây**.

## 4. Before / after đã chạy trên cùng synthetic input

Đây là so sánh legacy matcher B.4 và matcher mới với cùng Portfolio fixture,
không phải kết quả live hay khẳng định các kỹ năng của candidate thật.

| Trường hợp | Before | After |
|---|---|---|
| Angular Frontend Fresher, Angular USED | TARGET_MATCH | NOT_MATCHED |
| .NET Backend Fresher, bắt buộc 2 years | TARGET_MATCH | FALLBACK_MATCH |
| .NET Backend Fresher, bắt buộc bare 3 years | TARGET_MATCH | NOT_MATCHED |
| Generic Backend Fresher chỉ có SQL/Docker USED | TARGET_MATCH | POTENTIAL_MATCH |
| Python Backend Fresher có project evidence | TARGET_MATCH | TARGET_MATCH |

Regression còn bao gồm .NET/C#/ASP.NET Core, Python/FastAPI, Full-stack, optional
C# trong title Java, Node.js không phù hợp, Django không được tự suy từ Python,
Junior/Senior, 0–1 năm, >2 năm, closed, unknown years, configurable targets,
pool cap, profile/yield isolation, historical raw state và dedup-aware fallback.

## 5. Replay capture thật local

Đã đọc lại capture có sẵn `.debug/socialapis-response.json` bằng adapter hiện có,
và `.debug/portfolio-discovery-policy.json` làm operator-supplied Portfolio
snapshot, đổi target preferences trong memory; không ghi đè các file này.

- 8 provider items, 8 accepted; 20 extracted vacancies.
- 0 TARGET_MATCH, 0 POTENTIAL_MATCH, 0 FALLBACK_MATCH.
- 9 INSUFFICIENT_DATA, 11 NOT_MATCHED.
- Reasons: BACKEND_ROLE_UNVERIFIED 8; INCOMPATIBLE_PRIMARY_STACK 7;
  RECRUITMENT_UNVERIFIED 1; ROLE_MISMATCH 1; LOCATION_UNVERIFIED_OR_MISMATCH 1;
  FRONTEND_ONLY 1; SENIORITY_MISMATCH 1.
- sourceIdentity, body, raw contentHash và publishedAt không đổi.
- 0 network requests; không có ngày/URL giả, không xuất text/contact của capture.

Capture lịch sử không được coi là khảo sát live các query Backend mới. Việc
không có TARGET trong capture không được che bằng nới level/experience/evidence.

## 6. Safety / compatibility

- Không sửa transport, pagination/cursor selection, continuation encryption,
  core 4-page cap, 4-1-1, request/credit ledger, retry/pagination tự động ở adapter,
  checkpoint/artifact protocol hoặc workflow/schedule.
- Legacy policy không có targetJobPreferences vẫn giữ matcher/planner B.4.
- Preferences đi vào config hash: không resume cursor bằng target/profile khác.
  Finish/reconcile execution cũ; tạo logical run mới nếu chủ động đổi policy,
  giữ registry và ledger, không INITIALIZE_STATE lại.
- Historical jobs thiếu vacancies được extract tạm để ranking, không ghi ngược
  hoặc thay hash/raw body. Derived ranking không bị dùng nhầm từ target cũ.
- Freshness UNKNOWN vẫn UNKNOWN; provider date chưa được coi verified recent.
- Free/no-cash/public-artifact consent và production free-balance fail-closed
  giữ nguyên; không kích hoạt unattended mode.

## 7. Files và kiểm chứng

Modified:

- `package.json`: đưa regression mới vào full offline suite.
- `src/candidate-technology-profile.mjs`: preferences-based Backend planner,
  target-scoped yield, resolve policy trong boundary hiện có.
- `src/pagination-cli.mjs`: truyền preferences vào queryPlan report.
- `src/pagination-orchestrator.mjs`: áp dụng prefs, profile guard, fallback metrics
  và set-level ranking; giữ nguyên budget/checkpoint controls.

Added:

- `config/backend-target-preferences.json`
- `src/target-job-preferences.mjs`
- `src/backend-matching.mjs`
- `test/backend-discovery.test.mjs`
- `PHASE_B4_1_BACKEND_DISCOVERY.md`

Kết quả: `npm test` **345/345 PASS** (37 regressions mới), Chromium loopback
`npm run test:browser` **8/8 PASS**, YAML parse hai workflows + composite action
**3/3 PASS**, `git diff --check` **PASS**. Không network provider.
Giữ nguyên 11 evidence documents untracked từ trước; không staging.

## 8. Cách dùng và live validation đề xuất — CHƯA thực hiện

Trong policy JSON hiện có, dùng Portfolio snapshot đã export:

```js
{
  planQueries: true,
  portfolioSnapshot: snapshot,
  targetJobPreferences: JSON.parse(backendTargetPreferencesFile),
  target: 5
}
```

`backendTargetPreferencesFile` ở đây là nội dung file config, không phải một
filename mà workflow tự đọc. Chỉ truyền whitelist public data, không CV/contact/
secrets vào workflow input. Không cần tạo CLI/workflow mới.

Đề xuất tổng trần **2 Free credits**, tối đa **3** nếu operator đồng ý riêng:

1. Manual run mới: `.NET Backend Fresher Hà Nội`, max_requests=1, max_credits=1.
2. Manual run mới: `Python Backend Fresher Hà Nội`, max_requests=1, max_credits=1.
3. Tùy chọn: `FastAPI Backend Intern Hà Nội`, max_requests=1, max_credits=1.

Cả ba dùng cùng public Portfolio export, target preferences và date window được
operator chọn; không resume một query khác, không reset state/checkpoint.
Explicit override query qua input hiện có; max 1 request/run, không retry. Hai
manual runs khác query bảo đảm khảo sát cả .NET và Python, thay vì dùng cả hai
credits cho hai trang .NET. Kiểm tra có Free credits/no auto-top-up/no cash,
consent live + public artifacts trước từng run. Provider charge phải được xác
minh qua Credits-Deducted thực tế; không cộng Credits-Used vào chi phí. Unknown/
exceeded cost hoặc lỗi thì dừng theo guard hiện có, không tự rerun.

Đo accepted/rejected items, target/potential/fallback, required years status,
stack evidence, distinct opportunities, freshness UNKNOWN và raw preservation.
Không khẳng định sẽ tìm được việc: giới hạn coverage và thiếu chi tiết JD có
thể vẫn trả toàn NOT_MATCHED/INSUFFICIENT_DATA. Không suy đoán account balance,
không tự nâng quota hay bật schedule.

## 9. Giới hạn còn lại

Portfolio export vẫn do operator cung cấp, chưa tự tích hợp live production.
Deterministic role parser có false negatives khi responsibility/requirements
không được trích xuất rõ; ambiguous years hoặc framework evidence thiếu cần
review, không tự nới rule. Ranking trên registry có thể chứa JD cũ/UNKNOWN date;
không phải chứng nhận mọi opportunity còn mở. Query override/rotating/adaptive
do operator cung cấp có thể khác generated pool; operator chịu trách nhiệm chọn
Backend queries khi validation. Không triển khai frontend, backend API, database,
CV inference, AI classifier hoặc catalogue mới.
