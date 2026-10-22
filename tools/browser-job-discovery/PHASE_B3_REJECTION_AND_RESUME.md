# B.3 — Rejection diagnostics và cross-workflow pagination resume

Baseline kiểm tra: `bedff49`. Chỉ implementation local và offline verification.
Không gọi SocialAPIs/Facebook, dispatch Actions, đổi secrets/billing, stage,
commit, push hoặc bật schedule. Giữ nguyên 11 evidence files untracked ban đầu.

## 1. Điều tra `POST_ID_URL_MISMATCH`

**ROOT_CAUSE_UNVERIFIED cho 8 items Run #7.** Không có raw response hoặc artifact
Run #7 trong dữ liệu local đã tìm thấy. Không lấy lại dữ liệu qua API/GitHub.

Capture local `.debug/socialapis-response.json` là response cũ **18.567 bytes**,
8 items: **8 accepted, 0 rejected**, cả 8 là GROUP_POST. State local còn lại là
legacy replay 8 jobs, không có orchestration/cursor/Run #7 metadata. Vì vậy không
được dùng chúng để tuyên bố giải thích được 8 mismatch trong batch 11 items mới.

Trace source thực tế:

1. `normalizeSocialApisItem()` đọc `basic_info.post_id`, `post_text`, `url` cho
   nested item; flat compatibility dùng `post_id` và các URL fields đã hỗ trợ.
2. ID hợp lệ phải là chuỗi số hoặc opaque `pfbid...`. Không ép number thành ID,
   không tách/đoán composite `owner_post`, không coi numeric ID tương đương pfbid.
3. `canonicalFacebookPost()` nhận dạng Group posts/permalink, Page posts,
   story.php/permalink.php với đúng một `story_fbid` và một owner `id`. Với query
   permalink, post ID là `story_fbid`, không phải owner `id`.
4. Adapter chỉ phát `POST_ID_URL_MISMATCH` sau khi provider ID hợp lệ và ít nhất
   một URL được canonicalize ra post ID **khác theo phép so sánh chuỗi**. Với flat
   envelope, nhiều URL fields xung đột cũng bị reject dù có một URL trùng ID.
5. URL chưa hỗ trợ đơn thuần sẽ bị `POST_URL_INVALID_OR_UNSUPPORTED`, không phải
   `POST_ID_URL_MISMATCH`. Do đó nguyên nhân đã thu hẹp: cần cặp ID thực tế để phân
   biệt provider bất nhất với hai representation chưa chứng minh cùng một post.

Không tìm được bằng chứng đủ để mở rộng/relax normalization. **Giữ nguyên** các
kiểm tra ID/URL và grammar canonical hiện hành; không tự tạo permalink/alias.
Các URL variants trong regression là grammar đã có và test offline, không được
gọi là các variants đã xác minh live từ Run #7. Không tuyên bố coverage tăng khi
chưa biết vì sao từng item bị reject.

### Diagnostics cho runs tương lai

Artifact consented lưu `rejectionDiagnostics` cho từng item bị reject:

- Page number, item index và reason.
- URL field name kỹ thuật, enum loại URL/path, canonicalSupported và cờ query
  parameters trùng/ambiguous. Không lưu nguyên URL/path hoặc query values.
- Provider ID type/kind và SHA-256 fingerprint; fingerprint ID canonical lấy từ
  URL; boolean so sánh ID. Không lưu ID gốc vào diagnostics.
- `DIFFERENT_POST_IDS`, `DIFFERENT_ID_REPRESENTATIONS_UNVERIFIED`,
  `CONFLICTING_URL_FIELDS`, `MISSING_PROVIDER_ID`, `MALFORMED_PROVIDER_ID` hoặc
  `NOT_AN_ID_MISMATCH`; mapping decision luôn là reject với reason cụ thể.

Page có items hợp lệ vẫn tiếp tục và chỉ persist candidates hợp lệ như trước.
Nếu tất cả items invalid, giữ fail-closed `NO_VALID_PROVIDER_ITEMS`; diagnostics
được giữ trong failed attempt của `requestLedger`, không biến thành page thành
công hay tự refetch. Không mất evidence chỉ vì adapter từ chối cả page.
Diagnostics không được đưa vào console summary; không chứa post text, contact,
API key, cookie hoặc cursor thô. Cận 200 items/page, tối đa 4 URL fields/item và
giới hạn artifact 1 MiB vẫn áp dụng; không truncate âm thầm khi quá dung lượng.

## 2. Resume: nguyên nhân và thay đổi

Ở baseline, `chooseNext()` kiểm tra request/credit budget **trước khi** mã hóa
cursor từ response. Sau page 2 dùng hết hai requests, `plan` vẫn là input page 2;
`nextCursorPresent=true` không đủ để dựng cursor input page 3.

Cơ chế mới:

1. Sau page thành công, xác định next core plan hợp lệ trước budget stop, mã hóa
   cursor AES-256-GCM như trước. Lưu cả plan, page kế tiếp, fingerprint, savedAt,
   expiresAt và schemaVersion=3 trong checkpoint; không log cursor thô.
2. `status` độc lập với crash-safe `phase`: `RUNNING`, `STOPPED_RESUMABLE` hoặc
   `STOPPED_TERMINAL`. Budget stop chỉ resumable khi có next core plan an toàn.
   Đạt target, terminal/missing/repeated cursor, đủ 4 core pages hoặc lỗi không
   tạo continuation có thể resume.
3. Workflow mới yêu cầu input **`resume_continuation=true`**. Không có continuation,
   policy khác hoặc checkpoint không an toàn thì fail closed, không bắt đầu page
   1 hoặc chuyển query. Không bật resume bằng input bị gõ sai trong CLI.
4. Query/date/profile và policy khác phải khớp (object key order không quan trọng;
   array order vẫn phải khớp). Cho phép đổi **maxRequests/maxCredits**, mỗi giá trị
   1..6, vì chúng thuộc workflow mới. Target, rotating/adaptive pool không đổi.
5. Cursor phải đúng page tiếp theo của core page vừa hoàn thành, còn trong trần
   page 4, fingerprint khớp và chưa được dùng làm input trước đó. Checkpoints cần
   đủ receipts/usage đã xác nhận. Ambiguous/IN_FLIGHT/COMPLETED_UNCONFIRMED bị chặn.
6. `init` chỉ ack kết quả đã download bằng artifact receipt thực tế như trước.
   Structural cursor checks/policy checks tại init; giải mã, xác thực GCM và đối
   chiếu fingerprint xảy ra **trước transport** ở page step. Không mở rộng scope
   API key sang init hoặc tests. Key đổi/ciphertext hỏng: zero provider calls.
7. Local continuation TTL **24 giờ**, là safety bound của tool, **không phải** bằng
   chứng provider giữ cursor 24 giờ. Cursor có thể hết hạn sớm hơn; khi provider
   từ chối, dừng không retry. Core/policy/protection không được nới lỏng vì TTL.

Core pages 1–4 là một chuỗi xuyên workflow; không reset numbering, used cursor
hashes, initial identities, pages, jobs/content hashes, dedup registry hoặc yield
history. Không có automatic resume. Không triển khai resume rotating/adaptive
qua workflow mới: continuation hiện chỉ dành cho next CORE page an toàn.

### Run #7 compatibility

**LEGACY_CONTINUATION_CURSOR_UNAVAILABLE** nếu checkpoint không có continuation
schema mới. Không lấy cursor input page 2 giả làm input page 3; không dựng lại
cursor từ fingerprint hoặc `nextCursorPresent`.

Chưa có actual artifact Run #7 để kiểm tra contents. Theo code baseline nếu Run
#7 kết thúc ở budget sau page 2 thì không lưu cursor page 3. Không tuyên bố có
thể resume Run #7. Chỉ workflow mới lưu continuation đầy đủ mới được resume.
Legacy jobs/state vẫn đọc được và giữ lại khi operator chủ động chạy một logical
execution mới với resume=false; điều đó là fresh search, **không** là resume.

## 3. Metrics và cost

- `workflowRunId` đổi khi resume; `logicalExecutionId` giữ nguyên xuyên chuỗi.
- Mỗi page/attempt có pageNumber và inputCursorFingerprint; giữ field inputCursor
  cũ (cũng là fingerprint) để tương thích. Next cursor có fingerprint/present và
  continuationAvailable; không đưa ciphertext hoặc token vào console metrics.
- Giữ item totals, rejectionReasons, returnedPostIds **của page**, newlyInsertedIds,
  newRelevantVacancies, actualRequests và deducted credits. Không lấy state.jobs
  tích lũy làm số results của một page.
- Workflow budget tính theo requestLedger entries của workflowRunId hiện tại,
  bao gồm reservations/failed/ambiguous attempts; không theo số workflows hoặc
  tổng pages của logical execution. Unknown usage vẫn unknown, không coi là zero.
- Summary có actualRequests/totalDeductedCredits riêng workflow; logicalExecution
  có completedPages, actualRequests và creditsDeducted cộng xuyên workflows,
  bao gồm pending/failed attempt khi cần. Historical ledger không bị xóa.
- `Credits-Used` vẫn field riêng, không cộng hoặc thay `Credits-Deducted`.
- Max 6 requests/workflow, max 4 core pages/logical chain; no retry, manual-only,
  explicit Free/no-cash/public-artifact consent, max cost announcement trước HTTP,
  production monthly-balance gate và scheduler OFF giữ nguyên.

## 4. Files thay đổi

- `.github/workflows/socialapis-job-discovery.yml`: thêm input/env resume, không
  thêm schedule, secrets hay provider calls.
- `src/socialapis-live-smoke.mjs`: diagnostic helper chỉ dùng technical enums và
  fingerprints; normalizer/acceptance rules không đổi.
- `src/socialapis-adapter.mjs`: thu thập và chuyển tiếp rejection diagnostics.
- `src/pagination-orchestrator.mjs`: continuation/status, per-workflow budget,
  logical metrics, validity checks và lưu diagnostics vào page/failed attempt.
- `src/pagination-cli.mjs`: nối resume flag, fail closed và giữ atomic writes.
- `test/pagination.test.mjs`: URL pair/mismatch tests; page 3 resume, budgets,
  corrupted/expired cursor, incompatible policy, ambiguous/legacy và core cap.
- `test/workflow.test.mjs`: input safety và subprocess CLI E2E qua checkpoint
  thật trên đĩa với fake transport/network guard; không chỉ mock expected result.
- Báo cáo này. Các đường dẫn `src/test` trên thuộc `tools/browser-job-discovery`.

## 5. Offline verification

- `npm test`: **277 passed, 0 failed, 0 skipped**.
- `npm run test:browser`: **8 passed, 0 failed, 0 skipped**, Chrome/Chromium local.
- YAML parse workflow và composite action (unique keys): **2 PASS**.
- `git diff --check`: **PASS**.

Tất cả requests trong tests là mock/loopback; không tính synthetic header một
credit là credits thật đã dùng. **OFFLINE_PASS**, live/cloud B.3 chưa chạy.

Các trường hợp trọng yếu đã kiểm thử: valid Group/Page/story/permalink/pfbid ID
pairs; mismatches thật vẫn reject; malformed/missing ID; expired/corrupt/missing/
repeated continuation; query/date/profile/target mismatch không mutation; receipt
thiếu và ambiguous phase bị chặn; page 2 hết budget giữ cursor page 3; workflow
mới gửi đúng cursor page 3, không gửi lại page 1–2; không vượt core page 4; chi phí
workflow mới 1/2 requests khác tổng logical 3/4 requests; legacy Run #7-style state
không được nâng thành continuation giả; cả batch invalid vẫn có diagnostics.

## 6. Live validation tối đa 2 credits — CHƯA THỰC HIỆN

Sau khi người dùng tự review và đưa workflow mới lên remote khi sẵn sàng:

1. Kiểm tra account vẫn Free, đủ tối đa 2 credits, no-auto-top-up/no-cash và GitHub
   artifact storage/billing; xác nhận public artifact consent. Không dùng Re-run.
2. Không chọn resume từ artifact Run #7 thiếu continuation. Chạy workflow mới
   thủ công `resume_continuation=false`, initialize_state=false với history có
   sẵn, max_requests=1, max_credits=1; query/date/profile do operator cung cấp.
3. Nếu page 1 có next cursor, chưa đạt target và checkpoint đã persist/confirm,
   sẽ thấy STOPPED_RESUMABLE. Trong 24 giờ, chạy workflow mới với cùng query/date/
   profile/policy, `resume_continuation=true`, max_requests=1, max_credits=1.
   Tổng hai workflows **tối đa 2 HTTP requests/2 contract credits**, không ép dùng
   credit thứ hai nếu không có continuation an toàn. Không thêm retry/pagination
   ngoài các bước orchestrator đã kiểm soát.
4. Kiểm tra logicalExecutionId giống nhau, workflowRunId khác nhau, pageNumber
   tăng 1→2, input fingerprint khớp next fingerprint page trước; registry không
   duplicate. Xem rejectionDiagnostics trong artifact để phân loại mismatches.
   Không công khai post text, contacts, secrets hoặc cursor.
5. Nếu đã có checkpoint **schema mới, đã xác nhận**, page 2 budget-stopped và
   cursor page 3 chưa hết hạn: có thể thay hai bước trên bằng **một** workflow
   resume=true/max_requests=2/max_credits=2 để kiểm thử pages 3–4. Không áp dụng
   cách này cho legacy Run #7 chỉ có boolean nextCursorPresent.

Chưa có live/cloud execution của code B.3; không tuyên bố provider cursor còn
hiệu lực hoặc 8 mismatch đã được sửa chỉ từ offline tests. Technology profile
vẫn được inject qua boundary hiện có; không hardcode .NET thành production profile.
