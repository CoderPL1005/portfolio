# B.4.1 — Sửa matching và fallback

Baseline: `32e5285`. Chỉ sửa Backend matcher, regression tests và báo cáo này.
Không thay dữ liệu Portfolio, transport, ledger, pagination/cursor, workflow,
consent, budgets, Free-only safeguards hoặc scheduler.

## Root cause

1. Matcher chỉ so identity technology chính xác, nên ASP.NET Core USED không
   thỏa yêu cầu .NET tổng quát nếu snapshot không có record `.NET`. Chuỗi
   `C#.NET` còn bị xem như hai yêu cầu độc lập C# và .NET. Lexical overlap giữa
   tên tổng quát/cụ thể có thể tạo thêm yêu cầu không tồn tại.
2. `fallbackEligible = primary < target` nhầm discovery yield target với điều
   kiện cho phép fallback. Với target=5, 1–4 primary vẫn bật fallback.

## Bản sửa

- Compatibility **một chiều**: ASP.NET Core USED đáp ứng .NET tổng quát; không
  suy ngược .NET/C# USED thành ASP.NET Core hoặc mọi framework khác.
- `C#.NET` là một cách viết ecosystem .NET trong matcher, không yêu cầu hai
  record độc lập. `C# and .NET` vẫn là hai yêu cầu được nêu riêng.
- Ưu tiên token cụ thể dài hơn khi nhận diện requirements, tránh đếm nhiều tên
  chồng lấn. Không thay alias/catalogue hoặc thêm technology vào profile.
- LEARNING/EXPLORING không thành USED. Python/FastAPI vẫn giữ evidence độc lập;
  Python/FastAPI từ project vẫn được builder B.4 chứng minh như trước.
- Fallback chỉ eligible khi **distinct primary = 0**, không phụ thuộc target.
  Có ít nhất một primary thì defer fallback (tier 4); fallback vẫn nằm trong
  registry/derived view. Dedup diễn ra trước khi đếm primary.
- Không đổi primary yield hoặc điều kiện dừng discovery. UNKNOWN publication
  date vẫn UNKNOWN, không được coi là bằng chứng JD còn tuyển/gần đây.

Quy tắc `primary === 0` trong báo cáo này thay thế mô tả `< config.target` ở
báo cáo triển khai B.4.1 trước đó.

## Regression trước → sau

Fixture mới mô phỏng đúng cấu trúc Portfolio đã nêu: C#/ASP.NET Core USED,
không có technology `.NET` độc lập; Python LEARNING có project evidence USED.
Fixture không chứa dữ liệu cá nhân hoặc được tuyên bố là capture production.

| Trường hợp | Trước | Sau |
|---|---|---|
| Fresher .NET Backend Developer | POTENTIAL_MATCH | TARGET_MATCH |
| Fresher C#.NET Backend Developer | POTENTIAL_MATCH | TARGET_MATCH |
| Fresher ASP.NET Core Backend Developer | TARGET_MATCH | TARGET_MATCH |
| Fresher Python Backend Developer | TARGET_MATCH | TARGET_MATCH |
| Có 1–4 distinct primary, target=5 | Fallback bật | Fallback defer |
| Không có primary, có fallback hợp lệ | Fallback bật | Fallback vẫn bật |

Tests thêm: C# USED đơn thuần không chứng minh .NET; Core chỉ LEARNING/EXPLORING
không đạt TARGET .NET; generic .NET USED không chứng minh Core; Core không chứng
minh ASP.NET MVC; NET PARTNERS/lương NET/supporting tools không thành .NET;
Python/FastAPI giữ riêng mức evidence; legacy không truyền targetJobPreferences
giữ nguyên kết quả; repost dedup và fallback không biến mất khi bị defer.
Các test pipeline có sẵn tiếp tục chứng minh fallback không tăng primary yield
hoặc dừng sớm, và cursor resume/budget không đổi.

## Files thay đổi

- `src/backend-matching.mjs`
- `test/backend-discovery.test.mjs`
- `PHASE_B4_1_MATCHING_FALLBACK_CORRECTION.md`

## Kiểm chứng offline

- Focused Backend suite: **45 tests PASS**.
- `npm test`: **353/353 PASS**, không skip; tăng 8 tests so với baseline.
- `npm run test:browser`: **8/8 PASS**, Chromium với loopback fixtures.
- YAML parse: **3/3 PASS** (hai workflows discovery và composite page action).
- `git diff --check`: **PASS**.
- Không gọi SocialAPIs/API live, không dispatch Actions, không AI/Gmail,
  không đổi secrets/billing, không git add/commit/push.
- Giữ nguyên 11 evidence documents untracked có sẵn trước task.
