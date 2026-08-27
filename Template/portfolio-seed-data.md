# Portfolio Seed Data — Nguyễn Đình Phúc

> **Status:** Content checkpoint before implementation  
> **Purpose:** Canonical source for the initial portfolio seed. This file separates information supported by prior conversations/CV from items that still need confirmation and mock content that must not be imported from Stitch.
>
> **Rule:** `CONFIRMED` data may be used for `portfolio.seed.json`. `NEEDS_CONFIRMATION` data must not be silently invented. `DO_NOT_USE` data is UI mock/demo content and must not be treated as a personal fact.

---

## 1. Profile

**Status: CONFIRMED**

| Field | Seed value |
|---|---|
| Full name | Nguyễn Đình Phúc |
| Education status | Final-year Software Engineering student |
| University | Hanoi University of Civil Engineering |
| Primary career direction | Backend .NET Developer |
| Secondary direction | Full-stack .NET + Angular |
| Current technical focus | Backend engineering, scalable software, AI/RAG integration |
| Primary stack | C# / ASP.NET Core / Angular / SQL Server |
| GitHub | https://github.com/CoderPL1005 |

### Recommended public headline

```text
Backend .NET Developer | ASP.NET Core • Angular • SQL
```

This stays close to the CV headline **BACKEND .NET DEVELOPER** while still showing the full-stack technologies used in real work.

### Recommended short bio

```text
Final-year Software Engineering student focused on backend development with
C# and ASP.NET Core. Experienced in building REST APIs, working with SQL Server,
Angular-based web applications, authentication/authorization, and clean backend
architecture. Currently expanding into Python, cloud engineering, and AI/RAG
integration through hands-on projects.
```

### Recommended hero copy

```text
Building robust backend systems and intelligent web applications.
```

```text
I am a Backend .NET Developer focused on ASP.NET Core, SQL, Angular,
scalable application architecture, and practical AI/RAG integration.
```

### Profile fields still requiring confirmation

**Status: NEEDS_CONFIRMATION**

- Public email address
- Phone number, if it should appear publicly
- LinkedIn URL
- Exact university start date
- Expected graduation date
- GPA, if it should be public
- Avatar/profile image
- Public CV file
- Whether `Hanoi, Vietnam` should be displayed as location
- Exact wording for internship availability/current job-seeking status

---

## 2. Professional Experience

### RTC Technology Vietnam

**Status: CONFIRMED**

```yaml
company: RTC Technology Vietnam
position: Software Developer Intern
start_date: 2025-08
end_date: null
is_current: true
```

### Summary

```text
Participating in the migration and development of ERP functionality from
WinForms desktop applications to a modern web architecture using Angular,
ASP.NET Core and SQL Server.
```

### Responsibilities / highlights

- Participated in migrating ERP functionality from **WinForms to Web**.
- Developed frontend functionality using **Angular** and **NG-ZORRO**.
- Worked with **ASP.NET / ASP.NET Core** backend APIs.
- Integrated frontend and backend through **REST APIs**.
- Worked extensively with **SQL Server**.
- Worked with **SQL Server Stored Procedures**.
- Participated in query/database work and query optimization.
- Connected Angular UI flows with backend APIs and database operations.

### Technologies

```text
C#
ASP.NET Core
ASP.NET
Angular
TypeScript
NG-ZORRO
REST API
SQL Server
Stored Procedures
```

### Do not inflate this experience

Do not convert this internship into titles such as:

```text
Senior Backend Developer
Backend Lead
Software Architect
```

unless future real experience supports those titles.

---

## 3. Projects

# 3.1 SchoolSaaS

**Status: CONFIRMED**

```yaml
name: SchoolSaaS
slug: school-saas
project_type: Team / Portfolio Project
start_date: 2026-05
end_date: null
is_current: true
team_size: 2
recommended_role: Backend / Full-stack Developer
```

> Prior CV/history supports **05/2026–Present** and a **2-person team**. Stitch mock content showing `Oct 2023 – Present` or `Backend Lead` must not override this.

### Public title

```text
Multi-tenant School Management SaaS
```

### Summary

```text
A multi-tenant school management platform designed around secure tenant
isolation, authentication, authorization, scalable backend architecture,
and a web administration experience built with ASP.NET Core and Angular.
```

### Confirmed / strongly supported engineering work

- Designed and worked with a **multi-tenant architecture**.
- Used **ASP.NET Core** for the backend.
- Used **Angular** for the frontend.
- Used **SQL Server** for relational data.
- Used **Entity Framework Core**.
- Applied **Clean Architecture** principles.
- Implemented **JWT access-token authentication**.
- Implemented **refresh-token rotation**.
- Used **TokenVersion** as part of token/session invalidation.
- Used **DeviceId** in authentication/session design.
- Used **Redis** for token-version/session-related caching.
- Worked with **RBAC / permissions**.
- Worked with **subscription / feature management**.
- Worked with **audit logging**.
- Worked with **background processing / outbox-style processing**.
- Used a **custom request pipeline/decorator approach** rather than MediatR.

### Technology tags

```text
C#
ASP.NET Core
Angular
TypeScript
Entity Framework Core
SQL Server
Redis
JWT
Clean Architecture
Docker
```

### Recommended project-detail sections

1. Overview
2. Multi-tenant Architecture
3. Authentication & Session Security
4. Authorization & Feature Access
5. Data Access
6. Redis & Caching
7. Background Processing / Outbox
8. Challenges
9. Lessons Learned

### Suggested architecture highlight

```text
Tenant Request
    ↓
Authentication
    ↓
Tenant Context
    ↓
Authorization
    ↓
Application / Use Case
    ↓
EF Core
    ↓
Tenant-aware Data Access
```

### DO NOT USE from Stitch mock data

```text
Backend Lead
Oct 2023 – Present
MediatR (CQRS)
```

The actual project architecture preference/history uses a custom
`IRequestHandler / IRequestDispatcher` pipeline and decorators rather than
MediatR.

---

# 3.2 P-234 — Regulatory Document RAG Platform

**Status: CONFIRMED for project participation and backend scope**

```yaml
name: P-234
slug: p-234
recommended_role: Backend Developer
project_type: Team / AI + Backend Project
```

### Public title

```text
AI-powered Regulatory Document RAG Platform
```

### Summary

```text
Backend platform for managing regulatory documents and supporting an AI/RAG
question-answering system, with emphasis on secure document lifecycle,
authentication, authorization, department-based access control, auditing,
and safe integration between backend permissions and retrieval.
```

### Backend scope worked on

- Worked primarily on the **backend integration layer** around an existing AI/RAG component.
- Built/extended backend architecture in **Python / FastAPI**.
- Applied **Clean Architecture-style separation**.
- Worked with a custom request handler/dispatcher pipeline.
- Worked with validation, logging and audit decorators/pipeline behavior.
- Implemented/worked on **JWT authentication** and refresh-token handling.
- Implemented/worked on **RBAC and permissions**.
- Implemented **department-based document access control**.
- Ensured document access restrictions also apply to **AI/RAG chat/retrieval flows**.
- Worked on regulatory-document upload and lifecycle management.
- Worked on document versions and document relationships.
- Worked on effectiveness alerts/application scopes.
- Worked on document changelog/audit requirements.
- Worked with audit outbox / audit logging concepts.
- Worked with PostgreSQL migrations through **Alembic**.
- Used **Neon PostgreSQL** for backend relational data.
- Used/integrated **Cloudflare R2** for document/file storage.
- Integrated around **Qdrant / vector search / RAG** without replacing the existing AI implementation.
- Worked with Docker/deployment/integration concerns.
- Worked on rate limiting/auth hardening and backend release-readiness issues.

### Technology tags

```text
Python
FastAPI
PostgreSQL
Neon
SQLAlchemy
Alembic
Qdrant
Cloudflare R2
Redis
JWT
RAG
Vector Search
Docker
Clean Architecture
```

### Strong portfolio highlights

#### Department-based Access Control

```text
Authenticated User
      ↓
Current Role / Department
      ↓
Document ACL
      ↓
Authorized Documents Only
      ↓
Retrieval / RAG
      ↓
LLM Answer
```

The important engineering point is that authorization is not only a UI or
document-download check; restricted documents must also be excluded from the
knowledge supplied to the AI answer flow.

#### Secure document lifecycle

Relevant work includes:

```text
Upload / Replace Source
        ↓
Document Version
        ↓
Sections / Chunks
        ↓
Lifecycle / Legal Status
        ↓
Relations / Effectiveness
        ↓
Authorized Retrieval
        ↓
Audit Trail
```

### Recommended project-detail sections

1. Overview
2. My Backend Scope
3. Clean Architecture
4. Authentication & RBAC
5. Department-based ACL
6. Document Lifecycle & Versioning
7. Audit & Security
8. RAG Authorization Integration
9. Storage / Database / Vector Search
10. Challenges & Lessons Learned

### NEEDS_CONFIRMATION

- Exact project start/end date for public display
- Exact team size
- Public live-demo URL
- Which repository URL may be publicly linked
- Whether all internal architecture/security details should be public

---

# 3.3 Personal Portfolio + AI Agent

**Status: CONFIRMED — current project**

### Public title

```text
Developer Portfolio & Personal AI Agent
```

### Project goal

```text
A full-stack personal portfolio that behaves as a dynamic CV rather than a
static landing page. Portfolio content is stored in PostgreSQL and managed
through a protected admin interface. The platform is designed to later expose
a personal AI assistant capable of answering questions about experience,
projects and technical background using portfolio data as its knowledge base.
```

### Frozen/current architecture direction

```text
Angular
   ↓
ASP.NET Core API
   ↓
PostgreSQL + pgvector
   ↓
Portfolio Data / Knowledge
   ↓
OpenAI-backed Personal Agent
```

### Main capabilities

- Public developer portfolio.
- Dynamic project detail pages.
- Experience, education, training, skills and journey content.
- Protected admin account.
- Admin CRUD for portfolio content.
- Media/CV management.
- Contact messages.
- Agent settings.
- Knowledge-source management.
- Conversation history/monitoring.
- RAG-ready PostgreSQL schema using `pgvector`.

### Technology tags

```text
C#
ASP.NET Core
Angular
PostgreSQL
pgvector
OpenAI API
RAG
```

---

## 4. Education

### Hanoi University of Civil Engineering

**Status: CONFIRMED**

```yaml
institution: Hanoi University of Civil Engineering
field_of_study: Software Engineering
status: Final-year student
```

### NEEDS_CONFIRMATION

```yaml
start_date: null
expected_graduation_date: null
degree_name: null
gpa: null
```

Do not let the frontend or seed generator invent these values.

---

## 5. Training & Hands-on Learning

# VinUni AI Thực Chiến K3

**Status: CONFIRMED from prior project/lab discussions, but exact public certificate/title wording needs confirmation**

```yaml
name: VinUni AI Thực Chiến K3
type: Hands-on AI / Cloud training program
period: 2026
```

### Areas practiced in prior work

- Cloud AI on **AWS**.
- Terraform / Infrastructure as Code.
- AWS networking and compute labs.
- Bastion/private-subnet workflows.
- Security and data-governance exercises.
- PII detection and redaction.
- Policy enforcement.
- Prompt-injection containment testing.
- Tamper-evident audit ledger concepts.
- Compliance / DPIA reporting exercises.
- RAG / AI engineering topics.

### Cloud / infrastructure tools encountered

```text
AWS
Terraform
Docker
Cloudflare R2
GitHub
```

### Important classification

This belongs under:

```text
Training / Hands-on Learning
```

not under professional work experience.

### NEEDS_CONFIRMATION

- Official program/provider display name
- Exact start/end dates
- Whether a completion certificate exists
- Credential URL/ID, if any

---

## 6. Skills

The initial skill seed should be grouped rather than presenting every
technology as equal proficiency.

### Backend — primary

**Status: CONFIRMED**

```text
C#
ASP.NET Core
REST API
Entity Framework Core
SQL
JWT Authentication
```

### Frontend

**Status: CONFIRMED**

```text
Angular
TypeScript
RxJS
NG-ZORRO
HTML
CSS
```

### Database & Data

**Status: CONFIRMED**

```text
SQL Server
PostgreSQL
Stored Procedures
Entity Framework Core
Neon
```

### Architecture & Backend Engineering

**Status: CONFIRMED**

```text
Clean Architecture
Dependency Injection
Repository Pattern
Unit of Work
Custom Request Handler / Dispatcher
Validation Pipeline
Logging / Audit Decorators
Outbox Pattern
Background Processing
Multi-tenancy
```

### Authentication & Authorization

**Status: CONFIRMED**

```text
JWT
Refresh Token Rotation
TokenVersion
DeviceId
RBAC
Permissions
Department-based ACL
```

### AI / RAG

**Status: CONFIRMED as hands-on/current-focus skills**

```text
RAG
Vector Search
Qdrant
pgvector
OpenAI API integration
AI/Backend integration
```

### Python backend

**Status: CONFIRMED**

```text
Python
FastAPI
SQLAlchemy
Alembic
```

### Cloud / DevOps / Tools

**Status: CONFIRMED**

```text
Git
GitHub
Docker
GitHub Actions
AWS
Terraform
Cloudflare R2
Postman
```

---

## 7. Certificates

**Status: NEEDS_CONFIRMATION**

No certificate should be created merely because a technology, course or
training program was completed.

Initial seed:

```json
[]
```

Only add a certificate when the following real data is available:

```yaml
name:
issuer:
issue_date:
credential_id:
credential_url:
image:
```

---

## 8. Career / Learning Journey

**Status: CONFIRMED at milestone level; exact display dates may need refinement**

Recommended initial journey:

```text
Software Engineering studies
        ↓
C# / .NET / SQL foundations
        ↓
Angular & full-stack web development
        ↓
RTC Technology Vietnam internship — Aug 2025
        ↓
ERP WinForms → Web migration experience
        ↓
SchoolSaaS — May 2026
        ↓
Multi-tenancy / Auth / Redis / backend architecture
        ↓
Cloud AI & hands-on infrastructure/security learning
        ↓
Python / FastAPI backend work
        ↓
P-234
        ↓
RAG + secure document access integration
        ↓
Personal Portfolio + AI Agent
```

This should be rendered as a learning/professional progression, not as a list
of employment positions.

---

## 9. Social Links

### CONFIRMED

```yaml
- platform: GitHub
  url: https://github.com/CoderPL1005
  visible: true
```

### NEEDS_CONFIRMATION

```yaml
linkedin: null
email: null
other_links: []
```

Do not seed fake placeholders into production data.

---

## 10. Site Settings — Initial Draft

**Status: SAFE DEFAULT / COPY DRAFT**

```yaml
site_name: "Nguyễn Đình Phúc"
site_title: "Backend .NET Developer"
primary_stack_label: ".NET + Angular"
current_focus_label: "Backend + AI/RAG"
featured_project: "SchoolSaaS"
contact_enabled: true
chatbot_enabled: true
```

### SEO draft

```yaml
seo_title: "Nguyễn Đình Phúc | Backend .NET Developer"
seo_description: >
  Portfolio of Nguyễn Đình Phúc, a Software Engineering student and
  Backend .NET Developer working with ASP.NET Core, Angular, SQL,
  scalable backend architecture and AI/RAG integration.
```

---

## 11. Personal Agent — Initial Settings

**Status: COPY DRAFT**

```yaml
name: "Portfolio Assistant"
enabled: true
welcome_message: >
  Hi! You can ask me about Phúc's experience, projects,
  technical skills and engineering background.
suggested_questions:
  - "What did Phúc work on at RTC Technology Vietnam?"
  - "Tell me about SchoolSaaS."
  - "What did Phúc build in P-234?"
  - "What backend technologies has Phúc worked with?"
```

### Agent knowledge scope

The agent may derive knowledge from approved public portfolio content:

```text
Profile
Experience
Projects
Project Sections
Skills
Education
Training
Journey
```

Knowledge chunks and embeddings should be generated by the indexing pipeline,
not manually maintained as duplicate seed content.

---

## 12. Admin Bootstrap Data

The admin user is system bootstrap data, not public portfolio content.

Seed behavior:

```text
Read admin bootstrap credentials from environment
        ↓
Check whether admin exists
        ↓
Hash password in ASP.NET Core
        ↓
Insert only if missing
```

Never commit:

```text
plaintext admin password
password hash tied to a known password
OpenAI API key
JWT signing secret
database password
R2 secret
```

to this seed-data file or `portfolio.seed.json`.

---

## 13. Content Explicitly Rejected from Stitch Mockups

The following values appeared in generated UI/mock content and must **not**
become production seed facts without separate real-world confirmation:

```text
SchoolSaaS — Backend Lead
SchoolSaaS — Oct 2023 – Present
SchoolSaaS — MediatR (CQRS)
SchoolSaaS — Redis Pub/Sub cache invalidation across all nodes
SchoolSaaS — Quartz, unless actually used and confirmed
Any fake Live Demo URL
Any fake LinkedIn URL
Any fake email
Any fabricated certificate
Any fabricated metric/user count
Any fabricated production scale claim
```

The Stitch HTML is a **visual template**, not a factual source of personal data.

---

## 14. Initial Seed Dataset Summary

```text
Profile                         READY
RTC Experience                  READY
SchoolSaaS                      READY
P-234                           READY
Personal Portfolio project      READY
Education                       PARTIAL — dates/degree details missing
Training                        PARTIAL — official dates/credential missing
Skills                          READY
Journey                         READY
GitHub                          READY
LinkedIn                        MISSING
Public email                    MISSING
Certificates                    EMPTY until verified
Admin credentials               ENVIRONMENT ONLY
Agent settings                  READY AS INITIAL COPY
Knowledge chunks                GENERATED LATER
```

---

## 15. Next Transformation

After this document is reviewed, transform only `CONFIRMED` and explicitly
approved copy into:

```text
seed/
└── portfolio.seed.json
```

Then implement:

```text
Infrastructure/
└── Persistence/
    └── Seeding/
        ├── DatabaseSeeder.cs
        ├── AdminSeeder.cs
        └── PortfolioSeeder.cs
```

The seeder must be **idempotent** and must not overwrite later edits made
through the Admin CMS on every application startup.
