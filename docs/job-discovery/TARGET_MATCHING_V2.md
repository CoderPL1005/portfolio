# Target Matching V2 — deterministic design

Trạng thái: **TARGET_MATCHING_DESIGN_READY**. Đây là specification, chưa implement classifier, không dùng AI/Gemini và không thay đổi fit scoring hiện có.

## 1. Input và provenance

Mỗi record độc lập có:

```text
source_url, source_record_id, retrieved_at
employer_raw, employer_identity_status
title_raw, requirements_raw
technology_tokens + từng evidence span
minimum_experience_years: decimal|null
maximum_experience_years: decimal|null
experience_evidence, experience_conflict: boolean
seniority_labels + evidence spans
location_raw, location_status, normalized_location
published_at: timestamp|null, modified_at: timestamp|null
authorization_status, source_access_status
```

Không merge Facebook/website chỉ vì cùng employer hoặc backend technology. Record reconciliation cần identity evidence riêng. Unknown luôn là null hoặc UNVERIFIED; không default min/max=0.

## 2. Hai profile công nghệ

### Backend discovery profile

Ít nhất một skill backend .NET/C#/ASP.NET có evidence trong title/requirements, không chỉ trong marketing text hoặc tên unrelated link. Có Angular được giữ như evidence bổ sung; không bắt buộc cho profile này.

### Fullstack .NET/Angular profile của test case

Backend .NET/C# evidence **và** explicit Angular evidence. TypeScript một mình không đồng nghĩa Angular; AngularJS không đồng nghĩa Angular hiện đại. Token phải được xác định theo boundary, không match `network` thành `.NET`. Chuẩn hóa Unicode NFC, case-insensitive; bảo toàn text gốc/evidence.

Các family mapping được phép: `dotnet` ↔ `.NET`; `C sharp` ↔ `C#`. `.NET Core` không tự trở thành `ASP.NET Core`; MVC/jQuery không tự trở thành Angular.

Report phải có `profile`, `backend_match`, `angular_match` riêng để không biến backend-only thành fullstack match.

## 3. Location gate

- `Hà Nội`, `Ha Noi`, `Hanoi` được normalize thành Hanoi khi có evidence trong job location/requirements.
- Office address của công ty không tự trở thành job location.
- `Việt Nam`, `remote`, `global`, hoặc địa điểm thiếu dữ liệu không tự coi là Hà Nội.
- `Hà Nội hoặc theo dự án`: Hanoi được nêu nhưng ghi thêm `project_location_variability=true`; không khẳng định 100% onsite Hanoi.
- Record không có location: `location_status=UNVERIFIED`, không được tính vào confirmed target count.

Task này dùng Hà Nội; không mở rộng target thành remote Vietnam như các task trước.

## 4. Experience parser

Chỉ đọc requirements có ngữ cảnh kinh nghiệm, không lấy con số từ tuổi công ty, lương, deadline hoặc “mentor junior”. Parser rule-based hỗ trợ Vietnamese và English.

| Evidence | minimum | maximum | Ghi chú |
|---|---:|---:|---|
| `không yêu cầu kinh nghiệm`, `no experience required` | 0 | null | Không có upper bound được tuyên bố |
| `0–1 năm`, `0 to 1 year` | 0 | 1 | Range explicit |
| `0–2 years` | 0 | 2 | Không đổi thành minimum=2 |
| `6 tháng kinh nghiệm trở lên` | 0.5 | null | Convert months/12 |
| `từ 1 năm`, `at least 1 year`, `1+ years` | 1 | null | Không gán max=2 |
| `1–2 years` | 1 | 2 | Cả hai bounds explicit |
| `up to 2 years` | null | 2 | Không tự gán minimum=0 |
| `2+ years` | 2 | null | Chỉ biết lower bound |
| `3–5 years` | 3 | 5 | Ngoài entry-level target |
| Không có evidence | null | null | Unknown |

Nhiều yêu cầu theo từng skill được giữ riêng; không cộng số năm có thể trùng thời gian. Overall hard minimum chỉ chọn khi câu mô tả rõ kinh nghiệm tổng thể hoặc nhóm skill bắt buộc. Các câu optional không trở thành hard minimum. Nếu contradictory/ambiguous, giữ conflict và yêu cầu review, không lấy giá trị thấp nhất để match.

## 5. Decision order

Bốn nhãn là classification theo **profile được chọn**, không phải approval để ingest/ứng tuyển. Gate authorization hoạt động riêng.

1. Contradictory evidence: `NOT_MATCHED` kèm reason `EXPERIENCE_CONFLICT`. Missing technology/location xử lý ở bước 2; missing numerical experience không chặn explicit Junior/Intern ở bước 4, nhưng không đủ cho bước 5–6. Unknown không đồng nghĩa candidate chắc chắn không phù hợp.
2. Technology profile fail hoặc location không xác nhận: `NOT_MATCHED`, reason `TECHNOLOGY_MISMATCH`/`LOCATION_UNVERIFIED`/`LOCATION_MISMATCH`.
3. Explicit Senior/Lead/Middle-only hoặc hard minimum >2: `NOT_MATCHED`, ngay cả khi body có từ “junior” nói về đồng nghiệp. Nhãn Junior đi kèm hard minimum >2 tạo conflict, không được exact-match.
4. Title/structured seniority nói rõ Intern/Internship/Fresher/Junior, không có conflict/hard exclusion: `EXACT_ENTRY_LEVEL`.
5. Không có explicit level nhưng min xác minh trong [0,1], và nếu max tồn tại thì max≤1: `POTENTIAL_ENTRY_LEVEL`. Explicit `0–2` cũng là POTENTIAL vì chấp nhận người bắt đầu; reason `EXPLICIT_ZERO_TO_TWO_RANGE`, không giới hạn max thành 1.
6. Không có explicit level, min nằm trong [1,2], không conflict, technology profile fit: `STRETCH_OPPORTUNITY`. `min=1,max=null` thuộc nhóm này, reason `LOWER_BOUND_ONLY`; không khẳng định employer giới hạn tối đa 2 năm.
7. Các trường hợp còn lại: `NOT_MATCHED` với reason `SENIORITY_UNVERIFIED` hoặc `EXPERIENCE_NOT_IN_TARGET`.

Priority step 5 trước step 6 xử lý min=1/max=1 là potential; min=1/max=2 hoặc null là stretch. Min=0/max=null là potential nếu có explicit no-experience evidence; min=0/max=5 không thành potential. Với profile fullstack, thiếu Angular không được cứu bằng kinh nghiệm thấp.

## 6. Freshness và authorization là hai trục riêng

`freshness`:

- `RECENT_7D` khi absolute published_at đáng tin trong [evaluation_time−7d,evaluation_time].
- `OLDER` nếu published_at cũ hơn.
- `UNKNOWN` nếu thiếu hoặc chỉ có relative string không gắn observation timestamp đáng tin.
- `INVALID_FUTURE_DATE` nếu date vượt thời điểm đo ngoài timezone/clock tolerance xác định.

Modified_at, retrieved_at, search crawl time không được thay thế published_at. Facebook relative time giữ raw evidence, không tự bịa absolute date.

`eligible_for_automated_ingestion` chỉ true khi source có authorization đã review, JD accessible, target label được chấp nhận, location/profile verified và không conflict. Job được exact/potential/stretch không làm source tự trở thành authorized.

## 7. Áp dụng ground truth

| Record | Backend profile | Fullstack .NET/Angular profile | Điều chưa xác minh |
|---|---|---|---|
| Facebook theo dữ kiện người dùng | Không tính confirmed target | Không tính confirmed target | Location, post/image trực tiếp, absolute date và source access |
| Candidate website | `STRETCH_OPPORTUNITY` | `NOT_MATCHED: TECHNOLOGY_MISMATCH` | Angular absent; employer linkage/poll permission chưa xác minh |

Facebook có min=1/max=null theo input, nhưng location chưa xác minh nên reason `LOCATION_UNVERIFIED`. Website có evidence location riêng; không dùng nó sửa Facebook record. Backend-only stretch không chứng minh đây là vacancy DTMED trong ảnh.

## 8. Deterministic test cases cần implement sau approval

Các case sau là fixtures **giả định để test rule**, không phải JD thật tìm được:

| Case | Input rút gọn | Expected |
|---:|---|---|
| 1 | Junior .NET + Angular, Hanoi, không hard minimum cao | EXACT_ENTRY_LEVEL |
| 2 | ASP.NET Internship, Hanoi, backend profile | EXACT_ENTRY_LEVEL |
| 3 | .NET/Angular, Hanoi, 0–1 years, no level | POTENTIAL_ENTRY_LEVEL |
| 4 | .NET/Angular, Hanoi, 0–2 years | POTENTIAL_ENTRY_LEVEL; min0/max2 |
| 5 | .NET/Angular, Hanoi, 1+ years | STRETCH; min1/max=null |
| 6 | .NET/Angular, Hanoi, 1–2 years | STRETCH; min1/max2 |
| 7 | Junior title + hard minimum 5 years | NOT_MATCHED + conflict |
| 8 | Senior .NET developer mentoring juniors | NOT_MATCHED |
| 9 | Title .NET/Angular, location null | NOT_MATCHED + LOCATION_UNVERIFIED |
| 10 | .NET + jQuery, fullstack profile | NOT_MATCHED + TECHNOLOGY_MISMATCH |
| 11 | .NET + AngularJS, current Angular profile | NOT_MATCHED |
| 12 | .NET/Angular, Hanoi, up to 2 years only | NOT_MATCHED + SENIORITY_UNVERIFIED |
| 13 | .NET/Angular, Hanoi, 6+ months | POTENTIAL_ENTRY_LEVEL; min0.5/max=null |
| 14 | .NET/Angular, Hanoi, 3–5 years | NOT_MATCHED |
| 15 | .NET/Angular + Hanoi + min1, source unauthorized | STRETCH, automated ingestion=false |
| 16 | Post date 2021, modified yesterday | OLDER, không RECENT_7D |
| 17 | Location Vietnam remote only | NOT_MATCHED cho Hanoi profile |
| 18 | Same title/company, Angular khác nhau giữa hai URL | Không auto-merge records |

Tests cũng cần bắt canonical URL dedup, query không employer seed và budget bao gồm redirect/retry.

## 9. Dedup/report

Ưu tiên provider record ID + provider namespace, sau đó canonical URL chuẩn hóa. Query variants cùng guid chỉ tính một JD. Không bỏ query param có ý nghĩa job ID; không hợp nhất hai JD chỉ vì giống title/company. Fuzzy duplicates được flag riêng cho review.

Metrics: `source_candidates`, `records`, `unique_jobs`, `confirmed_target_matches_by_profile`, `duplicates`, `unknown_location`, `unknown_experience`, `unauthorized_sources`, request attempts và phí provider. Mẫu rỗng thì precision/duplicate rate=N/A. Đếm domain nguồn downstream riêng với aggregator provider.
