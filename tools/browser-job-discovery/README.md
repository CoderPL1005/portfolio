# Offline Browser Job Discovery PoC

Tool độc lập với frontend/backend. Chỉ phục vụ fixture local; chưa có Facebook live adapter hoặc authenticated session provider.

Yêu cầu Node >=22.12.0. Tại `tools/browser-job-discovery`:

```powershell
npm.cmd ci --ignore-scripts --no-audit --no-fund
$env:PLAYWRIGHT_BROWSERS_PATH = '.browser-cache'
npx.cmd playwright install chromium
npm.cmd test
npm.cmd run test:browser
npm.cmd run simulate
```

Nếu máy đã có Chrome, có thể dùng browser có sẵn thay bước download:

```powershell
$env:BROWSER_POC_EXECUTABLE = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
npm.cmd run test:browser
npm.cmd run simulate
```

Report JSON: `output/report.json` (gitignored). Có thể chỉ định `npm.cmd run simulate -- --output output/another-report.json`. Không nhận URL live, cookie, password, token hoặc session profile.

`npm test`: unit + workflow configuration tests. `test:browser`: Chromium thật với loopback fixtures; không skip nếu browser thiếu. `simulate`: 5 queries, 6 candidate reads, 5 unique posts, 1 duplicate và 1 target match synthetic. DTMED là USER_PROVIDED_FIXTURE thiếu location; image-only fixture không OCR và không được tính matched.

Fixtures cố ý trả một tập candidate cố định qua search form để kiểm tra query input/navigation, false positives, dedup và extraction. Không mô phỏng ranking/search semantics của Facebook. Selector `data-*` là fixture schema, không phải Facebook DOM selector đã xác minh.

Default budget: 5 searches, 20 candidates, 600.000 ms và 1 session. Chỉ cho phép giảm budget. Deadline đóng browser; cleanup có thể thêm một khoảng ngắn. Session lease chống chạy đồng thời trong process; workflow concurrency chống hai job cùng workflow. Không phải distributed lock trên nhiều máy.

Workflow `.github/workflows/browser-discovery-offline.yml` là `workflow_dispatch` only. Nó không tồn tại trên remote cho đến khi được commit/push trong bước khác được phê duyệt. V1 không push hoặc trigger workflow.

Không dùng kết quả này làm live Facebook discovery evidence. Xem ba tài liệu `FACEBOOK_BROWSER_*` và `FACEBOOK_ACCESS_AND_SECURITY_V1.md` trong `docs/job-discovery` để biết gates/blockers.
