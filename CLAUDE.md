# Sprint Boot

Ứng dụng quản lý công việc kiểu Jira, tự host được. Yêu cầu, kế hoạch và task nằm trên Docmost, space `SB`. Thiết kế nằm trên Penpot, file "Sprint Boot (All Design)".

## Cấu trúc

- `apps/api`: ABP modular monolith trên .NET 10.
- `apps/web`: React SPA.
- `packages/api-client`: TypeScript client sinh từ OpenAPI, không sửa tay.
- `deploy/docker`: Docker Compose cho dev.

## Quy ước

- Module chỉ reference `.Contracts` của module khác, không truy cập bảng của nhau. `ArchitectureTests` canh quy tắc này.
- Nghiệp vụ chỉ nằm ở application service. API có prefix `/api/v1`.
- Kiểm tra quyền bằng permission key, không so tên role.
- Giao diện gọi là Space, mã nguồn và tài liệu gọi là Project.
- Màu và chữ lấy từ MUI theme, Tailwind chỉ dùng cho layout và spacing.
- Token nằm ở `apps/web/src/theme/tokens.ts`, giá trị lấy từ Penpot. Line-height tối thiểu 1.4.
- Font là SVN-Poppins, không trộn với Poppins gốc. Icon là Lucide, nét 1.5px.
- Mọi chuỗi giao diện nằm trong file ngôn ngữ `en` và `vi`.
