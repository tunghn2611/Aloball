# Audit yêu cầu AloBall — 15/10/2026

## Kết luận

**Chưa đủ điều kiện nghiệm thu toàn bộ yêu cầu.** Mã nguồn đã có phần lớn các module: hồ sơ công khai, cầu thủ/đặt áo, trận nội bộ, bắt đối, rating/blacklist, Giữ Lửa, sổ AB, cúp tháng và minigame penalty. Tuy nhiên, còn thiếu luồng quản trị hồ sơ/giải đấu, một số trường chỉ tồn tại trong model, và có lỗi phân quyền/đồng bộ điểm ảnh hưởng trực tiếp đến yêu cầu.

Phạm vi: đối chiếu controllers, services, entities, Razor views, cấu hình dữ liệu và các test hiện có trong `D:\AloBall`. Đây là audit mã nguồn local, không phải nghiệm thu bản triển khai production. Không chỉnh sửa mã nguồn nghiệp vụ.

## Bảng đối chiếu

| Yêu cầu | Kết quả | Bằng chứng và khoảng thiếu |
|---|---|---|
| Ai cũng xem được thông tin đội | Có | `TeamsController.PublicProfile` có `AllowAnonymous`; route `/doi/{slug}`. Tuy nhiên cờ riêng tư của đội/trận chưa được thực thi đầy đủ, xem F04. |
| Tên đội, ngày thành lập, logo | Một phần | Tên có nhập và hiển thị. Ngày thành lập khi tạo luôn lấy hôm nay; trang public chỉ hiện năm. `LogoUrl` có trong model nhưng public dùng hai ký tự đầu tên đội, không render ảnh logo. Chưa có action/form sửa hồ sơ đội. |
| Danh sách cầu thủ, số áo, chiều cao, cân nặng | Một phần | Có model và giao diện hiển thị. `AddPlayer` không nhận chiều cao/cân nặng; không có luồng sửa cầu thủ đầy đủ. Cầu thủ mới mặc định không công khai chiều cao/cân nặng và chưa có form đổi cờ này. Dữ liệu demo đã có sẵn nên dễ tạo cảm giác hoàn thiện. |
| Thu thập thông tin để làm áo | Có phần cơ bản | Nhập tên in áo, số áo, size áo/quần; xuất Excel gồm cả chiều cao/cân nặng. Import hiện chỉ cập nhật tên in áo, size áo/quần. Luồng Excel có lỗi phân quyền F01. |
| Lịch sử đá giải | Chưa có luồng hoàn chỉnh | Có enum `Tournament`, nhưng không tìm thấy action/form tạo hoặc quản lý trận giải. Lịch sử public là 10 trận hoàn tất gần nhất, không phân mục giải đấu. Trang chi tiết gọi mọi trận không phải nội bộ là “Giao hữu”. |
| Thành tích tốt nhất từng đạt | Một phần | Có `CompetitionAchievement`, trường `IsFeatured` và phần hiển thị danh hiệu. Chưa có chức năng để lãnh đội thêm/sửa/xóa/chọn thành tích tốt nhất; dữ liệu được thêm ở seed. View liệt kê theo năm, chưa dùng `IsFeatured` để chọn thành tích nổi bật. |
| Đá nội bộ hằng tuần, cập nhật đội hình/tỉ số/bàn thắng/kiến tạo | Có phần cơ bản | Tạo buổi sinh hoạt, tạo trận nội bộ từ buổi, chia A/B, cập nhật đội hình, tỉ số, ghi bàn/kiến tạo. Mỗi buổi tạo thủ công; chưa thấy lịch lặp tự động (yêu cầu gốc không bắt buộc tự động). Có lỗi kiểm tra phạm vi và đồng bộ F02/F05/F06. |
| Chỉ lãnh đội sửa, thành viên chỉ xem | Chưa đạt | Phần lớn action kiểm tra Owner/Manager; nhưng import Excel không kiểm tra và một số action trận chỉ kiểm tra đội do request gửi lên. PlatformAdmin cũng được toàn quyền. Thành viên có thể tự báo tham gia/vắng, là quyền bổ sung ngoài chế độ chỉ xem. |
| Bắt đối theo sân nhà/đi khách, giờ, khu vực, sân | Một phần | Đăng kèo, đề nghị, mời trực tiếp, chốt đối có sẵn; có ngày, giờ bắt đầu/kết thúc, khu vực, tên sân. `PitchArrangement` chỉ có model/seed; form và action đăng kèo không nhận lựa chọn sân nhà/đi khách. |
| Rating 1–5 sau trận | Một phần | Có UI chọn sao và service đánh giá trận Completed, chỉ lãnh đội tham gia trận. Backend chưa chặn sao ngoài 1–5; chưa yêu cầu thời điểm trận đã qua hoặc hai bên xác nhận, xem F08. |
| Một rating 1 sao → blacklist; khóa bắt đối, giữ quản lý nội bộ | Có logic chính | Một đánh giá 1 sao hợp lệ bật `IsRestrictedFromMatchmaking`, đình chỉ tin mở; chặn đăng tin/gửi đề nghị/mời trực tiếp/chốt đối. Các trang nội bộ không bị khóa theo cờ này. Tính hợp lệ đầu vào rating vẫn cần sửa F08. |
| Giữ Lửa, chấm điểm/xếp hạng | Có | Trang riêng, bảng điểm theo tháng, lịch sử điểm cá nhân, tổng tích lũy, quy chế tính điểm. |
| Người cao điểm nhất tháng nhận cúp | Chưa đúng hoàn toàn | Có chốt kỳ, lưu cúp, mở lại kỳ. Nhưng lãnh đội có thể trao cúp cho người có điểm dương bất kỳ; không ràng buộc hạng 1, xem F07. Hiện là thao tác chốt thủ công, chưa có tác vụ tự trao cuối tháng. |
| Giá trị cầu thủ tính bằng AB coin | Có ở mức điểm tích lũy | Sổ điểm và popup “AB tích lũy (Giá trị)” đã có. Nếu hiểu giá trị cầu thủ = tổng AB thì có nền tảng. Chưa có cơ chế chuyển nhượng/giao dịch/ví; các cơ chế đó không được coi là bắt buộc từ yêu cầu gốc. |
| Minigame sút penalty, tên người sút/thủ môn lấy từ đội | Có trong mã giao diện | Hai dropdown lấy cầu thủ đang hoạt động, chặn chọn trùng, trò chơi chọn hướng sút và thủ môn ngẫu nhiên. Không cộng điểm game vào AB thi đấu. Chưa kiểm thử thao tác bằng trình duyệt trong audit này. |
| Có mặt +2 AB | Có công thức, chưa bảo đảm khi sửa dữ liệu | Cộng từ điểm danh thực tế sau chốt buổi. Sửa điểm danh sau chốt không tự đồng bộ AB, xem F05. |
| Bàn thắng +3, kiến tạo +1 AB | Có công thức | Đồng bộ khi trận Completed, có bút toán đảo khi sửa/xóa. Tuy nhiên lưu nguồn trước khi kiểm tra khóa kỳ có thể gây lệch sổ, xem F06. |
| Mỗi cầu thủ bên thắng +1 AB | Có công thức | Trận nội bộ xác định bên thắng bằng `TeamSide`; trận hai CLB xác định theo đội thắng. Phạm vi là cầu thủ có trong lineup, không phải toàn bộ danh sách CLB. Cờ `HasActuallyPlayed` hiện không được engine xét. |

## Các phát hiện cần xử lý

### F01 — P1: Thành viên hoặc người ngoài đội có thể sửa dữ liệu đặt áo qua import

[SheetsController.cs:65](/D:/AloBall/AloBall.Web/Controllers/SheetsController.cs:65) chỉ yêu cầu đăng nhập và CSRF, không gọi `HasManagerAccessAsync`. Service `ApplyPlayersImportAsync` chỉ kiểm tra PlayerId thuộc teamId do request cung cấp, không biết danh tính/vai trò người gọi.

Kịch bản: tài khoản thành viên gửi POST hợp lệ đến `/Sheets/ApplyImport` với teamId và PlayerId đã biết, thay đổi tên in áo hoặc size. Không cần là lãnh đội. Hai endpoint export cũng không kiểm tra thành viên của đội; một tài khoản đăng nhập có thể yêu cầu file của đội khác.

Khắc phục: kiểm tra lãnh đội trước preview/apply, kiểm tra quyền xem trước export, giữ kiểm tra phạm vi cầu thủ trong service. Ẩn nút trên UI không đủ bảo vệ request trực tiếp.

### F02 — P1: Action đội hình/bàn thắng không kiểm tra đầy đủ quan hệ đội–trận–cầu thủ

[MatchesController.cs:160](/D:/AloBall/AloBall.Web/Controllers/MatchesController.cs:160) và [MatchesController.cs:639](/D:/AloBall/AloBall.Web/Controllers/MatchesController.cs:639) kiểm tra quyền quản lý `teamId` do client gửi, nhưng không xác minh đội đó tham gia `matchId`, cũng không kiểm tra player/scorer/assist thuộc đội đó.

Kịch bản: lãnh đội C gửi teamId C kèm matchId của trận A–B; action vẫn có thể thêm lineup/sự kiện vào trận A–B. Hoặc lãnh đội A chọn ID cầu thủ đội B cho một sự kiện của A. Khóa ngoại riêng lẻ không đảm bảo quan hệ nghiệp vụ này.

`UpdateInternalLineup` cũng không kiểm tra Category=Internal và không kiểm tra tất cả PlayerId thuộc đội chủ quản. Cần áp dụng các kiểm tra đồng nhất cho mọi đường cập nhật, kể cả đường cũ bên cạnh chức năng chia đội mới.

### F03 — P2: Hồ sơ công khai thiếu luồng nhập/sửa các dữ liệu cốt lõi

[TeamsController.cs:170](/D:/AloBall/AloBall.Web/Controllers/TeamsController.cs:170): chỉ thêm cầu thủ với tên, số áo, vị trí, tên in áo, size. Không nhận chiều cao/cân nặng, không có action sửa cầu thủ.

[TeamsController.cs:254](/D:/AloBall/AloBall.Web/Controllers/TeamsController.cs:254): ngày thành lập mặc định hôm nay, không lấy ngày CLB thực sự thành lập. [PublicProfile.cshtml:17](/D:/AloBall/AloBall.Web/Views/Teams/PublicProfile.cshtml:17): logo là chữ viết tắt. Không tìm thấy action cập nhật logo/ngày thành lập/hồ sơ đội hoặc danh hiệu giải đấu.

Cần bổ sung form/action quản trị tương ứng; không thể nghiệm thu chỉ dựa trên việc entity đã có trường dữ liệu.

### F04 — P1: Các cờ public/private chưa được tôn trọng đầy đủ

[TeamsController.cs:32](/D:/AloBall/AloBall.Web/Controllers/TeamsController.cs:32) tìm theo slug mà không chặn `IsPublic=false` hoặc `IsDraft=true`. [MatchesController.cs:39](/D:/AloBall/AloBall.Web/Controllers/MatchesController.cs:39) cho anonymous xem theo ID mà không kiểm tra `match.IsPublic` hoặc quyền thành viên. Vì vậy ẩn khỏi danh sách không ngăn truy cập URL trực tiếp.

[PublicProfile.cshtml:208](/D:/AloBall/AloBall.Web/Views/Teams/PublicProfile.cshtml:208) và phần vị trí cầu thủ không xét `PublicKitNumber`/`PublicPosition`, dù model có những cờ này. Cần thống nhất chính sách công khai ở controller và view.

### F05 — P1: Sửa điểm danh sau chốt làm điểm AB bị cũ

[AttendanceController.cs:193](/D:/AloBall/AloBall.Web/Controllers/AttendanceController.cs:193) cho sửa Actual rồi chỉ chạy tính tiền phạt; không gọi `SyncAttendanceSessionPointsAsync` và không chặn kỳ đã đóng. UI vẫn cho lãnh đội sửa điểm danh sau chốt.

Kịch bản: chốt buổi khi cầu thủ có mặt → +2 AB; sau đó sửa thành vắng → dữ liệu điểm danh đã đổi nhưng +2 AB vẫn còn. Điều này làm sai xếp hạng/cúp tháng.

Tương tự, [MatchesController.cs:578](/D:/AloBall/AloBall.Web/Controllers/MatchesController.cs:578) cập nhật đội hình nội bộ nhưng không đồng bộ thưởng thắng, cũng không kiểm tra trận/kỳ đã chốt. Đổi bên A/B sau khi tính điểm có thể để lại thưởng cho người không còn thuộc bên thắng.

### F06 — P1: Nguồn dữ liệu được lưu trước khi kiểm tra khóa kỳ AB

[MatchesController.cs:115](/D:/AloBall/AloBall.Web/Controllers/MatchesController.cs:115) lưu tỉ số trước khi gọi đồng bộ. Service sau đó mới kiểm tra kỳ đóng tại [FireKeepingService.cs:279](/D:/AloBall/AloBall.Web/Services/FireKeepingService.cs:279) và ném lỗi. Cùng mẫu này xuất hiện ở lưu lineup, thêm/xóa bàn thắng, chốt/hủy buổi.

Kịch bản: sau khi trao cúp và đóng kỳ, lãnh đội gửi sửa tỉ số. Tỉ số mới có thể đã commit, còn đồng bộ AB bị từ chối; sổ AB/cúp vẫn phản ánh kết quả cũ. Transaction trong service không rollback được lần SaveChanges trước đó của controller.

Khắc phục: kiểm tra khóa kỳ trước khi sửa và dùng transaction bao trùm cả nguồn dữ liệu lẫn sổ AB.

### F07 — P2: Cúp tháng không bắt buộc dành cho người cao điểm nhất

[FireKeepingService.cs:873](/D:/AloBall/AloBall.Web/Services/FireKeepingService.cs:873) chỉ kiểm tra người được chọn có TotalAb > 0. [Index.cshtml:435](/D:/AloBall/AloBall.Web/Views/FireKeeping/Index.cshtml:435) còn cho chọn trong 5 người có điểm cao nhất, không chỉ hạng 1.

Ví dụ A=20 AB, B=10 AB: chọn B vẫn được cấp “Cầu thủ xuất sắc nhất tháng”. Cần ràng buộc max(TotalAb), xử lý đồng hạng theo quy tắc được thống nhất. Chốt kỳ cũng chỉ chặn buổi Draft, chưa chặn mọi trận chưa hoàn tất trong tháng mặc dù leaderboard có cảnh báo.

### F08 — P2: Rating chưa được giới hạn 1–5 tại điểm nhận dữ liệu

[MatchmakingService.cs:119](/D:/AloBall/AloBall.Web/Services/MatchmakingService.cs:119) không kiểm tra `stars < 1 || stars > 5`. `[Range(1,5)]` nằm trên entity được tạo bên trong service; controller nhận một tham số int và không kiểm tra ModelState. Vì vậy thuộc tính này không tự bảo vệ lần SaveChanges.

Ngoài ra service chỉ yêu cầu Completed; `UpdateScore` có thể đặt Completed cho trận tương lai. Do đó chưa thực thi chặt “sau trận” trước khi cho phép review có thể dẫn đến blacklist. Cần kiểm tra ngày/giờ và quy trình xác nhận kết quả. Khi đội B sửa tỉ số, code cũng không reset xác nhận cũ của đội A, khác chiều sửa của đội A.

### F09 — P2: Thiếu lựa chọn sân nhà/đi khách trong luồng đăng kèo

[MatchmakingController.cs:135](/D:/AloBall/AloBall.Web/Controllers/MatchmakingController.cs:135) không nhận `PitchArrangement`; form đăng kèo không có input tương ứng. Tin mới luôn dùng giá trị mặc định Flexible. Trang chủ còn hiển thị mọi giá trị khác HomePitch thành “Đi khách”, khiến Flexible bị mô tả sai.

Khắc phục: thêm lựa chọn và lưu giá trị; với sân nhà cho lấy sân mặc định của đội, kiểm tra sân đã nhập; hiển thị riêng Flexible.

### F10 — P2: Chưa có quản lý lịch sử giải đấu và thành tích tốt nhất

[MatchEntities.cs:10](/D:/AloBall/AloBall.Web/Data/Entities/MatchEntities.cs:10) có Tournament và entity danh hiệu, nhưng không có workflow CRUD tương ứng. `CompetitionAchievements.Add` hiện chỉ xuất hiện trong khởi tạo demo. Cần màn hình lịch sử giải, thêm/sửa trận giải, danh hiệu và chọn thành tích nổi bật, có quyền lãnh đội.

## Điểm AB và phạm vi đã đọc

[FireKeepingService.cs:93](/D:/AloBall/AloBall.Web/Services/FireKeepingService.cs:93) đặt mặc định đúng công thức:

`AB = 2 × số buổi có mặt + 3 × số bàn + 1 × số kiến tạo + 1 × số trận thắng`

Ví dụ có mặt, ghi 2 bàn, kiến tạo 1, thắng trận: 2 + 6 + 1 + 1 = **10 AB**. Repo đã có test cho trường hợp này, chống cộng lặp, đảo điểm khi sửa bàn, hòa thay thắng, thắng trận nội bộ, đồng hạng và khóa kỳ. Các test service riêng lẻ chưa đủ bao phủ thứ tự lưu trong controller hoặc request import thiếu quyền.

Lưu ý phạm vi thưởng thắng: engine lấy lineup của bên thắng, không xét `HasActuallyPlayed`. Nếu yêu cầu là mọi người đã được xếp vào bên thắng thì gần đúng; nếu chỉ thưởng người thực tế ra sân thì chưa có bảo đảm đó. Đây là khác biệt cần chốt khi nghiệm thu, không tự coi là yêu cầu mới.

## Ưu tiên hoàn thiện

1. Chặn sửa/xem chéo đội qua Sheets và các action trận; thực thi public/private.
2. Đồng bộ nguồn và AB trong một transaction; kiểm tra khóa kỳ trước khi ghi; sửa sau chốt phải cập nhật/đảo điểm đúng.
3. Ràng buộc cúp tháng cho hạng 1, giới hạn sao 1–5, kiểm tra điều kiện đánh giá sau trận.
4. Hoàn thiện quản trị hồ sơ/logo/ngày thành lập/chiều cao/cân nặng, giải đấu/thành tích và sân nhà/đi khách.
5. Kiểm thử end-to-end với Owner, Manager, Member, khách chưa đăng nhập và người ngoài đội; dùng dữ liệu đội mới để tránh dữ liệu seed che mất chức năng nhập còn thiếu.

## Kết quả kiểm thử của lần audit

- Đã restore NuGet vào `D:\AloBall\audit\packages` và build/test từ mã nguồn hiện tại; không dựa vào DLL cũ.
- Lần chạy toàn bộ: **37/39 đạt**; hai test `TeamsRuntimeVerificationTests` gặp lỗi quyền Windows Event Log trong sandbox. Log: [test.log](/D:/AloBall/audit/test.log), kết quả: [baseline.trx](/D:/AloBall/audit/test-results/baseline.trx).
- Chạy lại riêng hai test web với quyền môi trường phù hợp và tắt EventLog: **2/2 đạt**. Log: [runtime-verified.log](/D:/AloBall/audit/runtime-verified.log), kết quả: [runtime-verified.trx](/D:/AloBall/audit/test-results/runtime-verified.trx).
- Tổng hợp: cả **39 test hiện có đã có kết quả đạt** qua các lần chạy. Không tuyên bố cả 39 test đã chạy đạt trong cùng một lần.
- Web tests dùng connection string riêng `Data Source=D:\AloBall\audit\runtime-tests.db`; không dùng database ứng dụng đang có.
- Những phát hiện F01–F10 được xác định qua đối chiếu mã nguồn. Chưa bổ sung test tái hiện riêng cho từng phát hiện; chưa kiểm thử minigame bằng trình duyệt hoặc bản production. Test hiện có chạy đạt không đồng nghĩa toàn bộ yêu cầu đã hoàn chỉnh.
- Chỉ tạo báo cáo, cấu hình NuGet cục bộ và các artifact build/test trong lần audit; không sửa controllers/services/views nghiệp vụ.
