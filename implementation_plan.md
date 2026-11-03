# Kế hoạch Triển khai Nền tảng Quản lý Đội bóng AloBall

## 1. Báo cáo Kiểm tra Workspace & Quyết định Kỹ thuật
- **Workspace hiện tại**: `d:\AloBall` hiện là thư mục trống. Không có mã nguồn hay dữ liệu cũ cần lưu trữ.
- **Môi trường SDK**: 
  - .NET 8 LTS (`8.0.303`) & .NET 9 (`9.0.305`). Chọn target **.NET 8 LTS** hoặc **.NET 9** (chọn **.NET 8 LTS** để đảm bảo tính ổn định lâu dài và chuẩn LTS theo đặc tả, chạy tốt trên runtime .NET 8 / .NET 9 đã cài).
- **Cơ sở dữ liệu**:
  - Đặc tả yêu cầu EF Core + PostgreSQL.
  - Trong môi trường local development không có Docker/PostgreSQL native service chạy sẵn, cấu hình linh hoạt:
    - Mặc định hỗ trợ provider Npgsql (PostgreSQL) khi có connection string PostgreSQL.
    - Hỗ trợ switch fallback mượt sang SQLite (`AloBall.db`) cho môi trường dev local tức thì để build, chạy kiểm thử tự động, seed demo và kiểm tra trọn vẹn mọi luồng mà không bị nghẽn bởi hạ tầng PostgreSQL bên ngoài. Khi deploy staging/production, chỉ cần đổi ConnectionString & Provider sang PostgreSQL.
- **Kiến trúc Modular Monolith**:
  - `AloBall.Web` (ASP.NET Core MVC, Razor Views, PWA Manifest + Service Worker, Static Assets, Mobile-first CSS).
  - Tách namespace / feature areas:
    - `Identity` & Authorization (Multi-team roles: Owner, Manager, Member; PlatformAdmin).
    - `Teams` & `Players` (Hồ sơ công khai, thành viên, đặt áo).
    - `Attendance` & `Regulations` (Điểm danh phản hồi/thực tế, tính phạt/chuyên cần theo quy chế).
    - `Matches` & `Competitions` (Nội bộ, giao hữu, đá giải, đội hình, kết quả hai bên đối soát).
    - `Matchmaking` (Đăng tin, tìm đối, giao lưu, chốt đối qua transaction).
    - `Reviews` & `Moderation` (Đánh giá 1-5 sao, kích hoạt Blacklist tự động khi 1 sao, khiếu nại PlatformAdmin).
    - `Sheets` (Google Sheets Export/Import Adapter an toàn với Id mapping & preview).
  - `AloBall.Tests`: Bộ test xUnit kiểm thử toàn diện các luồng nghiệp vụ theo mục 14.

---

## 2. Tiêu chí Nghiệm thu & Thứ tự Triển khai (Checklist)

### Giai đoạn 1: Nền tảng, Identity & Phân quyền đa đội
- [ ] Khởi tạo project ASP.NET Core MVC (.NET 8 LTS), EF Core, SQLite/PostgreSQL provider, xUnit test project.
- [ ] Thiết kế Database Schema: `ApplicationUser`, `Team`, `TeamMember` (Role: Owner, Manager, Member, BTC), `PlatformAdmin`.
- [ ] Kiểm tra bảo mật server-side: User thuộc đội nào, quyền gì, không tự nâng quyền, không làm mất Owner cuối cùng.

### Giai đoạn 2: Hồ sơ Đội, Cầu thủ & Đặt áo (Trang công khai + nội bộ)
- [ ] Hồ sơ công khai: URL riêng cho từng đội (`/teams/{slug}`), danh sách cầu thủ công khai, thành tích, lịch sử thi đấu, rating & trạng thái bắt đối.
- [ ] Quản lý cầu thủ nội bộ: Số áo, vị trí, size quần áo, ghi chú nội bộ, liên kết tài khoản có xác minh.
- [ ] Quản lý đợt đặt áo: Tạo đợt, snapshot thông tin chốt, xuất bảng đặt áo.

### Giai đoạn 3: Chuyên cần & Quy chế (Attendance Engine)
- [ ] Buổi hoạt động: Hạn phản hồi, kỳ thống kê (YYYY-MM), liên kết trận đấu.
- [ ] Dữ kiện kép: Phản hồi (Chưa ghi nhận, Tham gia, Báo vắng, Không phản hồi) + Thực tế (Chưa điểm danh, Có mặt, Vắng).
- [ ] Engine quy chế tự động (theo đội): Không xác nhận phạt 30k, tham gia mà vắng phạt 50k, thiếu trận/tháng phạt 200k, BTC được miễn.
- [ ] Trạng thái chốt kỳ, không ghi đè lịch sử khi đổi quy chế.

### Giai đoạn 4: Trận đấu, Đội hình & Thành tích
- [ ] Phân loại: Nội bộ, Giao hữu, Đá giải.
- [ ] Đội hình theo trận, bàn thắng, kiến tạo, đối soát kết quả giữa hai đội trên hệ thống.
- [ ] Liên kết buổi điểm danh với trận đấu.

### Giai đoạn 5: Bắt đối Giao lưu (Matchmaking Engine)
- [ ] Tin tìm đối: ngày giờ, khu vực, sân, loại sân, kèo chia tiền, trình độ.
- [ ] Gửi đề nghị, chốt đối qua Database Transaction (chống race condition, chỉ chốt 1 bên).
- [ ] Tự động tạo trận chung khi chốt đối thành công.

### Giai đoạn 6: Rating, Blacklist & Khiếu nại
- [ ] Đánh giá sau trận chung đã xác nhận: 1-5 sao.
- [ ] **Blacklist Engine**: 1 sao hợp lệ lập tức khóa tính năng bắt đối của đội bị đánh giá.
- [ ] Khiếu nại & PlatformAdmin: Xem xét giải trình, giữ nguyên hoặc vô hiệu hóa đánh giá sai, recalculate blacklist status.

### Giai đoạn 7: Google Sheets Integration
- [ ] Google Sheets Export Service: Xuất chuyên cần tháng, danh sách cầu thủ / đặt áo.
- [ ] Google Sheets Import Service an toàn: Map bằng ID, preview sai khác, kiểm tra quyền và phiên bản dữ liệu.

### Giai đoạn 8: Mobile-first UI/UX, PWA & Kiểm thử
- [ ] Thiết kế giao diện tiếng Việt chuẩn Mobile-first (thao tác 1 tay, touch target >= 44px, bottom navigation bar).
- [ ] Cấu hình PWA (manifest.json, service worker an toàn, cache static assets).
- [ ] Viết & chạy toàn bộ Unit/Integration Tests (Authentication, Attendance penalty calculations, Blacklist enforcement, Concurrent matchmaking).
- [ ] Seed dữ liệu demo 2 đội bóng đầy đủ kịch bản (Owner, Manager, Member, điểm danh, trận đấu, tin bắt đối, rating).

---

## 3. Kế hoạch xác minh (Verification Plan)
- Chạy `dotnet test` tự động cho mọi test case nghiệp vụ.
- Khởi động ứng dụng, test API và giao diện bằng HTTP requests và kiểm tra rendered HTML.
- Soạn tài liệu hướng dẫn bàn giao chi tiết (README.md).
