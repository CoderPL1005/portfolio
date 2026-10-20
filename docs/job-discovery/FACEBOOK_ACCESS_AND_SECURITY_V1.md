# Facebook Access and Security V1

Ngày review: 2026-10-20.

## 1. Gates độc lập

| Gate | Bằng chứng cần | Hiện tại |
|---|---|---|
| BROWSER_TECHNICALLY_READY | Browser khởi động và xử lý các thao tác được phép | PASS local fixture |
| LOGIN_SESSION_READY | Người dùng login thành công trong authorized secure context, session chưa expire | UNVERIFIED |
| AUTOMATED_COLLECTION_AUTHORIZED | Bằng chứng permission đúng scope/nguồn/cách thu thập, còn hiệu lực | UNVERIFIED; denied by default |
| FACEBOOK_LIVE_DISCOVERY_VERIFIED | Unseeded queries thật, accessible posts, provenance và target evaluation | NOT_RUN |

Không có suy luận technical -> login -> authorization -> live success. Không thể set `authorized=true` từ CLI để mở khóa.

Meta công bố automated collection cần permission; nội dung có thể xem công khai không tự đủ quyền cho automation ([How We Combat Scraping](https://about.fb.com/news/2021/04/how-we-combat-scraping/)). Trang terms hiện tại [facebook.com/terms](https://www.facebook.com/terms/) redirect login trong research nên không claim đã đọc đầy đủ current policy text. Bằng chứng authorization cụ thể cho use case này vẫn thiếu.

## 2. Fail-closed implementation

`requireMode()` từ chối mọi mode ngoài offline. CLI chỉ nhận output path; không có live target/session/profile/password/cookie flags. Gate runtime luôn `allowed=false`, `FACEBOOK_ACCESS_UNVERIFIED`, `LIVE_UNSEEDED_DISCOVERY_NOT_RUN`.

Ngay cả khi một test gọi gate với technical/session readiness true và caller-supplied authorization flag, allowed vẫn false. Không có Facebook adapter đăng ký trong registry/production DI.

Đường browser offline chỉ tới `http://127.0.0.1:{fixture-port}` và exact route allowlist. External fetch/navigation bị route abort trước HTTP target; unit/browser tests kiểm chứng. Tests có URL Facebook để kiểm tra blocking, không dùng URL đó để collect bài thật. Service workers và WebSocket bị block. Fixture CSP chặn scripts/connect/frame và chỉ cho ảnh/form cùng origin.

## 3. Credentials và logs

- Không đọc browser profile cá nhân hoặc Windows cookie store.
- Không có `storageState()` export, persistent userDataDir, cookie jar serialization hoặc authenticated session artifact.
- Synthetic cookie trong browser test chỉ dùng chứng minh context mới không giữ state; không phải credential thật và không ghi JSON report.
- Browser errors chuyển thành safe Stop code; không log exception chi tiết chứa URL/HTML.
- Không screenshots/tracing/video/recordings; output chỉ dữ liệu fixture và safe counters.
- `output/`, `.browser-cache/`, node_modules gitignored.
- Browser temp profile do Playwright quản lý, đóng ở cleanup; không giữ reusable logged-in state.

## 4. Access controls

Adapter kiểm tra login form/password, login path/text, CAPTCHA/denied marker và checkpoint path sau navigation và sau search. Stop codes:

- `LOGIN_REQUIRED`
- `ACCESS_RESTRICTED`
- `NAVIGATION_FAILED`
- `TIME_BUDGET_EXCEEDED`
- `SEARCHES_BUDGET_EXCEEDED`
- `CANDIDATES_BUDGET_EXCEEDED`
- `SESSION_BUDGET_EXCEEDED` / `SESSION_ALREADY_ACTIVE`

Không retry login, solve CAPTCHA, alter fingerprint, đổi proxy hoặc tìm alternate endpoint để tránh restriction. Detection hiện chỉ verified trên fixtures; chưa có claim coverage mọi live Facebook screen.

## 5. Data boundaries

DTMED fixture chỉ giữ dữ kiện user-provided, không bổ sung location từ candidate website trước. Synthetic image không OCR. Unknown fields giữ null/blank và reason codes; không tạo normalized JobPosting thiếu trường bắt buộc.

Dedup trong tool là identity/content exact; hash format theo normalization conventions đã audit nhưng không gọi internal production JobHashing. No cross-provider fuzzy merge; không tự nhận hai posts cùng employer là một job.

Report có `eligibleForProductionIngestion=false` cho tất cả rows. Không có network/persistence client tới Render/Neon/R2/Gemini/Gmail. Không gọi existing screenshot analyzer hoặc fit workflow. Không có apply/comment/like/follow/message behavior.

## 6. Verification đã chạy

- 52 unit/workflow tests PASS: planning, experience bounds, missing evidence, matching, dedup, budget counters/deadline, access/login stops và fail-closed gate.
- 7 browser tests PASS: full fixture flow, login/checkpoint/navigation failure, external-request block, one-session lease, cookie không persist, candidate checkpoint stop và deadline đóng browser đang chờ.
- Simulation JSON thành công, 1 target synthetic và 0 live JD.
- Workflow YAML parse/test PASS: dispatch only, read permissions, timeout/concurrency, no artifacts/secrets.

Chưa có cloud/Facebook/persistent-session verification. Không thể quy đổi test PASS thành permission.

## 7. Remaining blockers

1. Authorization chưa có: giữ **FACEBOOK_ACCESS_UNVERIFIED** và live NOT_RUN.
2. User-login cloud flow/context vault chưa implement hoặc verify.
3. Cloud account/free entitlement và no-overage chưa verify.
4. Live DOM extraction chưa implement; image extraction chưa có offline capability.
5. Workflow mới chưa được đưa lên remote; không thể execute current uncommitted workflow với constraint no-push.

Không yêu cầu gửi password/cookie/token vào chat. Không tạo dịch vụ trả phí, không ghi production database và không commit/push.
