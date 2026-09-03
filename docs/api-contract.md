# API Contract V1 — Personal Portfolio + Personal RAG Agent

## 1. Scope

This document defines the REST API contract between:

- Frontend: Angular + TypeScript
- Backend: ASP.NET Core Web API + C#
- Database: PostgreSQL / Neon
- ORM: Entity Framework Core
- Vector Search: PostgreSQL + pgvector
- Media Storage: Cloudflare R2
- Public AI Agent: portfolio-focused RAG chatbot

The PostgreSQL Schema V1 is considered frozen with 26 tables.

The UI reference is the frozen Stitch `Template(3)` design.

This API contract is the source of truth for endpoint design, request/response DTOs, authentication requirements, validation, and expected HTTP responses.

---

# 2. General API Conventions

Base URL:

```text
/api/v1
```

Production example:

```text
https://api.example.com/api/v1
```

Content type:

```http
Content-Type: application/json
```

Authentication:

```http
Authorization: Bearer <access-token>
```

Admin endpoints require authentication unless explicitly stated otherwise.

Public endpoints do not require admin authentication.

---

# 3. Standard Response Shapes

## 3.1 Success with data

```json
{
  "success": true,
  "data": {}
}
```

## 3.2 Success without data

```json
{
  "success": true
}
```

## 3.3 Validation error

```json
{
  "success": false,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "One or more validation errors occurred.",
    "details": {
      "title": [
        "Title is required."
      ]
    }
  }
}
```

## 3.4 General error

```json
{
  "success": false,
  "error": {
    "code": "PROJECT_NOT_FOUND",
    "message": "Project was not found."
  }
}
```

Do not expose stack traces, connection strings, provider errors, API keys, or raw exception messages.

---

# 4. Pagination Contract

List endpoints that may grow support:

```text
?page=1&pageSize=20
```

Standard response:

```json
{
  "success": true,
  "data": {
    "items": [],
    "page": 1,
    "pageSize": 20,
    "total": 0,
    "totalPages": 0
  }
}
```

Default:

```text
page = 1
pageSize = 20
```

Recommended maximum:

```text
pageSize = 100
```

---

# 5. Sort and Display Order

Portfolio entities already contain `displayOrder`.

Public APIs must return items in business display order instead of database insertion order.

Default public ordering:

```text
displayOrder ASC
```

Where dates are relevant as a secondary ordering:

```text
startDate DESC
```

---

# 6. Enum Contracts

## ProjectStatus

```text
PLANNED
IN_PROGRESS
ACTIVE
COMPLETED
ARCHIVED
```

## SkillExperienceLevel

```text
USED
LEARNING
EXPLORING
```

## KnowledgeSourceType

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

## KnowledgeIndexingStatus

```text
PENDING
INDEXING
INDEXED
FAILED
```

## ChatSessionStatus

```text
ACTIVE
CLOSED
```

## ChatMessageRole

```text
USER
ASSISTANT
SYSTEM
```

## ChatFeedbackRating

```text
POSITIVE
NEGATIVE
```

## MediaType

```text
IMAGE
DOCUMENT
CV
OTHER
```

## ProjectMediaRole

```text
THUMBNAIL
SCREENSHOT
ARCHITECTURE
DIAGRAM
OTHER
```

---

# 7. Authentication API

## 7.1 Login

```http
POST /api/v1/auth/login
```

Authentication:

```text
Public
```

Request:

```json
{
  "email": "admin@example.com",
  "password": "********"
}
```

Response `200 OK`:

```json
{
  "success": true,
  "data": {
    "accessToken": "<jwt>",
    "expiresIn": 900,
    "admin": {
      "id": "uuid",
      "email": "admin@example.com",
      "fullName": "Nguyễn Đình Phúc"
    }
  }
}
```

Refresh token:

- Must not be returned as a reusable plain database token.
- Prefer secure HttpOnly cookie if frontend/backend deployment allows it.
- If bearer-style refresh is chosen later, only a secure opaque token may be returned and only its hash is persisted.

Validation:

- email required
- valid email format
- password required
- password max length must be enforced

Errors:

```text
400 VALIDATION_ERROR
401 INVALID_CREDENTIALS
403 ADMIN_DISABLED
429 TOO_MANY_REQUESTS
```

DB:

```text
admin_users
admin_refresh_tokens
```

UI:

```text
/admin/login
```

---

## 7.2 Refresh Session

```http
POST /api/v1/auth/refresh
```

Authentication:

```text
Refresh token
```

Response `200 OK`:

```json
{
  "success": true,
  "data": {
    "accessToken": "<jwt>",
    "expiresIn": 900
  }
}
```

Behavior:

- refresh-token rotation
- revoke old refresh token
- create replacement token
- transactionally persist rotation

Errors:

```text
401 INVALID_REFRESH_TOKEN
401 REFRESH_TOKEN_EXPIRED
401 REFRESH_TOKEN_REVOKED
```

---

## 7.3 Logout

```http
POST /api/v1/auth/logout
```

Authentication:

```text
Admin
```

Response:

```http
204 No Content
```

Behavior:

- revoke current refresh token/session

---

## 7.4 Current Admin

```http
GET /api/v1/auth/me
```

Authentication:

```text
Admin
```

Response:

```json
{
  "success": true,
  "data": {
    "id": "uuid",
    "email": "admin@example.com",
    "fullName": "Nguyễn Đình Phúc",
    "lastLoginAt": "2026-08-27T02:00:00Z"
  }
}
```

---

# 8. Public Portfolio API

## 8.1 Get Portfolio Home Aggregate

```http
GET /api/v1/public/portfolio
```

Authentication:

```text
Public
```

Purpose:

Return the data needed for the main public portfolio page in one aggregate response.

Response:

```json
{
  "success": true,
  "data": {
    "profile": {
      "fullName": "Nguyễn Đình Phúc",
      "professionalTitle": ".NET / Full-stack Developer",
      "secondaryTitle": "Backend · Full-stack · AI Engineering",
      "heroHeadline": "Architecting robust backends and intelligent systems.",
      "heroSummary": "...",
      "aboutMarkdown": "...",
      "email": "...",
      "location": "Vietnam",
      "university": "...",
      "major": "Software Engineering",
      "availabilityStatus": "...",
      "profileImageUrl": null,
      "cvUrl": "https://..."
    },
    "experiences": [],
    "featuredProjects": [],
    "skills": [],
    "educations": [],
    "trainings": [],
    "certificates": [],
    "journey": [],
    "socialLinks": []
  }
}
```

Rules:

- only published/visible content
- only active technologies
- only published profile
- only `featured = true` projects in `featuredProjects`
- sort by `displayOrder`

DB:

```text
profiles
experiences
experience_technologies
technologies
projects
project_technologies
skills
educations
trainings
certificates
journey_items
social_links
media_assets
```

UI:

```text
/
```

---

## 8.2 Get Public Projects

```http
GET /api/v1/public/projects
```

Optional query:

```text
?featured=true
```

Response:

```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "slug": "school-saas",
      "title": "SchoolSaaS",
      "subtitle": "Multi-tenant School Management SaaS",
      "shortDescription": "...",
      "role": "Backend / Full-stack Developer",
      "status": "ACTIVE",
      "featured": true,
      "thumbnailUrl": "https://...",
      "technologies": [
        {
          "id": "uuid",
          "name": "ASP.NET Core",
          "category": "Backend"
        }
      ]
    }
  ]
}
```

Rules:

- `isPublished = true`
- archived items may still be public if explicitly published
- default ordering by `displayOrder`

---

## 8.3 Get Public Project by Slug

```http
GET /api/v1/public/projects/{slug}
```

Response:

```json
{
  "success": true,
  "data": {
    "id": "uuid",
    "slug": "school-saas",
    "title": "SchoolSaaS",
    "subtitle": "...",
    "shortDescription": "...",
    "overviewMarkdown": "...",
    "role": "Backend / Full-stack Developer",
    "teamSize": 2,
    "startDate": "2026-05-01",
    "endDate": null,
    "status": "ACTIVE",
    "githubUrl": "...",
    "liveUrl": null,
    "thumbnailUrl": "...",
    "seo": {
      "title": "...",
      "description": "..."
    },
    "technologies": [],
    "sections": [
      {
        "id": "uuid",
        "sectionType": "ENGINEERING_FOCUS",
        "title": "Engineering Focus",
        "subtitle": null,
        "contentMarkdown": null,
        "content": {},
        "displayOrder": 1
      }
    ],
    "media": [
      {
        "id": "uuid",
        "role": "SCREENSHOT",
        "url": "https://...",
        "altText": "...",
        "caption": "...",
        "displayOrder": 1
      }
    ]
  }
}
```

Errors:

```text
404 PROJECT_NOT_FOUND
```

Rules:

- unpublished project behaves as not found

UI:

```text
/projects/:slug
```

---

# 9. Admin Dashboard API

## 9.1 Dashboard Summary

```http
GET /api/v1/admin/dashboard
```

Authentication:

```text
Admin
```

Response:

```json
{
  "success": true,
  "data": {
    "projects": 5,
    "experiences": 1,
    "skills": 24,
    "certificates": 0,
    "knowledge": {
      "indexed": 8,
      "pending": 1,
      "failed": 0
    },
    "conversations": 12,
    "recentUpdates": []
  }
}
```

No fake infrastructure metrics.

UI:

```text
/admin
```

---

# 10. Admin Profile API

## 10.1 Get Profile

```http
GET /api/v1/admin/profile
```

Authentication:

```text
Admin
```

Response:

```json
{
  "success": true,
  "data": {
    "id": "uuid",
    "fullName": "Nguyễn Đình Phúc",
    "professionalTitle": ".NET / Full-stack Developer",
    "secondaryTitle": "Backend · Full-stack · AI Engineering",
    "heroHeadline": "...",
    "heroSummary": "...",
    "aboutMarkdown": "...",
    "email": "...",
    "phone": null,
    "location": "Vietnam",
    "university": "...",
    "major": "Software Engineering",
    "availabilityStatus": "...",
    "profileImage": null,
    "cvMedia": null,
    "isPublished": true,
    "updatedAt": "..."
  }
}
```

---

## 10.2 Update Profile

```http
PUT /api/v1/admin/profile
```

Request:

```json
{
  "fullName": "Nguyễn Đình Phúc",
  "professionalTitle": ".NET / Full-stack Developer",
  "secondaryTitle": "Backend · Full-stack · AI Engineering",
  "heroHeadline": "...",
  "heroSummary": "...",
  "aboutMarkdown": "...",
  "email": "...",
  "phone": null,
  "location": "Vietnam",
  "university": "...",
  "major": "Software Engineering",
  "availabilityStatus": "...",
  "profileImageId": null,
  "cvMediaId": null,
  "isPublished": true
}
```

Response:

```text
200 OK
```

Behavior:

- update singleton profile
- mark/regenerate related `PROFILE` knowledge after successful business update

DB:

```text
profiles
knowledge_documents
```

---

# 11. Admin Experience API

## 11.1 List Experiences

```http
GET /api/v1/admin/experiences
```

Optional:

```text
?search=rtc
```

Response:

```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "companyName": "RTC Technology Vietnam",
      "roleTitle": "Software Developer Intern",
      "startDate": "2025-08-01",
      "endDate": null,
      "isCurrent": true,
      "isPublished": true,
      "displayOrder": 1,
      "technologies": []
    }
  ]
}
```

---

## 11.2 Get Experience

```http
GET /api/v1/admin/experiences/{id}
```

---

## 11.3 Create Experience

```http
POST /api/v1/admin/experiences
```

Request:

```json
{
  "companyName": "RTC Technology Vietnam",
  "roleTitle": "Software Developer Intern",
  "location": "Vietnam",
  "startDate": "2025-08-01",
  "endDate": null,
  "isCurrent": true,
  "summary": "...",
  "responsibilitiesMarkdown": "...",
  "companyUrl": null,
  "displayOrder": 1,
  "isPublished": true,
  "technologyIds": [
    "uuid"
  ]
}
```

Response:

```text
201 Created
```

---

## 11.4 Update Experience

```http
PUT /api/v1/admin/experiences/{id}
```

Request:

Same writable fields as create.

Behavior:

- replace selected technology associations atomically
- mark corresponding `EXPERIENCE` knowledge as pending

---

## 11.5 Delete Experience

```http
DELETE /api/v1/admin/experiences/{id}
```

Response:

```http
204 No Content
```

Associated bridge rows cascade.

---

## 11.6 Reorder Experiences

```http
PUT /api/v1/admin/experiences/reorder
```

Request:

```json
{
  "items": [
    {
      "id": "uuid",
      "displayOrder": 1
    }
  ]
}
```

---

# 12. Technologies API

Technologies are a shared lookup used by projects, experiences, and skills.

## 12.1 List Technologies

```http
GET /api/v1/admin/technologies
```

Optional:

```text
?search=asp&category=Backend
```

---

## 12.2 Create Technology

```http
POST /api/v1/admin/technologies
```

Request:

```json
{
  "name": "ASP.NET Core",
  "category": "Backend",
  "iconKey": "dotnet",
  "websiteUrl": "https://dotnet.microsoft.com/",
  "displayOrder": 1,
  "isActive": true
}
```

Errors:

```text
409 TECHNOLOGY_NAME_EXISTS
```

---

## 12.3 Update Technology

```http
PUT /api/v1/admin/technologies/{id}
```

---

## 12.4 Delete Technology

```http
DELETE /api/v1/admin/technologies/{id}
```

Return `409 TECHNOLOGY_IN_USE` if deletion would break intended referenced content and hard delete is not allowed by implementation policy.

---

# 13. Admin Projects API

## 13.1 List Projects

```http
GET /api/v1/admin/projects
```

Query:

```text
?page=1&pageSize=20
&search=school
&status=ACTIVE
&featured=true
&isPublished=true
```

Response:

```json
{
  "success": true,
  "data": {
    "items": [
      {
        "id": "uuid",
        "slug": "school-saas",
        "title": "SchoolSaaS",
        "role": "Backend / Full-stack Developer",
        "status": "ACTIVE",
        "featured": true,
        "isPublished": true,
        "displayOrder": 1,
        "thumbnailUrl": "...",
        "updatedAt": "..."
      }
    ],
    "page": 1,
    "pageSize": 20,
    "total": 1,
    "totalPages": 1
  }
}
```

---

## 13.2 Get Project

```http
GET /api/v1/admin/projects/{id}
```

Response includes:

```text
basic info
technologies
sections
media
SEO
```

---

## 13.3 Create Project

```http
POST /api/v1/admin/projects
```

Request:

```json
{
  "slug": "school-saas",
  "title": "SchoolSaaS",
  "subtitle": "Multi-tenant School Management SaaS",
  "shortDescription": "...",
  "overviewMarkdown": "...",
  "role": "Backend / Full-stack Developer",
  "teamSize": 2,
  "startDate": "2026-05-01",
  "endDate": null,
  "status": "ACTIVE",
  "githubUrl": "...",
  "liveUrl": null,
  "thumbnailMediaId": null,
  "featured": true,
  "isPublished": true,
  "displayOrder": 1,
  "seoTitle": "...",
  "seoDescription": "...",
  "technologyIds": []
}
```

Errors:

```text
409 PROJECT_SLUG_EXISTS
```

---

## 13.4 Update Project Basic Info

```http
PUT /api/v1/admin/projects/{id}
```

Same writable basic fields as create.

---

## 13.5 Delete Project

```http
DELETE /api/v1/admin/projects/{id}
```

Response:

```http
204 No Content
```

Cascade:

```text
project_technologies
project_sections
project_media
derived project knowledge
```

Media asset itself is not automatically deleted from R2 merely because a project relation is removed.

---

## 13.6 Reorder Projects

```http
PUT /api/v1/admin/projects/reorder
```

---

# 14. Project Technology API

No standalone CRUD controller for `project_technologies`.

## Replace Project Technologies

```http
PUT /api/v1/admin/projects/{projectId}/technologies
```

Request:

```json
{
  "items": [
    {
      "technologyId": "uuid",
      "displayOrder": 1
    }
  ]
}
```

Response:

```text
200 OK
```

Behavior:

Replace project technology associations atomically.

---

# 15. Project Sections API

## 15.1 List Project Sections

```http
GET /api/v1/admin/projects/{projectId}/sections
```

---

## 15.2 Create Project Section

```http
POST /api/v1/admin/projects/{projectId}/sections
```

Request:

```json
{
  "sectionType": "ENGINEERING_FOCUS",
  "title": "Engineering Focus",
  "subtitle": null,
  "contentMarkdown": null,
  "content": {
    "items": [
      {
        "title": "Multi-tenant Architecture",
        "description": "..."
      }
    ]
  },
  "displayOrder": 1,
  "isVisible": true
}
```

Validation:

`sectionType` must be one of:

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

---

## 15.3 Update Project Section

```http
PUT /api/v1/admin/projects/{projectId}/sections/{sectionId}
```

---

## 15.4 Delete Project Section

```http
DELETE /api/v1/admin/projects/{projectId}/sections/{sectionId}
```

---

## 15.5 Reorder Project Sections

```http
PUT /api/v1/admin/projects/{projectId}/sections/reorder
```

---

# 16. Project Media API

## 16.1 List Project Media

```http
GET /api/v1/admin/projects/{projectId}/media
```

---

## 16.2 Attach Media to Project

```http
POST /api/v1/admin/projects/{projectId}/media
```

Request:

```json
{
  "mediaAssetId": "uuid",
  "mediaRole": "SCREENSHOT",
  "caption": "...",
  "displayOrder": 1
}
```

---

## 16.3 Update Project Media Link

```http
PUT /api/v1/admin/projects/{projectId}/media/{projectMediaId}
```

---

## 16.4 Remove Media from Project

```http
DELETE /api/v1/admin/projects/{projectId}/media/{projectMediaId}
```

This removes only the project-media association.

---

# 17. Skills API

## 17.1 List Skills

```http
GET /api/v1/admin/skills
```

Optional:

```text
?category=Backend&experienceLevel=USED
```

---

## 17.2 Create Skill

```http
POST /api/v1/admin/skills
```

Request:

```json
{
  "name": "ASP.NET Core",
  "category": "Backend",
  "experienceLevel": "USED",
  "description": "...",
  "technologyId": "uuid",
  "displayOrder": 1,
  "isPublished": true
}
```

---

## 17.3 Update Skill

```http
PUT /api/v1/admin/skills/{id}
```

---

## 17.4 Delete Skill

```http
DELETE /api/v1/admin/skills/{id}
```

---

## 17.5 Reorder Skills

```http
PUT /api/v1/admin/skills/reorder
```

---

# 18. Education API

## List

```http
GET /api/v1/admin/education
```

## Get

```http
GET /api/v1/admin/education/{id}
```

## Create

```http
POST /api/v1/admin/education
```

Request:

```json
{
  "institution": "...",
  "degree": "...",
  "fieldOfStudy": "Software Engineering",
  "startDate": null,
  "endDate": null,
  "description": "...",
  "location": "Vietnam",
  "displayOrder": 1,
  "isPublished": true
}
```

## Update

```http
PUT /api/v1/admin/education/{id}
```

## Delete

```http
DELETE /api/v1/admin/education/{id}
```

## Reorder

```http
PUT /api/v1/admin/education/reorder
```

---

# 19. Trainings API

## List

```http
GET /api/v1/admin/trainings
```

## Get

```http
GET /api/v1/admin/trainings/{id}
```

## Create

```http
POST /api/v1/admin/trainings
```

Request:

```json
{
  "title": "...",
  "provider": "...",
  "description": "...",
  "startDate": null,
  "endDate": null,
  "credentialUrl": null,
  "displayOrder": 1,
  "isPublished": true
}
```

## Update

```http
PUT /api/v1/admin/trainings/{id}
```

## Delete

```http
DELETE /api/v1/admin/trainings/{id}
```

## Reorder

```http
PUT /api/v1/admin/trainings/reorder
```

---

# 20. Certificates API

## List

```http
GET /api/v1/admin/certificates
```

## Get

```http
GET /api/v1/admin/certificates/{id}
```

## Create

```http
POST /api/v1/admin/certificates
```

Request:

```json
{
  "name": "...",
  "issuer": "...",
  "issuedAt": null,
  "expiresAt": null,
  "credentialId": null,
  "credentialUrl": null,
  "certificateMediaId": null,
  "displayOrder": 1,
  "isPublished": true
}
```

## Update

```http
PUT /api/v1/admin/certificates/{id}
```

## Delete

```http
DELETE /api/v1/admin/certificates/{id}
```

## Reorder

```http
PUT /api/v1/admin/certificates/reorder
```

---

# 21. Journey API

## List

```http
GET /api/v1/admin/journey
```

## Get

```http
GET /api/v1/admin/journey/{id}
```

## Create

```http
POST /api/v1/admin/journey
```

Request:

```json
{
  "title": "Frontend Development",
  "subtitle": "...",
  "description": "...",
  "occurredAt": "2025-01-01",
  "iconKey": "code",
  "displayOrder": 1,
  "isPublished": true
}
```

## Update

```http
PUT /api/v1/admin/journey/{id}
```

## Delete

```http
DELETE /api/v1/admin/journey/{id}
```

## Reorder

```http
PUT /api/v1/admin/journey/reorder
```

---

# 22. Social Links API

## List

```http
GET /api/v1/admin/social-links
```

## Create

```http
POST /api/v1/admin/social-links
```

Request:

```json
{
  "platform": "GitHub",
  "label": "GitHub",
  "url": "https://github.com/...",
  "iconKey": "github",
  "displayOrder": 1,
  "isVisible": true
}
```

## Update

```http
PUT /api/v1/admin/social-links/{id}
```

## Delete

```http
DELETE /api/v1/admin/social-links/{id}
```

## Reorder

```http
PUT /api/v1/admin/social-links/reorder
```

---

# 23. Site Settings API

## Get Settings

```http
GET /api/v1/admin/site-settings
```

Response:

```json
{
  "success": true,
  "data": {
    "siteName": "Nguyễn Đình Phúc",
    "footerText": "...",
    "showAvailability": true,
    "showDownloadCv": true,
    "showJourney": true,
    "showAiAgent": true,
    "defaultSeoTitle": "...",
    "defaultSeoDescription": "..."
  }
}
```

The backend maps these typed settings to/from `site_settings` JSONB values.

## Update Settings

```http
PUT /api/v1/admin/site-settings
```

Do not expose arbitrary database JSON editing in the normal UI.

---

# 24. Media API

## 24.1 List Media

```http
GET /api/v1/admin/media
```

Query:

```text
?page=1&pageSize=20&mediaType=IMAGE&search=school
```

---

## 24.2 Upload Media

```http
POST /api/v1/admin/media
Content-Type: multipart/form-data
```

Fields:

```text
file
mediaType
altText
```

Response:

```json
{
  "success": true,
  "data": {
    "id": "uuid",
    "fileName": "school-saas.png",
    "mimeType": "image/png",
    "fileSize": 123456,
    "mediaType": "IMAGE",
    "storageKey": "portfolio/...",
    "publicUrl": "https://...",
    "altText": "..."
  }
}
```

Security:

- server-side file size limit
- MIME/type validation
- safe generated storage key
- never trust client filename for object path
- secrets remain backend-only

Storage:

```text
Cloudflare R2
```

DB:

```text
media_assets
```

---

## 24.3 Update Media Metadata

```http
PUT /api/v1/admin/media/{id}
```

Request:

```json
{
  "altText": "...",
  "mediaType": "IMAGE"
}
```

---

## 24.4 Delete Media

```http
DELETE /api/v1/admin/media/{id}
```

Behavior:

- reject if media is still referenced, unless the implementation explicitly performs a safe detach operation
- remove R2 object and DB metadata consistently
- no half-deleted state

Possible error:

```text
409 MEDIA_IN_USE
```

---

# 26. Agent Settings API

## 26.1 Get Agent Settings

```http
GET /api/v1/admin/agent/settings
```

Response:

```json
{
  "success": true,
  "data": {
    "id": "uuid",
    "name": "portfolio-agent",
    "enabled": true,
    "provider": "Gemini",
    "modelName": "...",
    "embeddingProvider": "Gemini",
    "embeddingModel": "...",
    "embeddingDimensions": 1536,
    "systemPrompt": "...",
    "welcomeMessage": "...",
    "fallbackMessage": "...",
    "maxContextChunks": 6,
    "minimumSimilarity": 0.6,
    "temperature": 0.2
  }
}
```

Never return API keys.

---

## 26.2 Update Agent Settings

```http
PUT /api/v1/admin/agent/settings
```

Request:

```json
{
  "enabled": true,
  "provider": "Gemini",
  "modelName": "...",
  "embeddingProvider": "Gemini",
  "embeddingModel": "...",
  "systemPrompt": "...",
  "welcomeMessage": "...",
  "fallbackMessage": "...",
  "maxContextChunks": 6,
  "minimumSimilarity": 0.6,
  "temperature": 0.2
}
```

`embeddingDimensions` is fixed to `1536` in DB V1 and is not writable from normal admin UI.

---

# 27. Knowledge Management API

Knowledge is derived data.

Portfolio domain data remains source of truth.

## 27.1 List Knowledge Documents

```http
GET /api/v1/admin/agent/knowledge
```

Query:

```text
?page=1&pageSize=20
&sourceType=PROJECT
&status=FAILED
&search=school
```

Response:

```json
{
  "success": true,
  "data": {
    "items": [
      {
        "id": "uuid",
        "sourceType": "PROJECT",
        "sourceRefId": "uuid",
        "sourceKey": "project:school-saas",
        "title": "SchoolSaaS",
        "version": 2,
        "indexingStatus": "INDEXED",
        "chunkCount": 8,
        "indexedAt": "...",
        "updatedAt": "...",
        "lastIndexError": null
      }
    ],
    "page": 1,
    "pageSize": 20,
    "total": 1,
    "totalPages": 1
  }
}
```

---

## 27.2 Get Knowledge Document Detail

```http
GET /api/v1/admin/agent/knowledge/{id}
```

Response:

```json
{
  "success": true,
  "data": {
    "id": "uuid",
    "sourceType": "PROJECT",
    "sourceRefId": "uuid",
    "sourceKey": "project:school-saas",
    "title": "SchoolSaaS",
    "content": "...",
    "contentHash": "...",
    "version": 2,
    "indexingStatus": "INDEXED",
    "indexedAt": "...",
    "lastIndexError": null,
    "chunks": [
      {
        "id": "uuid",
        "chunkIndex": 0,
        "content": "...",
        "tokenCount": 242,
        "embeddingModel": "..."
      }
    ]
  }
}
```

Do not return raw vector values.

---

## 27.3 Reindex Knowledge Document

```http
POST /api/v1/admin/agent/knowledge/{id}/reindex
```

Response:

```http
202 Accepted
```

```json
{
  "success": true,
  "data": {
    "status": "PENDING"
  }
}
```

Behavior:

- queue/mark source for indexing
- do not block request until LLM embedding work finishes

---

## 27.4 Reindex All Knowledge

```http
POST /api/v1/admin/agent/knowledge/reindex-all
```

Response:

```http
202 Accepted
```

The backend marks eligible active sources for indexing.

---

# 28. Public Chat API

## 28.1 Create Chat Session

```http
POST /api/v1/public/chat/sessions
```

Authentication:

```text
Public
```

Response:

```http
201 Created
```

```json
{
  "success": true,
  "data": {
    "sessionId": "public-session-uuid",
    "status": "ACTIVE",
    "welcomeMessage": "Ask me about Nguyễn Đình Phúc, his experience, projects, and technical skills."
  }
}
```

DB:

```text
chat_sessions
```

Security:

- session creation does not consume AI message quota
- do not expose internal chat session DB UUID if a public session identifier exists

---

## 28.2 Send Chat Message

```http
POST /api/v1/public/chat/sessions/{sessionId}/messages
```

Request:

```json
{
  "message": "What did Phúc work on in SchoolSaaS?"
}
```

Response:

```json
{
  "success": true,
  "data": {
    "messageId": "uuid",
    "answer": "The portfolio shows...",
    "sources": [
      {
        "title": "SchoolSaaS",
        "sourceType": "PROJECT",
        "sourceRefId": "uuid",
        "projectSlug": "school-saas",
        "rank": 1,
        "similarityScore": 0.82
      }
    ]
  }
}
```

Server flow:

```text
validate request
↓
resolve trusted forwarded headers and normalized client IP
-> apply 3/minute/IP burst limiter
-> derive HMAC visitor key
-> load active session and agent settings
-> atomically reserve session + daily IP + global quotas
↓
embed question
↓
pgvector retrieval
↓
apply minimum similarity
↓
select max context chunks
↓
build grounded prompt
↓
LLM generation
↓
persist USER message
↓
persist ASSISTANT message
↓
persist citations
↓
update chat session counters/timestamps
↓
return answer
```

Agent scope:

Only answer questions about:

```text
Nguyễn Đình Phúc
professional experience
projects
skills
education
training
certificates
engineering journey
```

If retrieval does not contain sufficient grounded data, use fallback behavior instead of inventing facts.

Errors:

```text
400 VALIDATION_ERROR
404 CHAT_SESSION_NOT_FOUND
409 CHAT_SESSION_CLOSED
429 CHAT_RATE_LIMITED
429 CHAT_DAILY_LIMIT_REACHED
429 CHAT_SESSION_LIMIT_REACHED
429 CHAT_GLOBAL_LIMIT_REACHED
503 AGENT_UNAVAILABLE
```

Chat-message protection flow:

```text
trusted forwarded headers
-> normalized client IP
-> 3/minute/IP burst limiter
-> HMAC-SHA256 visitor key
-> active session and settings checks
-> atomic session + daily IP + global quota reservation
-> Gemini embedding
-> retrieval
-> optional Gemini generation
```

Durable limits are `20/UTC day/HMAC visitor`, `20 USER messages/session`, and
`150/UTC day` globally. The 429 codes are `CHAT_RATE_LIMITED`,
`CHAT_DAILY_LIMIT_REACHED`, `CHAT_SESSION_LIMIT_REACHED`, and
`CHAT_GLOBAL_LIMIT_REACHED`. Raw IP is not persisted. Provider failures after a
successful reservation are not refunded. Session creation and feedback do not
consume AI quota.

---

## 28.3 Submit Chat Feedback

```http
POST /api/v1/public/chat/messages/{messageId}/feedback
```

Request:

```json
{
  "rating": "POSITIVE",
  "comment": null
}
```

Rules:

- feedback applies to assistant answer
- one feedback record per chat message

Response:

```text
201 Created
```

Errors:

```text
404 CHAT_MESSAGE_NOT_FOUND
409 FEEDBACK_ALREADY_EXISTS
```

DB:

```text
chat_message_feedback
```

---

# 29. Admin Conversations API

## 29.1 List Conversations

```http
GET /api/v1/admin/agent/conversations
```

Query:

```text
?page=1&pageSize=20&status=ACTIVE
```

Response:

```json
{
  "success": true,
  "data": {
    "items": [
      {
        "id": "uuid",
        "publicSessionId": "uuid",
        "status": "ACTIVE",
        "startedAt": "...",
        "lastMessageAt": "...",
        "messageCount": 4
      }
    ],
    "page": 1,
    "pageSize": 20,
    "total": 1,
    "totalPages": 1
  }
}
```

---

## 29.2 Get Conversation Detail

```http
GET /api/v1/admin/agent/conversations/{id}
```

Response:

```json
{
  "success": true,
  "data": {
    "id": "uuid",
    "publicSessionId": "uuid",
    "status": "ACTIVE",
    "startedAt": "...",
    "lastMessageAt": "...",
    "messageCount": 4,
    "messages": [
      {
        "id": "uuid",
        "role": "USER",
        "content": "...",
        "createdAt": "..."
      },
      {
        "id": "uuid",
        "role": "ASSISTANT",
        "content": "...",
        "modelName": "...",
        "promptTokens": 1200,
        "completionTokens": 220,
        "latencyMs": 950,
        "createdAt": "...",
        "sources": [
          {
            "title": "SchoolSaaS",
            "rank": 1,
            "similarityScore": 0.82
          }
        ],
        "feedback": {
          "rating": "POSITIVE",
          "comment": null
        }
      }
    ]
  }
}
```

---

## 29.3 Close Conversation

Optional but valid in V1:

```http
POST /api/v1/admin/agent/conversations/{id}/close
```

Response:

```text
200 OK
```

Sets:

```text
status = CLOSED
closedAt = current timestamp
```

---

# 30. Knowledge Synchronization Rules

Portfolio tables are canonical.

Knowledge is never edited as the source of portfolio content.

When a source entity changes:

```text
Portfolio entity updated
↓
business transaction commits
↓
knowledge document becomes PENDING
↓
background indexing process
↓
build canonical knowledge content
↓
chunk
↓
embed
↓
replace chunks safely
↓
mark INDEXED
```

Failure:

```text
indexingStatus = FAILED
lastIndexError = sanitized diagnostic
```

Public portfolio update must not fail merely because background knowledge indexing fails after the business transaction.

---

# 31. Knowledge Source Mapping

Recommended `sourceKey` values:

```text
profile:main

experience:{experienceId}

project:{projectSlug}

skills:all

education:{educationId}

training:{trainingId}

certificate:{certificateId}

journey:{journeyItemId}

manual:{manualKnowledgeId}
```

For V1, `MANUAL` may exist in the schema but does not require a standalone public uploader/editor UI.

---

# 32. Media Consistency Rules

Cloudflare R2 stores bytes.

PostgreSQL stores metadata.

Flow:

```text
Admin uploads file
↓
Backend validates file
↓
Backend uploads to R2
↓
media_assets inserted
↓
response returned
```

Deletion must avoid inconsistent state.

Do not leave:

```text
DB row with missing R2 object
```

or:

```text
R2 object permanently orphaned because DB transaction failed
```

Implementation must explicitly handle compensation/cleanup where required.

---

# 33. Public Cache Rules

Public endpoints may later use HTTP caching.

Potentially cacheable:

```text
GET /public/portfolio
GET /public/projects
GET /public/projects/{slug}
```

Do not cache:

```text
POST /public/chat/*
admin endpoints
```

Caching is not required to implement V1.

---

# 34. Validation Rules Summary

## Slug

```text
required
lowercase recommended
max 180
unique case-insensitively
```

## URLs

For:

```text
githubUrl
liveUrl
companyUrl
credentialUrl
socialLink.url
```

must be valid absolute URLs when supplied.

## Dates

Backend must enforce the same date constraints as DB.

Examples:

```text
experience.endDate >= startDate
project.endDate >= startDate
training.endDate >= startDate
certificate.expiresAt >= issuedAt
```

## Display Order

```text
integer >= 0
```

## Text Inputs

Define explicit maximum lengths in validation matching DB where applicable.

Do not rely only on PostgreSQL errors.

---

# 35. HTTP Status Contract

Use:

```text
200 OK
GET success
PUT/PATCH success

201 Created
resource created

202 Accepted
background indexing queued

204 No Content
successful delete/logout where no response body is needed

400 Bad Request
validation / malformed input

401 Unauthorized
authentication missing/invalid

403 Forbidden
authenticated admin disabled or operation forbidden

404 Not Found
resource absent or unpublished public resource

409 Conflict
unique collision / invalid business state

413 Payload Too Large
media upload exceeds configured limit

415 Unsupported Media Type
invalid upload type

429 Too Many Requests
rate limiting

500 Internal Server Error
unexpected backend error

503 Service Unavailable
LLM/agent dependency unavailable when request cannot be fulfilled
```

---

# 36. Error Codes

Suggested stable API error codes:

```text
VALIDATION_ERROR

INVALID_CREDENTIALS
ADMIN_DISABLED
INVALID_REFRESH_TOKEN
REFRESH_TOKEN_EXPIRED
REFRESH_TOKEN_REVOKED

PROFILE_NOT_FOUND

EXPERIENCE_NOT_FOUND

TECHNOLOGY_NOT_FOUND
TECHNOLOGY_NAME_EXISTS
TECHNOLOGY_IN_USE

PROJECT_NOT_FOUND
PROJECT_SLUG_EXISTS

PROJECT_SECTION_NOT_FOUND
PROJECT_MEDIA_NOT_FOUND

SKILL_NOT_FOUND
EDUCATION_NOT_FOUND
TRAINING_NOT_FOUND
CERTIFICATE_NOT_FOUND
JOURNEY_ITEM_NOT_FOUND
SOCIAL_LINK_NOT_FOUND

MEDIA_NOT_FOUND
MEDIA_IN_USE
MEDIA_TOO_LARGE
UNSUPPORTED_MEDIA_TYPE

CONTACT_MESSAGE_NOT_FOUND

AGENT_SETTINGS_NOT_FOUND

KNOWLEDGE_DOCUMENT_NOT_FOUND
KNOWLEDGE_INDEX_FAILED

CHAT_SESSION_NOT_FOUND
CHAT_SESSION_CLOSED
CHAT_MESSAGE_NOT_FOUND
FEEDBACK_ALREADY_EXISTS

TOO_MANY_REQUESTS
AGENT_UNAVAILABLE
INTERNAL_ERROR
```

---

# 37. Route Summary

```text
/api/v1

/auth
  POST   /login
  POST   /refresh
  POST   /logout
  GET    /me

/public
  GET    /portfolio
  GET    /projects
  GET    /projects/{slug}

  POST   /chat/sessions
  POST   /chat/sessions/{sessionId}/messages
  POST   /chat/messages/{messageId}/feedback

/admin
  GET    /dashboard

  GET    /profile
  PUT    /profile

  GET    /experiences
  GET    /experiences/{id}
  POST   /experiences
  PUT    /experiences/{id}
  DELETE /experiences/{id}
  PUT    /experiences/reorder

  GET    /technologies
  POST   /technologies
  PUT    /technologies/{id}
  DELETE /technologies/{id}

  GET    /projects
  GET    /projects/{id}
  POST   /projects
  PUT    /projects/{id}
  DELETE /projects/{id}
  PUT    /projects/reorder

  PUT    /projects/{id}/technologies

  GET    /projects/{id}/sections
  POST   /projects/{id}/sections
  PUT    /projects/{id}/sections/{sectionId}
  DELETE /projects/{id}/sections/{sectionId}
  PUT    /projects/{id}/sections/reorder

  GET    /projects/{id}/media
  POST   /projects/{id}/media
  PUT    /projects/{id}/media/{projectMediaId}
  DELETE /projects/{id}/media/{projectMediaId}

  GET    /skills
  POST   /skills
  PUT    /skills/{id}
  DELETE /skills/{id}
  PUT    /skills/reorder

  GET    /education
  GET    /education/{id}
  POST   /education
  PUT    /education/{id}
  DELETE /education/{id}
  PUT    /education/reorder

  GET    /trainings
  GET    /trainings/{id}
  POST   /trainings
  PUT    /trainings/{id}
  DELETE /trainings/{id}
  PUT    /trainings/reorder

  GET    /certificates
  GET    /certificates/{id}
  POST   /certificates
  PUT    /certificates/{id}
  DELETE /certificates/{id}
  PUT    /certificates/reorder

  GET    /journey
  GET    /journey/{id}
  POST   /journey
  PUT    /journey/{id}
  DELETE /journey/{id}
  PUT    /journey/reorder

  GET    /social-links
  POST   /social-links
  PUT    /social-links/{id}
  DELETE /social-links/{id}
  PUT    /social-links/reorder

  GET    /site-settings
  PUT    /site-settings

  GET    /media
  POST   /media
  PUT    /media/{id}
  DELETE /media/{id}


/admin/agent
  GET    /settings
  PUT    /settings

  GET    /knowledge
  GET    /knowledge/{id}
  POST   /knowledge/{id}/reindex
  POST   /knowledge/reindex-all

  GET    /conversations
  GET    /conversations/{id}
  POST   /conversations/{id}/close
```

---

# 38. UI → API Mapping

## Public Home

```text
GET /public/portfolio
```

## Public Projects

```text
GET /public/projects
```

## Public Project Detail

```text
GET /public/projects/{slug}
```

## Contact

The frontend uses profile email and published social links from `GET /public/portfolio`.

## Portfolio Agent

```text
POST /public/chat/sessions
POST /public/chat/sessions/{sessionId}/messages
POST /public/chat/messages/{messageId}/feedback
```

## Admin Dashboard

```text
GET /admin/dashboard
```

## Admin Profile

```text
GET /admin/profile
PUT /admin/profile
```

## Admin Experience

```text
/admin/experiences/*
```

## Admin Projects

```text
/admin/projects/*
```

## Admin Skills

```text
/admin/skills/*
```

## Admin Education

```text
/admin/education/*
```

## Admin Trainings

```text
/admin/trainings/*
```

## Admin Certificates

```text
/admin/certificates/*
```

## Admin Journey

```text
/admin/journey/*
```

## Admin Social Links

```text
/admin/social-links/*
```

## Admin Site Settings

```text
/admin/site-settings
```

## Admin Media Library

```text
/admin/media/*
```

## Admin Agent Settings

```text
/admin/agent/settings
```

## Admin Knowledge

```text
/admin/agent/knowledge/*
```

## Admin Conversations

```text
/admin/agent/conversations/*
```

---

# 39. V1 Non-Goals

Do not add APIs for:

```text
roles
permissions
teams
organizations
tenants
billing
subscriptions
payments
blog
articles
CRM
support tickets
visitor accounts
Kafka
RabbitMQ
Qdrant
Redis
server monitoring
infrastructure management
```

Bridge tables do not receive standalone generic CRUD controllers unless explicitly defined in this contract.

---

# 40. Freeze Rule

This document is the API Contract V1 baseline.

Implementation should not silently invent new endpoints or DTO fields because a generated UI contains placeholder concepts.

Priority of truth:

```text
1. PostgreSQL Schema V1
2. API Contract V1
3. Frozen Stitch UI
4. Seed/mock data
```

If implementation discovers a true missing requirement, update this contract deliberately before changing backend and frontend behavior.
