# Phase B.1 Preflight — Cloud workflow integration

## Kết luận và phạm vi

**OFFLINE PREFLIGHT PASS** trên working tree Phase A/A.1/A.1.1/B.0 hiện tại. Chưa thực hiện live validation hoặc xác nhận cloud execution. Chỉ thay đổi:

1. `.github/workflows/socialapis-job-discovery.yml`.
2. `tools/browser-job-discovery/test/workflow.test.mjs`.
3. Tài liệu này.

Không sửa matcher, extraction, contentHash, source identity, CLI/provider transport hoặc persistence của B.0. Các thay đổi local trước đó được giữ nguyên. Gap workflow trong báo cáo B.0 được giải quyết **ở source local**, không có nghĩa workflow remote đã cập nhật.

## Inputs trước/sau

Trước: workflow có query, ba consent inputs và initialize_state; chưa có date inputs.

Sau: thêm `start_time`, `end_time`, mỗi input có type string/default rỗng. Query default vẫn `tuyển fresher .NET Hà Nội`; các consent/defaults hiện có không đổi. Trigger duy nhất vẫn workflow_dispatch, không có schedule.

Giá trị đi qua environment `DISCOVERY_START_TIME`, `DISCOVERY_END_TIME`, cùng `DISCOVERY_QUERY`. Bash step dùng array:

```bash
args=(src/cli.mjs --source socialapis --live --query "$DISCOVERY_QUERY")
if [ -n "$DISCOVERY_START_TIME" ]; then
  args+=(--start-time "$DISCOVERY_START_TIME")
fi
if [ -n "$DISCOVERY_END_TIME" ]; then
  args+=(--end-time "$DISCOVERY_END_TIME")
fi
node "${args[@]}"
```

Không có GitHub input expression trong shell source. Quote/Unicode/C# hoặc chuỗi giống command substitution được chuyển nguyên thành argument, không được eval. Input rỗng không tạo date flag; không chọn ngày mặc định. CLI hiện có kiểm tra YYYY-MM-DD, lịch/ngày nhuận và range, trước mọi provider transport. Invalid date không bị âm thầm sửa hoặc bỏ qua.

Validation ngày vẫn xảy ra tại live step qua CLI B.0: một dispatch sai ngày có thể đã đi qua checkpoint và vẫn bị tính vào rolling quota một cách bảo thủ, nhưng không gọi provider.

## Rolling cap fail closed

Trước: biến repository thiếu thì expression fallback về 20, script cho phép range 1..20.

Sau:

- Chỉ đọc `${{ vars.SOCIALAPIS_MAX_31_DAY_REQUESTS }}`, không có fallback.
- Script chấp nhận đúng chuỗi cấu hình `1` hoặc `2`. Missing, empty, whitespace, số âm/0, fractional/non-integer, NaN, 3/20/1000 hoặc cách viết mơ hồ như `02`, `2e0`, `2.0` đều bị `INVALID_ROLLING_REQUEST_CAP`.
- Thiếu/sai cấu hình bị chặn trước cả repository API check trong checkpoint script, và trước bước có provider secret.
- Không thay đổi giá trị remote; effective configuration thực tế vẫn chưa được đọc hoặc xác minh trong task offline này.
- Giữ nguyên rolling window 31 ngày, main/workflow_dispatch query và conservative count tất cả dispatches, kể cả failed/no-request.
- Nếu response listing chưa chứa current run, vẫn cộng 1 để reserve current attempt. Hai dispatch cũ với cap 2 sẽ chặn dispatch mới.
- Rerun cũ vẫn bị chặn bằng GITHUB_RUN_ATTEMPT; không tự retry hoặc thay accounting mechanism.

Không tăng cap để chạy hết test matrix B.1. Giá trị 1 là mức hạn chế hơn; upper bound được phê duyệt vẫn là 2.

## Security/cost/state controls giữ nguyên

- main-only, ba explicit consent inputs, public-repository check.
- Concurrency group `socialapis-job-discovery-state`, cancel-in-progress=false; GitHub permissions chỉ contents/actions read.
- ubuntu-latest standard runner, Node 22.12.0, timeout workflow 5 phút; npm test trước provider access.
- Restore artifact, validate/prepare checkpoint, capacity guard và upload pre-request checkpoint trước live step.
- SOCIALAPIS_API_KEY chỉ ở duy nhất live discovery step; không đưa lên workflow/job env hoặc log.
- SOCIALAPIS_MAX_REQUESTS=1, SOCIALAPIS_MAX_CREDITS=1; adapter timeout tối đa 20 giây không đổi. Không retry/pagination hoặc nhiều request/run.
- Hai artifact hiện có vẫn dùng cùng state/report JSON, retention 90 ngày, include-hidden-files, if-no-files-found=error; giới hạn 500000 bytes trước checkpoint và 1048576 bytes sau discovery không đổi. Không thêm artifact.
- CLI vẫn persist queryYield (có requestMetadata), providerUsage, itemStats, storage, jobs tích lũy và accumulatedMetrics. Không thêm persistence backend.
- Date filters không xác minh publishedAt. Historical replay vẫn mode REPLAY/requestCount=0; không được báo thành live request.
- Không in full JD/contact data. Consent cho dữ liệu trong downloadable artifact của public repository vẫn được giữ nguyên, người dùng phải đọc kỹ trước live run.

## Kiểm chứng offline

- Focused `node --test test/workflow.test.mjs`: **6/6 PASS**.
- Full `npm test`: **233/233 PASS**, so với baseline B.0 230; không skipped hoặc nới assertion cũ. Bổ sung ba tests và mở rộng checkpoint/security assertions hiện có.
- `npm run test:browser`: **8/8 PASS** trên Chrome local, chỉ loopback fixtures.
- YAML parsing và JavaScript syntax check: PASS.
- `git diff --check`: PASS.

Tests parse YAML thật, thực thi checkpoint script thật bằng AsyncFunction với GitHub API mocks. Bash tests chạy **chính nội dung live step**, không chỉ kiểm tra comments:

1. Stub node capture argv cho input rỗng, start-only/end-only, cả hai ngày, Unicode/C# và các chuỗi giống shell injection.
2. Dùng Bash step với CLI thật, socket guard và fake fetch transport. Ngày không tồn tại/range ngược fail trước transport và giữ checkpoint byte-for-byte.
3. Fake successful response đi qua adapter/extractor/state merge thật; đọc JSON persisted để kiểm tra queryYield/requestMetadata/providerUsage/itemStats/storage/jobs/accumulatedMetrics. Lần hai không insert lại post.
4. Kiểm tra npm-test/checkpoint/download/upload/live/upload ordering; đúng một step có API key; request/credit limit vẫn 1.

Các giá trị requestCount/credits trong fake transport là dữ liệu synthetic cho tests, **không phải live/provider usage**. Không dùng credentials thật. Không có network ra SocialAPIs/GitHub/Facebook; browser tests chỉ dùng server local. Không trigger Actions.

## Điều kiện còn lại trước live validation

- Review và đưa thay đổi lên remote theo quyết định riêng của người dùng; task này không stage/commit/push.
- Người dùng xác minh repository variable được cấu hình rõ là `2` (hoặc mức an toàn hơn), số dispatch còn lại trong rolling window và checkpoint/backup còn hiệu lực. Nếu thiếu/sai, workflow mới sẽ fail closed thay vì fallback.
- Người dùng xác nhận key an toàn, Free credits đủ, không auto-top-up/phát sinh tiền mặt và đồng ý dữ liệu artifact của public repository.
- Chọn và phê duyệt query/window cụ thể; tối đa một request cho mỗi manual run, không vượt rolling cap, không rerun tự động.
- Cần GitHub-hosted execution thật để xác nhận môi trường cloud/artifact upload-restore. Local Bash/mock tests không được tính là cloud PASS.
- Hiệu lực start_time/end_time, timezone, boundary inclusivity, publication timestamps và target freshness thực tế vẫn **UNVERIFIED với SocialAPIs**. Chỉ B.1 live có phê duyệt mới tạo được bằng chứng này.
- Pagination vẫn ngoài scope và chưa hỗ trợ. Không tự follow cursor hoặc thêm request để bù kết quả.

Không provider/Account Usage/Gmail/AI request, không thay repository variables/secrets, không tạo resource, schedule, commit/push hoặc triển khai.
