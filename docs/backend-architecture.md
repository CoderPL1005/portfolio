# Backend Architecture V1 — Personal Portfolio + Personal RAG Agent

## 1. Status

This document freezes the backend architecture for V1 of the personal portfolio project.

The backend stack is:

- C#
- ASP.NET Core Web API
- Entity Framework Core
- PostgreSQL / Neon
- PostgreSQL + pgvector
- Cloudflare R2
- Gemini API
- ASP.NET Core BackgroundService
- Angular frontend consuming REST API

The following are already considered frozen together with this document:

```text
PostgreSQL Schema V1        ✅
Stitch Template(3)          ✅
API Contract V1             ✅
Backend Architecture V1     ✅
```

The backend should remain intentionally compact and should not be expanded into an enterprise platform.

---

# 2. Solution Structure

```text
Portfolio/
│
├── Portfolio.sln
│
├── src/
│   ├── Portfolio.Domain/
│   ├── Portfolio.Application/
│   ├── Portfolio.Infrastructure/
│   └── Portfolio.Api/
│
├── tests/
│   ├── Portfolio.UnitTests/
│   └── Portfolio.IntegrationTests/
│
├── database/
│   ├── schema.sql
│   └── schema.puml
│
└── docs/
    ├── api-contract.md
    └── backend-architecture.md
```

---

# 3. Dependency Rule

The dependency direction is:

```text
┌──────────────────────────┐
│          API             │
└────────────┬─────────────┘
             │
       composition root
             │
      ┌──────┴───────┐
      ▼              ▼
Application     Infrastructure
      │              │
      ▼              │
    Domain ◄─────────┘
```

Exact project references:

```text
Portfolio.Domain
  → references nothing

Portfolio.Application
  → Portfolio.Domain

Portfolio.Infrastructure
  → Portfolio.Application
  → Portfolio.Domain

Portfolio.Api
  → Portfolio.Application
  → Portfolio.Infrastructure
```

Core rule:

```text
Domain
must not know about:
EF Core
PostgreSQL
Gemini
Cloudflare R2
HTTP
Angular
```

---

# 4. Portfolio.Domain

Purpose:

```text
Pure business/domain model.
```

Suggested structure:

```text
Portfolio.Domain/
│
├── Common/
│   ├── Entity.cs
│   └── AuditableEntity.cs
│
├── Entities/
│   ├── AdminUser.cs
│   ├── AdminRefreshToken.cs
│   ├── Profile.cs
│   ├── Experience.cs
│   ├── ExperienceTechnology.cs
│   ├── Technology.cs
│   ├── Project.cs
│   ├── ProjectTechnology.cs
│   ├── ProjectSection.cs
│   ├── ProjectMedia.cs
│   ├── Skill.cs
│   ├── Education.cs
│   ├── Training.cs
│   ├── Certificate.cs
│   ├── JourneyItem.cs
│   ├── SocialLink.cs
│   ├── SiteSetting.cs
│   ├── MediaAsset.cs
│   ├── AgentSetting.cs
│   ├── KnowledgeDocument.cs
│   ├── KnowledgeChunk.cs
│   ├── ChatSession.cs
│   ├── ChatMessage.cs
│   ├── ChatMessageSource.cs
│   └── ChatMessageFeedback.cs
│
└── Enums/
    ├── ProjectStatus.cs
    ├── SkillExperienceLevel.cs
    ├── KnowledgeSourceType.cs
    ├── KnowledgeIndexingStatus.cs
    ├── ChatSessionStatus.cs
    ├── ChatMessageRole.cs
    ├── ChatFeedbackRating.cs
    ├── MediaType.cs
    ├── ProjectMediaRole.cs
    └── ProjectSectionType.cs
```

The 26 database tables map to domain entities, but that does NOT mean the API should expose 26 CRUD controllers.

---

# 5. Portfolio.Application

Purpose:

```text
Use cases
business orchestration
validation
application abstractions
```

Use a custom request/handler pipeline.

Do NOT use MediatR.

Flow:

```text
Command / Query
      ↓
IRequest<T>
      ↓
IRequestHandler<TRequest,TResponse>
      ↓
IRequestDispatcher
      ↓
Handler
```

Suggested structure:

```text
Portfolio.Application/
│
├── Common/
│   ├── Abstractions/
│   │   ├── Messaging/
│   │   │   ├── IRequest.cs
│   │   │   ├── IRequestHandler.cs
│   │   │   └── IRequestDispatcher.cs
│   │   │
│   │   ├── Persistence/
│   │   │   ├── IApplicationDbContext.cs
│   │   │   └── IUnitOfWork.cs
│   │   │
│   │   ├── Authentication/
│   │   │   ├── IJwtTokenService.cs
│   │   │   ├── IPasswordHasher.cs
│   │   │   └── ICurrentUser.cs
│   │   │
│   │   ├── Storage/
│   │   │   └── IFileStorage.cs
│   │   │
│   │   └── AI/
│   │       ├── IEmbeddingService.cs
│   │       ├── IChatCompletionService.cs
│   │       └── IKnowledgeSearchService.cs
│   │
│   ├── Behaviors/
│   │   ├── ValidationBehavior.cs
│   │   └── LoggingBehavior.cs
│   │
│   ├── Exceptions/
│   │   ├── NotFoundException.cs
│   │   ├── ConflictException.cs
│   │   ├── UnauthorizedException.cs
│   │   └── ValidationException.cs
│   │
│   ├── Models/
│   │   ├── PagedResult.cs
│   │   └── CurrentAdmin.cs
│   │
│   └── Validation/
│
├── Features/
│
└── DependencyInjection.cs
```

---

# 6. Application Feature Organization

Use vertical feature folders.

Do NOT organize the entire Application layer as:

```text
Services/
Repositories/
DTOs/
Commands/
Queries/
```

Instead:

```text
Features/
│
├── Auth/
├── Dashboard/
├── Profile/
├── Experiences/
├── Technologies/
├── Projects/
├── Skills/
├── Education/
├── Trainings/
├── Certificates/
├── Journey/
├── SocialLinks/
├── SiteSettings/
├── Media/
├── Knowledge/
└── Chat/
```

Example:

```text
Features/
└── Projects/
    ├── Create/
    │   ├── CreateProjectCommand.cs
    │   ├── CreateProjectHandler.cs
    │   ├── CreateProjectValidator.cs
    │   └── CreateProjectResult.cs
    │
    ├── Update/
    ├── Delete/
    ├── GetById/
    ├── GetList/
    ├── GetPublicBySlug/
    ├── GetPublicList/
    ├── Reorder/
    │
    ├── Technologies/
    │   └── Replace/
    │
    ├── Sections/
    │   ├── Create/
    │   ├── Update/
    │   ├── Delete/
    │   └── Reorder/
    │
    └── Media/
        ├── Attach/
        ├── Update/
        └── Remove/
```

Every use case should map cleanly to `api-contract.md`.

Example:

```text
PUT /api/v1/admin/projects/{id}/sections/{sectionId}
                 ↓
UpdateProjectSectionCommand
                 ↓
UpdateProjectSectionHandler
```

---

# 7. Persistence Strategy

Do NOT create one repository per entity.

Avoid:

```text
IProjectRepository
IExperienceRepository
ISkillRepository
IEducationRepository
ITrainingRepository
ICertificateRepository
...
```

Do NOT use a generic:

```text
IRepository<T>
```

as the main persistence abstraction.

Preferred abstraction:

```csharp
public interface IApplicationDbContext
{
    DbSet<Project> Projects { get; }
    DbSet<ProjectSection> ProjectSections { get; }
    DbSet<Technology> Technologies { get; }

    // other required DbSets

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
```

Infrastructure implements:

```text
Application
    ↓
IApplicationDbContext

Infrastructure
    ↓
ApplicationDbContext : DbContext,
                       IApplicationDbContext
```

Use specialized abstractions only when a true infrastructure boundary exists, such as:

```text
Cloudflare R2
Gemini
JWT
Password hashing
pgvector search
```

---

# 8. Portfolio.Infrastructure

Purpose:

```text
External implementations
database
storage
authentication
AI provider
knowledge indexing
background worker
```

Suggested structure:

```text
Portfolio.Infrastructure/
│
├── Persistence/
│   ├── ApplicationDbContext.cs
│   │
│   ├── Configurations/
│   │   ├── AdminUserConfiguration.cs
│   │   ├── AdminRefreshTokenConfiguration.cs
│   │   ├── ProfileConfiguration.cs
│   │   ├── ExperienceConfiguration.cs
│   │   ├── TechnologyConfiguration.cs
│   │   ├── ProjectConfiguration.cs
│   │   ├── ProjectSectionConfiguration.cs
│   │   ├── ProjectMediaConfiguration.cs
│   │   ├── SkillConfiguration.cs
│   │   ├── EducationConfiguration.cs
│   │   ├── TrainingConfiguration.cs
│   │   ├── CertificateConfiguration.cs
│   │   ├── JourneyItemConfiguration.cs
│   │   ├── SocialLinkConfiguration.cs
│   │   ├── SiteSettingConfiguration.cs
│   │   ├── MediaAssetConfiguration.cs
│   │   ├── AgentSettingConfiguration.cs
│   │   ├── KnowledgeDocumentConfiguration.cs
│   │   ├── KnowledgeChunkConfiguration.cs
│   │   ├── ChatSessionConfiguration.cs
│   │   ├── ChatMessageConfiguration.cs
│   │   ├── ChatMessageSourceConfiguration.cs
│   │   └── ChatMessageFeedbackConfiguration.cs
│   │
│   └── Migrations/
│
├── Authentication/
│   ├── JwtTokenService.cs
│   ├── PasswordHasher.cs
│   └── CurrentUser.cs
│
├── Storage/
│   └── R2FileStorage.cs
│
├── AI/
│   ├── GeminiEmbeddingService.cs
│   └── GeminiChatCompletionService.cs
│
├── Knowledge/
│   ├── KnowledgeContentBuilder.cs
│   ├── KnowledgeChunker.cs
│   ├── KnowledgeIndexer.cs
│   └── KnowledgeSearchService.cs
│
├── BackgroundJobs/
│   └── KnowledgeIndexingWorker.cs
│
└── DependencyInjection.cs
```

---

# 9. PostgreSQL + pgvector

V1 uses PostgreSQL for both:

```text
relational portfolio data
+
vector search
```

No separate vector database.

Concept:

```text
PostgreSQL
├── portfolio relational tables
└── knowledge_chunks
      └── embedding VECTOR(1536)
```

Vector-specific EF configuration belongs to Infrastructure.

Example concept:

```csharp
builder.Property(x => x.Embedding)
    .HasColumnType("vector(1536)");
```

Application must not depend directly on pgvector implementation details.

Every stored vector and query vector must come from the same embedding model.
After changing embedding providers/models, mark all active knowledge documents
for reindexing and rebuild their chunks before relying on retrieval results.

Use:

```text
IKnowledgeSearchService
```

as the Application abstraction.

Implementation:

```text
KnowledgeSearchService
```

in Infrastructure.

---

# 10. Knowledge Architecture

Portfolio content is the canonical source of truth.

Never render the public portfolio from `knowledge_documents`.

Correct direction:

```text
Portfolio Data
      ↓
Knowledge Builder
      ↓
knowledge_documents
      ↓
Chunking
      ↓
knowledge_chunks
      ↓
Embedding / pgvector
```

Not:

```text
knowledge_documents
      ↓
Public Portfolio
```

---

# 11. Knowledge Indexing Flow

Do NOT perform embedding synchronously inside normal admin CRUD requests.

Wrong:

```text
PUT Project
   ↓
Gemini embedding
   ↓
wait
   ↓
200 OK
```

Correct:

```text
Admin updates portfolio data
        ↓
Save business data
        ↓
Knowledge document becomes PENDING
        ↓
return success
```

Background worker:

```text
KnowledgeIndexingWorker
        ↓
find eligible PENDING / FAILED records
        ↓
KnowledgeContentBuilder
        ↓
KnowledgeChunker
        ↓
GeminiEmbeddingService
        ↓
replace chunks safely
        ↓
INDEXED
```

Failure:

```text
indexing_status = FAILED
last_index_error = sanitized message
```

Knowledge indexing failure must not undo a successfully committed portfolio edit after that edit has completed.

---

# 12. Knowledge Builder

`KnowledgeContentBuilder` creates canonical RAG content from portfolio entities.

Examples:

```text
PROFILE
→ Profile fields

EXPERIENCE
→ Experience + selected technologies

PROJECT
→ Project
 + Technologies
 + Project Sections

SKILLS
→ Published skills

EDUCATION
→ Education

TRAINING
→ Training

CERTIFICATE
→ Certificate

JOURNEY
→ Journey item
```

Knowledge records are derived representations, not independently authored copies of the portfolio.

---

# 13. Chat Architecture

Feature structure:

```text
Features/
└── Chat/
    ├── CreateSession/
    ├── SendMessage/
    ├── SubmitFeedback/
    ├── GetConversations/
    ├── GetConversationDetail/
    └── CloseConversation/
```

`SendChatMessageHandler` orchestrates:

```text
validate request
      ↓
load AgentSetting
      ↓
agent enabled?
      ↓
embed user question
      ↓
IKnowledgeSearchService
      ↓
retrieve relevant chunks
      ↓
minimum similarity filter
      ↓
max context chunk selection
      ↓
IChatCompletionService
      ↓
persist USER message
      ↓
persist ASSISTANT message
      ↓
persist chat_message_sources
      ↓
update chat session
      ↓
return grounded answer
```

Do NOT call Gemini directly from a Controller.

The agent is portfolio-scoped.

It may answer about:

```text
profile
professional experience
projects
skills
education
training
certificates
engineering journey
```

When context is insufficient, it must use fallback behavior rather than invent facts.

---

# 14. Authentication Architecture

Admin authentication uses:

```text
JWT access token
+
refresh token rotation
```

Relevant abstractions:

```text
IJwtTokenService
IPasswordHasher
ICurrentUser
```

Refresh token rules:

```text
store only token hash
support revocation
support replacement token link
rotate atomically
```

Admin authentication is intentionally single-admin for V1.

Do NOT add:

```text
roles
permissions
multi-admin RBAC
teams
organizations
```

---

# 15. Media / Cloudflare R2

Application abstraction:

```text
IFileStorage
```

Infrastructure implementation:

```text
R2FileStorage
```

Upload flow:

```text
Admin upload
   ↓
API validates metadata/request
   ↓
Application use case
   ↓
IFileStorage
   ↓
Cloudflare R2
   ↓
media_assets metadata
```

Required protections:

```text
server-side max file size
allowed MIME/type validation
safe generated object key
do not trust client filename for object path
secrets backend-only
```

Deleting a media record must avoid inconsistent DB/R2 state.

---

# 16. Portfolio.Api

Purpose:

```text
HTTP only
authentication entry
request binding
response mapping
routing
middleware
composition root
```

Suggested structure:

```text
Portfolio.Api/
│
├── Controllers/
│   ├── AuthController.cs
│   ├── PublicPortfolioController.cs
│   ├── PublicProjectsController.cs
│   ├── PublicContactController.cs
│   ├── PublicChatController.cs
│   │
│   └── Admin/
│       ├── DashboardController.cs
│       ├── ProfileController.cs
│       ├── ExperiencesController.cs
│       ├── TechnologiesController.cs
│       ├── ProjectsController.cs
│       ├── SkillsController.cs
│       ├── EducationController.cs
│       ├── TrainingsController.cs
│       ├── CertificatesController.cs
│       ├── JourneyController.cs
│       ├── SocialLinksController.cs
│       ├── SiteSettingsController.cs
│       ├── MediaController.cs
│       ├── AgentSettingsController.cs
│       ├── KnowledgeController.cs
│       └── ConversationsController.cs
│
├── Contracts/
│   └── Common/
│       └── ApiResponse.cs
│
├── Middleware/
│   └── ExceptionHandlingMiddleware.cs
│
├── Extensions/
│
├── Program.cs
│
├── appsettings.json
└── appsettings.Development.json
```

Controllers must remain thin.

Controllers must not contain:

```text
EF queries
Gemini calls
R2 calls
business rules
knowledge chunking logic
```

---

# 17. Public vs Admin Queries

Do not duplicate the whole business layer merely because public/admin APIs differ.

Example:

```text
Projects/
├── GetById/
├── GetList/
├── GetPublicBySlug/
└── GetPublicList/
```

Admin projection may contain editable fields.

Public projection must expose only published data.

Unpublished public resources should behave as not found.

---

# 18. API Response Rule

The API contract is defined in:

```text
docs/api-contract.md
```

Backend implementation must follow it.

Controllers should produce standardized responses such as:

```json
{
  "success": true,
  "data": {}
}
```

Errors must use stable error codes.

Do not expose:

```text
stack traces
raw exception text
connection strings
Gemini provider errors
API secrets
R2 credentials
```

---

# 19. Validation

Application validation must enforce business/API constraints before PostgreSQL raises errors where practical.

Examples:

```text
project slug
unique case-insensitively

date ranges
end >= start

displayOrder
>= 0

URLs
valid absolute URL when supplied

text
explicit maximum lengths
```

Validation belongs near the use case through Validators / ValidationBehavior.

---

# 20. Exception Handling

Use a central exception handling mechanism.

Map application exceptions to stable HTTP behavior.

Examples:

```text
ValidationException
→ 400

UnauthorizedException
→ 401

NotFoundException
→ 404

ConflictException
→ 409
```

Unexpected failures:

```text
500 INTERNAL_ERROR
```

Never return:

```csharp
return StatusCode(500, exc.ToString());
```

---

# 21. Behaviors

V1 supports:

```text
ValidationBehavior
LoggingBehavior
```

Do NOT add an audit framework.

The project has no audit-log requirement in V1.

Do not copy audit architecture from unrelated projects.

---

# 22. Dependency Injection

Application:

```csharp
services.AddApplication();
```

Infrastructure:

```csharp
services.AddInfrastructure(configuration);
```

API:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();

var app = builder.Build();

app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
```

Keep `Program.cs` small.

---

# 23. Configuration and Secrets

Non-secret configuration may live in `appsettings.json`.

Example:

```json
{
  "Jwt": {
    "Issuer": "Portfolio",
    "Audience": "Portfolio.Admin",
    "AccessTokenMinutes": 15
  },
  "Media": {
    "MaxUploadBytes": 10485760
  },
  "Knowledge": {
    "WorkerIntervalSeconds": 10
  }
}
```

Secrets must come from environment variables / deployment secrets.

Examples:

```text
ConnectionStrings__Database

Jwt__SecretKey

Gemini__ApiKey
Gemini__ChatModel
Gemini__EmbeddingModel
Gemini__EmbeddingDimensions
Gemini__EnableIndexingWorker

ChatProtection__BurstPermitLimit
ChatProtection__BurstWindowSeconds
ChatProtection__DailyPerVisitorLimit
ChatProtection__SessionUserMessageLimit
ChatProtection__GlobalDailyLimit
ChatProtection__IpHashSecret

Proxy__ForwardLimit
Proxy__KnownProxies__0
Proxy__KnownNetworks__0

R2__AccountId
R2__AccessKeyId
R2__SecretAccessKey
R2__BucketName
R2__PublicBaseUrl
```

Current Gemini defaults are `gemini-3.1-flash-lite` for chat,
`gemini-embedding-2` for embeddings, and `1536` embedding dimensions. The API
key remains environment-only.

Public chat messages use a `3/minute/IP` in-process burst limiter and durable
PostgreSQL quotas of `20/UTC day/HMAC visitor`, `20 USER messages/session`, and
`150/UTC day` globally. `ChatProtection__IpHashSecret` is a distinct required
secret and raw client IP addresses are never persisted. Configure Render's
actual trusted proxy addresses/networks through `Proxy__KnownProxies__N` or
`Proxy__KnownNetworks__N`; forwarded headers from untrusted peers are ignored.
Session, visitor, and global reservations are made atomically before Gemini
calls, and no database transaction remains open during provider HTTP requests.

Never commit real secrets.

---

# 24. Testing Structure

```text
tests/
├── Portfolio.UnitTests/
│   ├── Auth/
│   ├── Projects/
│   ├── Knowledge/
│   └── Chat/
│
└── Portfolio.IntegrationTests/
    ├── Auth/
    ├── Public/
    ├── Admin/
    ├── Knowledge/
    └── Chat/
```

Unit tests:

```text
business rules
validators
handlers
knowledge-content building
chat orchestration
```

Integration tests:

```text
HTTP
 ↓
Controller
 ↓
Application
 ↓
EF Core
 ↓
PostgreSQL
```

Ordinary tests should not call real:

```text
Gemini
Cloudflare R2
```

Use test doubles/fakes around those abstractions.

---

# 25. Important V1 Non-Goals

Do NOT add:

```text
MediatR
AutoMapper
generic IRepository<T>
repository per table
Kafka
RabbitMQ
event bus
Redis
Qdrant
Elasticsearch
microservices
separate Python AI service
Hangfire
Quartz
Kubernetes
audit framework
roles
permissions
multi-admin RBAC
teams
organizations
billing
subscriptions
payments
blog
CRM
support tickets
```

If these appear in generated code without a newly approved requirement, they should be removed.

---

# 26. Background Processing

V1 uses:

```text
ASP.NET Core BackgroundService
```

for knowledge indexing.

No external job system is required.

Suggested worker:

```text
KnowledgeIndexingWorker
```

Responsibilities:

```text
poll eligible knowledge_documents
claim/process safely
build content
chunk
embed
replace chunks
mark INDEXED or FAILED
```

The worker must avoid processing the same knowledge item concurrently if multiple backend instances are ever introduced later.

V1 may run a single backend instance, but implementation should not rely on unsafe in-memory state for persistent indexing status.

---

# 27. Transaction Boundaries

Normal business writes should be atomic.

Examples:

```text
Project update
+
project technologies replacement
+
project section updates where included in same use case
+
mark related knowledge as PENDING
```

should use an intentional transaction where those operations belong to one business action.

Do not call `SaveChangesAsync()` repeatedly without reason inside one use case.

External systems such as R2/Gemini cannot participate in PostgreSQL transactions; handle them explicitly with safe sequencing/compensation where required.

---

# 28. Naming Rules

Use:

```text
Domain entity:
Project

Application command:
CreateProjectCommand

Application handler:
CreateProjectHandler

Validator:
CreateProjectValidator

API controller:
ProjectsController

Infrastructure EF configuration:
ProjectConfiguration
```

Keep feature and API terminology consistent with:

```text
database/schema.sql
docs/api-contract.md
```

Do not invent alternate names for the same domain concept.

---

# 29. Source of Truth Priority

When implementation sources disagree, use this priority:

```text
1. PostgreSQL Schema V1
2. API Contract V1
3. Backend Architecture V1
4. Frozen Stitch UI
5. Mock/seed data
```

Generated UI placeholder data must never override the database/domain contract.

---

# 30. Freeze Rule

This file defines Backend Architecture V1.

Do not silently change:

```text
project count
layer dependency direction
persistence strategy
request/handler architecture
knowledge indexing architecture
chat orchestration
storage boundary
AI boundary
```

during implementation.

If a genuine missing requirement is discovered:

```text
requirement identified
        ↓
review impact
        ↓
update schema/API/architecture deliberately
        ↓
then implement
```

Do not let Codex introduce architecture changes merely because they are common patterns or generated defaults.

---

# 31. Final Backend V1 Summary

```text
ASP.NET Core Web API
        │
        ▼
Thin Controllers
        │
        ▼
Custom IRequest / Handler Pipeline
        │
        ▼
Application Features
        │
        ├── Portfolio CMS
        ├── Authentication
        ├── Knowledge
        └── Chat
        │
        ▼
Domain
```

Infrastructure:

```text
Infrastructure
├── EF Core
│    └── PostgreSQL / Neon
│         └── pgvector
│
├── Cloudflare R2
│
├── Gemini
│    ├── Embeddings
│    └── Chat Completion
│
└── BackgroundService
     └── Knowledge Indexing
```

V1 remains:

```text
single backend
single database
single admin
single portfolio profile
no microservices
no separate vector database
no unnecessary enterprise infrastructure
```
