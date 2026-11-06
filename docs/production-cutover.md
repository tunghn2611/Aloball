# AloBall — Hướng Dẫn Chuyển Đổi Lên Môi Trường Sản Xuất (Production Cutover Guide)

Tài liệu này mô tả chi tiết quy trình đưa ứng dụng **AloBall** lên môi trường Production (Vercel + PostgreSQL) đảm bảo tính bảo toàn dữ liệu 100%, bảo mật vòng khóa Data Protection và loại bỏ hoàn toàn các rủi ro từ hệ thống tệp tạm thời (ephemeral storage).

---

## 1. Yêu Cầu Về Biến Môi Trường Trên Vercel (Environment Variables)

Truy cập **Vercel Dashboard -> Settings -> Environment Variables** và cấu hình các biến sau cho môi trường **Production** *(Chỉ liệt kê TÊN biến, tuyệt đối không hardcode mật khẩu vào mã nguồn)*:

| Tên Biến (Name) | Mục đích & Mô tả |
| :--- | :--- |
| POSTGRES_URL | Chuỗi kết nối PostgreSQL (hỗ trợ định dạng URI chuẩn postgresql://user:pass@host:port/dbname từ Vercel Postgres, Neon, Supabase, AWS RDS). Ứng dụng tự động chuẩn hóa và bật chế độ SslMode=Require. |
| ASPNETCORE_ENVIRONMENT | Đặt giá trị là Production. Ở chế độ này, ứng dụng sẽ kích hoạt cơ chế bảo vệ nghiêm ngặt: **từ chối chạy nếu thiếu PostgreSQL** và **tắt hoàn toàn việc tự động chạy seed dữ liệu**. |
| PORT | Cổng mạng do Vercel cấp phát động (mặc định container tự động nhận diện 80 hoặc 8080). |
| SUPER_ADMIN_EMAIL | *(Khởi tạo lần đầu)* Email cho tài khoản Super Admin duy nhất của hệ thống. |
| SUPER_ADMIN_PASSWORD | *(Khởi tạo lần đầu)* Mật khẩu mạnh cho Super Admin (tối thiểu 8 ký tự). Xóa biến này khỏi Vercel sau khi hoàn tất khởi tạo. |

---

## 2. Thiết Lập Cơ Sở Dữ Liệu PostgreSQL

1. **Khởi tạo cơ sở dữ liệu:**
   - Tạo một database PostgreSQL phiên bản 15 hoặc 16 trên dịch vụ được chọn (ví dụ: Vercel Postgres, Neon.tech, Supabase, hoặc AWS Aurora).
   - Đảm bảo cơ sở dữ liệu có hỗ trợ SSL/TLS (sslmode=require).
2. **Quy tắc bảo mật:**
   - Database chỉ được phép truy cập từ dải IP của Vercel hoặc qua kết nối SSL có xác thực thông tin đăng nhập mạnh.

---

## 3. Quy Trình Áp Dụng Migration SQL Vào PostgreSQL

> **Lưu ý quan trọng:** Ứng dụng tuân thủ quy tắc không tự ý chạy migration DDL tự động khi container khởi động (fail-safe). Mọi schema phải được áp dụng qua kịch bản SQL đã qua kiểm duyệt.

1. File migration SQL chuẩn đã được sinh tự động và lưu trữ tại:
   - docs/migrations/postgres_idempotent.sql (hoặc docs/migrations/initial_postgres.sql)
2. Kịch bản này có tính lũy thừa (Idempotent), sử dụng bảng __EFMigrationsHistory để kiểm tra trạng thái trước khi thực thi từng bảng, chỉ mục và ràng buộc khóa ngoại.
3. **Cách áp dụng:**
   - **Cách 1 (Qua psql CLI):**
     `ash
     psql "<POSTGRES_CONNECTION_STRING>" -f docs/migrations/postgres_idempotent.sql
     `
   - **Cách 2 (Qua giao diện SQL Editor của Neon/Supabase/Vercel):**
     Mở file docs/migrations/postgres_idempotent.sql, copy toàn bộ nội dung và bấm **Run** trong tab SQL Query Console.

---

## 4. Quy Trình Khởi Tạo Quản Trị Viên Đầu Tiên (Super Admin Bootstrap)

Để đảm bảo môi trường Production **không bao giờ chứa dữ liệu mẫu hay mật khẩu mặc định**, AloBall sử dụng cơ chế an toàn one-time bootstrap:

1. **Trước khi deploy lần đầu:**
   - Thêm 2 biến môi trường vào Vercel:
     - SUPER_ADMIN_EMAIL = your-admin@domain.com
     - SUPER_ADMIN_PASSWORD = <Mật khẩu mạnh riêng của bạn>
2. **Cơ chế hoạt động:**
   - Khi ứng dụng khởi động ở môi trường Production, AdminBootstrap.ProvisionSuperAdminAsync() sẽ kiểm tra:
     - Nếu đã tồn tại bất kỳ tài khoản có vai trò PlatformAdmin, quy trình **tự động bỏ qua** (không ghi đè, không thay đổi mật khẩu).
     - Nếu chưa có, ứng dụng tạo tài khoản với mật khẩu được băm (hashed) bằng thuật toán an toàn của ASP.NET Core Identity và gán vai trò PlatformAdmin.
     - Tuyệt đối không có mật khẩu nào được in ra console hay ghi vào nhật ký log.
3. **Sau khi deploy thành công:**
   - Đăng nhập kiểm tra tại https://aloball.vercel.app/Account/Login.
   - Vào Vercel Settings -> Environment Variables, **xóa biến SUPER_ADMIN_PASSWORD** để đảm bảo bảo mật tuyệt đối.

---

## 5. Cấu Hình Data Protection & Vòng Khóa Phân Tán

- **Cơ chế lưu trữ:**
  - Ứng dụng sử dụng Microsoft.AspNetCore.DataProtection.EntityFrameworkCore để lưu trữ vòng khóa (DataProtectionKeys) trực tiếp vào cơ sở dữ liệu PostgreSQL.
  - Bảng DataProtectionKeys đã được tạo sẵn qua migration postgres_idempotent.sql.
- **Hoạt động đa instance & Giải quyết triệt để lỗi đăng xuất khi đổi tab / lỗi HTTP 400:**
  - Mọi container serverless độc lập trên Vercel khi khởi chạy đều đọc và ghi chung vào bảng DataProtectionKeys này.
  - Tên ứng dụng được cố định là AloBall (SetApplicationName("AloBall")).
  - Trước đây, khi không có Data Protection tập trung, mỗi lần Vercel xoay vòng container sẽ tạo khóa mã hóa tạm thời mới. Người dùng khi gửi token CSRF hoặc cookie đăng nhập từ tab khác sẽ bị Kestrel từ chối với lỗi **HTTP ERROR 400 (Bad Request)** hoặc bị ép đăng xuất. Với PostgreSQL persistent key ring, lỗi này được loại bỏ triệt để.
- **Tác động tới phiên người dùng hiện tại (Session Invalidation Impact):**
  - Khi triển khai vòng khóa PostgreSQL lần đầu tiên, các cookie phiên cũ (được mã hóa bằng khóa tạm thời trước đây) sẽ không thể giải mã.
  - **Tất cả người dùng sẽ phải đăng nhập lại đúng 1 lần duy nhất**. Kể từ sau đó, phiên đăng nhập sẽ duy trì ổn định 30 ngày ngay cả khi chuyển tab, làm mới trang, hoặc khi Vercel xoay vòng container.
- **Bảo mật khóa tại chỗ (Encryption at Rest):**
  - Khóa Data Protection trong bảng DataProtectionKeys được bảo vệ bằng phân quyền mạng và chứng chỉ SSL/TLS khi truyền tải.
  - Trong trường hợp doanh nghiệp yêu cầu chuẩn mã hóa bổ sung cấp chứng chỉ (X.509 PKI), có thể cấu hình ProtectKeysWithCertificate() thông qua chứng chỉ được tải từ biến môi trường.

---

## 6. Xác Minh Sao Lưu & Phục Hồi Dữ Liệu (Backup & Restore Verification)

- **Trước khi cutover:**
  - Kích hoạt tính năng **Automated Daily Backups** và **Point-in-Time Recovery (PITR)** trên nhà cung cấp PostgreSQL.
  - Chạy thử lệnh xuất bản sao lưu kiểm tra:
    `ash
    pg_dump "<POSTGRES_CONNECTION_STRING>" --format=custom -f aloball_pre_cutover.dump
    `
- **Kiểm tra khôi phục:**
  - Khôi phục bản dump thử nghiệm vào một database tạm để xác nhận tính toàn vẹn của dữ liệu và chỉ mục.

---

## 7. Trình Tự Triển Khai (Deployment Order)

Thực hiện tuần tự theo 6 bước sau để đảm bảo không có thời gian chết (zero-downtime):

1. **Bước 1 — Chuẩn bị Database:** Tạo database PostgreSQL và kiểm tra quyền kết nối SSL.
2. **Bước 2 — Áp dụng Migration:** Thực thi kịch bản docs/migrations/postgres_idempotent.sql lên database. Xác nhận bảng __EFMigrationsHistory và các bảng dữ liệu đã sẵn sàng.
3. **Bước 3 — Cấu hình Environment Variables:** Thêm POSTGRES_URL, ASPNETCORE_ENVIRONMENT=Production, SUPER_ADMIN_EMAIL, SUPER_ADMIN_PASSWORD trong Vercel Dashboard.
4. **Bước 4 — Deploy Ứng Dụng:** Trigger deployment mới trên Vercel. Ứng dụng sẽ khởi động, kiểm tra biến môi trường thành công, kết nối PostgreSQL, khởi tạo Super Admin an toàn và kích hoạt Data Protection tập trung.
5. **Bước 5 — Smoke Test:**
   - Truy cập trang chủ https://aloball.vercel.app.
   - Đăng nhập với tài khoản Super Admin vừa cấu hình tại /Account/Login.
   - Mở tab mới, làm mới trang (F5), kiểm tra trạng thái đăng nhập vẫn được duy trì 100%.
   - Thử nghiệm truy cập /Account/AccessDenied đối với tài khoản không có quyền, đảm bảo trả về HTTP 403 Forbidden chuẩn xác.
6. **Bước 6 — Xóa Mật Khẩu Khởi Tạo:** Xóa biến SUPER_ADMIN_PASSWORD khỏi Vercel Environment Variables.

---

## 8. Kế Hoạch Rollback (Nếu Gặp Sự Cố)

1. Nếu deployment gặp sự cố:
   - Truy cập **Vercel Dashboard -> Deployments**, bấm **Promote to Production** bản deploy ổn định trước đó.
2. Dữ liệu trên PostgreSQL vẫn được giữ nguyên độc lập với mã nguồn container, cho phép kiểm tra log và tái triển khai mà không làm mất dữ liệu người dùng.