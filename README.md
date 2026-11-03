# AloBall - Nền Tảng Quản Trị & Kết Nối Đội Bóng Phong Trào

AloBall là nền tảng quản lý đội bóng thể thao phong trào hiện đại, được thiết kế theo kiến trúc **Modular Monolith** trên nền tảng **ASP.NET Core 8.0 LTS**, hỗ trợ đa nền tảng web và **PWA** trên thiết bị di động (Android / iOS).

---

## 1. Kiến Trúc & Công Nghệ

- **Framework**: .NET 8.0 LTS (C# 12)
- **Kiến trúc**: Modular Monolith (tách biệt rõ ràng các phân hệ: Identity, Teams & Players, Attendance & Regulations, Matches, Matchmaking, Reviews & Moderation, Sheets Integration).
- **Cơ sở dữ liệu**:
  - Entity Framework Core 8.0.
  - **Môi trường cục bộ**: SQLite (`AloBall.db`) với đầy đủ tính toàn vẹn khóa ngoại và ràng buộc duy nhất (Unique constraints).
  - **Môi trường Production/Staging**: Hỗ trợ PostgreSQL (Npgsql) qua cấu hình connection string.
- **Frontend & PWA**:
  - ASP.NET Core MVC + Razor Views.
  - Giao diện thiết kế theo triết lý **Mobile-First**, tối ưu thao tác 1 tay trên điện thoại (touch target $\ge 44\text{px}$, bottom navigation bar).
  - Web App Manifest + Service Worker an toàn (chỉ cache static assets, không cache private API/trang nội bộ).
- **Tích hợp Excel & Google Sheets**: Thư viện ClosedXML, cơ chế map ID chuẩn xác, hỗ trợ xem trước (preview) thay đổi trước khi ghi dữ liệu.

---

## 2. Hướng Dẫn Cài Đặt & Chạy Cục Bộ (Local Development)

### Yêu Cầu Môi Trường
- .NET 8.0 SDK (hoặc .NET 9.0 SDK tương thích ngược).

### Các Bước Thực Hiện
1. Clone hoặc mở thư mục dự án `d:\AloBall`.
2. Khôi phục packages và build:
   ```powershell
   dotnet restore
   dotnet build
   ```
3. Chạy bộ kiểm thử tự động (14 unit/integration tests bao quát toàn bộ nghiệp vụ và kiểm tra view):
   ```powershell
   dotnet test
   ```
4. Khởi động ứng dụng Web:
   ```powershell
   cd d:\AloBall\AloBall.Web
   dotnet run
   ```
5. Mở trình duyệt và truy cập: `https://localhost:5001` (hoặc cổng HTTP hiển thị trên terminal).

---

## 3. Dữ Liệu Demo Khởi Tạo Sẵn (Seeded Demo Accounts)

Hệ thống đã tự động cấu hình sẵn dữ liệu mẫu thực tế của 2 đội bóng: **KLB FC** (theo quy chế mẫu thực tế) và **Sao Vàng FC**.

| Vai trò | Email đăng nhập | Mật khẩu | Phạm vi quyền hạn |
| :--- | :--- | :--- | :--- |
| **Platform Admin** | `admin@aloball.vn` | `Admin@123456` | Quản trị toàn hệ thống, xử lý khiếu nại đánh giá, mở/khóa blacklist |
| **Owner (KLB FC)** | `owner@klbfc.vn` | `Owner@123456` | Chủ tịch CLB KLB FC, toàn quyền quản trị đội, thành viên BTC |
| **Manager (KLB FC)** | `captain@klbfc.vn` | `Captain@123456` | Đội trưởng KLB FC, quản lý điểm danh, tạo buổi đấu, chốt đối, thành viên BTC |
| **Member (KLB FC)** | `member@klbfc.vn` | `Member@123456` | Cầu thủ KLB FC, xem nội bộ, tự phản hồi báo quân (không tự sửa điểm danh) |
| **Owner (Sao Vàng FC)** | `captain@saovangfc.vn` | `SaoVang@123456` | Đội trưởng Sao Vàng FC, gửi thách đấu giao lưu trên Sàn bắt đối |

---

## 4. Các Quy Tắc Nghiệp Vụ Cốt Lõi Đã Kiểm Chứng

1. **Phân quyền đa đội (Multi-team Isolation)**:
   - Một người dùng có thể thuộc nhiều đội bóng với các vai trò khác nhau.
   - Server-side validation kiểm tra nghiêm ngặt: Lãnh đạo Đội A tuyệt đối không thể xem/sửa dữ liệu nội bộ của Đội B.
   - Thành viên thường (`Member`) không thể can thiệp dữ liệu điểm danh thực tế hoặc quy chế đội.
2. **Quy chế Chuyên cần & Phạt điểm danh (Attendance Engine)**:
   - Xác nhận tham gia nhưng vắng: phạt **50.000đ**.
   - Không phản hồi đúng hạn: phạt **30.000đ**.
   - Báo vắng đúng hạn: **miễn phạt** (0đ).
   - Thành viên Ban Tổ Chức (BTC): **miễn phạt hoàn toàn**.
   - Trường hợp chưa rõ dữ kiện: đánh dấu **"Cần xác nhận"**, không đưa vào nợ đã chốt.
3. **Sàn Bắt Đối & Quy Tắc Blacklist Bắt Buộc**:
   - Chốt đối thủ thông qua Database Transaction, chống duplicate / race conditions.
   - **Quy tắc 1 sao**: Bất kỳ đội bóng nào nhận **1 đánh giá 1 sao hợp lệ** từ đối thủ sau trận đấu sẽ **tự động bị đưa vào Blacklist**, khóa ngay lập tức tính năng đăng tin tìm đối và gửi đề nghị giao lưu.
   - Đội bị hạn chế vẫn sử dụng bình thường các chức năng quản lý nội bộ.
   - Khiếu nại được gửi lên `PlatformAdmin`. Khi admin xác minh và vô hiệu hóa đánh giá sai, hệ thống tự động giải phóng đội khỏi Blacklist (nếu không còn căn cứ 1 sao khác).

---

## 5. Danh Mục Kiểm Tra Nghiệm Thu (Acceptance Test Checklist)

- [x] **Xác thực & Phân quyền**: Đăng nhập, bảo vệ CSRF, chặn bypass bằng request trực tiếp.
- [x] **Hồ sơ đội & Cầu thủ**: Trang công khai riêng biệt theo slug (`/teams/klb-fc`), che giấu thông tin nội bộ cá nhân.
- [x] **Điểm danh tương tác**: Giao diện Mobile-first touch, cập nhật tức thời phản hồi và điểm danh tại sân.
- [x] **Quy chế phạt**: Đã kiểm thử qua xUnit test suite (`AttendanceCalculationTests`).
- [x] **Bắt đối & Blacklist**: Đã kiểm thử qua xUnit test suite (`MatchmakingAndBlacklistTests`).
- [x] **Bảo mật đa đội**: Đã kiểm thử qua xUnit test suite (`SecurityAndMultiTeamTests`).
- [x] **Tích hợp Excel/Sheets**: Adapter xuất/nhập ClosedXML an toàn, ánh xạ bằng ID, có xem trước (preview).
- [x] **Hỗ trợ PWA**: Web App Manifest (`manifest.json`) và Service Worker an toàn (`sw.js`).
