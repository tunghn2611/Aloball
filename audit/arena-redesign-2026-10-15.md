# AloBall — thay mới toàn bộ giao diện

Hoàn tất ngày 15/10/2026. Bản xem thử: http://localhost:5188/.

## Thiết kế

Hướng thiết kế mới: AloBall Clubhouse, nền trắng ngà, xanh navy và cam san hô. Bố cục gồm thanh điều hướng bên trái trên máy tính, menu trượt trên điện thoại, tiêu đề lớn và các khu vực thao tác theo công việc của đội bóng.

Thay mới giao diện trang chủ, đăng nhập/đăng ký, danh sách đội, tạo/sửa đội, không gian quản lý đội, hồ sơ công khai, thành viên, lịch hoạt động, điểm danh, chia đội, trận đấu, tìm đối, bảng điểm AB/thành tích tháng, penalty, quản trị và các trang thông báo. Trang chủ tập trung vào tìm đối, lịch đấu, kết quả và hồ sơ đội. Các màn quản lý tập trung vào thao tác ra sân hằng tuần.

Toàn bộ trang dùng bộ CSS/JS độc lập `arena.css` và `arena.js`. Đã bỏ các tài nguyên giao diện cũ `site.css`, `club.css`, `site.js`, stylesheet layout cũ và view Dashboard không được sử dụng. PWA cập nhật tên cache và màu chủ đạo.

Giữ các điều kiện phân quyền và luồng xử lý nghiệp vụ hiện có. Menu mobile hỗ trợ Escape, giữ focus trong menu, aria-expanded và inert khi đóng. Nhãn trường nhập liệu, nút đóng và vùng thông báo penalty đã được bổ sung/chỉnh sửa.

## Kiểm chứng

- `dotnet test AloBall.sln --no-restore`: 57 đạt, 0 lỗi, 0 bỏ qua. Kết quả tại `test-results/redesign-regression.trx`, log tại `redesign-regression.log`.
- Kiểm thử render 16 đường dẫn với layout mới; kiểm tra đích modal/tab và tải thư viện validation của đăng nhập/đăng ký.
- Kiểm tra trực tiếp desktop 1440×1000 và mobile 390×844: trang chủ, không gian đội, hồ sơ công khai, tìm đối/modal đăng lịch, danh sách cầu thủ/tìm kiếm, lịch điểm danh/chi tiết, trận đấu, bảng điểm/penalty, đăng nhập và tạo đội.
- Đã thử menu mobile, chuyển tab kết quả, tìm cầu thủ và lượt sút penalty. Chỉnh tên cầu thủ đầy đủ, bố cục đội hình nhỏ và bộ đếm lượt penalty trong quá trình kiểm tra.
- Preview và test sử dụng cơ sở dữ liệu riêng trong `audit`; không chạy các thử nghiệm trên cơ sở dữ liệu ứng dụng chính.

## Tài liệu bàn giao

- Ảnh desktop: `arena-desktop.jpg`.
- Ảnh mobile: `arena-mobile.jpg`.
- Bản sao giao diện trước khi thay: `ui-before-redesign-20261015-222454.zip`.
- Audit nghiệp vụ trước đó: `requirements-audit-2026-10-15.md` và `refactor-completion-2026-10-15.md`.

Đây là bản chạy local phục vụ kiểm tra; chưa triển khai lên môi trường production.
