# Facebook Discovery Adapter Spike V2

Ngày: 2026-10-20. Phạm vi: isolated tool `tools/browser-job-discovery`; không production Job Hunting. **ADAPTER_READY trong phạm vi offline/mock**, không phải FACEBOOK_POST_DISCOVERY_VERIFIED.

## 1. Baseline và quyết định

Đã đọc ba báo cáo V1, source ports/runtime và workflow manual-only. Không thay đổi kết quả V1: 9 component probes không phải live discovery; quyền Facebook/upstream chưa được xác minh. Không copy legacy crawler, selectors mobile cũ, retry, cookie import hoặc filter blob. V2 chỉ tham khảo luồng keyword search → permalink của kevinzg; code mới viết cho Page đã được lease.

Core pipeline, CLI và authorization gate V1 giữ nguyên. Chỉ thêm một route fixture vào allowlist server, shared discovery contract, hai strategy và tests; npm scripts đưa tests mới vào các command hiện có. Không thêm package/runtime.

## 2. Shared contract

`src/contracts.d.ts` định nghĩa DiscoveryCandidate; hai `discover` methods trả cùng cấu trúc:

```json
{
  "source": "FACEBOOK",
  "query": "C# .NET Angular Junior Hà Nội",
  "candidate_post_url": "https://www.facebook.com/groups/fixture.jobs/posts/1001",
  "source_post_id": "1001",
  "retrieved_at": "2026-10-20T00:00:00.000Z",
  "evidence": { "kind": "SYNTHETIC_FIXTURE", "live_verified": false },
  "authorization_status": "OFFLINE_FIXTURE_ONLY",
  "discovery_strategy": "FACEBOOK_BROWSER"
}
```

Ví dụ synthetic, timestamp minh họa; runtime dùng thời gian thu nhận thật, không giả publication date. Strategy B dùng `SOCIALAPISHUB_HTTP`. Evidence chứa fixture/search URL ở A hoặc endpoint/schema status ở B. Không author/private data, credential hoặc provider response body trong output.

Đây là discovery-stage DTO, không RawJobPosting/JobPosting write model. Chưa nối vào ingestion hay extractor V1. `search(page, query, budget)` của A có convenience projection ra URL; không được thay whole `IBrowserDiscoveryAdapter` trong `runDiscovery` vì V2 không implement post-body `read`. B không giả làm Playwright adapter.

## 3. Strategy A — FacebookBrowserDiscoveryAdapter

- Nhận leased Playwright Page từ session provider cũ; không launch browser hoặc tạo context thứ hai.
- `buildFacebookSearchUrl` dùng URL + URLSearchParams; C# không trở thành fragment, Unicode round-trip đúng.
- URL search Facebook chỉ là evidence. Navigation thực tế chỉ đến `/facebook-search?q=...` trên origin HTTP 127.0.0.1 của fixture server.
- Đọc tối đa 200 anchors trong HTML synthetic; chỉ lấy href, không copy DOM selectors Facebook chưa verify.
- Validator chỉ nhận HTTPS Facebook hosts chính xác và grammar group posts/permalink, owner posts, story/permalink.php có ID hợp lệ. Chuẩn hóa host/path, bỏ tracking, dedup canonical URL; search/Page/group/profile/redirect/external URL bị loại.
- Reuse checkAccess: login/checkpoint/CAPTCHA dừng ngay. Body phải có marker synthetic; không nhận dữ liệu live dưới nhãn fixture.
- Budget V1: tối đa 5 searches, 20 unique candidates, 10 phút, 1 session; có thể giảm. Consume trước operation/output; vượt cap fail closed, không retry.
- Timeout mặc định 5 giây, tối đa 10 giây/operation, không vượt thời gian budget còn lại; native navigation timeout và deadline wrapper. Không tiếp tục extraction sau deadline.
- `mode != offline`, nonloopback origin hoặc caller flag tự nhận authorized không mở live gate. Fixture session vẫn chặn external network.

Giới hạn: grammar URL bảo thủ, có false negatives; chưa chứng minh selectors/search UI Facebook hiện tại hoặc sự tồn tại của synthetic permalink. V2 không đọc JD live.

## 4. Strategy B — SocialApisHubHttpAdapter (mock-only)

### Bằng chứng provider, không suy diễn từ MCP descriptor

Đọc [trang endpoint chính thức](https://socialapis.io/api-sources/facebook-search-posts) và [docs chung](https://docs.socialapis.io/integrations/sdks), không gọi playground/data API. Request đã có bằng chứng: GET `/facebook/search/posts`, query bắt buộc, `end_cursor` tùy chọn, API token trong header `x-api-token`.

Tuy nhiên ví dụ output chỉ có `data.results[].post_id`, không có permalink; ví dụ phân trang dùng `page_info.end_cursor/has_next` nhưng phần hướng dẫn lại nói `next_cursor`. Chưa đủ chốt response-to-permalink mapping. **PROVIDER_RESPONSE_SCHEMA_UNVERIFIED** giữ nguyên; docs example không phải live verification. Không tự ghép pfbid thành URL dự đoán.

### Cách triển khai an toàn

- Constructor nhận `baseUrl`, `apiKey` từ caller/configuration injection; không hardcode credential, đọc env, hoặc tự load secret file. Caller tương lai phải dùng secret manager; V2 chỉ cần key synthetic. Không thay deployment config.
- Base URL phải là HTTPS `api.socialapis.io`, không credential/query/path tùy ý; key private field, không có trong JSON/call diagnostics. Không truyền key qua URL.
- Không có live fetch transport. Chỉ nhận transport do `createMockSocialApisTransport` tạo; arbitrary fetch/function bị từ chối. `mode != offline` luôn bị khóa dù caller gửi authorized flag.
- Mock authorization phải là `OFFLINE_FIXTURE_ONLY`; thiếu/denied bị chặn trước request. Đây không phải bằng chứng quyền upstream.
- **Mock wire envelope nội bộ**, không phải schema SocialAPIs: `{fixture_kind: 'SYNTHETIC_FIXTURE', posts: [{url}], next_cursor: string|null}`. Required fields, URL, cursor, tối đa 20 rows/page và response 64,000 bytes được kiểm tra. Provider response thật không được chấp nhận qua mock parser.
- Request GET/query/header construction theo docs; `credentials: omit`, `redirect: error` được đưa vào request descriptor. Mock không thực hiện HTTP; không tuyên bố đã kiểm chứng hành vi HTTP library/redirect ngoài mạng.
- Mặc định 2 pages, tối đa 3; mỗi page tiêu search budget chung (tối đa 5). Repeated cursor/page cap fail closed, không silently return partial set. Candidate dedup trước consume budget.
- Timeout mặc định 5 giây, AbortSignal đến mock; retries = **0**, kể cả 401/403/429/500. Không top-up/upgrade, account lookup hoặc paid endpoint.
- Không log; exceptions không trả raw browser/provider diagnostics. Call metadata chỉ method/URL/redirect/credentials, không headers/key.

Để triển khai live cần xác minh schema/permalink và quyền truy cập trước, sau đó review riêng transport thật, response streaming-size bound và quota account cứng. Không bật bằng một boolean trong V2.

## 5. Tests và kết quả

Từ `tools/browser-job-discovery`:

```powershell
npm.cmd test
$env:BROWSER_POC_EXECUTABLE = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
npm.cmd run test:browser
```

Kết quả: **95/95 unit/config tests PASS**, **8/8 real Chromium tests PASS**, 0 fail/skip. Trong đó V2 có 43 unit tests và 1 real-browser test; V1 giữ 52 unit/config và 7 browser regression tests. Không cài browser/dependency mới; Chrome đã có sẵn.

Coverage: C#/Unicode encoding, canonicalization/search rejection/unsafe URL, duplicate aliases, shared fields/provenance, login/CAPTCHA stop, missing key/auth, schema invalid/missing, body-size bound, search/candidate/page budget, repeated cursor, timeout/no retry, safe diagnostics và no production/network/write dependencies. Browser test V2 lấy 3 **synthetic** canonical URLs từ fixture, không mở các URL Facebook đó.

Static dependency checks bổ trợ tests hành vi; không thay thế full security audit. Test mock không chứng minh wire schema/provider availability hoặc real Facebook authorization.

## 6. Status theo strategy

| Gate | A | B |
|---|---|---|
| Implementation offline | VERIFIED: real Page + synthetic DOM | VERIFIED: request construction + mock response |
| Live discovery | NOT_RUN | NOT_RUN |
| Access authorization | UNVERIFIED; live disabled | UNVERIFIED; live disabled |
| Response schema | Fixture-only; live DOM UNVERIFIED | PROVIDER_RESPONSE_SCHEMA_UNVERIFIED |
| Cloud execution | UNVERIFIED | UNVERIFIED |
| Cost | Offline $0; live/cloud UNVERIFIED | Mock $0; account/live cost UNVERIFIED |
| FACEBOOK_POST_DISCOVERY_VERIFIED | NO | NO |

## 7. Scope và blocker

Không production DB, Gmail/Gemini, R2, Job Hunting pipeline, workflow schedule, resource provisioning, cookie/profile import, stealth/proxy/CAPTCHA bypass. Không commit/push. Existing docs/tool/workflow trước task được giữ lại.

Blockers live: authorization đúng collection path; secure session nếu A; verified response/permalink contract + provider key/account hard-budget nếu B; live coverage .NET/Angular/Hà Nội; cloud run thực tế. Không có blocker cho acceptance **offline/mock V2**. Verdict: **ADAPTER_READY (OFFLINE/MOCK ONLY)**.
