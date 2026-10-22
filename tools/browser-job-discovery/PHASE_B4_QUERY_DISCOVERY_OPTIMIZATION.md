# B.4 — Facebook query discovery optimization

Baseline: `main`, `d41a958`. Implementation local + offline verification.
Không SocialAPIs/Facebook/Portfolio production calls, Actions dispatch, AI/Gmail,
DB writes, migrations, billing/secrets changes, schedule, stage/commit/push.
Giữ nguyên 11 evidence documents untracked từ trước.

## 1. Audit Portfolio — source of truth thực tế

Đã đọc Domain entities, EF configurations, initial migration, current schema,
Application handlers/contracts, controllers và candidate factual assembler:

| Bảng / entity | Dữ liệu và quy tắc thực tế |
|---|---|
| `technologies` / `Technology` | UUID Id, Name, Category, IsActive, DisplayOrder; không có proficiency hoặc learning status |
| `skills` / `Skill` | TechnologyId nullable FK, Name, ExperienceLevel, IsPublished; check constraint `USED/LEARNING/EXPLORING` |
| `experience_technologies` / `ExperienceTechnology` | composite PK ExperienceId + TechnologyId, DisplayOrder; FKs cascade |
| `project_technologies` / `ProjectTechnology` | composite PK ProjectId + TechnologyId, DisplayOrder; FKs cascade |
| `experiences`, `projects` | IsPublished kiểm soát public evidence; project Status khác publication, không tự loại project chỉ vì COMPLETED/ARCHIVED |

`CandidateFactualSnapshotAssembler` trong `JobFitFeatures.cs` đã có quy tắc:
published USED skills + active technologies gắn published experiences/projects
là demonstrated; LEARNING/EXPLORING là developing, sau đó loại demonstrated
khỏi developing. Backend scoring/assembler này **không bị sửa hay gọi lại**.
CandidateJobPreferences.PreferredTechnologies chỉ là preference, không đủ chứng
minh đã sử dụng. CV không được dùng làm nguồn công nghệ.

Public data có thể tái sử dụng:

- `GET /api/v1/public/portfolio`: active TechnologySummary, published PublicSkill
  (có technologyId/experienceLevel), published Experiences với active technology
  links. `GetPublicPortfolioQueryHandler` và `PublicJourneyTimeline` đã lọc ở server.
- `GET /api/v1/public/projects`: mọi published project, active technology links,
  order DisplayOrder/Id. Source thực tế là `PublicProjectFeatures.cs`; không có
  file `ProjectQueries.cs` trong baseline này.
- Home chỉ trả **featuredProjects**: không dùng nó để tuyên bố đầy đủ project
  evidence; export toàn bộ public projects nếu cần coverage đầy đủ.
- DTOs đã kiểm tra ở `ContentModels.cs`, `Phase4BModels.cs`; không thêm repository,
  DbContext, endpoint, technology catalogue, model hoặc migration.

## 2. Candidate Technology Profile boundary

`buildCandidateTechnologyProfile()` nhận snapshot whitelisted của các public
contracts hiện có; không tự fetch API, không đọc CV hoặc database. Snapshot shape:

```js
// home/projects là response đã được operator export trước đó, KHÔNG phải fetch
// chạy ngầm bởi tool. Chỉ truyền các trường tối thiểu, không paste toàn bộ profile.
const snapshot = {
  schemaVersion: 1,
  source: 'PORTFOLIO_PUBLIC_API_EXPORT',
  technologies: home.data.technologies,
  skills: home.data.skills.map(({id,name,technologyId,experienceLevel}) =>
    ({id,name,technologyId,experienceLevel})),
  experiences: home.data.experiences.map(({id,technologies}) => ({id,technologies})),
  projects: projects.data.map(({id,status,technologies}) => ({id,status,technologies}))
};
```

UIDs/levels/array bounds/duplicate identities được validate. Explicit inactive
technologies hoặc unpublished records không nâng mức evidence. Skill không có
FK không được tự nối bằng email/domain/tên gần giống; đây là hạn chế conservative.
Catalogue-only technology không thành kỹ năng. LEARNING/EXPLORING được giữ riêng;
nếu project mới PLANNED thì link chỉ là ý định, không nâng thành demonstrated.
Project status phải có trong export. Đây là guard conservative riêng của boundary
B.4; không sửa quy tắc/scoring của CandidateFactualSnapshotAssembler hiện hữu.
nếu cùng technology có project/experience usage thì effective USED, đồng thời
vẫn giữ declared learning level và nguồn evidence để audit. **USED không phải
EXPERT/mastery**. Không có demonstrated technology thì fail trước HTTP.

Profile có version, fingerprint, technology IDs, evidence IDs/kinds/levels. Cả
planner và matcher dùng cùng profile đó. Source luôn ghi
`OPERATOR_SUPPLIED_PUBLIC_EXPORT_NOT_LIVE_VERIFIED`: không xác thực được tác giả,
độ mới hoặc tính đầy đủ của JSON export chỉ từ file. Snapshot production tự động
**CHƯA TÍCH HỢP**. Raw snapshot/contact/CV extra fields không được persist vào
state; tuyệt đối không đưa private data vào GitHub workflow inputs.

## 3. Query planner V1 và cách chạy

Trong workflow/CLI **hiện có**, policy có thể truyền:

```js
{
  planQueries: true,
  portfolioSnapshot: snapshot,
  profile: {locations: ['Hanoi', 'Hà Nội'], seniorities: ['Intern', 'Fresher', 'Junior']},
  target: 5
}
```

Để query input rỗng để planner chọn; hoặc override query để so sánh có kiểm soát.
Legacy explicit `{profile:{technologies,locations,seniorities},query}` vẫn chạy,
được ghi `legacy-operator-profile`, không gọi nó là verified Portfolio profile.
Không cho automatic planner chạy khi thiếu Portfolio snapshot.

- Tạo queries từ USED technologies thực tế, không Groups/company/post URL seeds.
- Aliases chỉ là spelling equivalence (`CSharp/C sharp/C#`, `DotNet/.NET`,
  `ASP NET Core/ASP.NET Core`, `Entity Framework Core/EF Core`); không phải bảng
  catalogue mới hay suy ra biết ASP.NET Core chỉ vì có C#.
- Bare `NET` bị loại vì ambiguous; không match NET salary hoặc NET PARTNERS như
  một .NET skill. Unknown names như Node.js dùng literal-boundary regex từ source
  technology, không cần thêm vào một danh sách production .NET cố định.
- Round-robin templates: technology + location; thêm seniority; Developer;
  tuyển dụng. Tối đa 32 distinct normalized queries, tối đa 4 location variants
  và 8 seniority variants trong generation; explicit query override không bị
  giới hạn vào pool. Pool giới hạn không đồng nghĩa đã khảo sát toàn bộ profile.
- Normalize spelling/case/Unicode, Hanoi/Ha Noi/Hà Nội và HCM/Ho Chi Minh/Sài Gòn để tránh equivalent
  queries. Khác seniority không mặc định tương đương nhau.
  Matcher nhận Intern/Internship/thực tập là cùng mức; không đồng nhất Junior/Senior.
- Ranking theo **new relevant content opportunities / verified deducted credit**,
  cùng profile fingerprint + normalized query identity. Tie-break ưu tiên
  experience usage, project usage, USED skill, rồi query identity.
- Unmeasured candidates có exploration prior **0.25** chỉ để sắp xếp, được ghi
  `UNMEASURED_EXPLORATION_PRIOR`, **không phải yield đo được**. Unknown/zero credit
  denominator không được bịa thành measured score. Adaptive chỉ dùng measured
  history; không có LLM hoặc provider call bổ sung cho planning.
- History có page number/depth và date window; ranking hiện tổng hợp historical
  windows cùng profile, chưa hiệu chỉnh mùa tuyển dụng/provider date semantics.
  Kết quả 0 ở depth 1/2 không chứng minh query không có bài ở những pages sau.
- Resume giữ query/rotating/adaptive plan cũ, không tự rerank đổi query giữa chain.

Ví dụ candidates **chỉ với synthetic test profile** có USED C#/.NET/Angular:
`C# Hanoi`, `C# Fresher Hanoi`, `.NET Junior Hanoi`, `Angular Hanoi`.
Nếu Portfolio có ASP.NET Core USED thì tự có candidates tương ứng. Đây không phải
production profile hoặc bằng chứng có JD từ query đó.

## 4. Matching quality — lỗi thật và sửa

Baseline matcher chỉ dùng `primaryTechnologies` từ lexer hữu hạn, exact string
comparison và binary TARGET/NOT. Do đó compact recruitment headline/CSharp hoặc
technology ngoài lexer có false negatives. Nó cũng chưa chặn closed recruitment
và có thể promote Java/React vì một supporting SQL requirement.

Matcher mới xét title + mandatory requirements/compact recruitment headline,
loại optional skills. Explicit competing title stack không được promote bằng
supporting SQL. Title/role evidence không vay từ role khác; footer/body mention
không đủ để tự tạo skill. Phân loại:

- TARGET_MATCH: demonstrated role technology + location + explicit target level.
- POTENTIAL_MATCH: developing-only compatible role, hoặc experience <=2 khi thiếu
  explicit level. Developing không được coi là mastered/TARGET.
- INSUFFICIENT_DATA: thiếu technology/level/location hoặc địa điểm ambiguous.
- NOT_MATCHED: khác stack/level/location rõ ràng, seeker/non-target role, closed.

Giới hạn: vẫn là deterministic parser, không general NLP. Role segmentation,
tiếng lóng, wording đóng tuyển chưa biết, generic requirements hoặc location chưa
được nhận diện có thể cần review; không quy hết 0 matches cho provider.

### Before/after trên cùng evidence

Đã chạy actual baseline `classifyProfile` từ commit d41a958 và implementation mới
trên cùng raw input/profile (không chỉ so hai expected mocks):

| Synthetic fixture | Baseline | B.4 |
|---|---|---|
| Compact “Tuyển Fresher C#.NET tại Hà Nội” | NOT_MATCHED | TARGET_MATCH |
| Node.js demonstrated role | NOT_MATCHED | TARGET_MATCH |
| Bài kỹ thuật đã tuyển đủ/ngừng nhận hồ sơ | TARGET_MATCH | NOT_MATCHED |
| Java/React title, SQL supporting requirement | TARGET_MATCH | NOT_MATCHED |

Capture thật local **18.567 bytes**, 8/8 accepted, cùng fixture profile dùng để
so sánh: baseline **4 TARGET + 4 NOT**; B.4 **4 TARGET + 3 NOT + 1 INSUFFICIENT**.
Vẫn 4 relevant vacancies; cả 8 publication dates UNKNOWN. Reconstructed content
registry có 20 groups của **mọi vacancy candidates**, không phải 20 verified JD
hay 20 relevant jobs. Không có response của run 4 pages/0 matches hiện tại để
quy nguyên nhân cụ thể hoặc khẳng định matcher mới repair được live run đó.

## 5. Content-level dedup và provenance

- `sourceIdentity` dedup giữ nguyên. Không xóa job/post, raw body, URL, post ID,
  contentHash hoặc publishedAt cũ.
- Canonical **content** opportunity ID dùng full NFC/whitespace-normalized body
  + role block + title + vacancy index. Full contacts/numbers/deadlines vẫn tham
  gia identity: shared footer/company không đủ gộp hai roles hoặc các campaign
  ghi deadline khác nhau. Raw contentHash cũ không thay bằng hash normalized này.
- Tối thiểu title và body 80 ký tự để grouping; thiếu evidence/short posts giữ
  SOURCE_ONLY identity. Multi-vacancy posts giữ nhiều opportunity identities.
- Exact repost groups giữ tất cả sources (sourceIdentity/postId/URL/vacancyIndex),
  confidence `EXACT_CONTENT_REPOST_GROUP`, **không phải** verified same hiring
  campaign. Identical wording trong hai đợt tuyển nhưng không có thông tin phân
  biệt vẫn là ambiguity; mọi raw sources được giữ để review, không auto-apply.
- Near duplicates dùng token Jaccard >=**0.92**, cùng role titles và numeric
  evidence; <=100 historical source records, <=100 suggestions/page. Chỉ
  `REVIEW_ONLY_NOT_MERGED`: không đổi canonical ID, không xóa raw evidence và
  không giảm candidate count vì near similarity chưa chứng minh cùng opening.
- Legacy records chưa có vacancies được parse lại **trong memory** để so sánh,
  không rewrite raw registry/hash; không tính content cũ là opportunity mới chỉ
  vì vừa được nâng phiên bản extraction metadata.
- Fresh logical executions dùng distinct new relevant content opportunities cho
  target, tránh đủ 5 chỉ vì một JD repost năm lần. Pending B.3 chains vẫn giữ
  source-based target semantics cũ khi chưa có opportunityMode; không đổi giữa
  resume. Chỉ số relevant vacancies/source posts vẫn được báo riêng.

## 6. Yield allocation, metrics và safety

Per page: query/normalized identity, profile version/fingerprint, page number,
raw/accepted/rejected counts, unique source posts, canonical/new opportunities,
new relevant opportunities, vacancy counts, duplicate/repost ratio (theo vacancy
opportunity occurrences), near-review suggestions, UNKNOWN/UNVERIFIED freshness,
HTTP attempts và deducted credits, new relevant opportunities per credit.

Workflow summary không dùng accumulated state.jobs làm response count; có scoped
opportunities và depth đã khảo sát. Registry là accumulated history riêng. Console
chỉ ghi query fingerprint/depth, không post body/contact/cursor; details thuộc
artifact đã consent. Credits-Used không thay/add vào Credits-Deducted.

Nếu **hai core pages liên tiếp** cho 0 new relevant opportunities và rotating
query cùng snapshot profile có measured positive yield, có thể thử rotating trước
khi đọc sâu tiếp. Không pivot chỉ vì page 1 không có match. Core cursor vẫn mã
hóa/deferred, sau rotating có thể quay lại core hoặc resume qua workflow mới.
Adaptive dùng distinct-opportunity yield, không bị 999 duplicate vacancies làm
sai ranking. Không có measured alternative thì không suy diễn lợi ích để pivot.

Max 4 core pages + 1 rotating + 1 adaptive; tối đa 6 HTTP attempts/workflow,
production cap 6/day theo Asia/Ho_Chi_Minh và ledger conservative như trước.
Không retry, paid fallback, top-up hoặc schedule. Monthly Free-balance guard
production chưa có: production vẫn fail closed/OFF. Free/no-cash/public-artifact
consent, claim/result receipts, budgets, cursor expiry/corruption checks giữ nguyên.

## 7. Files và kiểm chứng

Modified:

- `.github/workflows/socialapis-job-discovery.yml`: optional query override,
  snapshot/planner policy description; manual-only/secret scope không đổi.
- `tools/browser-job-discovery/package.json`: đưa suite B.4 vào npm test.
- `src/pagination-cli.mjs`: resolve controlled profile/query policy ở cùng entrypoint.
- `src/pagination-orchestrator.mjs`: matcher integration, content target/yield,
  metrics, measured query pivot và deferred cursor preservation.
- `test/pagination.test.mjs`: các synthetic distinct jobs có reference riêng,
  tránh vô tình test repost khi mục tiêu fixture là pagination identities.
- `test/workflow.test.mjs`: optional input và actual CLI snapshot→planner→matcher.

New (src/test dưới `tools/browser-job-discovery`):

- `src/candidate-technology-profile.mjs`
- `src/profile-matching.mjs`
- `src/content-opportunities.mjs`
- `test/query-optimization.test.mjs`
- Báo cáo này.

Kiểm chứng cuối:

- `npm test`: **308 passed, 0 failed, 0 skipped**.
- `npm run test:browser`: **8 passed, 0 failed, 0 skipped**, Chrome local/loopback.
- YAML parse workflow + composite action: **2 PASS**.
- `git diff --check`: **PASS**; không stage files.

Không source changes trong backend/frontend, schema, migrations, auth, Gmail,
CV package hoặc application lifecycle. Git diff --stat chỉ tính tracked files;
các modules/test/report mới vẫn untracked, không bị bỏ sót trong danh sách trên.

## 8. Live comparison tối đa 2–3 Free credits — CHƯA CHẠY

1. Người dùng tự review/commit/push nếu chấp thuận. Chuẩn bị minimal snapshot từ
   public Portfolio data thật; kiểm tra coverage, USED evidence, Free balance,
   no-auto-top-up/no-cash và public artifact storage consent. Không paste CV,
   API keys, private contacts hay toàn bộ admin payload vào workflow inputs.
2. Chạy 2 hoặc 3 manual workflow executions, mỗi run max_requests=1/max_credits=1.
   Dùng cùng profile/date window; chọn query khác nhau từ `queryPlan.candidates`
   bằng explicit query override, resume_continuation=false. Mỗi run có thể dừng
   sau một request; không ép dùng credit còn lại khi lỗi/quota/unknown cost.
3. Backup prior artifact trước fresh query comparison: fresh logical execution
   thay current plan như trước; muốn tiếp tục chain cũ phải dùng đúng saved
   checkpoint/query/policy với resume_continuation=true, không dựng lại cursor.
4. So unique relevant content opportunities/credit, accepted/rejected và depth=1;
   không gọi query “không có JD” từ một page. Kiểm tra raw evidence/citations tại
   artifacts đã consent, không công khai contact/cursor. Không kết luận freshness
   verified từ date filters hoặc thời điểm discovery/repost.
5. Sau dữ liệu thật mới cân nhắc query tiếp theo hoặc resume; không bật scheduler
   khi monthly Free/zero-cash enforcement chưa được triển khai và kiểm chứng.

**OFFLINE IMPLEMENTATION; PRODUCTION PROFILE FETCH / LIVE QUERY IMPROVEMENT /
CLOUD EXECUTION B.4 CHƯA ĐƯỢC XÁC MINH.**
