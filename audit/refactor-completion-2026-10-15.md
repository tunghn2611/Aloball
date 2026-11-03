# AloBall — kết quả hoàn thiện và refactor giao diện

Ngày: 15/10/2026. Phạm vi: mã nguồn local tại `D:\AloBall`.

## Thay đổi đã thực hiện

| Nhóm | Kết quả |
|---|---|
| Không gian đội bóng | Dashboard mới tập trung vào buổi đá tiếp theo, cầu thủ, lịch thi đấu, Giữ Lửa và lời mời giao lưu. Thanh điều hướng dùng chung giữa các mục. |
| Giao diện | Tông xanh sân cỏ, nền sáng, phân cấp nội dung rõ; thẻ cầu thủ trên điện thoại, nút lưu dễ chạm, trạng thái chỉ xem và thông báo lỗi rõ hơn. |
| Hồ sơ đội | Lãnh đội sửa tên, ngày thành lập thực tế, logo qua URL HTTPS, khu vực, sân nhà, mô tả và trạng thái công khai. Trang public hiển thị logo và ngày đầy đủ. |
| Hồ sơ cầu thủ / áo đấu | Thêm và sửa tên, số áo, vị trí, chiều cao, cân nặng, tên in áo, size áo/quần, trạng thái hoạt động và cờ công khai. Excel nhập chỉ dành cho lãnh đội; xuất dành cho thành viên trong đội. |
| Giải đấu / thành tích | Có màn hình lịch sử trận với bộ lọc nội bộ/giao hữu/giải, tạo trận giải hoặc giao hữu ngoài, cập nhật kết quả qua trang trận; thêm/sửa/xóa danh hiệu, chọn thành tích tốt nhất. Trang public có lịch sử giải riêng. |
| Nội bộ hằng tuần | Điểm danh và trận nội bộ đi qua quyền lãnh đội; thành viên có phần xem đội hình và bàn thắng/kiến tạo. Sửa dữ liệu sau chốt buổi đồng bộ lại AB nếu kỳ còn mở. |
| Phân quyền / riêng tư | Chặn thao tác Excel trái quyền, cầu thủ khác đội, đội không tham gia trận; chặn khách xem hồ sơ/trận riêng tư; áp dụng các lựa chọn công khai cầu thủ. |
| Bắt đối | Chọn sân nhà/đi khách/linh hoạt cho tin công khai và lời mời trực tiếp, lấy sân nhà mặc định, kiểm tra ngày và khung giờ; lời mời trực tiếp chỉ các đội liên quan xem được. |
| Rating / blacklist | Chỉ nhận 1–5 sao sau giờ thi đấu, trận hoàn thành và hai đội xác nhận. Một đánh giá 1 sao hợp lệ hạn chế bắt đối và đình chỉ tin mở; quản lý nội bộ vẫn truy cập được. |
| Giữ Lửa / AB | Giữ công thức mặc định có mặt +2, ghi bàn +3, kiến tạo +1, thắng +1. Sửa nguồn và ghi sổ AB nằm trong cùng giao dịch; lỗi ghi điểm không để lại nguồn bị thay đổi dở dang. Kỳ đóng từ chối sửa trước khi ghi. |
| Cúp tháng | Chỉ trao cho người đồng hạng nhất có điểm dương; chặn chốt khi còn buổi/trận chưa hoàn tất. Điểm cá nhân mặc định dùng tháng hiện tại. |
| Penalty | Giữ minigame hiện có; đã thử chọn người sút/thủ môn từ danh sách đội và thực hiện cú sút thành công trên trình duyệt. |

## Cách sử dụng

1. Vào **Đội của tôi → Tổng quan → Sửa hồ sơ** để cập nhật đội và logo.
2. Vào **Cầu thủ & áo đấu** để nhập/sửa thể hình, số áo và size; xuất Excel khi đặt áo.
3. Vào **Sinh hoạt** để tạo buổi đá hằng tuần, điểm danh và tạo trận nội bộ. Cập nhật đội hình, tỉ số, bàn thắng và kiến tạo trong trang trận.
4. Vào **Trận đấu & giải** để thêm trận giải/giao hữu ngoài và quản lý thành tích; chọn một thành tích nổi bật nhất.
5. Vào **Tìm đối giao lưu** để chọn khung giờ, khu vực, sân nhà/đi khách. Sau trận, hai đội xác nhận kết quả rồi lãnh đội đánh giá đối thủ.
6. Vào **Giữ Lửa** để xem bảng điểm tháng, sổ AB cá nhân, cúp và penalty. Khi đã hoàn tất dữ liệu tháng, lãnh đội chốt danh hiệu.

## Kiểm chứng

- Chạy toàn bộ solution bằng `dotnet test AloBall.sln --no-restore`: **54/54 đạt, 0 lỗi, 0 bỏ qua** trong cùng lần chạy cuối.
- Bổ sung 15 trường hợp kiểm thử so với 39 test ban đầu, bao phủ quyền import/export, sửa chéo đội, khóa kỳ, đảo AB điểm danh, riêng tư, form cầu thủ, giải/thành tích, giới hạn sao, người nhận cúp, hiển thị trang theo vai trò, rollback khi sổ AB lỗi, blacklist và sân nhà/khung giờ.
- Test rollback dùng lỗi SQLite chủ động tại bước ghi sổ AB, xác nhận cả bàn thắng và revision trận vẫn giữ nguyên.
- Test blacklist đi qua HTTP gửi review 1 sao, xác nhận tin chuyển Suspended, không đăng tin mới được và dashboard nội bộ vẫn trả HTTP 200.
- Đã kiểm tra trực quan dashboard desktop 1440px, mobile 390px, danh sách và form cầu thủ trên điện thoại; form không tràn ngang. Đã kiểm tra thao tác tab và lượt sút penalty.
- [Log kiểm thử](refactor-regression.log), [kết quả TRX](test-results/refactor-regression.trx), [ảnh desktop](preview-desktop.jpg), [ảnh mobile](preview-mobile.jpg).

## Phạm vi vận hành

- Đây là bản sửa local, chưa triển khai production. Preview tại `http://localhost:5188` dùng `audit/ui-preview.db`, độc lập database ứng dụng hiện có.
- Cúp tháng được lãnh đội chốt thủ công; buổi đá được tạo thủ công. Không có lịch tự tạo buổi hay tác vụ tự trao cúp.
- Thưởng thắng dành cho các cầu thủ nằm trong đội hình bên thắng. AB là điểm/giá trị tích lũy trong hệ thống; chưa có ví hoặc giao dịch chuyển nhượng.
- Minigame là giải trí, không cộng AB thi đấu. Logo hiện nhập qua đường dẫn HTTPS, chưa có upload ảnh.
- Bản sao mã nguồn trước sửa: `audit/source-before-refactor.zip`. Báo cáo audit trước sửa được giữ tại `audit/requirements-audit-2026-10-15.md` để đối chiếu.
