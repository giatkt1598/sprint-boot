# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# Sprint Boot

Self-hosted, Jira-style work management app. Requirements, plans and tasks live on Docmost, space `SB`. Designs live on Penpot, file "Sprint Boot (All Design)".

## Layout

- `apps/api`: ABP modular monolith on .NET 10.
- `apps/web`: React SPA.
- `packages/api-client`: TypeScript client generated from OpenAPI. Do not edit by hand.
- `deploy/docker`: Docker Compose for development.
- `docs`: feature docs (assistant, SSO, storage).

## Commands

Requires .NET SDK 10, Node 24, pnpm 12 and Docker.

- `pnpm dev`: starts the Compose services (PostgreSQL, Redis, RabbitMQ, Mailpit), runs the DbMigrator, then the Host (http://localhost:5080, Swagger at `/swagger`) and Vite (http://localhost:5173).
- `pnpm build`, `pnpm lint`, `pnpm test`: run across the pnpm workspace (web: `tsc` + Vite, ESLint, Vitest).
- Web, from `apps/web`: `pnpm vitest run src/path/file.test.ts` for one test; `pnpm test:a11y` runs Playwright on a production preview (`e2e/`); `pnpm test:e2e` runs `e2e-live/` against the running dev app.
- API tests use xUnit v3 on Microsoft.Testing.Platform, so filters go after `--`:
  - `dotnet test --project apps/api/test/SprintBoot.UnitTests`
  - `dotnet test --project apps/api/test/SprintBoot.IntegrationTests -- --filter-class "*TicketTests"` (or `--filter-method "*Name"`). Integration tests start PostgreSQL, Redis and RabbitMQ with Testcontainers, so Docker must run.
- While `pnpm dev` runs, the Host locks its `bin` folder; build or test with `--artifacts-path <temp dir>`.
- `pnpm gen:api`: regenerates `packages/api-client` after an API change. It runs the integration test `OpenApi_document_matches_the_committed_client` with `UPDATE_OPENAPI=1` (writes `openapi.json`), then `openapi-typescript`. That test fails when the committed client is stale.
- Migrations, from `apps/api/src`: `dotnet ef migrations add <Name> --project SprintBoot.EntityFrameworkCore` (`dotnet-ef` is a local tool in `apps/api/.config`).

## Architecture

- API modules live in `apps/api/src/Modules/<Name>/`, each split into `SprintBoot.<Name>` (implementation) and `SprintBoot.<Name>.Contracts` (DTOs, service interfaces, `*Eto` events). There is one `SprintBootDbContext` in `SprintBoot.EntityFrameworkCore`; each module contributes its tables through a `Configure<Name>()` model builder extension, and migrations are shared.
- HTTP endpoints are ABP conventional controllers generated from application services (`SprintBootHostModule`), under `/api/v1`.
- Modules talk to each other through Contracts interfaces or distributed events (`*Eto`, RabbitMQ with the ABP outbox/inbox). Cleanup across modules goes through events such as `ProjectPurgedEto`, which each module handles in its own `ProjectPurgedHandler`.
- `SprintBoot.Host` also hosts the MCP server (`Mcp/`). The AI assistant (Assistant module) uses the same tools; tools that change data only run after the person confirms them (see `docs/assistant.md`).
- `Runtime:Role` (`All`, `Api`, `Worker`) picks what a Host process runs: dev runs `All`, production runs Api and Worker from the same image.
- The web app calls the API only through `apiClient` (`apps/web/src/api.ts`, `openapi-fetch` over the generated schema) with TanStack Query. Features live in `apps/web/src/features/<area>`, shared UI in `src/components`, themes and tokens in `src/theme`.
- Deployment: pushes to `master` build the `api` and `web` images (`.gitea/workflows/docker-image.yml`), and Argo CD rolls them out from the `giatk-k8s` repo. Production settings are listed in `README.md`.

## Conventions

- Everything in the repo is written in English: code, comments, logs, exceptions, docs, commit messages. Vietnamese UI strings live only in the `vi` locale file.
- A module may only reference `.Contracts` of another module and never touches its tables. `ArchitectureTests` enforces this, and also checks the module project count, so update it when adding a module.
- Business logic lives only in application services. APIs are prefixed `/api/v1`.
- Check permissions by permission key, never by role name.
- The UI says Space; code and docs say Project.
- Colors and typography come from the MUI theme. Tailwind is for layout and spacing only.
- Tokens live in `apps/web/src/theme/tokens.ts`, values come from Penpot. Minimum line-height is 1.4.
- Themes live in `apps/web/src/theme/themes`, one definition each; components read semantic tokens and never branch on a theme. Dark is a theme, not a mode.
- The font is SVN-Poppins, never mixed with stock Poppins. A theme may suggest a heading font (self-hosted, OFL, with Vietnamese glyphs), which people can change in Preferences; body text stays SVN-Poppins. Icons come from the themed registry `apps/web/src/components/icons.tsx`, never from an icon library directly; Lucide with a 1.5px stroke is the Classic set and the fallback.
- All UI strings live in the `en` and `vi` locale files (`apps/web/src/i18n/locales`); `locales.test.ts` checks them.
