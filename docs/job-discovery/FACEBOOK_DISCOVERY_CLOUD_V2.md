# Facebook Discovery Cloud V2

Ngày: 2026-10-20. **CLOUD_EXECUTION_UNVERIFIED**.

## 1. Workflow baseline

`.github/workflows/browser-discovery-offline.yml` vẫn untracked; `git ls-tree HEAD` và `git ls-tree origin/main` không có file đó. `origin/main` là ref cache local, không phải live remote freshness check. Không push/fetch/dispatch hoặc bật schedule trong task. Chưa có workflow remote được xác minh đủ điều kiện chạy; không tuyên bố cloud PASS.

Local YAML/config regression test PASS: chỉ workflow_dispatch, permissions contents:read, checkout không persist credential, job timeout 10 phút, concurrency không cancel phiên đang chạy, Node 22.12.0, fixture-only commands, không secret context/profile/cache upload. Không sửa YAML V1.

`npm test` hiện bao gồm V2 unit tests; `npm run test:browser` hiện bao gồm V2 Page test. Khi workflow được người dùng review/push trong task khác, command hiện có sẽ chạy cả V1 regression và V2. Local parse/test không chứng minh action refs, runner install, browser dependencies hoặc account quota sẽ thành công trên GitHub.

## 2. Đã thực thi local

Windows, Node 22.12.0, Playwright 1.64.1, Chrome local có sẵn qua BROWSER_POC_EXECUTABLE. Unit/config 95/95 PASS; Chromium loopback 8/8 PASS, không skip.

Browser adapter dùng chính FixtureBrowserSessionProvider: một session lease, temporary context, service workers/WebSockets/external HTTP bị chặn, không persisted cookies. HTTP adapter dùng canned mock transport, không fetch. Không cần production/provider credentials để chạy toàn bộ test suite.

Local command:

```powershell
Set-Location C:\ProjectCaNhan\tools\browser-job-discovery
npm.cmd test
$env:BROWSER_POC_EXECUTABLE = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
npm.cmd run test:browser
```

Không cần gọi simulate để verify adapters; simulator V1 không được viết lại. Không gắn production API/database vào test.

## 3. Cloud verification kế tiếp — chưa chạy

Sau approval riêng để push workflow, người dùng có thể chạy workflow_dispatch offline một lần và kiểm tra logs: 95 unit/config + 8 browser tests, không fail/skip, chỉ loopback navigation và mock transport. Không thêm provider secret để làm offline run. Không lấy offline report làm live discovery evidence.

Workflow có bước cài dependencies/browser trên runner; local task không download thêm chúng. Cần kiểm tra entitlement và spending settings của account trước khi chạy. Chưa tạo cloud resource, không card/payment hoặc free-trial signup trong spike.

## 4. Cost và live gates

Chi phí provider của V2 tests: **$0**; số data endpoint requests thật = 0. Local execution $0 không chứng minh recurring cloud $0. Không có monthly allowance/account verification, không paid fallback/top-up.

A live/cloud cần bằng chứng authorization và session provisioning an toàn, không dùng cookie harvesting. B cần verified response/permalink schema, quyền upstream/provider và hard monthly budget. Mock-only implementation không có đường mạng để tiêu provider credits; giới hạn 3 pages/5 search operations chỉ là run budget, không phải monthly cost guarantee cho transport live tương lai.

## 5. Verdict

**ADAPTER_READY — offline/mock**, **CLOUD_EXECUTION_UNVERIFIED**, **LIVE_FACEBOOK_DISCOVERY_NOT_RUN**. Không có workflow activation, commit/push, production write hoặc provider call. Cloud/live verification là blocker riêng, không được chuyển thành PASS từ fixture tests.
