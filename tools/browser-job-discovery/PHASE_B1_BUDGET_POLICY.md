# Phase B.1 — Điều chỉnh ngân sách development

## Baseline và kết luận

Baseline xác minh: `696a6de` — `feat: add vacancy-aware job discovery and freshness preflight`. Tracked tree sạch khi bắt đầu; 11 tài liệu untracked có sẵn được giữ nguyên.

**Development policy:** cho phép các lần kiểm thử manual có kiểm soát sử dụng toàn bộ Free allowance thực tế của SocialAPIs. Không còn quota dispatch theo tháng/ngày do GitHub workflow áp đặt. Không cho phép paid upgrade, top-up hoặc chi phí tiền mặt.

Tài liệu này thay thế phần rolling-cap policy trong các báo cáo B.0/B.1 preflight trước đó; các báo cáo cũ là lịch sử, không phải policy development hiện hành.

## Files thay đổi

- `.github/workflows/socialapis-job-discovery.yml`: xóa MAX_31_DAY_REQUESTS và toàn bộ truy vấn/count/enforcement cửa sổ 31 ngày; đổi tên checkpoint step cho đúng mục đích development safeguards/state restoration.
- `tools/browser-job-discovery/test/workflow.test.mjs`: thay quota assertions cũ bằng kiểm chứng không phụ thuộc biến cũ, giữ state/security tests, bổ sung insufficient-credit/unknown-cost regression.
- Tài liệu này.

Không sửa CLI, provider transport, matcher/extractor, raw contentHash, source identity hoặc cơ chế merge/dedup. Không thay remote variable/secrets/billing; biến SOCIALAPIS_MAX_31_DAY_REQUESTS có thể vẫn tồn tại nhưng workflow không đọc nó.

## Giới hạn mỗi run vẫn giữ nguyên

- workflow_dispatch only; không schedule, retry hoặc automatic pagination.
- Chỉ main, ba explicit consent inputs mặc định false, public-repository check.
- SOCIALAPIS_MAX_REQUESTS=1, SOCIALAPIS_MAX_CREDITS=1; key chỉ ở live step.
- Không rerun workflow cũ: GITHUB_RUN_ATTEMPT khác 1 vẫn fail.
- Không có paid fallback, auto-purchase hoặc balance endpoint call.
- Node/runner/timeout, read-only GitHub permissions, npm test trước provider access không đổi.
- Concurrency group và cancel-in-progress=false giữ nguyên.

Không giả định còn 200 credits hoặc bất kỳ số dư cố định nào. Giới hạn một credit là upper bound cấu hình hiện tại, không phải giả định mọi response tốn đúng một credit. Usage thiếu bằng chứng vẫn null/fail closed; không cộng header Credits-Used vào Credits-Deducted.

## Checkpoint và state safety

Xóa rolling quota không xóa truy vấn history dùng để tìm checkpoint. `listWorkflowRuns` hiện chỉ đọc latest runs của đúng workflow/main/workflow_dispatch, không dùng created-date filter hoặc tính dispatch quota.

`per_page=2` chỉ nhằm chọn current/prior run, **không phải cap hai requests**. `total_count>1` trong first-initialization guard chỉ ngăn reset history thành state rỗng, không chặn development runs khi có checkpoint hợp lệ. Test dùng total_count=10000 vẫn restore được.

Run trước phải completed; conclusion=failure không tự cấm restore:

1. Có artifact `socialapis-discovery-state` còn hiệu lực: chọn state.
2. Chỉ có `socialapis-discovery-checkpoint` còn hiệu lực: restore pre-request checkpoint.
3. Thiếu/hết hạn artifact: STOP `CHECKPOINT_MISSING_OR_EXPIRED...`; không tự initialize hoặc âm thầm lùi về artifact cũ hơn có thể mất dữ liệu.

Download, validate/prepare state và upload pre-request checkpoint vẫn xảy ra trước provider access. Artifact retention 90 ngày, size limits và đường dẫn không đổi. Không thêm artifact/persistence backend.

Nếu một manual run thất bại **trước khi có bất kỳ checkpoint artifact nào**, run sau có thể vẫn bị chặn do thiếu checkpoint của run gần nhất. Đây là fail-closed state protection, không phải quota: cần khôi phục backup đã xác minh theo một thao tác recovery riêng được review. Không dùng initialize_state=true để vượt guard. Không scan/skip history một cách không có bằng chứng.

Không reset historical jobs. Khi provider báo lỗi, CLI vẫn giữ jobs/source identity/hash trong checkpoint. Metadata report cũ không được biến thành thành công của request lỗi; pre-request checkpoint là state phục hồi cho run tiếp theo.

## Provider budget/error behavior đã kiểm chứng

Tests chạy workflow/CLI/adapter thật với transport giả và socket guard, không gọi provider:

- HTTP 402 với insufficient-credit payload: dừng PROVIDER_HTTP_REJECTED.
- HTTP 200 có structured provider error INSUFFICIENT_CREDITS: dừng PROVIDER_ERROR.
- HTTP 200 không có Credits-Deducted, chỉ có Credits-Used: dừng PROVIDER_CREDIT_COST_UNVERIFIED_OR_EXCEEDED, không giả định cost=1.
- Mỗi trường hợp chỉ có một mock request, không retry, historical jobs và contentHash không đổi. Không log key hoặc nội dung JD.

Đây là fake response fixtures, không phải xác nhận response schema thiếu credits hoặc số dư của tài khoản thật. Envelope chưa từng quan sát vẫn phải được đối chiếu với lỗi thực tế; không tuyên bố mọi provider error shape đã được live-verified.

Không có provider balance integration trong phase này. Free/no-cash consent là xác nhận của người vận hành, không phải kiểm tra billing tự động. Giới hạn client kiểm tra cost sau response không chứng minh tài khoản không bật auto-top-up; phải xác nhận điều kiện tài khoản trước run.

## Verification

- Focused workflow tests: **7/7 PASS**.
- `npm test`: **234/234 PASS**, 0 skipped/failed (baseline 233).
- `npm run test:browser`: **8/8 PASS**, chỉ Chromium local/loopback fixtures.
- YAML parsing, JavaScript syntax và `git diff --check`: PASS.

Tests xác minh biến quota cũ missing/empty/invalid không block run; lịch sử nhiều dispatch vẫn restore; thiếu/hết hạn checkpoint, reset history, private repository và rerun vẫn bị chặn. Các tests date inputs, Bash quoting, persisted reports, một key-scoped step và limits=1 vẫn PASS.

Không gọi SocialAPIs, Account Usage, GitHub Actions hoặc dịch vụ ngoài. Các GitHub APIs trong tests là mocks, fetch transport là fake, không dùng key thật. Không gọi Gmail/AI/R2, không sửa production.

## Hướng dẫn live validation an toàn — CHƯA THỰC HIỆN

1. Review/commit/push thay đổi bằng một quyết định riêng. Task này không thực hiện các thao tác đó hoặc dispatch workflow.
2. Kiểm tra Free allowance **thực tế** còn đủ cho một request theo contract hiện hành; xác nhận không paid plan/top-up/auto-top-up/phát sinh tiền mặt. Không cần chỉnh biến quota GitHub cũ.
3. Xác minh checkpoint/backup gần nhất, retention và GitHub artifact storage còn phù hợp. Không reset history hoặc xóa state để thử lại.
4. Trên main, tạo một workflow_dispatch mới, chọn query và optional dates; đọc và xác nhận cả ba consent inputs. initialize_state chỉ dùng lần đầu thật sự chưa có history.
5. Run chỉ được gửi tối đa một provider request. Không dùng Re-run jobs. Sau lỗi, đọc error/providerUsage và checkpoint trước khi cân nhắc một manual dispatch mới.
6. Nếu insufficient credits hoặc unknown/excessive cost: STOP; không mua credits/nâng gói để tiếp tục. Chờ Free allowance phù hợp hoặc quyết định chính sách riêng, không tự triển khai paid fallback.
7. Review queryYield, separate credit headers và freshness evidence. Date filters không chứng minh ngày đăng; không gán capture UNKNOWN thành verified-recent.

Cho phép dùng hết Free allowance không có nghĩa có quyền truy cập dữ liệu không giới hạn, hoặc được bỏ qua consent, state safety, provider terms và các điều kiện $0 của GitHub artifacts.

## Chính sách production tương lai — CHỈ TÀI LIỆU

Định hướng: **4 core queries/ngày + 1 rotating query/ngày + 1 adaptive query/ngày = tối đa 6 requests/ngày** khi có production policy được phê duyệt.

Không kích hoạt giới hạn này trong workflow development. Daily scheduler, adaptive allocation, query-performance tracking và provider balance integration thuộc phase tiếp theo. Không thêm cron, orchestration, retries hoặc pagination trong task hiện tại.

## Giới hạn còn lại

Chưa kiểm chứng live tài khoản/billing/Free balance, provider date-filter semantics hoặc cloud execution của policy mới. Missing checkpoint vẫn cần recovery có chủ ý. Nếu provider đã trả dữ liệu nhưng post-request artifact upload thất bại, pre-request checkpoint không chứa các kết quả chưa persist; không coi một run lỗi là bằng chứng dữ liệu đã được lưu. Đây là giới hạn workflow hiện hữu, không được che bằng reset hoặc auto-retry.

Không stage/commit/push, trigger Actions, đổi remote settings hoặc thực hiện provider request.
