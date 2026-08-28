# Implementation Plan V1 — Personal Portfolio + Personal RAG Agent

## 1. Purpose

This document defines the implementation sequence for Codex.

The goal is to build the project incrementally and keep each phase reviewable.

Do NOT ask Codex to implement the whole system in one giant pass.

Frozen inputs:

```text
database/schema.sql
database/schema.puml

docs/api-contract.md
docs/backend-architecture.md
docs/frontend-architecture.md

seed/portfolio.seed.json

Template_UI/
```

Source-of-truth priority:

```text
1. PostgreSQL Schema V1
2. API Contract V1
3. Backend Architecture V1
4. Frontend Architecture V1
5. Frozen Stitch Template(3/4)
6. Seed/mock data
```

---

# 2. Project Targets

Frontend:

```text
Angular
TypeScript
Tailwind CSS
Reactive Forms
Signals where useful
```

Backend:

```text
C#
ASP.NET Core Web API
Entity Framework Core
Custom IRequest / Handler pipeline
```

Database:

```text
PostgreSQL / Neon
pgvector
```

External services:

```text
Cloudflare R2
OpenAI API
```

Deployment target:

```text
Frontend → Vercel
Backend  → Render / Railway / Docker VPS
Database → Neon
Storage  → Cloudflare R2
```

---

# 3. Global Implementation Rules

Codex must NOT introduce:

```text
MediatR
AutoMapper
generic IRepository<T>
repository per table
NgRx
Redis
Qdrant
Kafka
RabbitMQ
microservices
separate Python AI service
Hangfire
Quartz
Kubernetes
roles/permissions
multi-admin RBAC
blog
billing
CRM
support-ticket workflow
```

unless a later approved requirement explicitly changes V1.

Controllers must remain thin.

Angular must not directly call:

```text
OpenAI
Cloudflare R2 with secrets
PostgreSQL
```

Secrets must remain server-side.

---

# 4. Phase Overview

```text
Phase 0  — Repository bootstrap
Phase 1  — Backend foundation
Phase 2  — Database + migrations + seed
Phase 3  — Authentication
Phase 4  — Core Admin CMS backend
Phase 5  — Public Portfolio backend
Phase 6  — Angular foundation
Phase 7  — Public Portfolio frontend
Phase 8  — Admin CMS frontend
Phase 9  — Media / Cloudflare R2
Phase 10 — Knowledge indexing / pgvector
Phase 11 — Personal RAG chatbot
Phase 12 — Integration hardening
Phase 13 — Tests
Phase 14 — Deployment preparation
Phase 15 — Production deployment
```

Each phase should be implemented, reviewed, tested, and committed separately.

---

# 5. Phase 0 — Repository Bootstrap

## Goal

Create the initial repository structure only.

Expected structure:

```text
Portfolio/
├── Portfolio.sln
├── src/
│   ├── Portfolio.Domain/
│   ├── Portfolio.Application/
│   ├── Portfolio.Infrastructure/
│   └── Portfolio.Api/
├── frontend/
├── tests/
│   ├── Portfolio.UnitTests/
│   └── Portfolio.IntegrationTests/
├── database/
├── docs/
├── seed/
└── Template_UI/
```

## Backend setup

Create:

```text
Portfolio.Domain
Portfolio.Application
Portfolio.Infrastructure
Portfolio.Api
Portfolio.UnitTests
Portfolio.IntegrationTests
```

Configure project references exactly as defined in `backend-architecture.md`.

## Frontend setup

Create Angular project under:

```text
frontend/
```

Set up:

```text
Angular Router
HttpClient
Tailwind CSS
environment configuration
```

Do not implement features yet.

## Exit criteria

```text
dotnet build → PASS
Angular build → PASS
No business feature code yet
```

Suggested commit:

```text
chore: bootstrap portfolio solution
```

---

# 6. Phase 1 — Backend Foundation

## Goal

Implement the backend architecture skeleton.

Create:

```text
Domain/Common
Domain/Entities
Domain/Enums

Application/Common/Abstractions
Application/Common/Behaviors
Application/Common/Exceptions
Application/Common/Models

Infrastructure/DependencyInjection.cs

Api/Middleware
Api/Contracts/Common
```

Implement:

```text
IRequest<T>
IRequestHandler<TRequest,TResponse>
IRequestDispatcher
ValidationBehavior
LoggingBehavior
ApiResponse<T>
ExceptionHandlingMiddleware
PagedResult<T>
```

No feature CRUD yet.

## Exit criteria

- Request/handler pipeline works.
- Validation behavior can execute.
- Exception middleware maps stable errors.
- API runs.
- `/health` may return basic app health.

Suggested commit:

```text
feat: add backend application foundation
```

---

# 7. Phase 2 — Database, EF Core, Migrations and Seed

## Goal

Map PostgreSQL Schema V1 exactly.

Create all 26 domain entities corresponding to schema.

Create:

```text
ApplicationDbContext
IApplicationDbContext
EF Core configurations
initial migration
```

Enable pgvector support for:

```text
knowledge_chunks.embedding VECTOR(1536)
```

Implement seed infrastructure:

```text
Infrastructure/Persistence/Seeding/
├── DatabaseSeeder.cs
├── AdminSeeder.cs
└── PortfolioSeeder.cs
```

Use:

```text
seed/portfolio.seed.json
```

Seed rules:

- idempotent
- do not overwrite content edited later in Admin
- only create initial missing records
- admin password comes from environment
- no secrets in seed file
- test placeholders may remain unpublished

## Required checks

```text
dotnet ef database update
```

on an empty PostgreSQL database must succeed.

Then verify:

```text
26 tables exist
seed content inserted
one admin created only when bootstrap env is available
```

Suggested commit:

```text
feat: add persistence schema and initial seed
```

---

# 8. Phase 3 — Admin Authentication

## Goal

Implement:

```text
POST /api/v1/auth/login
POST /api/v1/auth/refresh
POST /api/v1/auth/logout
GET  /api/v1/auth/me
```

Implement:

```text
PasswordHasher
JwtTokenService
CurrentUser
refresh token rotation
refresh token hashing
revocation
replacement chain
```

Rules:

- rate limit login
- validate password max length
- do not store plaintext refresh token
- rotate refresh token atomically
- disabled admin cannot log in

## Tests

At minimum:

```text
valid login
invalid password
disabled admin
refresh success
old refresh rejected after rotation
logout revokes token
/me requires authentication
```

Suggested commit:

```text
feat: implement admin authentication
```

---

# 9. Phase 4 — Core Admin CMS Backend

Implement backend CRUD first, before frontend Admin.

Recommended order:

```text
Profile
Technologies
Experiences
Projects
Skills
Education
Trainings
Certificates
Journey
Social Links
Site Settings
Dashboard
```

---

## 9.1 Profile

Implement:

```text
GET /admin/profile
PUT /admin/profile
```

Rules:

```text
single canonical profile
```

---

## 9.2 Technologies

Implement:

```text
GET    /admin/technologies
POST   /admin/technologies
PUT    /admin/technologies/{id}
DELETE /admin/technologies/{id}
```

Check case-insensitive uniqueness.

---

## 9.3 Experiences

Implement:

```text
GET    /admin/experiences
GET    /admin/experiences/{id}
POST   /admin/experiences
PUT    /admin/experiences/{id}
DELETE /admin/experiences/{id}
PUT    /admin/experiences/reorder
```

Technology associations must update atomically.

Do not add DRAFT status.

---

## 9.4 Projects

Implement:

```text
GET    /admin/projects
GET    /admin/projects/{id}
POST   /admin/projects
PUT    /admin/projects/{id}
DELETE /admin/projects/{id}
PUT    /admin/projects/reorder
```

Status values only:

```text
PLANNED
IN_PROGRESS
ACTIVE
COMPLETED
ARCHIVED
```

---

## 9.5 Project Technologies

Implement:

```text
PUT /admin/projects/{id}/technologies
```

Replace associations atomically.

---

## 9.6 Project Sections

Implement:

```text
GET    /admin/projects/{id}/sections
POST   /admin/projects/{id}/sections
PUT    /admin/projects/{id}/sections/{sectionId}
DELETE /admin/projects/{id}/sections/{sectionId}
PUT    /admin/projects/{id}/sections/reorder
```

Support only approved section types.

---

## 9.7 Project Media Links

Implement relation endpoints, but actual file upload can wait until Phase 9.

```text
GET    /admin/projects/{id}/media
POST   /admin/projects/{id}/media
PUT    /admin/projects/{id}/media/{projectMediaId}
DELETE /admin/projects/{id}/media/{projectMediaId}
```

---

## 9.8 Skills

Implement:

```text
GET    /admin/skills
POST   /admin/skills
PUT    /admin/skills/{id}
DELETE /admin/skills/{id}
PUT    /admin/skills/reorder
```

Valid levels:

```text
USED
LEARNING
EXPLORING
```

---

## 9.9 Education / Training / Certificate / Journey

Implement all endpoints from `api-contract.md`.

Keep entities separate.

---

## 9.10 Social Links / Site Settings

Implement typed API behavior.

Do not expose arbitrary raw JSON editing for normal settings.

---

## 9.12 Dashboard

Implement:

```text
GET /admin/dashboard
```

Only meaningful portfolio/CMS stats.

No fake infrastructure analytics.

## Exit criteria

All Admin CMS backend endpoints from API contract except Media/RAG can be manually tested.

Suggested commits may be split by feature group, for example:

```text
feat: implement profile and experience management
feat: implement project management
feat: implement portfolio content management
```

---

# 10. Phase 5 — Public Portfolio Backend

Implement:

```text
GET  /public/portfolio
GET  /public/projects
GET  /public/projects/{slug}
```

Rules:

- only published content
- public project by slug returns 404 if unpublished
- sort by displayOrder
- Home uses aggregate endpoint

## Exit criteria

A Postman/Swagger test can render all required public portfolio data from the seed.

Suggested commit:

```text
feat: implement public portfolio api
```

---

# 11. Phase 6 — Angular Foundation

## Goal

Create the frontend architecture defined in `frontend-architecture.md`.

Implement:

```text
core/
shared/
layout/
features/public/
features/admin/
```

Set up:

```text
App routing
PublicLayout
AdminLayout
AuthStore
AuthInterceptor
ErrorInterceptor
adminAuthGuard
adminLoginGuard
ApiClientService
ApiResponse model
PagedResult model
```

Do not implement all screens in one file.

Match frozen Stitch colors, typography and layout tokens.

## Exit criteria

- `/` renders Public Layout shell.
- `/admin/login` renders login shell.
- `/admin` is protected.
- Admin sidebar shell matches Stitch.
- Build passes.

Suggested commit:

```text
feat: add angular application foundation
```

---

# 12. Phase 7 — Public Portfolio Frontend

Recommended implementation order:

```text
1. Home
2. Projects list
3. Project detail
4. Experience
5. Skills
6. Journey
7. Contact
```

---

## 12.1 Home

Use:

```text
GET /public/portfolio
```

Render:

```text
Hero
Quick Summary
About
Experience preview
Featured Projects
Skills
Journey
Education
Training/Certificates where applicable
Contact CTA
```

Do not hardcode personal content into Angular.

---

## 12.2 Projects

Use:

```text
GET /public/projects
```

---

## 12.3 Project Detail

Generic route:

```text
/projects/:slug
```

One renderer must support both:

```text
SchoolSaaS
P-234
future projects
```

Do not create separate hardcoded Angular pages per project.

Use `project_sections`.

---

## 12.4 Contact

Render the profile email and published social links as outbound contact options.

Do not submit or persist contact messages.

## Exit criteria

Public portfolio is fully usable without the Admin UI.

Suggested commit:

```text
feat: implement public portfolio frontend
```

---

# 13. Phase 8 — Admin CMS Frontend

Implement screens based on frozen Stitch HTML.

Recommended order:

```text
Login
Dashboard
Profile
Experience
Projects
Skills
Education
Trainings
Certificates
Journey
Social Links
Site Settings
```

Use Reactive Forms.

Do not use NgRx.

---

## 13.1 Login

Wire:

```text
POST /auth/login
POST /auth/refresh
POST /auth/logout
GET /auth/me
```

---

## 13.2 Project Editor

Must preserve exact logical tabs:

```text
Basic Info
Technologies
Sections
Media
SEO Settings
```

Do not expose raw JSON.

---

## 13.3 Unsaved State

Important forms must support:

```text
dirty
saving
saved
error
```

Warn on navigation with unsaved changes where appropriate.

## Exit criteria

Admin can edit all seeded portfolio content and see changes reflected through public APIs/UI.

Suggested commit:

```text
feat: implement admin cms frontend
```

---

# 14. Phase 9 — Media / Cloudflare R2

Implement:

```text
GET    /admin/media
POST   /admin/media
PUT    /admin/media/{id}
DELETE /admin/media/{id}
```

Infrastructure:

```text
IFileStorage
R2FileStorage
```

Security:

```text
max upload size
MIME validation
safe generated storage keys
no client-controlled secret
no R2 credential in Angular
```

Frontend:

```text
Media Library
Media Upload
Media Picker
Project Media
Profile Image
CV
Certificate Media
```

## Consistency

Avoid DB/R2 half-deletion.

If upload fails after R2 write or DB insert fails, perform cleanup/compensation.

Suggested commit:

```text
feat: add media storage and r2 integration
```

---

# 15. Phase 10 — Knowledge Indexing + pgvector

Implement:

```text
Agent Settings backend
KnowledgeContentBuilder
KnowledgeChunker
OpenAIEmbeddingService
KnowledgeIndexer
KnowledgeSearchService
KnowledgeIndexingWorker
```

---

## 15.1 Agent Settings

Implement:

```text
GET /admin/agent/settings
PUT /admin/agent/settings
```

No API keys in UI/DB.

---

## 15.2 Knowledge Generation

When public portfolio entities change:

```text
Portfolio update
      ↓
mark knowledge PENDING
      ↓
return normal CRUD success
```

Do not synchronously embed in normal CRUD request.

---

## 15.3 Background Worker

Worker:

```text
find PENDING / eligible FAILED
↓
claim item
↓
build canonical content
↓
chunk
↓
embed
↓
replace chunks safely
↓
INDEXED
```

On failure:

```text
FAILED
lastIndexError
```

---

## 15.4 Knowledge Admin API

Implement:

```text
GET  /admin/agent/knowledge
GET  /admin/agent/knowledge/{id}
POST /admin/agent/knowledge/{id}/reindex
POST /admin/agent/knowledge/reindex-all
```

Do not return raw vectors.

---

## 15.5 Admin Knowledge UI

Implement:

```text
Knowledge List
Knowledge Detail
Chunks
Reindex
Retry
```

Statuses:

```text
PENDING
INDEXING
INDEXED
FAILED
```

Suggested commit:

```text
feat: implement portfolio knowledge indexing
```

---

# 16. Phase 11 — Personal RAG Chatbot

Backend:

```text
POST /public/chat/sessions
POST /public/chat/sessions/{sessionId}/messages
POST /public/chat/messages/{messageId}/feedback
```

Admin:

```text
GET  /admin/agent/conversations
GET  /admin/agent/conversations/{id}
POST /admin/agent/conversations/{id}/close
```

---

## Chat orchestration

```text
User message
↓
Validate
↓
Load Agent Settings
↓
Embed question
↓
pgvector retrieval
↓
Similarity filter
↓
Top context chunks
↓
Grounded LLM prompt
↓
Persist USER message
↓
Persist ASSISTANT message
↓
Persist citations
↓
Return answer
```

Rules:

- portfolio scoped only
- no web search
- no unsupported personal claims
- fallback when knowledge is insufficient
- rate limit
- message max length
- token/context limits
- API key backend-only

Frontend:

```text
Chat Launcher
Chat Panel
Suggested Questions
Sources
Feedback
```

Suggested commit:

```text
feat: add personal rag chatbot
```

---

# 17. Phase 12 — Integration Hardening

Review cross-feature consistency.

Required checks:

```text
Admin edit
↓
public API reflects change

Admin edit
↓
knowledge becomes PENDING
↓
worker indexes
↓
chat uses updated content
```

Also verify:

```text
unpublished project not public
unpublished content not indexed for public agent if policy says only public knowledge
contact rate limiting
auth refresh rotation
file upload limits
API validation
global exception handling
```

Review all Stitch mock values and ensure none were accidentally seeded as real facts.

Suggested commit:

```text
fix: harden portfolio integration flows
```

---

# 18. Phase 13 — Testing

## Unit tests

Priority:

```text
Auth token rotation
Project validation
Project status validation
Experience date validation
KnowledgeContentBuilder
KnowledgeChunker
Knowledge indexing status transitions
Chat fallback logic
```

## Integration tests

Required:

```text
Auth login/refresh/logout
Admin profile CRUD
Experience CRUD
Project CRUD
Project sections
Public portfolio filtering
Public project publication behavior
Contact submission
Media upload validation
Knowledge reindex
Chat session/message/feedback
```

External providers should use fakes/test doubles.

Do not call real OpenAI or real R2 during standard CI tests.

## Exit criteria

```text
dotnet test → PASS
ng test / chosen Angular test command → PASS
frontend build → PASS
backend build → PASS
```

Suggested commit:

```text
test: add portfolio integration coverage
```

---

# 19. Phase 14 — Deployment Preparation

Prepare:

```text
Dockerfile for ASP.NET Core backend
Vercel config if needed
environment examples
production CORS
health check
database migration startup strategy
```

Environment variables:

```text
ConnectionStrings__Database

Jwt__SecretKey

AdminBootstrap__Email
AdminBootstrap__Password

OpenAI__ApiKey

R2__AccountId
R2__AccessKeyId
R2__SecretAccessKey
R2__BucketName
R2__PublicBaseUrl
```

Do NOT commit real values.

Create:

```text
.env.example
```

with placeholders only.

---

# 20. Phase 15 — Production Deployment

Recommended initial deployment:

```text
Angular
→ Vercel

ASP.NET Core
→ Render / Railway

PostgreSQL + pgvector
→ Neon

Media
→ Cloudflare R2

LLM / Embedding
→ OpenAI API
```

Deployment order:

```text
1. Neon
2. R2
3. Backend
4. Database migration
5. Initial seed
6. Frontend
7. CORS/config validation
8. Knowledge indexing
9. Chat smoke test
```

---

# 21. Production Smoke Test

After deployment verify:

```text
Public homepage loads
Projects load
Project detail loads
Admin login works
Admin CRUD works
Media upload works
Contact form works
Knowledge indexing works
Chat answers from portfolio knowledge
Chat citations work
Unpublished content is hidden
No frontend secret leakage
```

---

# 22. Codex Working Rules

Codex should work phase-by-phase.

For each phase:

```text
1. Read source-of-truth docs
2. Inspect current repository
3. Implement only requested phase
4. Do not refactor unrelated finished phases
5. Run build/tests
6. Report changed files
7. Report migrations/config changes
8. Stop
```

Do not let Codex continue automatically into the next phase without review.

---

# 23. Suggested Codex Prompt Pattern

Use prompts like:

```text
Implement Phase 3 — Admin Authentication only.

Before coding, read:
- docs/api-contract.md
- docs/backend-architecture.md
- database/schema.sql

Constraints:
- do not implement Phase 4+
- do not change schema unless contract cannot be implemented
- do not add MediatR/AutoMapper/generic repository
- run relevant build/tests when finished
- report all changed files
```

Avoid:

```text
"Build the entire portfolio project."
```

---

# 24. Commit Strategy

Recommended major commits:

```text
chore: bootstrap portfolio solution

feat: add backend application foundation

feat: add persistence schema and initial seed

feat: implement admin authentication

feat: implement profile and experience management

feat: implement project management

feat: implement portfolio content management

feat: implement public portfolio api

feat: add angular application foundation

feat: implement public portfolio frontend

feat: implement admin cms frontend

feat: add media storage and r2 integration

feat: implement portfolio knowledge indexing

feat: add personal rag chatbot

fix: harden portfolio integration flows

test: add portfolio integration coverage

chore: prepare production deployment
```

Exact commit count may vary, but do not collapse the whole project into one commit.

---

# 25. Definition of Done — V1

V1 is complete when:

```text
PUBLIC PORTFOLIO
✅ Dynamic data from PostgreSQL
✅ Home
✅ Projects
✅ Project detail
✅ Experience
✅ Skills
✅ Journey
✅ Contact
✅ Responsive UI

ADMIN
✅ Login
✅ Profile
✅ Experiences
✅ Projects
✅ Skills
✅ Education
✅ Trainings
✅ Certificates
✅ Journey
✅ Social Links
✅ Site Settings
✅ Media Library
✅ Contact Inbox

AI AGENT
✅ Agent Settings
✅ Portfolio → Knowledge indexing
✅ pgvector retrieval
✅ Public chatbot
✅ Citations
✅ Feedback
✅ Conversation inspection

INFRASTRUCTURE
✅ Neon PostgreSQL
✅ Cloudflare R2
✅ OpenAI backend integration
✅ Deployment configuration

QUALITY
✅ Validation
✅ Error handling
✅ Auth hardening
✅ Rate limiting
✅ Tests
✅ No secrets in frontend/repo
```

---

# 26. V1 Freeze Reminder

If a requirement is not present in:

```text
schema
API contract
backend architecture
frontend architecture
this implementation plan
```

Codex should not invent it.

Any new requirement must be reviewed before implementation.
