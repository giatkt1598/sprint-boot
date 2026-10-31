# Sprint Boot

Ứng dụng quản lý công việc kiểu Jira, tự host được.

## Yêu cầu

.NET SDK 10, Node 24, pnpm 12, Docker kèm Compose.

## Chạy môi trường dev

```sh
pnpm install
pnpm dev
```

Lệnh này dựng PostgreSQL, Redis, RabbitMQ và Mailpit, chạy migration, rồi chạy API và web.

| Dịch vụ | Địa chỉ |
| --- | --- |
| Web | http://localhost:5173 |
| API | http://localhost:5080 |
| OpenAPI | http://localhost:5080/swagger |
| RabbitMQ | http://localhost:15672 |
| Mailpit | http://localhost:8025 |
