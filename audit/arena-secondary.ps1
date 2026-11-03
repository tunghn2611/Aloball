$ErrorActionPreference = 'Stop'
$views = 'D:\AloBall\AloBall.Web\Views'
$utf8 = New-Object System.Text.UTF8Encoding($false)
function ReadView($name) { [IO.File]::ReadAllText("$views\$name.cshtml") }
function SaveView($name, $value) { [IO.File]::WriteAllText("$views\$name.cshtml", $value, $utf8) }
$create = @'
@{ ViewData["Title"] = "Thành lập đội bóng"; }
<header class="page-heading"><div><span class="eyebrow">MỘT MÀU ÁO · MỘT TẬP THỂ</span><h1>Câu chuyện bắt đầu từ đây.</h1><p>Tạo ngôi nhà chung cho đội bóng của bạn.</p></div><a asp-action="Index" class="btn btn-arena-outline">Đội của tôi <i class="bi bi-arrow-up-right"></i></a></header>
<div class="creation-layout">
    <aside class="creation-story"><span class="eyebrow">WELCOME TO THE CLUB</span><h2>Đội bóng nhỏ.<br />Đam mê lớn.</h2><p>Từ những buổi đá cuối tuần đến mùa giải đáng nhớ, giữ mọi hoạt động của đội ở một nơi.</p><div class="creation-step"><span>01</span><div><h3>Tạo bản sắc</h3><p>Tên, logo và câu chuyện của đội.</p></div></div><div class="creation-step"><span>02</span><div><h3>Tập hợp anh em</h3><p>Thêm danh sách cầu thủ và thông tin áo đấu.</p></div></div><div class="creation-step"><span>03</span><div><h3>Hẹn ngày ra sân</h3><p>Lên lịch, điểm danh và ghi lại thành tích.</p></div></div></aside>
    <form asp-action="Create" method="post" class="editor-panel">
        <div asp-validation-summary="ModelOnly" class="text-danger mb-3"></div>
        <section class="editor-section"><h2>Thông tin nhận diện</h2><p>Những thông tin này xuất hiện trên hồ sơ công khai.</p><div class="row g-3"><div class="col-12"><label for="name" class="form-label">Tên đội bóng *</label><input id="name" name="name" class="form-control" placeholder="Ví dụ: KLB FC" required /></div><div class="col-md-6"><label for="establishedDate" class="form-label">Ngày thành lập</label><input type="date" id="establishedDate" name="establishedDate" class="form-control" /></div><div class="col-md-6"><label for="logoUrl" class="form-label">Đường dẫn logo (HTTPS)</label><input type="url" id="logoUrl" name="logoUrl" class="form-control" placeholder="https://…/logo.png" /></div></div></section>
        <section class="editor-section"><h2>Nơi chúng ta ra sân</h2><p>Giúp các đội khác biết bạn ở đâu và sẵn sàng giao lưu.</p><div class="row g-3"><div class="col-md-6"><label for="activityArea" class="form-label">Khu vực hoạt động</label><input id="activityArea" name="activityArea" class="form-control" placeholder="Quận, huyện, thành phố" /></div><div class="col-md-6"><label for="homePitch" class="form-label">Sân nhà</label><input id="homePitch" name="homePitch" class="form-control" placeholder="Tên sân bóng (nếu có)" /></div><div class="col-12"><label for="description" class="form-label">Câu chuyện của đội</label><textarea id="description" name="description" class="form-control" rows="4" placeholder="Lịch đá quen thuộc, phong cách và tinh thần của đội…"></textarea></div></div></section>
        <div class="editor-footer"><a asp-action="Index" class="btn btn-arena-outline">Để sau</a><button type="submit" class="btn btn-arena-primary">Thành lập đội <i class="bi bi-arrow-right"></i></button></div>
    </form>
</div>
'@
SaveView 'Teams\Create' $create
$errorPage = @'
@model ErrorViewModel
@{ ViewData["Title"] = "Có lỗi xảy ra"; }
<section class="error-stage"><span class="eyebrow">TẠM DỪNG MỘT NHỊP</span><h1>Chưa thể hoàn tất thao tác.</h1><p>Vui lòng thử lại. Nếu lỗi vẫn tiếp diễn, hãy gửi mã bên dưới cho người quản trị.</p>@if (Model.ShowRequestId) { <p class="small text-muted">Mã hỗ trợ: <code>@Model.RequestId</code></p> }<a asp-controller="Home" asp-action="Index" class="btn btn-arena-primary">Về trang khám phá <i class="bi bi-arrow-right"></i></a></section>
'@
SaveView 'Shared\Error' $errorPage
$source = ReadView 'Attendance\Sessions'
$start = $source.IndexOf('<div class="page-heading">')
$end = $source.IndexOf('<!-- Modal')
$new = @'
<header class="page-heading"><div><span class="eyebrow">LỊCH SINH HOẠT · @team?.Name</span><h1>Tuần này, ra sân nhé.</h1><p>Lên lịch, chốt quân số và theo dõi chuyên cần của đội.</p></div>
@if (isManager) { <div class="d-flex gap-2"><a asp-controller="Sheets" asp-action="ExportAttendance" asp-route-teamId="@team?.Id" asp-route-period="@period" class="btn btn-arena-outline"><i class="bi bi-download"></i> Báo cáo</a><button class="btn btn-arena-primary" data-bs-toggle="modal" data-bs-target="#createSessionModal"><i class="bi bi-plus-lg"></i> Tạo buổi mới</button></div> }</header>
<div class="attendance-toolbar"><p><strong>@Model.Count() buổi sinh hoạt</strong> trong tháng đang xem</p><form method="get" class="d-flex align-items-center gap-2"><input type="hidden" name="teamId" value="@team?.Id" /><label for="sessionPeriod" class="visually-hidden">Tháng sinh hoạt</label><input id="sessionPeriod" type="month" name="period" value="@period" class="form-control" /><button class="btn btn-arena-dark" type="submit">Xem</button></form></div>
@if (!Model.Any()) { <div class="arena-empty-state"><i class="bi bi-calendar2-plus"></i><h3>Một lịch mới đang chờ</h3><p>Tháng @period chưa có buổi sinh hoạt. Lãnh đội có thể tạo buổi đầu tiên để hẹn cả đội ra sân.</p></div> }
<div class="session-timeline">
@foreach (var s in Model) {
    var present = s.Records.Count(r => r.Actual == AloBall.Web.Data.Entities.ActualStatus.Present);
    var checkedCount = s.Records.Count(r => r.Actual == AloBall.Web.Data.Entities.ActualStatus.Present || r.Actual == AloBall.Web.Data.Entities.ActualStatus.Absent);
    <article class="session-item"><div class="session-date"><strong>@s.SessionDateTime.ToString("dd")</strong><small>THÁNG @s.SessionDateTime.ToString("MM")</small></div><div><span class="badge @(s.Status == AloBall.Web.Data.Entities.SessionStatus.Finalized ? "bg-success-subtle text-success" : "bg-warning-subtle text-dark")">@(s.Status == AloBall.Web.Data.Entities.SessionStatus.Finalized ? "Đã chốt điểm danh" : "Đang mở")</span><h2>@s.Title</h2><div class="session-info"><span><i class="bi bi-clock"></i> @s.SessionDateTime.ToString("HH:mm")</span><span><i class="bi bi-geo-alt"></i> @(s.PitchName ?? "Chưa chọn sân") · @s.PitchType</span></div><p class="small text-muted mt-2 mb-0">Chốt quân số: @s.ResponseDeadline.ToString("dd/MM · HH:mm")</p></div><div class="session-progress"><div><strong>@present</strong><small>Có mặt</small></div><div><strong>@checkedCount/@s.Records.Count</strong><small>Đã điểm danh</small></div><a asp-action="Detail" asp-route-id="@s.Id" class="btn btn-arena-outline">Mở buổi <i class="bi bi-arrow-right"></i></a></div></article>
}
</div>

'@
SaveView 'Attendance\Sessions' ($source.Substring(0,$start)+$new+$source.Substring($end))
$source = ReadView 'Teams\Players'
$start = $source.IndexOf('    <div>', $source.IndexOf('<div class="page-heading">'))
$end = $source.IndexOf('    @if (isManager)', $start)
$header = @'
    <div><span class="eyebrow">ĐỘI HÌNH · @team?.Name</span><h1>Những người cùng màu áo.</h1><p>@Model.Count() cầu thủ · Hồ sơ, số áo và thông tin đặt áo của đội.</p></div>

'@
SaveView 'Teams\Players' ($source.Substring(0,$start)+$header+$source.Substring($end))
foreach ($name in @('Attendance\Detail','Matchmaking\Details','Sheets\PreviewImport')) {
    $source = ReadView $name
    $source = $source.Replace('col-12 col-lg-8','col-12').Replace('col-12 col-lg-10','col-12')
    $source = $source.Replace('class="d-flex align-items-center gap-2 mb-3"','class="page-heading"')
    SaveView $name $source
}
# Only the first heading on each screen is its page title.
Get-ChildItem $views -Recurse -Filter '*.cshtml' | Where-Object { $_.Name -notlike '_*' } | ForEach-Object {
    $value = [IO.File]::ReadAllText($_.FullName)
    $matches = [regex]::Matches($value, '<h1\b[^>]*>.*?</h1>', [Text.RegularExpressions.RegexOptions]::Singleline)
    for ($index = $matches.Count - 1; $index -ge 1; $index--) {
        $match = $matches[$index]
        $value = $value.Remove($match.Index,$match.Length).Insert($match.Index,$match.Value.Replace('<h1','<h3').Replace('</h1>','</h3>'))
    }
    [IO.File]::WriteAllText($_.FullName,$value,$utf8)
}
