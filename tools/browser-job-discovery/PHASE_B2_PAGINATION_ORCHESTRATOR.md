# Phase B.2 — Pagination orchestrator V1

## Phạm vi và trạng thái

Baseline: `main`, `99152d1`. Triển khai trong tool discovery và workflow thủ công;
không sửa backend, frontend, DB, Gmail, CV, application lifecycle hoặc provider billing.
Không gọi SocialAPIs thật, dispatch Actions, stage, commit hay push trong task này.

**OFFLINE_IMPLEMENTATION_VERIFIED**. Phân trang live và workflow mới trên GitHub
chưa được chạy: **LIVE_PAGINATION_UNVERIFIED / CLOUD_EXECUTION_UNVERIFIED**.
Không dùng replay làm bằng chứng tìm được tin mới hoặc tiêu thụ credits thật.

## Các file thay đổi

- `.github/workflows/socialapis-job-discovery.yml`: manual-only, input ngân sách,
  profile/policy, khôi phục journal mới và artifacts cũ, tối đa sáu slot tuần tự.
- `.github/actions/socialapis-discovery-page/action.yml`: claim → upload reservation
  → HTTP → upload result → xác nhận persistence. Không lặp/retry HTTP.
- `tools/browser-job-discovery/src/socialapis-adapter.mjs`: tham số `endCursor`,
  URLSearchParams `end_cursor`, đọc thêm terminal flag `has_next` nếu có.
- `tools/browser-job-discovery/src/pagination-orchestrator.mjs`: state machine,
  phân bổ 4-1-1, budget, matching theo profile, registry, metrics, cursor mã hóa.
- `tools/browser-job-discovery/src/pagination-cli.mjs`: các action của cùng CLI,
  atomic state write, validation và receipt gates.
- `tools/browser-job-discovery/src/cli.mjs`: dispatch `--orchestrate`; chặn legacy
  writer xóa journal mới. Legacy single-page/replay vẫn dùng được với state legacy
  hoặc output riêng; journal mới dùng orchestrator max_requests=1 nếu muốn một page.
- `tools/browser-job-discovery/test/pagination.test.mjs`: fixtures nhiều page,
  yield/dedup, budgets, crash/recovery và safety regressions.
- `tools/browser-job-discovery/test/workflow.test.mjs`: kiểm tra workflow/composite
  thực tế, script chọn artifact và CLI subprocess với transport giả/no-network.
- `tools/browser-job-discovery/package.json`: đưa tests pagination vào `npm test`.
- Báo cáo này.

Các tài liệu untracked có từ đầu trong `docs/job-discovery` không bị sửa.
Không thêm dependency, bảng dữ liệu hoặc catalogue công nghệ mới.

## Thuật toán và mặc định deterministic

1. Operator cung cấp query, date filters tùy chọn, `profile` và ngân sách.
   Không có profile .NET mặc định cho production. Query/date giữ nguyên giữa các
   core pages; date filters vẫn qua validator lịch hiện có.
2. Core bắt đầu page 1 không có cursor; response `data.page_info.cursor` hợp lệ
   trở thành `end_cursor` page sau, không gọi lại page 1 giả làm page 2.
3. Mỗi page dùng adapter/normalizer thật hiện có, JobPostExtractor, raw hash cũ,
   profile matching, sourceIdentity registry và báo cáo. Tối đa 200 items/page
   theo adapter hiện có; không áp nhầm browser candidate cap 20 vào provider page.
4. Mặc định target = **5 vacancy phù hợp mới**; minMarginal = **1**. Target tính
   theo `(sourceIdentity, vacancy index)` thuộc post chưa có lúc logical execution
   bắt đầu, không theo tổng jobs lịch sử. Matching dùng primary technology,
   seniority/location được extractor hỗ trợ. Bằng chứng quận Hà Nội đã xác minh
   bởi extractor được tái sử dụng, không bắt buộc địa chỉ chứa chữ “Hà Nội”.
5. Dừng sớm khi đạt target, hết request/credit budget. Dừng nhánh core khi cursor
   thiếu/invalid/lặp, `has_next=false`, page rỗng, marginal yield thấp hoặc đủ
   bốn core pages. Nếu còn cần kết quả, còn budget và operator cấu hình, chuyển
   tối đa một rotating query rồi tối đa một adaptive query (mỗi query một page).
6. Adaptive chỉ chọn query chưa dùng trong pool cấu hình có yield/credits đã
   biết trong history; xếp theo tổng new relevant vacancies / tổng deducted
   credits. Tie-break theo thứ tự chuỗi, không LLM. Không có evidence thì bỏ qua,
   không tiêu credits để “explore” một query tự bịa. History dùng cửa sổ 100 pages;
   raw jobs và request ledger không bị cắt hay reset.

Profile JSON là boundary để bước sau lấy dữ liệu technology từ Portfolio.
Hiện operator truyền profile, chưa có truy cập Portfolio production. Extractor
hiện hữu vẫn có catalogue/pattern ngôn ngữ hữu hạn; truyền tên technology chưa
được extractor nhận diện không tự tạo evidence. Đây không phải fit-scoring mới.

## Request budget và billing

- Development: max_requests/max_credits độc lập, số nguyên **1..6**, mặc định
  **1/1**; không có cap dispatch theo tháng/ngày thay cho allowance Free.
- Mỗi HTTP adapter instance chỉ được một attempt, tối đa 20 giây, redirect manual,
  không retry, không auto-pagination bên trong adapter. Orchestrator quyết định
  từng page sau khi checkpoint đã bền vững.
- CLI init in **maximumHttpRequests**, **maximumPermittedDeductedCredits** và
  worst-case contract exposure `min(max_requests,max_credits)` trước HTTP. Consent
  trên form áp dụng tới giới hạn operator chọn, không mặc định cho phép sáu lần.
- Trước mỗi request dự phòng một credit theo contract hiện tại. Sau response dùng
  riêng `X-SocialAPIs-Credits-Deducted`; số lẻ được cộng theo evidence. Thiếu header,
  cost vượt một credit/request, HTTP/provider lỗi hay timeout thì dừng an toàn.
  `X-SocialAPIs-Credits-Used` giữ nguyên ở field riêng, không cộng vào deducted.
- Không thể ngăn provider tự thay giá sau khi nhận request hoặc hoàn tác overcharge.
  Không giả định còn 200 credits. Operator phải kiểm tra balance Free và no-top-up
  trước run. Không có paid fallback hay request kiểm tra billing tự động.
- Future production: module có cap sáu **reservations/HTTP attempts mỗi ngày
  Asia/Ho_Chi_Minh**, kể cả attempt ambiguous, qua mọi workflow trong ledger.
  Reservation qua ngày không được gửi HTTP bằng accounting ngày cũ. Guard số dư
  tháng/zero-cash tin cậy là bắt buộc. CLI/workflow hiện **từ chối production mode**
  vì chưa có guard thực tế; schedule hoàn toàn không tồn tại. Không bật unattended
  chỉ vì operator có một boolean consent hoặc vì test guard trả true.

## Crash/recovery và artifacts

- State giữ tên `socialapis-discovery.json`, chấp nhận evidenceKind/jobs lịch sử
  và giữ metadata cũ. Không khởi tạo rỗng khi restore thiếu hoặc artifacts hết hạn.
- Mỗi slot có artifact riêng `socialapis-page-RUN-SLOT-claim/result` (retention 90
  ngày). Chỉ gọi HTTP khi upload claim trả artifact ID; chỉ cho page tiếp theo
  khi upload result trả artifact ID. Các upload không dùng `continue-on-error`.
- Claim ghi query/date/page, cursor hash và dự phòng usage trước HTTP. Nếu chết
  sau provider success nhưng trước result upload, artifact còn lại là claim:
  **AMBIGUOUS_PAGE_NO_AUTOMATIC_REFETCH**. Không tự kết luận request chưa xảy ra.
- Restore chọn slot cao nhất, result ưu tiên claim cùng slot; claim slot cao hơn
  luôn thắng result slot thấp. Latest artifact hết hạn thì dừng, không lùi state.
  Nếu chỉ có artifacts lịch sử thì dùng state/checkpoint cũ như trước.
- Result đã download là bằng chứng persistence, cho phép ack và resume kế hoạch
  cursor/config cũ; không replay page hoàn thành. Pending execution yêu cầu cùng
  policy/query/date/budget. Có thể cần operator đối soát provider khi ambiguous;
  task này không thêm nút reset hoặc tự động “bỏ qua” reservation.
- Cursor tiếp nối mã hóa AES-256-GCM bằng key dẫn xuất từ secret provider; artifacts
  và logs không có raw cursor. Metrics inputCursor là SHA-256 fingerprint. Đổi
  secret giữa pending pages làm fail closed, cần đối soát thủ công, không refetch.
- Atomic temp/rename, giới hạn state output **1 MiB** (reader cũ tối đa 10 MiB để
  nhận diện lịch sử). Vượt dung lượng dừng; nếu sau HTTP thì claim vẫn chặn retry.
  Tối đa 14 snapshots/run (init + 12 page snapshots + final), mỗi file <=1 MiB.
  Artifact không phải storage vĩnh viễn: cần backup có kiểm soát trước hết hạn;
  không tự xóa history để kéo dài dung lượng. Mất latest artifact đã bị xóa hoàn
  toàn ngoài hệ thống vẫn là rủi ro vận hành; không được tự chọn registry cũ.

## Metrics và freshness

- Per-page: query/date/allocation/page, inputCursor fingerprint, nextCursorPresent,
  totalItems/acceptedItems/rejectedItems/rejectionReasons, returnedPostIds của các
  post hợp lệ được normalize trong response page (không phải accumulated jobs),
  newlyInsertedIds, relevantVacancies, newRelevantVacancies, overlap, HTTP status,
  deducted/alternate credits, storage delta, outcome và receipt.
- returnedPostIds không bao gồm item bị reject; totalItems và rejectionReasons
  vẫn báo riêng toàn bộ batch. Trùng source identity không tạo job thứ hai.
- Per-workflow: actualRequests (null nếu chưa biết), knownActualRequests,
  reservedAttempts, uncertainAttempts, deducted totals (null khi unknown), stop
  reason, unique/new relevant vacancies, overlap và storage statistics. Có summary
  logical execution riêng khi resume qua workflow khác, tránh lẫn hai phạm vi.
- publishedAt=null giữ nguyên; UNKNOWN không biến thành verified recent. Date có
  mặt vẫn UNVERIFIED; verifiedRecentVacancies=0 khi chưa có kiểm chứng semantics
  ngày của provider. Target là useful new vacancies, không tuyên bố “5 tin mới
  trong 7 ngày” chỉ dựa vào query filter hoặc publication UNKNOWN.

## Kiểm chứng offline

- `npm test`: **258 passed, 0 failed, 0 skipped**.
- `npm run test:browser`: **8 passed, 0 failed, 0 skipped**, Chromium fixture
  regression qua Chrome local, không truy cập Facebook.
- Parse YAML workflow và composite action: **2 PASS**; `git diff --check`: **PASS**.
- Tests: nhiều cursor thực sự khác nhau; ba page-1 calls chỉ cho một identity so
  với ba pages cho ba identities; no/invalid/repeated/terminal cursor; rỗng; poor
  yield; early success; 4-1-1; adaptive history; request/credit exhaustion; missing
  cost; insufficient credits; overcharge; timeout; concurrent in-process call;
  claim/result crash boundaries; lịch sử; partial items; profile/secret safety.
- Replay capture local **18.567 bytes** qua orchestrator với transport giả: **8
  items, 8 accepted, 0 rejected, 4 relevant posts/4 relevant vacancies, 8 unknown
  publication dates, 0 verified recent**. Zero network và zero credits thật.
  Đây là replay response cũ, không phải bằng chứng live pagination mới. Không
  chép nội dung/contacts thật vào fixture hoặc báo cáo công khai.

## Hướng dẫn live validation có giới hạn hai page (CHƯA THỰC HIỆN)

1. Người dùng tự review/commit/push khi sẵn sàng; task này không làm việc đó.
   Backup latest discovery artifact; nếu đang có ambiguous claim thì đối soát
   trước, không reset/initialize registry. Không dùng Re-run jobs.
2. Kiểm tra Free plan, balance đủ **tối đa hai credits**, không auto-top-up/cash,
   GitHub artifact storage/billing. Secret `SOCIALAPIS_API_KEY` chỉ ở Actions.
3. Mở workflow thủ công trên main, confirm live/free/public-artifact consent.
   `initialize_state=false` với history có sẵn; `max_requests=2`, `max_credits=2`.
   Query ví dụ fixture: `tuyển fresher .NET Hà Nội`; date tùy operator, dạng
   YYYY-MM-DD hợp lệ. Policy test (không phải profile production cố định):

```json
{"profile":{"technologies":[".NET","C#","ASP.NET Core","Angular"],"locations":["Hanoi","Hà Nội"],"seniorities":["Intern","Fresher","Junior"]},"target":5,"minMarginal":1}
```

4. Không cấu hình rotating/adaptive cho lần thử này. Run in exposure trước HTTP.
   Chỉ có page 2 nếu page 1 còn thiếu target, có cursor hợp lệ và ít nhất một
   vacancy phù hợp mới; stop sớm sau một page là hợp lệ, không ép dùng credits.
5. Kiểm tra artifacts page 1/2, pageNumber và cursor fingerprint, returned vs
   inserted IDs, overlap và hai credit headers riêng biệt. Không công khai cursor,
   API key hoặc raw private response. Không retry run lỗi tự động.
6. Chỉ sau kết quả thật mới kết luận LIVE_PAGINATION_VERIFIED. Chưa có monthly
   balance/zero-cash guard đáng tin cậy thì vẫn không bật schedule production.
