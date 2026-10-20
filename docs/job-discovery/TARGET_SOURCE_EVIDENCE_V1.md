# Target Source Evidence V1

Ngày thu thập: 2026-10-20
Hình thức: HTTP GET read-only, bounded, không credentials.

## 1. Executive evidence ledger

| Source | DISCOVERED | ACCESSIBLE | AUTHORIZED | VERIFIED_LIVE | TARGET_MATCH | BLOCKED |
|---|---:|---:|---:|---:|---:|---:|
| NCC Career WordPress REST/RSS | YES | YES | YES, anonymous public GET | YES | YES | NO |
| Softdreams WordPress REST | YES | YES | YES, anonymous public GET | YES | Partial: API thiếu location | NO cho API; HTML chưa duyệt |
| AvePoint Greenhouse | YES | YES | YES, official public GET | YES | NO current match | NO |
| LinkedIn | YES | Browser/search only | NO automated path verified | Không gọi | Có manual signals | YES |
| ITviec | YES | Browser | NO nếu chưa written consent | Manual evidence only | Có signals | YES |
| Joboko/TopCV | YES | Browser/search | NO automated path verified | Không gọi API | Có mirrors | YES |

## 2. NCC request evidence

### 2.1 API discovery

```http
GET https://career.ncc.plus/wp-json/
```

Observed:

- HTTP read thành công.
- Namespace `wp/v2` được công bố.
- Không gửi `Authorization`, cookie hoặc token.

```http
GET https://career.ncc.plus/wp-json/wp/v2/types
```

Observed public types gồm `post`, `page`, `attachment` và các WordPress types khác.

### 2.2 Target discovery

```http
GET https://career.ncc.plus/wp-json/wp/v2/search?search=.Net&per_page=20&type=post
```

Observed:

- 7 search results.
- Target result:
  - `id`: `256`
  - `title`: `Internship Programs: .NET`
  - `subtype`: `post`
  - `url`: `https://career.ncc.plus/internship-programs-net/`

### 2.3 Canonical target record

```http
GET https://career.ncc.plus/wp-json/wp/v2/posts/256?context=view
```

Safe extracted fields:

```json
{
  "id": 256,
  "date": "2021-05-11T14:39:41",
  "modified": "2026-09-17T09:28:48",
  "status": "publish",
  "link": "https://career.ncc.plus/internship-programs-net/",
  "title": "Internship Programs: .NET",
  "address": ["Ha Noi 3"],
  "meta": {
    "salary": "Negotiation",
    "type": "Full time/ Part time",
    "name_job": "Internship Programs - .Net",
    "short_description": "Full-time or Part-time (Minimum 30hs/week)"
  }
}
```

Content evidence:

- Employer text identifies NCCPLUS Vietnam Joint Stock Company.
- Training/job content mentions RESTful API and ASP.NET Boilerplate.
- Candidate requirement mentions basic `.Net`, OOP and database.
- Full-time or part-time, minimum 30 hours/week.
- Page remains listed on current [NCC career index](https://career.ncc.plus/).

No email address, applicant data, credential or private data was collected into the repository.

### 2.4 RSS evidence

```http
GET https://career.ncc.plus/feed/
```

Observed:

- RSS 2.0.
- `lastBuildDate`: `Wed, 07 Oct 2026 04:00:32 +0000`.
- 5 items in current feed response.
- Target item:
  - title `Internship Programs: .NET`;
  - canonical link above;
  - `pubDate` `Tue, 11 May 2021 07:39:41 +0000`.

RSS therefore supports cheap change discovery, but its publication date proves the target is not a new seven-day posting.

### 2.5 robots and authorization evidence

```text
User-agent: *
Disallow:
Sitemap: https://career.ncc.plus/sitemap_index.xml
```

WordPress official documentation states the REST API supplies public data accessible anonymously and defines machine-readable discovery/list/retrieve endpoints:

- <https://developer.wordpress.org/rest-api/reference/>
- <https://developer.wordpress.org/rest-api/reference/posts/>
- <https://developer.wordpress.org/rest-api/frequently-asked-questions/>

Evidence classification:

- `DISCOVERED`: PASS.
- `ACCESSIBLE`: PASS.
- `AUTHORIZED`: PASS for anonymous bounded GET through the exposed API/feed.
- `VERIFIED_LIVE`: PASS.
- `TARGET_MATCH`: PASS.
- Seven-day freshness: FAIL.

## 3. Softdreams evidence

### 3.1 API discovery and target

```http
GET https://softdreams.vn/wp-json/
GET https://softdreams.vn/wp-json/wp/v2/search?per_page=20&type=post&search=.Net
GET https://softdreams.vn/wp-json/wp/v2/types/tuyen-dung
GET https://softdreams.vn/wp-json/wp/v2/tuyen-dung/1712?context=view
```

Observed:

```json
{
  "id": 1712,
  "date": "2024-04-02T16:16:12",
  "modified": "2026-10-16T10:34:19",
  "status": "publish",
  "title": ".Net/C# Developer (Fresher – Junior)",
  "link": "https://softdreams.vn/tuyen-dung/fresher-junior-dotnet/"
}
```

Public API content includes:

- .NET MVC 5;
- Web API;
- Entity Framework;
- experience 6 months–3 years;
- SQL Server and related stack.

Canonical page shows Hà Nội locations and deadline 31/10/2026, but those fields were not returned by the REST record. `modified` is within seven days of the audit; `date` is not. No claim is made that it was newly posted.

Decision: credible second source candidate, but not counted as the sole acceptance proof until the location is available through the approved API/feed or page retrieval receives a separate policy decision.

## 4. Greenhouse/AvePoint evidence

Official API contract:

- <https://docs.greenhouse.io/job-board>
- Greenhouse explicitly states job-board data is public and GET endpoints require no authentication.

Requests:

```http
GET https://boards-api.greenhouse.io/v1/boards/avepoint/jobs?content=true
GET https://boards-api.greenhouse.io/v1/boards/avepoint/jobs/6200673
```

Observed:

- list total: 291 current jobs;
- current Hanoi title matches for `.NET|C#|Software Developer`: 0;
- former indexed `Junior Software Developer - Hanoi` ID `6200673`: HTTP 404;
- no current TARGET_MATCH.

This proves why search-index snippets cannot replace canonical endpoint validation.

## 5. Bounded experiment accounting

The experiment used only GET requests against:

- two WordPress REST sites;
- one WordPress RSS feed;
- two robots/API-discovery surfaces;
- one official Greenhouse Job Board API.

Repeated GETs were limited to extracting safe fields after broad filters produced false positives or truncated diagnostics. No pagination beyond the first response, application POST, browser login, cookie, proxy, CAPTCHA handling or stealth was used.

No request contained:

- credentials;
- personal/session cookies;
- production API secrets;
- applicant data;
- payment information.

Actual provider cost: **$0**.

## 6. Request limit and GitHub Actions profile

No documented numeric quota was found for the two site-owned WordPress APIs. That is a risk, not permission to issue unlimited requests.

Safe PoC profile:

1. one NCC RSS request per manual run;
2. one REST detail request only for an unseen/changed item;
3. maximum 5 job details and 10 total HTTP requests/run;
4. 30-second request timeout and 10-minute workflow timeout;
5. at most two runs/day during evaluation;
6. stop on 401/403/429 or schema drift; no scraping fallback;
7. identifiable User-Agent with repository/contact information;
8. retain only minimum normalized fields plus canonical evidence URL.

All accepted endpoints use public HTTPS GET and need no secret, so they are compatible with GitHub Actions. Actual execution from a GitHub runner remains unverified because no workflow was created or triggered.

## 7. Final evidence verdict

### GO

`career.ncc.plus` satisfies the explicit acceptance gate: allowed anonymous machine-readable endpoint, live target JD, canonical URL, real company/title/location and `$0` cloud-compatible access.

### Caveats

- No target posting published in the last seven days was verified.
- NCC target is a long-lived/evergreen internship post.
- Source discovery at runtime still needs a lawful discovery channel; this spike discovered the source during bounded research, not via an implemented agent.
- Before scheduled production polling, perform one manual GitHub Actions run and re-check site terms/contact preference.

### NO-GO paths

No scraping adapter should be built for LinkedIn, ITviec, TopCV or Joboko from this evidence. No scheduled workflow should be enabled in this phase.
