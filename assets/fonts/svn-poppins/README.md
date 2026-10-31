# SVN-Poppins

Bản Việt hóa của Poppins, đủ 146 ký tự tiếng Việt ở 6 độ đậm: Light, Regular, Medium, SemiBold, Bold, Black.

- Poppins gốc: Indian Type Foundry, giấy phép SIL OFL 1.1, xem `OFL.txt`.
- Nguồn: gói npm `@glints/poppins` 1.0.2. Gói ghi MIT nhưng file font ghi OFL, nên font theo OFL.
- `woff2/`: file nguyên bản, dùng cho web qua `svn-poppins.css`.
- `ttf/`: giải nén từ woff2, thống nhất tên family thành `SVN-Poppins` để cài máy và tải lên Penpot.

## Lưu ý

- Dùng SVN-Poppins cho toàn bộ giao diện, không trộn với Poppins gốc. Bản này dựa trên Poppins đời cũ: chữ hẹp hơn khoảng 1 đến 2%, ascent và descent là 800 và -250 thay vì 1050 và -350.
- Luôn đặt `line-height` rõ ràng, tối thiểu 1.4. Dấu kép như Ẩ cao tới 1016 đơn vị, vượt ascent 800.
- Không dùng `svn-poppins` trên npm: file TTF ở bản 1.0.0 có bảng `post` lỗi (kiểm tra ở bản Black).
