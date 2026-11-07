# Sprint Boot

Self-hosted, Jira-style work management app. Requirements, plans and tasks live on Docmost, space `SB`. Designs live on Penpot, file "Sprint Boot (All Design)".

## Layout

- `apps/api`: ABP modular monolith on .NET 10.
- `apps/web`: React SPA.
- `packages/api-client`: TypeScript client generated from OpenAPI. Do not edit by hand.
- `deploy/docker`: Docker Compose for development.

## Conventions

- Everything in the repo is written in English: code, comments, logs, exceptions, docs, commit messages. Vietnamese UI strings live only in the `vi` locale file.
- A module may only reference `.Contracts` of another module and never touches its tables. `ArchitectureTests` enforces this.
- Business logic lives only in application services. APIs are prefixed `/api/v1`.
- Check permissions by permission key, never by role name.
- The UI says Space; code and docs say Project.
- Colors and typography come from the MUI theme. Tailwind is for layout and spacing only.
- Tokens live in `apps/web/src/theme/tokens.ts`, values come from Penpot. Minimum line-height is 1.4.
- Themes live in `apps/web/src/theme/themes`, one definition each; components read semantic tokens and never branch on a theme. Dark is a theme, not a mode.
- The font is SVN-Poppins, never mixed with stock Poppins. A theme may suggest a heading font (self-hosted, OFL, with Vietnamese glyphs), which people can change in Preferences; body text stays SVN-Poppins. Icons are Lucide with a 1.5px stroke.
- All UI strings live in the `en` and `vi` locale files.
