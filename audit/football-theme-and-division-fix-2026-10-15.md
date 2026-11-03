# Màu sân cỏ và sửa luồng chia đội

## Giao diện

Đổi bộ màu toàn ứng dụng sang xanh cỏ (#198448), xanh đậm (#082f24), trắng và vàng (#e5d56a). Đồng bộ sidebar, nút, biểu mẫu, hồ sơ đội, bảng tỉ số, minh họa sân và penalty. Cập nhật theme-color, manifest và cache PWA.

## Chia đội

- Khi 16 cầu thủ tham gia sơ đồ sân 7, mở bảng chọn quân số với hướng dẫn chọn thêm 2 dự bị. Đếm trực tiếp số tham gia, đá chính và dự bị; nút Xác nhận & chia đội hoạt động khi đủ 14 đá chính.
- Bỏ chọn tham gia sẽ bỏ trạng thái dự bị của người đó. Trận chưa có đội hình hiển thị danh sách tham gia mặc định rõ ràng.
- API phân biệt danh sách rỗng do người dùng gửi với việc không truyền danh sách; danh sách rỗng không tự chọn lại cả đội.
- Đội hình đã lưu không tự trở thành khóa bên/khóa vị trí trong lần chia tiếp theo. Lỗi cũ có thể giữ cả hai thủ môn cùng bên và làm thuật toán không tìm được đội hình.
- Lãnh đội vẫn chủ động chỉ định dự bị và khóa người. Preview chưa ghi đội hình; thao tác Lưu đội hình giữ nguyên.

## Kiểm chứng

61/61 kiểm thử đạt, gồm regression cho danh sách rỗng, danh sách mặc định và việc không tự khóa đội hình cũ. Log: division-flow-regression.log. TRX: test-results/division-flow-regression.trx.

Kiểm tra trực tiếp trận nội bộ KLB trên dữ liệu preview: chọn Lê Cầu Thủ và Hà Đức Chinh làm dự bị, kết quả 7 người bên A + 7 người bên B + 2 dự bị; 11 vị trí đúng sở trường chính. Không bấm Lưu đội hình thử nghiệm.

Ảnh: division-fixed.jpg, football-desktop.jpg, football-mobile.jpg. Preview: http://localhost:5188/.
