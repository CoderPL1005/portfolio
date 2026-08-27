# Frontend Architecture V1 — Angular Personal Portfolio + Personal RAG Agent

## 1. Status

This document freezes the Angular frontend architecture for V1 of the personal portfolio project.

The frontend stack is:

- Angular
- TypeScript
- Tailwind CSS
- Angular Router
- Angular HttpClient
- Reactive Forms
- Signals where useful
- No NgRx in V1
- No server-side rendering requirement in V1
- REST API provided by ASP.NET Core

The following are considered frozen together:

```text
PostgreSQL Schema V1        ✅
Stitch Template(3)          ✅
API Contract V1             ✅
Backend Architecture V1     ✅
Frontend Architecture V1    ✅
```

The frontend must follow the frozen Stitch design while remaining maintainable, modular, and aligned with the API contract.

---

# 2. Application Scope

The Angular application contains two major areas:

```text
PUBLIC PORTFOLIO
+
ADMIN CMS
```

Public:

```text
/
├── Home
├── Projects
├── Project Detail
├── Experience
├── Skills
├── Journey
├── Contact
└── Personal Chatbot
```

Admin:

```text
/admin/login

/admin
├── Dashboard
├── Profile
├── Experience
├── Education
├── Trainings
├── Certificates
├── Projects
├── Skills
├── Journey
├── Social Links
├── Site Settings
├── Media Library
├── Contact Messages
├── Knowledge
├── Agent Settings
└── Conversations
```

The public and admin areas share design tokens and low-level reusable components where useful, but their feature components remain separate.

---

# 3. Final Angular Project Structure

```text
frontend/
│
├── src/
│   ├── app/
│   │   ├── core/
│   │   ├── shared/
│   │   ├── layout/
│   │   ├── features/
│   │   │   ├── public/
│   │   │   └── admin/
│   │   ├── app.routes.ts
│   │   ├── app.config.ts
│   │   └── app.component.ts
│   │
│   ├── assets/
│   │   ├── images/
│   │   ├── icons/
│   │   └── placeholders/
│   │
│   ├── styles/
│   │   ├── _tokens.css
│   │   ├── _typography.css
│   │   ├── _utilities.css
│   │   └── styles.css
│   │
│   ├── environments/
│   │   ├── environment.ts
│   │   └── environment.production.ts
│   │
│   ├── index.html
│   └── main.ts
│
├── public/
│
├── angular.json
├── package.json
├── tailwind.config.js
├── tsconfig.json
└── README.md
```

---

# 4. High-Level Folder Responsibilities

## `core/`

Contains app-wide singleton services and infrastructure concerns.

Examples:

```text
core/
├── api/
├── auth/
├── guards/
├── interceptors/
├── models/
├── services/
├── config/
└── utils/
```

Do NOT place page components here.

Do NOT place feature-specific UI here.

---

## `shared/`

Contains reusable presentational components, directives, pipes, and UI primitives.

Examples:

```text
shared/
├── components/
├── directives/
├── pipes/
├── forms/
└── models/
```

Shared components must remain generic.

Examples:

```text
ButtonComponent
ModalComponent
ConfirmDialogComponent
StatusBadgeComponent
EmptyStateComponent
LoadingSkeletonComponent
PaginationComponent
SearchInputComponent
MarkdownViewerComponent
MediaPickerComponent
```

Do NOT put business-specific pages like `ProjectEditComponent` in `shared`.

---

## `layout/`

Contains shell/layout components.

```text
layout/
├── public-layout/
│   ├── public-header/
│   ├── public-footer/
│   └── public-layout.component.ts
│
└── admin-layout/
    ├── admin-sidebar/
    ├── admin-topbar/
    └── admin-layout.component.ts
```

Public and Admin layouts must match frozen Stitch `Template(3)`.

---

# 5. Core API Layer

Use Angular HttpClient.

Suggested structure:

```text
core/
└── api/
    ├── api-client.service.ts
    ├── api-response.model.ts
    ├── paged-result.model.ts
    └── api-error.model.ts
```

Generic response:

```ts
export interface ApiResponse<T> {
  success: boolean;
  data: T;
}
```

Error contract must match `docs/api-contract.md`.

Do NOT invent alternative response formats in the frontend.

---

# 6. Authentication

Structure:

```text
core/
└── auth/
    ├── auth.service.ts
    ├── auth.store.ts
    ├── auth.models.ts
    └── token-storage.service.ts
```

V1 supports:

```text
Admin Login
Refresh
Logout
Current Admin
```

Routes:

```text
/admin/login
/admin/**
```

Protected routes use:

```text
adminAuthGuard
```

Suggested flow:

```text
Admin Login
   ↓
POST /auth/login
   ↓
Access Token
   ↓
Auth Store
   ↓
Admin routes
```

Refresh-token implementation must follow backend/API contract.

Do not store refresh-token secrets insecurely in arbitrary local storage if the backend uses secure cookie-based refresh.

---

# 7. HTTP Interceptors

Structure:

```text
core/
└── interceptors/
    ├── auth.interceptor.ts
    └── error.interceptor.ts
```

`auth.interceptor`:

```text
attach access token to admin API requests
```

`error.interceptor`:

```text
normalize API errors
handle 401 consistently
never show raw backend exception text
```

Do NOT put feature business logic into interceptors.

---

# 8. Guards

```text
core/
└── guards/
    ├── admin-auth.guard.ts
    └── admin-login.guard.ts
```

Rules:

```text
/admin/login
→ redirect authenticated admin to /admin

/admin/**
→ require authenticated admin
```

No role/permission guards are needed in V1.

---

# 9. Public Feature Structure

```text
features/
└── public/
    ├── home/
    ├── projects/
    ├── project-detail/
    ├── experience/
    ├── skills/
    ├── journey/
    ├── contact/
    └── chat/
```

---

# 10. Public Home

Suggested structure:

```text
features/public/home/
├── pages/
│   └── home-page.component.ts
│
├── components/
│   ├── hero-section.component.ts
│   ├── quick-summary.component.ts
│   ├── about-section.component.ts
│   ├── experience-preview.component.ts
│   ├── featured-projects.component.ts
│   ├── skills-preview.component.ts
│   ├── journey-preview.component.ts
│   ├── education-preview.component.ts
│   └── contact-cta.component.ts
│
├── models/
│   └── portfolio-home.model.ts
│
└── services/
    └── public-portfolio.service.ts
```

Primary API:

```text
GET /api/v1/public/portfolio
```

Do not call many small APIs on first Home load unless a future deliberate requirement changes the contract.

---

# 11. Public Projects

```text
features/public/projects/
├── pages/
│   └── projects-page.component.ts
│
├── components/
│   ├── project-card.component.ts
│   └── project-filter.component.ts
│
├── models/
│   └── public-project.model.ts
│
└── services/
    └── public-projects.service.ts
```

API:

```text
GET /api/v1/public/projects
```

---

# 12. Public Project Detail

```text
features/public/project-detail/
├── pages/
│   └── project-detail-page.component.ts
│
├── components/
│   ├── project-hero.component.ts
│   ├── project-tech-stack.component.ts
│   ├── project-section-renderer.component.ts
│   ├── project-architecture.component.ts
│   ├── project-media-gallery.component.ts
│   └── project-learning-section.component.ts
│
├── models/
│   ├── project-detail.model.ts
│   └── project-section.model.ts
│
└── services/
    └── public-project-detail.service.ts
```

Route:

```text
/projects/:slug
```

API:

```text
GET /api/v1/public/projects/{slug}
```

`project-section-renderer.component.ts` must support:

```text
OVERVIEW
RESPONSIBILITIES
ARCHITECTURE
ENGINEERING_FOCUS
FEATURES
CHALLENGES
LEARNINGS
SCREENSHOTS
CUSTOM
```

Do not hardcode one component per known project.

SchoolSaaS and P-234 must both use the same generic Project Detail page.

---

# 13. Public Experience

```text
features/public/experience/
├── pages/
│   └── experience-page.component.ts
├── components/
│   └── experience-timeline.component.ts
└── models/
```

For V1, this page may use data already available from the portfolio aggregate or a future dedicated endpoint only if the API contract is deliberately updated.

Do not silently invent a new API.

---

# 14. Public Skills

```text
features/public/skills/
├── pages/
│   └── skills-page.component.ts
├── components/
│   ├── skill-group.component.ts
│   └── skill-badge.component.ts
└── models/
```

Skill levels:

```text
USED
LEARNING
EXPLORING
```

Do NOT add:

```text
percentage
stars
mastery
expert score
```

---

# 15. Public Journey

```text
features/public/journey/
├── pages/
│   └── journey-page.component.ts
├── components/
│   └── journey-timeline.component.ts
└── models/
```

Keep terminology grounded.

Do not introduce unsupported "Mastered" wording from mock UI.

---

# 16. Public Contact

```text
features/public/contact/
├── pages/
│   └── contact-page.component.ts
├── components/
│   └── contact-form.component.ts
├── models/
│   └── contact-request.model.ts
└── services/
    └── public-contact.service.ts
```

API:

```text
POST /api/v1/public/contact
```

Use Reactive Forms.

Validation must mirror API constraints.

Do not trust frontend validation as the only protection.

---

# 17. Public Chat

```text
features/public/chat/
├── components/
│   ├── chat-launcher.component.ts
│   ├── chat-panel.component.ts
│   ├── chat-message.component.ts
│   ├── chat-source.component.ts
│   └── chat-feedback.component.ts
│
├── models/
│   ├── chat-session.model.ts
│   ├── chat-message.model.ts
│   └── chat-source.model.ts
│
├── services/
│   └── public-chat.service.ts
│
└── stores/
    └── chat.store.ts
```

APIs:

```text
POST /api/v1/public/chat/sessions
POST /api/v1/public/chat/sessions/{sessionId}/messages
POST /api/v1/public/chat/messages/{messageId}/feedback
```

The chat launcher can be globally mounted inside the public layout if `showAiAgent = true`.

Chat store may use Angular Signals.

Do not add NgRx for this.

---

# 18. Admin Feature Structure

```text
features/
└── admin/
    ├── dashboard/
    ├── profile/
    ├── experiences/
    ├── education/
    ├── trainings/
    ├── certificates/
    ├── projects/
    ├── skills/
    ├── journey/
    ├── social-links/
    ├── site-settings/
    ├── media/
    ├── contact-messages/
    └── agent/
        ├── settings/
        ├── knowledge/
        └── conversations/
```

Each feature should own its own:

```text
pages/
components/
models/
services/
forms/
```

only when those folders are needed.

Do not create empty folders mechanically.

---

# 19. Admin Dashboard

```text
features/admin/dashboard/
├── pages/
│   └── dashboard-page.component.ts
├── components/
│   ├── summary-card.component.ts
│   ├── recent-updates.component.ts
│   └── knowledge-summary.component.ts
└── services/
    └── dashboard.service.ts
```

API:

```text
GET /api/v1/admin/dashboard
```

No fake infrastructure-monitoring widgets.

---

# 20. Admin Profile

```text
features/admin/profile/
├── pages/
│   └── profile-edit-page.component.ts
├── forms/
│   └── profile-form.factory.ts
├── models/
└── services/
    └── admin-profile.service.ts
```

APIs:

```text
GET /api/v1/admin/profile
PUT /api/v1/admin/profile
```

Only one canonical profile exists.

Do not build a profile list.

---

# 21. Admin Experiences

```text
features/admin/experiences/
├── pages/
│   ├── experience-list-page.component.ts
│   └── experience-edit-page.component.ts
├── components/
│   ├── experience-form.component.ts
│   └── technology-selector.component.ts
├── models/
└── services/
    └── admin-experiences.service.ts
```

APIs:

```text
GET    /admin/experiences
GET    /admin/experiences/{id}
POST   /admin/experiences
PUT    /admin/experiences/{id}
DELETE /admin/experiences/{id}
PUT    /admin/experiences/reorder
```

Do not add `DRAFT`.

Publication is:

```text
isPublished
```

---

# 22. Admin Projects

This is the richest admin feature.

```text
features/admin/projects/
├── pages/
│   ├── project-list-page.component.ts
│   └── project-edit-page.component.ts
│
├── components/
│   ├── project-basic-info.component.ts
│   ├── project-technologies.component.ts
│   ├── project-sections.component.ts
│   ├── project-section-editor.component.ts
│   ├── project-media.component.ts
│   ├── project-seo.component.ts
│   ├── project-preview.component.ts
│   └── project-status-badge.component.ts
│
├── forms/
│   ├── project-form.factory.ts
│   └── project-section-form.factory.ts
│
├── models/
│   ├── admin-project.model.ts
│   ├── project-section.model.ts
│   └── project-media.model.ts
│
└── services/
    └── admin-projects.service.ts
```

Preserve the frozen Stitch Project Editor structure:

```text
Basic Info
Technologies
Sections
Media
SEO Settings
```

Statuses:

```text
PLANNED
IN_PROGRESS
ACTIVE
COMPLETED
ARCHIVED
```

Do NOT add:

```text
DRAFT
PENDING_REVIEW
APPROVED
```

---

# 23. Project Section Rendering / Editing

Use structured UI.

Do NOT expose raw JSON editing.

Example UI adapters may exist for:

```text
ENGINEERING_FOCUS
FEATURES
CHALLENGES
```

but persistence still maps to:

```text
contentMarkdown
content
```

Use one generic data model plus type-specific editor UI where useful.

---

# 24. Admin Skills

```text
features/admin/skills/
├── pages/
│   └── skills-management-page.component.ts
├── components/
│   └── skill-form.component.ts
├── models/
└── services/
    └── admin-skills.service.ts
```

Levels:

```text
USED
LEARNING
EXPLORING
```

No percentage bars.

---

# 25. Education / Trainings / Certificates

Keep them separate.

Suggested feature structures:

```text
features/admin/education/
features/admin/trainings/
features/admin/certificates/
```

Each may contain:

```text
pages/
components/
models/
services/
```

Do not merge these into a single generic entity even if the UI forms look similar.

Reusable low-level form controls may live in `shared`.

---

# 26. Admin Journey

```text
features/admin/journey/
├── pages/
│   └── journey-management-page.component.ts
├── components/
│   └── journey-item-form.component.ts
├── models/
└── services/
    └── admin-journey.service.ts
```

Support reorder.

Do not add mastery/expert semantics.

---

# 27. Social Links

```text
features/admin/social-links/
├── pages/
│   └── social-links-page.component.ts
├── components/
│   └── social-link-form.component.ts
└── services/
    └── social-links.service.ts
```

---

# 28. Site Settings

```text
features/admin/site-settings/
├── pages/
│   └── site-settings-page.component.ts
├── forms/
│   └── site-settings-form.factory.ts
└── services/
    └── site-settings.service.ts
```

The UI should be typed and structured.

Do not expose raw JSON editing in normal admin flow.

---

# 29. Media Library

```text
features/admin/media/
├── pages/
│   └── media-library-page.component.ts
├── components/
│   ├── media-grid.component.ts
│   ├── media-card.component.ts
│   ├── media-upload.component.ts
│   └── media-details-drawer.component.ts
├── models/
└── services/
    └── media.service.ts
```

API:

```text
GET    /admin/media
POST   /admin/media
PUT    /admin/media/{id}
DELETE /admin/media/{id}
```

Use `multipart/form-data` for upload.

Do not place R2 credentials in frontend config.

---

# 30. Contact Messages

```text
features/admin/contact-messages/
├── pages/
│   ├── contact-message-list-page.component.ts
│   └── contact-message-detail-page.component.ts
├── components/
│   └── message-status-badge.component.ts
├── models/
└── services/
    └── contact-messages.service.ts
```

Statuses:

```text
NEW
READ
REPLIED
ARCHIVED
```

No CRM logic.

---

# 31. Agent Settings

```text
features/admin/agent/settings/
├── pages/
│   └── agent-settings-page.component.ts
├── forms/
│   └── agent-settings-form.factory.ts
├── models/
└── services/
    └── agent-settings.service.ts
```

Do NOT expose API keys.

Embedding dimensions:

```text
1536
```

read-only in V1.

---

# 32. Knowledge Management

```text
features/admin/agent/knowledge/
├── pages/
│   ├── knowledge-list-page.component.ts
│   └── knowledge-detail-page.component.ts
│
├── components/
│   ├── knowledge-status-badge.component.ts
│   ├── knowledge-filter-bar.component.ts
│   └── knowledge-chunk-list.component.ts
│
├── models/
│   ├── knowledge-document.model.ts
│   └── knowledge-chunk.model.ts
│
└── services/
    └── knowledge.service.ts
```

Statuses:

```text
PENDING
INDEXING
INDEXED
FAILED
```

Supported source types:

```text
PROFILE
EXPERIENCE
PROJECT
SKILLS
EDUCATION
TRAINING
CERTIFICATE
JOURNEY
MANUAL
```

Do not show:

```text
BLOG
ARTICLE
URL crawler
PDF uploader
```

Do not render raw vectors.

---

# 33. Conversations

```text
features/admin/agent/conversations/
├── pages/
│   ├── conversation-list-page.component.ts
│   └── conversation-detail-page.component.ts
│
├── components/
│   ├── conversation-message.component.ts
│   ├── conversation-source.component.ts
│   └── feedback-badge.component.ts
│
├── models/
└── services/
    └── conversations.service.ts
```

Valid session statuses:

```text
ACTIVE
CLOSED
```

Conversation detail is read-oriented.

No support-agent takeover.

No visitor account management.

---

# 34. Routing

Top-level route structure:

```ts
export const routes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./layout/public-layout/public-layout.component')
        .then(m => m.PublicLayoutComponent),
    children: [
      // public routes
    ]
  },
  {
    path: 'admin/login',
    loadComponent: () =>
      import('./features/admin/auth/login-page.component')
        .then(m => m.LoginPageComponent)
  },
  {
    path: 'admin',
    canActivate: [adminAuthGuard],
    loadComponent: () =>
      import('./layout/admin-layout/admin-layout.component')
        .then(m => m.AdminLayoutComponent),
    children: [
      // admin routes
    ]
  }
];
```

Feature pages should be lazy loaded.

Do not eagerly import the entire admin area into the public bundle.

---

# 35. Suggested Route Map

Public:

```text
/
 /projects
 /projects/:slug
 /experience
 /skills
 /journey
 /contact
```

Admin:

```text
/admin
/admin/profile
/admin/experience
/admin/experience/new
/admin/experience/:id

/admin/education
/admin/trainings
/admin/certificates

/admin/projects
/admin/projects/new
/admin/projects/:id

/admin/skills
/admin/journey

/admin/social-links
/admin/site-settings
/admin/media
/admin/contact-messages
/admin/contact-messages/:id

/admin/agent
/admin/agent/knowledge
/admin/agent/knowledge/:id
/admin/agent/conversations
/admin/agent/conversations/:id
```

Route naming must remain consistent with the frozen Stitch UI and API contract.

---

# 36. State Management

V1 does NOT use NgRx.

Use:

```text
local component state
+
Angular Signals
+
feature-level stores/services
```

Use a store only where shared state is genuinely useful.

Recommended stores:

```text
AuthStore
ChatStore
```

Potential lightweight feature stores only if implementation proves useful:

```text
ProjectEditorStore
MediaPickerStore
```

Do not create a global state architecture for every CRUD screen.

---

# 37. Forms

Use Angular Reactive Forms.

Do NOT use template-driven forms for complex Admin pages.

Examples:

```text
Profile
Experience
Project
Project Section
Skills
Education
Training
Certificate
Journey
Agent Settings
Site Settings
```

Validation should mirror API constraints.

Form state should support:

```text
pristine
dirty
saving
saved
error
```

Protect against accidental navigation when important unsaved changes exist.

---

# 38. Design System

The frozen Stitch UI is the visual reference.

Extract reusable design tokens into:

```text
styles/_tokens.css
```

Examples:

```text
surface colors
primary purple
border colors
text colors
spacing
container width
radius
font families
font sizes
```

Use Tailwind utilities built around these tokens.

Do not copy large arbitrary inline Tailwind class blobs everywhere if a reusable component or token can express the same pattern.

---

# 39. Typography

Preserve Stitch typography:

```text
Headings:
Space Grotesk-like

Body:
Geist-like
```

Do not randomly substitute fonts per feature.

Use consistent typography across:

```text
Public
Admin
Chat
```

---

# 40. Shared UI Components

Recommended:

```text
shared/components/
├── app-button/
├── status-badge/
├── confirm-dialog/
├── empty-state/
├── loading-skeleton/
├── pagination/
├── search-input/
├── filter-bar/
├── markdown-viewer/
├── markdown-editor/
├── media-picker/
├── form-field/
├── page-header/
└── toast/
```

Only create a shared component after at least two features genuinely need the abstraction, except obvious foundational controls.

Do not over-componentize simple markup.

---

# 41. Markdown Rendering

Portfolio content may contain Markdown.

Use a safe Markdown renderer.

Requirements:

```text
sanitize rendered HTML
no trusted arbitrary script execution
no raw dangerous HTML from admin content
```

Markdown is used for:

```text
About
Experience responsibilities
Project overview/sections
other long-form portfolio content
```

---

# 42. Media Handling

Frontend sends files only to ASP.NET Core.

Never:

```text
Angular
   ↓
Cloudflare R2 secret credentials
```

Correct:

```text
Angular
   ↓
ASP.NET Core
   ↓
Cloudflare R2
```

Frontend environment only knows:

```text
API base URL
```

No R2 secrets.

---

# 43. OpenAI / Chat Security

Never put:

```text
OPENAI_API_KEY
```

in:

```text
environment.ts
environment.production.ts
Angular source
Vercel public environment variables
frontend bundle
```

Correct:

```text
Angular
   ↓
POST /api/v1/public/chat/...
   ↓
ASP.NET Core
   ↓
OpenAI
```

---

# 44. Environment Configuration

Frontend environment:

```ts
export const environment = {
  production: false,
  apiBaseUrl: 'http://localhost:5000/api/v1'
};
```

Production:

```ts
export const environment = {
  production: true,
  apiBaseUrl: 'https://api.example.com/api/v1'
};
```

No secrets.

---

# 45. Error UX

Errors must be translated into user-facing states.

Examples:

```text
VALIDATION_ERROR
→ show field validation

401
→ refresh session or redirect login

404 PROJECT_NOT_FOUND
→ public not-found state

409
→ show business conflict

429
→ show rate-limit feedback

503 AGENT_UNAVAILABLE
→ chatbot temporary-unavailable message
```

Do not render raw JSON exception objects to normal users.

---

# 46. Loading UX

Use scoped loading states.

Examples:

```text
Page skeleton
Button saving spinner
Media upload progress
Chat typing state
Knowledge reindex queued state
```

Do not block the entire app for every request.

---

# 47. Public Performance

Use lazy loading for:

```text
Admin
Project Detail
heavy galleries
chat UI if appropriate
```

Avoid shipping the full Admin CMS in the initial public bundle.

Images:

```text
lazy load
responsive sizing
alt text
```

No requirement for complex image CDN transformations in V1.

---

# 48. Accessibility

Required:

```text
semantic headings
keyboard navigation
visible focus states
form labels
button accessible names
sufficient contrast
touch-friendly controls
meaningful alt text
```

Do not rely only on color for status.

---

# 49. Responsive Rules

Frozen Stitch desktop design is primary.

Responsive behavior:

```text
Public navbar
→ mobile menu

Admin sidebar
→ drawer/collapsed navigation

Tables
→ horizontal scroll or stacked cards

Forms
→ single column on small screens

Project editor tabs
→ scrollable or dropdown on mobile
```

Do not create a completely separate mobile product.

---

# 50. Public/Admin Separation

Public feature code must not depend on Admin feature code.

Bad:

```text
PublicProjectCard
imports AdminProjectEditorModel
```

Preferred:

```text
Public project models
Admin project models
shared primitive models only where truly identical
```

Services remain separated:

```text
PublicProjectsService
AdminProjectsService
```

because response shapes and concerns differ.

---

# 51. Model Strategy

Use explicit frontend DTO/view models aligned to API contracts.

Do not directly model PostgreSQL tables in Angular.

Example:

```text
DB Project
≠
AdminProjectDto
≠
PublicProjectCardDto
≠
PublicProjectDetailDto
```

Angular models should reflect the API response shape.

---

# 52. API Service Strategy

One feature service per business area is preferred.

Examples:

```text
PublicPortfolioService
PublicProjectsService
PublicChatService

AdminProjectsService
AdminExperiencesService
MediaService
KnowledgeService
ConversationsService
```

Do not create one huge:

```text
ApiService
```

containing every endpoint in the system.

`ApiClientService` may provide low-level common HTTP helpers only.

---

# 53. No Generated API Client Requirement

V1 does not require:

```text
OpenAPI generated TypeScript client
NSwag generated frontend SDK
```

Manual typed feature services are sufficient.

If a generated client is introduced later, it must be deliberate and not disrupt the feature structure.

---

# 54. Testing

Suggested structure:

```text
src/app/
...
```

Tests colocated or under Angular testing conventions.

Priority frontend tests:

```text
Auth guard
Auth service
Project editor validation
Contact form validation
Chat store
Knowledge status mapping
Critical public page rendering
```

Do not create excessive snapshot tests for every small presentational component.

---

# 55. V1 Non-Goals

Do NOT add:

```text
NgRx
Redux
Akita
micro frontends
SSR requirement
Angular Universal requirement
GraphQL
WebSockets
SignalR
React
Next.js
Node backend
Firebase
Supabase client
direct PostgreSQL access
direct R2 client with secrets
direct OpenAI calls
role/permission UI
multi-admin management
blog CMS
analytics platform
CRM
support dashboard
```

---

# 56. Naming Rules

Use consistent Angular naming.

Examples:

```text
project-list-page.component.ts
project-edit-page.component.ts
project-card.component.ts
admin-projects.service.ts
public-projects.service.ts
project-form.factory.ts
```

Prefer feature terminology from:

```text
database/schema.sql
docs/api-contract.md
docs/backend-architecture.md
```

Do not invent alternate terms.

---

# 57. Source of Truth Priority

If implementation sources disagree:

```text
1. PostgreSQL Schema V1
2. API Contract V1
3. Backend Architecture V1
4. Frontend Architecture V1
5. Frozen Stitch Template(3)
6. Mock/seed data
```

For pure visual styling, frozen Stitch remains the design reference.

For behavior/data shape, API contract wins.

---

# 58. Final Route / Feature Mapping

```text
PUBLIC

/
→ Home
→ GET /public/portfolio

/projects
→ Public Projects
→ GET /public/projects

/projects/:slug
→ Project Detail
→ GET /public/projects/{slug}

/contact
→ Contact Form
→ POST /public/contact

Chat Widget
→ POST /public/chat/sessions
→ POST /public/chat/sessions/{id}/messages
→ POST /public/chat/messages/{id}/feedback
```

```text
ADMIN

/admin
→ Dashboard

/admin/profile
→ Profile

/admin/experience
→ Experience

/admin/education
→ Education

/admin/trainings
→ Trainings

/admin/certificates
→ Certificates

/admin/projects
→ Projects

/admin/skills
→ Skills

/admin/journey
→ Journey

/admin/social-links
→ Social Links

/admin/site-settings
→ Site Settings

/admin/media
→ Media Library

/admin/contact-messages
→ Inbox

/admin/agent
→ Agent Settings

/admin/agent/knowledge
→ Knowledge

/admin/agent/conversations
→ Conversations
```

---

# 59. Freeze Rule

This file defines Frontend Architecture V1.

Do not silently change:

```text
feature boundaries
public/admin separation
routing structure
state-management strategy
API service strategy
auth strategy
Project Detail generic renderer
Project Editor structure
chat API boundary
media upload boundary
```

during Codex implementation.

If a genuine missing frontend requirement is discovered:

```text
identify requirement
      ↓
check API/domain impact
      ↓
update source-of-truth docs deliberately
      ↓
then implement
```

Do not let generated code redesign architecture merely because another Angular pattern is common.

---

# 60. Final Frontend V1 Summary

```text
Angular
   │
   ├── Public Layout
   │    ├── Home
   │    ├── Projects
   │    ├── Project Detail
   │    ├── Experience
   │    ├── Skills
   │    ├── Journey
   │    ├── Contact
   │    └── Chat Widget
   │
   └── Admin Layout
        ├── Dashboard
        ├── Portfolio CMS
        ├── Website Settings
        ├── Media
        ├── Inbox
        └── AI Agent Admin
```

Core technical rules:

```text
Angular Router
Reactive Forms
HttpClient
Signals where useful
Feature-based folders
Lazy-loaded Admin
No NgRx
No direct OpenAI
No direct R2 secrets
No direct database access
API contract is authoritative
Stitch Template(3) is visual reference
```
