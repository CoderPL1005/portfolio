# Phase B.0 — Chuẩn bị freshness và discovery yield offline

## Phạm vi và kết luận

Hoàn thành phần chuẩn bị offline trên working tree Phase A/A.1/A.1.1 hiện có, không phục hồi code từ main. Không thay đổi vacancy matching, raw hash, workflow, ngân sách, billing controls hoặc lịch chạy. **Provider date-filter behavior vẫn UNVERIFIED. B.1 chưa được xác nhận cloud-ready.**

Files của B.0:

- `src/discovery-contract.mjs`: validation date-only dùng chung, lỗi `INVALID_DATE_FILTER` / `INVALID_DATE_RANGE`.
- `src/socialapis-adapter.mjs`: nhận optional `startTime`, `endTime`, thêm URLSearchParams tương ứng khi có giá trị.
- `src/cli.mjs`: nhận `--start-time`, `--end-time`, validation trước checkpoint/provider; thêm `queryYield` trước merge state.
- `src/discovery-yield.mjs`: helper báo cáo thuần, không I/O và không mutate job/state; không phải pipeline/persistence mới.
- `test/discovery-adapters.test.mjs`: mock transport, CLI, yield và state regressions.
- Tài liệu này.

## Request/date filters

Không truyền filter thì URL giữ nguyên hoàn toàn, gồm `query` và `recent_posts=true`. Start-only/end-only đều được hỗ trợ. Ngày là chuỗi ASCII `YYYY-MM-DD`, kiểm tra lịch Gregorian và năm nhuận; không chấp nhận format thiếu zero, ngày không tồn tại, timestamp, whitespace hoặc start lớn hơn end. Không đặt range mặc định, không chuyển giá trị gửi provider thành timestamp.

Live adapter vẫn yêu cầu opt-in/key/Free/no-cash attestations và đúng ngân sách cũ: tối đa một request, một credit trong cấu hình hiện tại, timeout tối đa 20 giây, manual redirect, không retry/pagination. Chỉ thêm optional URL parameters; không gọi Account Usage hoặc billing API.

Replay vẫn không cần key hoặc date filters. Nếu truyền filters khi replay, metadata ghi lại các tham số đó nhưng `mode=REPLAY`, `requestCount=0`, `providerSearchPerformed=false`: không tuyên bố capture lịch sử được lấy qua date filters.

## Freshness evidence

Không sửa `publishedAt`, `published_at` hoặc `match.freshness`:

- Nested capture không có ngày: hai publication fields vẫn null, freshness UNKNOWN.
- Timestamp hiện được normalizer hỗ trợ vẫn giữ trạng thái UNVERIFIED của provider matcher, kể cả nằm trong date window.
- `requestMetadata` tách filters được yêu cầu, có thực hiện provider search không, unknown publication dates, timestamp-present-unverified và verified-recent.
- `verifiedRecentRelevantVacancies` chỉ đếm nếu job **đã có** freshness `RECENT_7D` cùng publication timestamp hợp lệ trong 7 ngày, không ở tương lai. Không tạo trạng thái này từ date filters/firstSeenAt. Provider matcher hiện không phát sinh RECENT_7D, nên bằng chứng provider hiện tại cho số verified-recent bằng 0.
- Đây chỉ là accounting, không thêm gate chặn discovery hoặc thay đổi matching. 0 verified-recent không có nghĩa đã chứng minh không có JD mới.

## Yield/state semantics

`queryYield` mô tả duy nhất query/lượt hiện tại, tính trên jobs đã deduplicate **trước** khi merge lịch sử. Fields cũ, `metrics`, `storage`, `jobs` tích lũy và checkpoint format vẫn được giữ.

| Metric | Ý nghĩa |
| --- | --- |
| query/requestCount/creditsDeducted | Query thực của adapter và request/credits của lượt hiện tại; replay không gán lại chi phí lịch sử |
| totalItems/acceptedItems/rejectedItems | Item-level statistics hiện có; PARTIAL_SUCCESS không đổi |
| newSourcePosts/existingSourcePosts | Unique sourceIdentity chưa có/đã có trong registry trước lượt chạy |
| relevantSourcePosts/newRelevantSourcePosts | Distinct post có TARGET hoặc POTENTIAL vacancy; new là mới trong registry, không phải mới đăng |
| relevantVacancies/newRelevantVacancies | Số vacancy TARGET/POTENTIAL; new chỉ thuộc post mới trong registry |
| duplicateRate | `(existing unique posts + accepted duplicate occurrences trong response) / acceptedItems`; không tính rejected items; null nếu denominator=0 |
| freshnessUnknownCount | Số post không có publication timestamp hoặc freshness UNKNOWN |
| verifiedRecentRelevantVacancies | Relevant vacancies có verified recent evidence sẵn có, không suy từ filters |
| relevantVacanciesWithContactEmail | Own vacancy email hoặc email ở intro/shared footer được trích xuất rõ; không gán role ownership mới |

Post cũ có content update vẫn thuộc existingSourcePosts. Các counter `contentUpdated`, `extractionRefreshed`, `metadataUpdated`, `unchanged` của Phase A tiếp tục phân biệt nguyên nhân. Không đếm historical jobs không xuất hiện trong response hiện tại thành discovery mới.

Ba tỷ số per-credit lấy đúng `creditsDeducted` hữu hạn và >0 làm mẫu số. Unknown/zero credits trả null. `creditsUsedAlternateHeader` được giữ riêng, không cộng và không tự diễn giải cumulative semantics. Test dùng cả 0.5 credit để chứng minh không hardcode denominator=1; không có credit thật bị tiêu thụ.

## Ví dụ sanitized từ replay thực tế

Đây là metadata của **offline replay** capture 18.567 bytes, không phải live request hoặc bằng chứng hiệu lực date filter:

```json
{
  "scope": "CURRENT_RUN",
  "query": "tuyển fresher .NET Hà Nội",
  "requestCount": 0,
  "creditsDeducted": null,
  "creditsUsedAlternateHeader": null,
  "totalItems": 8,
  "acceptedItems": 8,
  "rejectedItems": 0,
  "newSourcePosts": 8,
  "existingSourcePosts": 0,
  "relevantSourcePosts": 4,
  "newRelevantSourcePosts": 4,
  "relevantVacancies": 4,
  "newRelevantVacancies": 4,
  "duplicateRate": 0,
  "freshnessUnknownCount": 8,
  "verifiedRecentRelevantVacancies": 0,
  "relevantVacanciesWithContactEmail": 4,
  "relevantVacanciesPerCredit": null,
  "newRelevantVacanciesPerCredit": null,
  "verifiedRecentVacanciesPerCredit": null,
  "requestMetadata": {
    "mode": "REPLAY",
    "startTime": "2026-10-01",
    "endTime": "2026-10-07",
    "dateFiltersRequested": true,
    "providerSearchPerformed": false,
    "providerDateFilterBehavior": "UNVERIFIED",
    "publicationDates": { "unknown": 8, "timestampPresentUnverified": 0, "verifiedRecent": 0 }
  }
}
```

Lần replay thứ hai cùng registry: newSourcePosts=0, existingSourcePosts=8, newRelevantSourcePosts=0, newRelevantVacancies=0, duplicateRate=1, unchanged=8. Relevant posts/vacancies vẫn là 4. Hai lần đều không có network; jobs arrays bằng nhau. File tạm của kiểm chứng được dọn, capture gốc không đổi. Không đưa raw JD/contact vào tài liệu hoặc stdout.

## Test results và giới hạn bằng chứng

- Baseline Phase A.1.1: 206 unit/config tests PASS, 8 browser tests PASS.
- B.0: **230/230 unit/config tests PASS**, thêm 24 tests; không skip hoặc nới assertions cũ.
- **8/8 browser tests PASS**, dùng Chrome local qua BROWSER_POC_EXECUTABLE, chỉ fixture loopback; không truy cập Facebook/provider thật.
- Syntax checks và `git diff --check`: PASS.
- Mock live adapter kiểm tra URL không filters giữ nguyên, hai filters, từng filter riêng, ngày nhuận, ngày sai/range ngược bị từ chối trước transport, unknown/UNVERIFIED publication, partial item isolation, separate credit headers, one-request/no-follow.
- CLI test chứng minh flags tới URL của adapter qua fake transport, lần hai không có post mới, invalid date không thay đổi state. Socket thật bị chặn trong các subprocess kiểm chứng.
- Tests state cũ tiếp tục chứng minh phân biệt raw update với extraction refresh và giữ historical jobs. Raw contentHash và matcher không thay đổi.
- Historical capture chỉ có một query; không tạo bảng so sánh giả giữa các query, không chứng minh bài nằm trong 7 ngày hoặc query nào tốt hơn.

## Checklist Phase B.1 — đề xuất, CHƯA THỰC HIỆN

### Gate trước mọi live request

1. Người dùng phê duyệt từng request, Free credits còn đủ, không auto-top-up/phát sinh tiền mặt; không in key.
2. Kiểm tra checkpoint được restore/backup và chưa hết retention; giữ one-request budget, không rerun/retry tự động.
3. **Giữ rolling cap 2 như yêu cầu.** B.0 không đọc/sửa GitHub variables. File workflow hiện có fallback `20` và hard validation tối đa `20`, nhưng effective cap có thể do `SOCIALAPIS_MAX_31_DAY_REQUESTS` override. Vì không gọi GitHub trong B.0, chưa xác minh effective remote value=2. Phải kiểm tra và review mismatch này trước B.1; không được mặc định có 20 lượt, không tăng cap để chạy hết ma trận.
4. Chọn rõ window date-only và cách xác minh thời gian đăng độc lập. Timezone, inclusive/exclusive boundaries và hiệu lực provider filters vẫn chưa biết.

### Ma trận đề xuất

| Thứ tự đề xuất | CLI query/filters | Mục đích |
| --- | --- | --- |
| 1 | `--query "tuyển fresher .NET Hà Nội"`, không date flags | Baseline live mới, không dùng capture cũ như kết quả hiện tại |
| 2 | Cùng query + `--start-time YYYY-MM-DD --end-time YYYY-MM-DD` đã duyệt | Kiểm tra filter; đối chiếu source IDs và timestamps thật nếu có |
| 3 | `--query "ASP.NET Core Hà Nội"` | Coverage variant, dùng cùng window để so sánh nếu được duyệt |
| 4 | `--query "C# Fresher Hà Nội"` | Kiểm tra C# entry-level coverage |
| 5 | `--query ".NET Developer Hà Nội"` | Kiểm tra query rộng hơn và vacancy-level matching |
| 6 | Một additional query HOẶC một cursor page được duyệt riêng | Chỉ khi còn budget và có contract đã xác minh |

Không chạy toàn bộ ma trận khi cap chỉ còn tối đa 2. Hai lượt baseline/date-filter đã dùng hết 2 nếu cửa sổ rolling đang trống; các lượt còn lại phải chờ ngân sách được phép, không lách cap hay tự tăng giới hạn. Mỗi lượt vẫn manual, requestCount tối đa 1 và không đổi billing controls.

### Workflow integration còn thiếu

Workflow hiện không có date inputs. B.0 **không sửa workflow**. B.1 cần review riêng:

- Thêm hai optional workflow_dispatch string inputs `start_time`, `end_time`, default empty.
- Truyền qua environment, tạo shell argument array có quote; chỉ thêm `--start-time`/`--end-time` khi giá trị không rỗng. Không interpolate input trực tiếp thành shell code.
- Giữ toàn bộ consent, checkpoint restore/persist, concurrency, rolling request cap, secret scope, timeout và single-request controls hiện có.
- Xuất artifact chứa queryYield để so sánh theo từng lượt, không coi các metrics tích lũy là discovery mới.
- Pagination chưa được implement. Payload cursor hiện chỉ được đọc, không follow. Trước optional page test phải xác minh **request cursor contract**; sau đó review bounded cursor input, accounting/checkpoint và một request độc lập. Không tự thêm loop hoặc orchestration.

Kết luận: **B.0 OFFLINE READY; B.1 LIVE/CLOUD VALIDATION NOT RUN**. Không stage/commit/push, trigger Actions, gọi provider/Account Usage/Gmail/AI, thêm dependency, tạo DB hoặc chỉnh production.
