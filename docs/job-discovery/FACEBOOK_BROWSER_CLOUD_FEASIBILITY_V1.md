# Facebook Browser Cloud Feasibility V1

Ngày research: 2026-10-20. Không tạo provider account/resource, không chạy live Facebook.

## 1. Verdict

**CLOUD_BROWSER_RUNTIME_UNVERIFIED**, **PERSISTENT_SESSION_UNVERIFIED**, **ZERO_COST_UNVERIFIED**.

Local Chromium fixture pipeline PASS không chứng minh GitHub-hosted execution, cloud authenticated session hoặc free billing dài hạn. Chi phí dịch vụ thực tế của task này bằng $0; không thêm payment method.

## 2. GitHub Actions standard runner

| Khía cạnh | Evidence và kết luận |
|---|---|
| Browser support | Playwright có hướng dẫn chính thức cài Chromium/deps và chạy trên Actions: [CI](https://playwright.dev/docs/ci). |
| Chạy khi máy cá nhân tắt | Có về kiến trúc GitHub-hosted runner; chưa execute run hiện tại. |
| Cost | Standard runners cho public repo miễn phí; private repo phụ thuộc included minutes/storage và có thể có charges. Chưa kiểm tra account/budget: [GitHub billing](https://docs.github.com/en/actions/concepts/billing-and-usage). |
| Session persistence | Runner ephemeral; không có authenticated Facebook state qua runs. V1 chủ động không lưu state. |
| Login | Headless runner không cung cấp human interactive login được kiểm chứng. Không lấy cookie từ laptop. |
| Security | No secrets input, no session artifact/cache/profile. Traces/videos/screenshots không bật. |
| Limits | Workflow timeout 10 phút; core 5 searches/20 candidates/10 phút/1 session; install nằm trong workflow timeout. |
| Terms | Runtime availability không cấp Facebook collection permission. Account vẫn chịu GitHub terms và target-site rules. |

Workflow mới: `.github/workflows/browser-discovery-offline.yml`.

- Chỉ `workflow_dispatch`; không schedule/push/PR trigger.
- `contents: read`; checkout `persist-credentials: false`.
- Concurrency một workflow; không matrix nhiều sessions.
- Node 22.12.0; package-lock pinned; npm ci với install scripts disabled.
- Cài browser, chạy 52 unit/config và 7 real-browser fixture tests, sinh synthetic report.
- Không upload artifacts hoặc save authenticated profile.

Không trigger vì file mới chỉ ở working tree và user cấm push. Sau một bước commit/push riêng được cho phép, một manual fixture-only run có thể xác minh browser startup/runtime mà không cần Facebook credential. Không gửi cookie/token vào workflow inputs.

## 3. Cloud browser với persistent authenticated sessions

### Browserbase

[Pricing](https://www.browserbase.com/pricing) công bố Free $0, 1 browser hour, 15 phút/session. [Contexts](https://docs.browserbase.com/platform/browser/core-features/contexts) mô tả profile persistence và encryption-at-rest; context không tự expire nhưng website vẫn có thể logout. Chưa xác minh entitlement của persistent contexts trên Free/account thực hoặc hard no-overage/no-card boundary.

[Terms](https://www.browserbase.com/terms-of-service) yêu cầu use hợp pháp/đúng chức năng; session recording có thể giữ dữ liệu nhạy cảm. Không tạo account, Context hoặc session. Verdict: technical mechanism có tài liệu, **$0 persistent Facebook operation UNVERIFIED**.

### Browserless

[Pricing](https://cloud.browserless.io/pricing) công bố Free không cần card, 1.000 units/tháng, session tối đa 2 phút và persisted state tối đa 1 ngày. [Unit consumption](https://docs.browserless.io/overview/unit-consumption) mô tả free quota hết thì reject, không dùng paid overage. [Persisting state](https://docs.browserless.io/browserql/session-management/persisting-state) mô tả TTL theo plan.

Giới hạn 2 phút thấp hơn activity budget 10 phút của PoC; có thể dùng run ngắn hơn nhưng không bảo đảm session đăng nhập lâu dài. [Terms](https://www.browserless.io/terms-of-service) cần được tuân thủ cùng Facebook authorization. Không dùng các feature stealth/CAPTCHA solving/proxy được quảng cáo; task không cho phép chúng. Không tạo token/account/session. Verdict: **UNVERIFIED** cho persistent authenticated $0 end-to-end.

## 4. Security của future manual login

Nếu sau này có authorization phù hợp và được duyệt thiết kế:

1. Người dùng mở remote browser qua authenticated one-time viewer; tự nhập password/MFA trực tiếp.
2. Không truyền password/cookie/token cho agent, prompt, log hoặc artifact.
3. Profile lưu trong provider-controlled encrypted context, ACL một user; tắt/redact recordings và review retention trước login.
4. Workload chỉ nhận opaque context reference qua secret store; không URL có bearer token trong logs.
5. Một session lease; checkpoint/logout stop ngay và yêu cầu manual recovery, không automatic bypass.
6. Có revoke/delete context, expiry policy và audit access.

Đây là thiết kế tương lai, không có provider implementation trong V1. Persistent cookies trong vault vẫn là credentials; không coi chúng an toàn chỉ vì không nằm trong repository.

## 5. Cost decision

- **GO** cho local offline simulator không có dịch vụ tính phí.
- **GO có điều kiện** cho GitHub fixture smoke sau khi có quyền đưa workflow lên remote và kiểm tra free runner entitlement.
- **NO_GO hiện tại** cho lời hứa Facebook browser discovery cloud persistent $0 khi user devices tắt: authorization, persistent login/account entitlement và runtime live chưa được verify.
- Không dùng free trial có billing fallback hoặc thêm card để vượt quota. Hết quota thì stop.

Các con số là tài liệu provider tại ngày research; không phải invoice/account verification. Không có paid resource được tạo.
