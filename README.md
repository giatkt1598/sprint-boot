# Sprint Boot

Self-hosted, Jira-style work management app.

## Requirements

.NET SDK 10, Node 24, pnpm 12, Docker with Compose.

## Development

```sh
pnpm install
pnpm dev
```

This starts PostgreSQL, Redis, RabbitMQ and Mailpit, applies migrations, then runs the API and the web app.

| Service | Address |
| --- | --- |
| Web | http://localhost:5173 |
| API | http://localhost:5080 |
| OpenAPI | http://localhost:5080/swagger |
| RabbitMQ | http://localhost:15672 |
| Mailpit | http://localhost:8025 |

## Deployment

Pushes to `master` build two images (`.gitea/workflows/docker-image.yml`):

- `git.giatk.win/admin/sprint-boot/api` (`apps/api/Dockerfile`): the Host, plus the DbMigrator at `/app/migrator`.
- `git.giatk.win/admin/sprint-boot/web` (`apps/web/Dockerfile`): the SPA on nginx, proxying the API paths to `sprint-boot-api:8080`.

The workflow then writes the new `master-<run>` tags into the `giatk-k8s` repo, where Argo CD rolls them out
(manifests in `base/sprint-boot` and `overlays/prod/sprint-boot` there).

Production settings that differ from development:

| Setting | Why |
| --- | --- |
| `OpenIddict:Certificate`, `OpenIddict:CertificatePassword` | Base64 PFX used to sign and encrypt tokens. Without it the development certificate is used, which is per container. |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` | TLS ends at the proxy; the auth server must see `https`. |
| `Auth:SpaBaseUrl` | Public URL, used for OAuth redirect URIs and links. |
