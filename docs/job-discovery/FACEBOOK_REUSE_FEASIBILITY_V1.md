# Facebook Reuse Feasibility V1

Ngày: 2026-10-20. Đọc actual `tools/browser-job-discovery/src/{contracts.d.ts,core.mjs,browser.mjs}` và package.json.

## 1. Quyết định

**ADAPT cho một bước tích hợp có kiểm soát trong tương lai; NO_GO cho live/production enablement hiện tại.** Không BUILD lại browser runtime. Không REUSE nguyên khối repo scraper/posting/MCP.

Lý do: local runtime đã có session lifecycle, budget, stop codes, report và fixture tests. Thứ thiếu là authorized keyword search → validated post URLs trên DOM/provider thực; secure session nếu chọn browser; coverage Hà Nội và cloud verification.

## 2. Mapping với ports hiện tại

| Local port | Reuse hợp lý | Điều phải tránh |
|---|---|---|
| ISearchPlanner | Giữ target queries hiện hữu; có thể tham khảo FBIntelPy keyword encoding | Không biến search-page URL thành post result |
| IBrowserSessionProvider | Giữ abstraction và lease/abort; future provider phải được review riêng | Không import browser cookies, credentials login hoặc session dump từ upstream |
| IBrowserDiscoveryAdapter.search | Tham khảo kevin global search route và permalink candidates | Không gọi mobile-HTML crawler/retry loop nguyên trạng |
| IBrowserDiscoveryAdapter.read | Tham khảo permalink validation/field extraction | Không lấy extra author/comment data ngoài JD evidence |
| IActivityBudget | Giữ code local 5/20/10 phút/1 session | Không kế thừa unbounded pagination/upstream retry |
| IJobPostExtractor | Giữ missing evidence/matching boundaries | Không tự thêm Hanoi/Angular/max experience |
| IDiscoveryReporter | Giữ source/evidence markers và gates | Không report real/live từ response mock hoặc URL builder |

`FixtureBrowserSessionProvider` chỉ allow loopback; không mở allowlist thành facebook.com trong task này. `AccessiblePost.evidenceKind` hiện chỉ có fixture enums; adapter live tương lai cần contract review rõ cho provenance, không cast dữ liệu live thành SYNTHETIC/USER_PROVIDED.

## 3. Candidate A — ADAPT kevinzg component

Source path có keyword → global posts search, không cần Group ID. Dependency Requests-HTML/mobile parser không dùng được trực tiếp với Playwright Page port. Mang cả library vào sẽ tạo HTTP/session stack thứ hai và có cookie import/fallback/retry trái requirements.

Bước tích hợp cụ thể sau approval/access gate:

1. Pin revision `567711fbab3e014504a1d4f33f882c2b29d71584`, giữ MIT notice nếu copy đoạn code đáng kể.
2. Tạo `FacebookBrowserDiscoveryAdapter` nhận Page từ session provider local; không launch browser riêng.
3. Dùng URL/URLSearchParams cho keyword, sửa C# fragment bug đã tái hiện; không chép filter blob cũ chưa live verify.
4. Chỉ khi có authorization/session hợp lệ mới dùng UI search và đọc anchors trong accessible results. Validator chỉ nhận post permalink hợp lệ, không Page/group/search URL.
5. Mỗi navigation/candidate/redirect tính budget; stop ngay restriction. Không đưa upstream retry/fallback/cookie harvesting vào adapter.
6. Test selectors bằng authorized captured HTML đã loại private data và existing fixture tests. Sau đó một live query bounded có quyền mới quyết định discovery PASS.

Đây là **ADAPT thuật toán/reference**, không phải adapter hiện đã tích hợp. DOM cũ không chứng minh còn hoạt động ở Facebook năm 2026.

## 4. Candidate B — SocialAPIsHub provider adapter

Source wrapper có query endpoint không cần seed Page/Group; backend engine không open source trong repo. Node HTTP adapter không cần browser runtime, nhưng không nên giả triển khai Playwright API khi nó thực sự là external provider. Có thể giữ browser runtime như một strategy và thêm provider discovery strategy sau contract review.

[Pricing hiện tại](https://socialapis.io/pricing): free plan định kỳ 200 calls/tháng, không cần card, hết quota trả insufficient_credits; không phải trial một lần. [Terms](https://socialapis.io/terms) cho phép nghiên cứu nội dung public qua API nhưng vẫn đặt trách nhiệm upstream terms/data rights lên người dùng. Không thấy bằng chứng riêng rằng provider được Meta cấp quyền cho collection path này.

Do đó không gọi là “paid-only”, nhưng cũng không ZERO_COST_VERIFIED/ACCESS_AUTHORIZATION_VERIFIED. Cần account/key được quản lý an toàn, review scope/rights, kiểm tra schema trả post URL và coverage bằng live test được phép. Không tạo account hoặc gọi data endpoint trong task.

Một pilot tương lai có thể dùng 5 keyword queries/ngày, tối đa 150–155 initial calls/tháng, nhưng pagination/location lookup/retries/chi phí theo credit thực có thể làm vượt 200. Không tự động top-up/upgrade; hard stop khi hết allowance. Chỉ dùng HTTP header secret, không browser Facebook password/cookie.

## 5. Candidate C — FBIntelPy chỉ reuse query construction

Offline probe chứng minh `C#` và Unicode query được encode đúng, nhưng output là search-page URL. Có thể tham khảo normalization/encoding trong adapter A. Không thêm tkinter/clipboard GUI vào cloud. Không coi đây là discovery engine.

## 6. Các hướng loại khỏi tích hợp

- Hidrix: search comment/input thuộc actor khác với current actor; group mode bật residential proxy. Không chạy ngay cả khi có free Apify credits.
- Social-search MCP: cần search provider/instance được phép; local SearXNG không đáp ứng cloud independence. Repo không có license file dù metadata classifier nói MIT; cần làm rõ trước copy.
- Autopost/group posting bots: có write side effects và cookie/profile artifacts; không cần mang chúng vào read-only tool.
- Ripple: OSM business/Page hints, không job posts.
- Bangalore crawler: seeded Group IDs và old scraper dependency; không source discovery.
- Playwright MCP: thêm protocol/primitives, không giải quyết business discovery còn thiếu.
- README-only/unavailable repos: không đủ implementation/license để reuse.

## 7. Cloud và cost

GitHub standard public runners có thể miễn phí và chạy khi laptop tắt ([GitHub billing](https://docs.github.com/en/actions/concepts/billing-and-usage)). Không verify account entitlement hoặc execute workflow trong task. Persistent browser login vẫn cần encrypted cloud context/human login; no raw cookie artifacts. Existing manual workflow giữ nguyên.

Cloud browser free offerings đã nghiên cứu ở [cloud feasibility trước](FACEBOOK_BROWSER_CLOUD_FEASIBILITY_V1.md); không dùng trial credits làm bằng chứng $0 lâu dài, không tạo provider resource. No-context HTTP provider không giải quyết quyền upstream.

[Apify pricing](https://apify.com/pricing) có allowance $5/tháng trên Free, compute/actor/proxy/storage vẫn tiêu allowance và hết allowance thì dừng. Không thể suy ra đủ cho số query/job mục tiêu trước khi đo. Hidrix còn có proxy constraint nên không chọn.

Actual cost của audit/probes: $0 API/provider fee; không AI/Gmail/Gemini, không Facebook data request, không account signup. **ZERO_COST_VERIFIED chỉ ở phạm vi offline probe; live cloud recurring cost UNVERIFIED.**

## 8. Release gates còn thiếu

- ACCESS_AUTHORIZATION_VERIFIED cho đúng collection path.
- Secure session nếu browser strategy; provider key nếu API strategy.
- End-to-end keyword → real post URLs, không seed; coverage target có evidence.
- Cloud run và account hard-budget thực tế.
- License compliance và bounded URL validation/secret logging review.

Không có thay đổi production/runtime/workflow trong task. Chưa có lý do xây browser core mới; bước tiếp theo là một adapter spike được giới hạn scope sau khi các gate cho live test được đáp ứng.
