# Facebook Discovery Experiment V1

Ngày thực hiện: 2026-10-20. Phạm vi: source audit và offline component execution; không phải Facebook live collection.

## 1. Selected repos và snapshot

Chọn đúng 3 repo cho controlled execution sau static review:

| Repo | Commit | Thành phần thực thi |
|---|---|---|
| kevinzg/facebook-scraper | `567711fbab3e014504a1d4f33f882c2b29d71584` | keyword iterator, group-ID parser, permalink method và URL whitelist helper |
| UncleSocks/FBIntelPy | `560cea2fe72639bc9ecdc530cac342550046c07b` | encode + ConstructFbUrl; không GUI |
| SocialAPIsHub/mcp-server | `02b731e4d1c46732b3fd96f88b4eb5eeb93b1bc5` | declarative tool exports; không server/network client |

Source SHA-256 guards trong probe xác nhận các primary source files không đổi. Helper URL parser tải từ cùng branch ở thời điểm audit; không import whole scraper package. Các repo khác chỉ static audit, không gọi entrypoint.

## 2. Dependency/security review trước execution

- Không pip/npm install upstream. Không import scraper __init__ tạo singleton/request session hoặc FBIntel GUI.
- Python AST chọn đúng các function/class đã đọc; bỏ type annotations/decorators để không cần upstream type imports. Body/control flow của các function giữ nguyên.
- request_fn, pagination dispatcher và DOM node được thay bằng inert local fakes có kiểm soát; không claim full upstream parser/runtime đã chạy.
- Python socket/create_connection bị thay bằng exception; không module network client nào được load.
- Node chỉ import declarative tools array sau khi kiểm tra hash, không import/require/fetch trong file; global fetch bị chặn. Không import Express/MCP server.
- Không execute autopost, Chrome-profile access, token reading, cookie import, stealth/proxy setup hoặc CAPTCHA helper.
- Đây là targeted manifest/source review, không phải full transitive CVE scan hoặc security certification.

## 3. Local artifacts và commands

Thư mục gitignored:

```text
tools/browser-job-discovery/output/repository-audit-v1/
  downloaded upstream source/manifest/license files
  offline_probe.py
  socialapis_probe.mjs
```

Commands đã chạy từ repository root:

```powershell
python tools/browser-job-discovery/output/repository-audit-v1/offline_probe.py
node tools/browser-job-discovery/output/repository-audit-v1/socialapis_probe.mjs
```

Result: Python **6/6 PASS**, Node **3/3 probe assertions PASS**, exit 0. Upstream Python AST parse phát một DeprecationWarning cho escape sequence cũ; không có test bị skip. Không chạy simulator local cũ hoặc tạo simulator mới.

Các file tải xuống/probe là scratch audit artifacts, không thêm vào production hoặc intended commit. Ba báo cáo là deliverables mới duy nhất. Các file không phải entrypoint safe cho live usage; không chạy với Facebook credentials.

## 4. Exact results

### kevinzg — 4 probes

1. Real upstream keyword iterator nhận `.NET Junior Hanoi`, tạo path `/search/posts`, không `/groups/`, và truyền page_limit=1 sang pagination dispatcher fake. Chứng minh source routing, không chứng minh trả real results.
2. Input `C# Angular Junior Hà Nội` tạo URL có fragment: query q không còn nguyên chuỗi. **Concrete reuse defect confirmed**: code interpolate keyword không URL-encode; fragment không phải request query. Test PASS vì xác nhận lỗi tồn tại, không phải vì query processing đúng.
3. Group-ID parser trích đúng ID từ một fragment synthetic. Không discover một Group thật.
4. Permalink method lấy story ID/account ID từ fake DOM anchor và bỏ tracking query. Không có external request, không có real post discovered.

### FBIntelPy — 2 probes

1. Generic posts-search builder giữ đúng C#/Unicode keyword sau round-trip decode, không cần post/group seed.
2. Generic groups-search builder tạo search-page URL không cần Group ID.

Cả hai trả search-page string; không fetch/query results, không trả danh sách post URLs. Không gọi POST_DISCOVERY_VERIFIED cho URL builder.

### SocialAPIsHub — 3 probes

1. Actual schema của facebook_search_posts chỉ yêu cầu query; không bắt Page/Group/post URL.
2. Actual descriptor có GET endpoint và không execute method: đây là wrapper metadata, không local engine.
3. Group/Page retrieval descriptors tồn tại riêng với global post search.

Không mock response thành real results; không có backend request nên chưa xác minh URL field/schema/coverage của provider output.

## 5. Verdict matrix

| Repo | SOURCE_CODE_CAPABILITY_CONFIRMED | OFFLINE_REUSE_VERIFIED | LIVE_FACEBOOK_DISCOVERY_VERIFIED | CLOUD_EXECUTION_VERIFIED | ZERO_COST_VERIFIED | ACCESS_AUTHORIZATION_VERIFIED |
|---|---|---|---|---|---|---|
| kevinzg | YES: global keyword/group route và extraction | PARTIAL_COMPONENT_ONLY | NOT_RUN | UNVERIFIED | YES cho offline; live UNVERIFIED | UNVERIFIED |
| FBIntelPy | YES: URL construction ONLY | PARTIAL_COMPONENT_ONLY | NOT_RUN | UNVERIFIED | YES cho offline; live UNVERIFIED | UNVERIFIED |
| SocialAPIsHub | YES: provider wrapper ONLY | SCHEMA_ONLY, chưa full adapter reuse | NOT_RUN | UNVERIFIED | Offline $0; recurring plan documented, account UNVERIFIED | API-use terms documented; upstream authorization UNVERIFIED |

Không repo nào đạt **FACEBOOK_POST_DISCOVERY_VERIFIED**. OFFLINE_REUSE_VERIFIED toàn adapter chưa đạt; không dùng component/schema PASS để che giới hạn này.

## 6. Execution metrics

| Metric | Giá trị |
|---|---:|
| Candidate repositories | 13 |
| Accessible repositories | 12 |
| Repos có implementation audit | 10 |
| README-only repos | 2 |
| Unavailable repo | 1 |
| Repos controlled offline probes | 3 |
| Passed probes | 9 |
| Live Facebook/provider data calls | 0 |
| Real Facebook post URLs discovered | 0 |
| Real JD/target matches | 0 |
| Provider/API execution fee | $0 |

Keyword probes không dùng DTMED/company/Group ID làm input discovery. Synthetic anchor/group-ID parser tests có ID vì mục đích test extraction riêng, không tính unseeded discovery. Không có request cap 20 áp dụng cho tổng GitHub source-research; task này không yêu cầu cap đó. Browser activity budget cũ vẫn giữ nguyên.

## 7. Vì sao dừng trước live test

Facebook authorization chưa được xác minh. SocialAPIs cần provider account/key; không tạo account hoặc xin credential trong prompt. Có [Free 200 calls/tháng no-card](https://socialapis.io/pricing), nhưng không phải bằng chứng account hiện đã được cấp hoặc provider có quyền upstream. [Terms](https://socialapis.io/terms) cho phép research qua API và vẫn yêu cầu tuân thủ platform/data rights.

Không thử anonymous Facebook HTML/mobile fallback, không replay internal GraphQL, không lấy cookie browser cá nhân. Không gọi Apify actors vì vừa có token/cost vừa có proxy constraint. Public pricing/docs GET không phải live data test.

## 8. Quyết định

**ADAPT** ở mức reference/components, **NO_GO** bật live collection ngay. Ưu tiên:

1. Nếu có authorization và secure session: port search/permalink strategy từ kevinzg vào IBrowserDiscoveryAdapter của runtime hiện hữu, sửa encoding và bỏ legacy retry/cookie paths.
2. Nếu provider scope/terms/key được xác minh: evaluate một SocialAPIs query bounded qua header secret, schema validation và hard monthly budget; không cần thêm browser stack.
3. FBIntelPy chỉ là helper encoding; không đáng thêm GUI/MCP/runtime dependency.

Capability còn thiếu không phải generic browser automation: đó là **đường discovery hiện hành, được cấp quyền, có coverage mục tiêu và được live/cloud verify**. Không sửa production, database, Gmail/Gemini, workflow hoặc schedule; không commit/push.
