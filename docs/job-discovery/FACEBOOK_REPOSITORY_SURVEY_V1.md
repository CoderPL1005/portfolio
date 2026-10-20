# Facebook Repository Survey V1

Ngày audit: 2026-10-20. Input mục tiêu: .NET/C#/Angular, entry level, Hà Nội; không company/post/group seed.

## 1. Phương pháp và kết luận

Khảo sát **13 ứng viên**: 12 repo truy cập được, trong đó 10 có implementation liên quan để audit và 2 chỉ có README; một URL repo cũ trả 404. Đọc GitHub metadata/tree, 30 source/manifest/license files qua raw GitHub; không cài dependency upstream hoặc chạy application entrypoint. Các source tải xuống và probe script nằm trong `tools/browser-job-discovery/output/repository-audit-v1/`, đã gitignore.

Có implementation global keyword search thật ở kevinzg, wrapper provider ở SocialAPIsHub và hidrix. **Chưa có repo nào được FACEBOOK_POST_DISCOVERY_VERIFIED** trong task này: không gọi Facebook/provider data API, không nhận URL bài thật từ live query.

License code không phải authorization lấy Facebook data. Last push không đồng nghĩa selector còn hoạt động. README/demo chỉ dùng tìm candidate, không quyết định capability.

## 2. Capability definitions

- `PAGE_DISCOVERY`: tìm Page identity/URL chưa được cấp trước.
- `GROUP_DISCOVERY`: tìm Group identity/URL từ query; ghi rõ global hay groups đã tham gia.
- `POST_DISCOVERY`: query → candidate post identities/permalinks; không đồng nghĩa Page/group listing đã seed.
- `POST_EXTRACTION`: đọc nội dung/metadata từ post hoặc feed đã xác định.
- `BROWSER_AUTOMATION_ONLY`: browser primitives, chưa có Facebook discovery logic.
- URL trang `/search/posts` không phải URL một bài đăng.

## 3. Xếp hạng theo capability phù hợp, không phải production readiness

| Hạng | Repository | Capability thực tế | Keyword → post URLs | Group ID trước? | Kết luận reuse |
|---:|---|---|---|---|---|
| 1 | [kevinzg/facebook-scraper](https://github.com/kevinzg/facebook-scraper) | POST_DISCOVERY, GROUP_DISCOVERY, POST_EXTRACTION | Có code path; live chưa verify | Không cho global/group discovery; có cho group-feed fetch | ADAPT component, không import crawler nguyên trạng |
| 2 | [SocialAPIsHub/mcp-server](https://github.com/SocialAPIsHub/mcp-server) | POST/PAGE_DISCOVERY qua provider; group/page/post retrieval | Wrapper có query endpoint; backend response chưa verify | Không cho search; có cho group-feed retrieval | ADAPT có điều kiện provider/access gate |
| 3 | [sonpiaz/hidrix-tools](https://github.com/sonpiaz/hidrix-tools) | Claimed POST_DISCOVERY qua Apify; POST_EXTRACTION | Có wrapper nhưng actor/input mismatch | Có cho group mode; không cho search wrapper | NO_GO as-is |
| 4 | [shubhamekapure/social-search-mcp](https://github.com/shubhamekapure/social-search-mcp) | Index-based candidate discovery | Có search-engine results, không lọc chắc là post | Không | ADAPT pattern nếu có search provider hợp lệ; license cần làm rõ |
| 5 | [UncleSocks/FBIntelPy](https://github.com/UncleSocks/FBIntelPy) | Search URL construction, chưa discovery results | Không: trả một search-page URL | Không ở generic search; có ở filtered modes | REUSE nhỏ cho query URL encoding |
| 6 | [darideveloper/facebook-groups-post-bot](https://github.com/darideveloper/facebook-groups-post-bot) | GROUP_DISCOVERY trong joined groups + posting | Không tìm/extract post URLs theo keyword | Discovery không cần ID, nhưng cần account membership | NO_GO whole app; group-link idea tham khảo |
| 7 | [RippleCheck/Ripple-Lead-Finder](https://github.com/RippleCheck/Ripple-Lead-Finder) | PAGE_DISCOVERY gián tiếp từ OSM business metadata | Không | Không | Không giải quyết job-post discovery |
| 8 | [aditeyabaral/bangalore-flat-and-flatmates-crawler](https://github.com/aditeyabaral/bangalore-flat-and-flatmates-crawler) | Seeded Group feed + POST_EXTRACTION/local keyword tagging | Không có global source discovery | Có | NO_GO cho yêu cầu unseeded |
| 9 | [microsoft/playwright-mcp](https://github.com/microsoft/playwright-mcp) | BROWSER_AUTOMATION_ONLY | Không Facebook-specific implementation | Không áp dụng | Giữ runtime Playwright hiện hữu |
| 10 | [ariknih/autopost-facebook-group](https://github.com/ariknih/autopost-facebook-group) | BROWSER_AUTOMATION_ONLY cho posting; liệt kê groups của account | Không | groups.json hoặc account groups | NO_GO cho discovery/read-only |
| 11 | [BoldBastion/facebook-page-details-scraper](https://github.com/BoldBastion/facebook-page-details-scraper) | README mô tả seeded Page metadata; không code tree | Chưa có code chứng minh | Cần Page URLs theo README | NO_GO reuse source |
| 12 | [harismuneer/Ultimate-Facebook-Scraper](https://github.com/harismuneer/Ultimate-Facebook-Scraper) | Tree hiện tại chỉ README | Chưa xác minh implementation hiện tại | Không xác định từ code hiện tại | NO_GO current source |
| 13 | [sowdust/fb-search](https://github.com/sowdust/fb-search) | Historical candidate; GitHub API 404 | Không kiểm chứng | Không xác định | BLOCKED/unavailable |

## 4. Source evidence, auth, dependencies và maintenance

### 4.1 kevinzg/facebook-scraper

Revision `567711fbab3e014504a1d4f33f882c2b29d71584`; default-branch commit 2023-10-30, repo pushed_at 2024-06-22. MIT. Code có global keyword iterator, group search và permalink extraction: [search method L196](https://github.com/kevinzg/facebook-scraper/blob/567711fbab3e014504a1d4f33f882c2b29d71584/facebook_scraper/facebook_scraper.py#L196), [iterator L50](https://github.com/kevinzg/facebook-scraper/blob/567711fbab3e014504a1d4f33f882c2b29d71584/facebook_scraper/page_iterators.py#L50), [group search L1123](https://github.com/kevinzg/facebook-scraper/blob/567711fbab3e014504a1d4f33f882c2b29d71584/facebook_scraper/facebook_scraper.py#L1123), [permalink L459](https://github.com/kevinzg/facebook-scraper/blob/567711fbab3e014504a1d4f33f882c2b29d71584/facebook_scraper/extractors.py#L459).

Source cho phép anonymous requests hoặc credentials/cookies; live login requirement chưa biết. Requests-HTML chạy không cần browser UI, nhưng mobile HTML selectors cũ. Có cookie-from-browser option, fallback và retry; không dùng các path đó. Probe tái hiện lỗi encoding C#. Không có mandatory paid provider trong search path đã đọc.

### 4.2 SocialAPIsHub/mcp-server

Revision `02b731e4d1c46732b3fd96f88b4eb5eeb93b1bc5`, pushed 2026-10-16, MIT. [Tool descriptor L364](https://github.com/SocialAPIsHub/mcp-server/blob/02b731e4d1c46732b3fd96f88b4eb5eeb93b1bc5/src/tools.js#L364) nhận query, location/date/cursor; [backend forwarding L87](https://github.com/SocialAPIsHub/mcp-server/blob/02b731e4d1c46732b3fd96f88b4eb5eeb93b1bc5/server.js#L87) thực hiện HTTP với provider key. Không có local Facebook crawler source để kiểm chứng engine/authorization. Group-feed API cần ID; không có group-discovery tool được xác nhận trong descriptor audit.

Không cần cung cấp Facebook account cho wrapper, nhưng cần SocialAPIs account/key. Node/HTTP phù hợp cloud, không có persistent Facebook browser session ở client. Manifest dùng MCP SDK/Express/CORS với ranges; chưa audit toàn dependency graph. Free plan định kỳ có tài liệu, chưa account/live verify.

### 4.3 sonpiaz/hidrix-tools

Pushed 2026-09-12, MIT. [Facebook module L36–133](https://github.com/sonpiaz/hidrix-tools/blob/main/tools/facebook-scraper/index.ts#L36) dùng Apify actors, cần token. Search handler truyền keywords dạng startUrls nhưng actor hiện tại là pageId-based; comments vẫn nói actor khác. Group mode bật residential proxy. [Apify helper L30](https://github.com/sonpiaz/hidrix-tools/blob/main/lib/apify.ts#L30) tạo run, poll rồi đọc dataset; không thấy abort remote run khi timeout. Không execute.

Bun/TypeScript, dependencies MCP/readability/linkedom; code import zod nhưng manifest trực tiếp không khai báo zod. Không nên coi search backend functioning hoặc $0 đã được chứng minh. Ads mode không tương đương organic job posts.

### 4.4 shubhamekapure/social-search-mcp

Pushed 2026-03-18. [server.py L25/L56/L99/L177](https://github.com/shubhamekapure/social-search-mcp/blob/main/src/social_search_mcp/server.py#L177) thêm site filter rồi gọi SearXNG/Serper/Google; trả search results, chưa validate permalink/job. Không cần Facebook session. SearXNG mặc định localhost, muốn cloud phải có instance/quyền engine; Serper/Google cần key. Không chạy search engines trong audit.

HTTP/Python có thể cloud, chưa verify no-payment provider. Metadata classifier nói MIT nhưng repo tree không có license file và GitHub license=null; không copy implementation trước khi làm rõ grant. Dependencies broad/unpinned.

### 4.5 UncleSocks/FBIntelPy

Revision `560cea2fe72639bc9ecdc530cac342550046c07b`, pushed 2026-05-25, MIT. [ConstructFbUrl L127](https://github.com/UncleSocks/FBIntelPy/blob/560cea2fe72639bc9ecdc530cac342550046c07b/fbintel/fbintel.py#L127) encode keyword và tạo URL; không fetch kết quả/DOM/permalink. Generic Search section không cần Group ID, filtered modes có IDs.

GUI tkinter/clipboard, không phải headless agent. Pure URL builder đã chạy offline bằng AST nodes; không import GUI. Không cần account để tính URL; đọc search page thật vẫn chưa verify auth. Dependency pyperclip; không paid API.

### 4.6 darideveloper/facebook-groups-post-bot

Pushed 2024-09-05, MIT. [save_groups L93](https://github.com/darideveloper/facebook-groups-post-bot/blob/master/libs/scraper.py#L93) nhận keyword, điều khiển Selenium và lưu joined-group links; [post_in_groups L32](https://github.com/darideveloper/facebook-groups-post-bot/blob/master/libs/scraper.py#L32) có write side effects. Không phải post-discovery agent.

Dùng profile Chrome local; constructor gọi wrapper với start_killing=true, wrapper có taskkill Chrome. Headless có tùy chọn nhưng session cloud không sẵn. Selenium/python-dotenv pinned cũ. Không execute; không tái sử dụng whole app.

### 4.7 RippleCheck/Ripple-Lead-Finder

Pushed 2026-08-07, MIT. [engine.py L439](https://github.com/RippleCheck/Ripple-Lead-Finder/blob/main/engine.py#L439) đọc OSM tags thành business leads/social handles; [L336](https://github.com/RippleCheck/Ripple-Lead-Finder/blob/main/engine.py#L336) dựng social URL. Không query Facebook posts. Không cần Facebook account hoặc Group ID. Flask/stdlib có thể server/cloud; account/AI optional ở use case khác không được chạy. OSM terms/quota cần review riêng nếu chọn; chưa phải job discovery.

### 4.8 aditeyabaral/bangalore-flat-and-flatmates-crawler

Pushed 2022-12-02, GitHub license=null; không có license file trong tree đã đọc. [crawler.py L26/L46](https://github.com/aditeyabaral/bangalore-flat-and-flatmates-crawler/blob/main/app/crawler.py#L26) gọi facebook-scraper với Group ID hoặc list post URLs, không discover nguồn. Có random user-agent switching nên không reuse nguyên trạng. Dependencies facebook-scraper 0.2.59/requests 2.28.1 cũ; auth do scraper dependency. Python headless khả thi nhưng chưa verify live/session/cost.

### 4.9 microsoft/playwright-mcp

Pushed 2026-10-19, Apache-2.0. [index.js L18](https://github.com/microsoft/playwright-mcp/blob/main/index.js#L18) export connection từ playwright-core; [package.json](https://github.com/microsoft/playwright-mcp/blob/main/package.json) pin alpha Playwright khác runtime local đang dùng. Có browser primitives nhưng không Facebook-specific search/permalink code ở entrypoint đã audit. Có thể headless/cloud, không bắt buộc AI API; agent model phía caller là chuyện riêng. Thêm MCP không giải quyết missing discovery adapter/session/access grant.

### 4.10 ariknih/autopost-facebook-group

Pushed 2026-02-28, MIT. [main.py L70](https://github.com/ariknih/autopost-facebook-group/blob/main/main.py#L70) manual login rồi ghi cookies JSON; [L89](https://github.com/ariknih/autopost-facebook-group/blob/main/main.py#L89) posting loop; [L332](https://github.com/ariknih/autopost-facebook-group/blob/main/main.py#L332) liệt kê groups account. Không keyword job search/permalink extraction. Headful mặc định, UA override, repeated posting; không phù hợp read-only/no-credentials-artifact. Không import hoặc execute.

### 4.11–4.13 Không đủ source

- BoldBastion: pushed 2026-06-17; current tree chỉ README, license=null. README mô tả input Page URLs và metadata/Apify, không đủ để confirm implementation/dependencies/headless/auth.
- Ultimate-Facebook-Scraper: pushed 2025-06-07; current tree chỉ README, license=null. Không dùng danh tiếng/history làm bằng chứng source hiện tại. Không khảo cổ rồi chạy historical version.
- sowdust/fb-search: repository API 404. Không phân biệt được removed/private/renamed nên không claim implementation tồn tại.

## 5. Maintenance evidence ledger

Pushed_at lấy từ GitHub public repository API trong audit; không dùng star count làm freshness. Các repo truy cập được đều archived=false theo metadata. Ba repo thử nghiệm được pin commit ở trên; source files có SHA-256 trong local download ledger. Với các repo chỉ static-audit, source links theo default branch tại ngày audit và có thể đổi về sau.

Source audit không phải full vulnerability audit. Không install/pip/npm upstream, không chạy npm test upstream vì có entrypoint network; chỉ chạy reviewed nodes/declarative exports trong ba probe scope.

## 6. Kết luận

**Có code capability**, nhưng không có live discovery verification. Ưu tiên ADAPT nhỏ từ kevinzg cho URL/permalink ideas; SocialAPIsHub là provider option đáng đánh giá vì có free plan định kỳ, song backend/authorization/cost thực tế chưa verify. Không có repo đủ điều kiện REUSE nguyên khối vào production dưới tất cả constraints hiện tại.
