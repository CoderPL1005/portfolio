# Personal Portfolio + Personal RAG Agent

Phase 0 repository bootstrap for an ASP.NET Core backend and standalone Angular frontend. Business features, persistence, authentication, media storage, and RAG integrations are intentionally deferred to later phases.

## Structure

- `src/` — Domain, Application, Infrastructure, and API projects
- `tests/` — .NET unit and integration test projects
- `frontend/` — Angular 21 application with Router, HttpClient, and Tailwind CSS 4
- `database/`, `docs/`, `seed/` — frozen project artifacts
- `Template_UI/` — preserved Stitch HTML visual references

## Build

```powershell
dotnet restore
dotnet build
dotnet test

Set-Location frontend
npm.cmd install
npm.cmd run build
```
