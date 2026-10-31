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
