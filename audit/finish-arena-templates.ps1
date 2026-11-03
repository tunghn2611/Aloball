$ErrorActionPreference = 'Stop'
$views = 'D:\AloBall\AloBall.Web\Views'
$utf8 = New-Object System.Text.UTF8Encoding($false)
function ReadView($name) { [IO.File]::ReadAllText("$views\$name.cshtml") }
function SaveView($name, $value) { [IO.File]::WriteAllText("$views\$name.cshtml", $value, $utf8) }
Get-ChildItem $views -Recurse -Filter '*.cshtml' | ForEach-Object {
    $value = [IO.File]::ReadAllText($_.FullName).Replace('tarena-', 'tab-')
    [IO.File]::WriteAllText($_.FullName, $value, $utf8)
}

$source = ReadView 'Matchmaking\Index'
$modal = $source.Substring($source.IndexOf('<!-- Modal'))
$page = @'
@model IEnumerable<AloBall.Web.Data.Entities.MatchmakingPost>
@{
    ViewData["Title"] = "Tìm đối giao lưu";
    var managedTeams = ViewBag.ManagedTeams as IEnumerable<AloBall.Web.Data.Entities.Team> ?? Enumerable.Empty<AloBall.Web.Data.Entities.Team>();
}
<header class="page-heading editorial-heading">
    <div><span class="eyebrow">KẾT NỐI TRÊN SÂN</span><h1>Một trận hay.<br />Một người bạn mới.</h1><p>Tìm đội phù hợp và hẹn nhau một trận bóng đẹp.</p></div>
    @if (User.Identity?.IsAuthenticated == true && managedTeams.Any())
    { <button class="btn btn-arena-primary" data-bs-toggle="modal" data-bs-target="#newPostModal"><i class="bi bi-plus-lg"></i> Đăng lịch giao lưu</button> }
</header>
<div class="market-layout">
    <aside class="market-filter arena-card">
        <span class="eyebrow">CHỌN TRẬN CỦA BẠN</span><h2>Tìm lịch phù hợp</h2>
        <form method="get" class="d-grid gap-3">
            <div><label class="form-label" for="filterArea">Khu vực hoặc sân</label><input id="filterArea" name="area" value="@ViewBag.SelectedArea" class="form-control" placeholder="Ví dụ: Cầu Giấy" /></div>
            <div><label class="form-label" for="filterDate">Ngày thi đấu</label><input id="filterDate" type="date" name="date" value="@ViewBag.SelectedDate" class="form-control" /></div>
            <div><label class="form-label" for="filterPitch">Loại sân</label><select id="filterPitch" name="pitchType" class="form-select"><option value="">Tất cả loại sân</option>@foreach (var type in new[] { "5 người", "7 người", "11 người" }) { <option value="@type" selected="@(ViewBag.SelectedPitchType == type)">Sân @type</option> }</select></div>
            <button class="btn btn-arena-dark" type="submit"><i class="bi bi-search"></i> Tìm trận</button><a asp-action="Index" class="text-center small text-muted">Xóa bộ lọc</a>
        </form>
        <div class="filter-note"><i class="bi bi-stars"></i><p>Giao lưu có trách nhiệm.<br />Đánh giá sau mỗi trận giúp cộng đồng tìm được những đối thủ phù hợp.</p></div>
    </aside>
    <section><div class="section-heading"><h2>Lịch đang tìm đối</h2><span class="count-tag">@Model.Count() lời hẹn</span></div>
    @if (!Model.Any()) { <div class="arena-empty-state"><i class="bi bi-calendar2-plus"></i><h3>Chưa có lịch phù hợp</h3><p>Thử khu vực hoặc ngày khác, hoặc đăng lịch của đội bạn.</p></div> }
    @foreach (var post in Model)
    {
        <article class="invitation-card">
            <div class="invitation-date"><strong>@post.MatchDate.ToString("dd")</strong><span>THÁNG @post.MatchDate.ToString("MM")</span></div>
            <div class="invitation-main"><div class="d-flex align-items-center gap-2 mb-2"><span class="badge bg-success-subtle text-success">Đang tìm đối</span><span class="small text-muted">@post.PitchType · @(post.PitchArrangement == AloBall.Web.Data.Entities.PitchArrangement.AwayPitch ? "Đi khách" : "Sân nhà")</span></div>
                <h2>@post.Team.Name</h2><div class="invitation-meta"><span><i class="bi bi-clock"></i> @post.StartTime.ToString(@"hh\:mm") – @post.EndTime.ToString(@"hh\:mm")</span><span><i class="bi bi-geo-alt"></i> @(post.PitchName ?? post.Area)</span></div>
                <div class="invitation-meta"><span>Trình độ: @post.DesiredLevel</span><span>Tiền sân: @post.CostSharing</span><span><i class="bi bi-star-fill"></i> @post.Team.AverageRating.ToString("0.0") · @post.Team.ValidReviewCount đánh giá</span></div>
                @if (!string.IsNullOrWhiteSpace(post.Notes)) { <p class="small text-muted mt-2 mb-0">@post.Notes</p> }
            </div>
            <a asp-action="Details" asp-route-id="@post.Id" class="btn btn-arena-outline">Xem lịch <i class="bi bi-arrow-up-right"></i></a>
        </article>
    }
    </section>
</div>
'@
SaveView 'Matchmaking\Index' ($page + "`n" + $modal)

$source = ReadView 'FireKeeping\Index'
$start = $source.IndexOf('        <!-- Header Banner')
$end = $source.IndexOf('        <!-- Cảnh báo')
$header = @'
        <header class="page-heading editorial-heading">
            <div><span class="eyebrow">GIỮ LỬA · @Model.Name</span><h1>Mỗi lần ra sân.<br />Một bước tiến lên.</h1><p>Tích lũy AB Coin và cùng chinh phục chiếc cúp xuất sắc nhất tháng.</p></div>
            <form method="get" class="period-picker"><input type="hidden" name="teamId" value="@Model.Id" /><label class="form-label" for="firePeriod">Tháng thi đua</label><div class="d-flex gap-2"><input id="firePeriod" type="month" name="period" value="@period" class="form-control" /><button class="btn btn-arena-dark" type="submit">Xem</button></div></form>
        </header>
        <div class="coin-rules">
            <div><i class="bi bi-person-check"></i><span>Có mặt</span><strong>+@(regulation?.PointsPerAttendance ?? 2) <small>AB</small></strong></div>
            <div><i class="bi bi-bullseye"></i><span>Ghi bàn</span><strong>+@(regulation?.PointsPerGoal ?? 3) <small>AB</small></strong></div>
            <div><i class="bi bi-arrow-up-right-circle"></i><span>Kiến tạo</span><strong>+@(regulation?.PointsPerAssist ?? 1) <small>AB</small></strong></div>
            <div><i class="bi bi-trophy"></i><span>Thắng trận</span><strong>+@(regulation?.PointsPerWin ?? 1) <small>AB</small></strong></div>
        </div>
        <p class="small text-muted mb-4">Điểm được tính từ điểm danh và kết quả trận đấu đã xác nhận · Quy chế phiên bản @(regulation?.Version ?? 1).</p>

'@
$source = $source.Substring(0,$start) + $header + $source.Substring($end)
SaveView 'FireKeeping\Index' ($source.Replace('col-12 col-xl-10','col-12'))

$source = ReadView 'Matches\Detail'
$start = $source.IndexOf('        <!-- Match Score Banner -->')
$end = $source.IndexOf('            <div class="mt-2 d-flex')
$header = @'
        <header class="page-heading"><div><span class="eyebrow">MATCHDAY</span><h1>Ngày thi đấu</h1><p>@Model.MatchDateTime.ToString("dd/MM/yyyy · HH:mm") · @Model.PitchName · @Model.PitchType</p></div><span class="badge bg-light text-dark">@(Model.Category == AloBall.Web.Data.Entities.MatchCategory.Internal ? "Đá nội bộ" : Model.Category == AloBall.Web.Data.Entities.MatchCategory.Tournament ? "Đá giải" : "Giao hữu")</span></header>
        <div class="match-scoreboard mb-4">
            <div class="scoreboard-teams">
                <div><span class="scoreboard-crest"><i class="bi bi-shield-shaded"></i></span><h2>@(Model.Category == AloBall.Web.Data.Entities.MatchCategory.Internal ? "Bên A" : Model.TeamA.Name)</h2><span class="small">CHỦ NHÀ</span></div>
                <div class="scoreboard-result"><strong>@(Model.ScoreA?.ToString() ?? "–")<span>:</span>@(Model.ScoreB?.ToString() ?? "–")</strong><small>TỈ SỐ TRẬN ĐẤU</small></div>
                <div><span class="scoreboard-crest away"><i class="bi bi-shield-shaded"></i></span><h2>@(Model.Category == AloBall.Web.Data.Entities.MatchCategory.Internal ? "Bên B" : Model.TeamB?.Name ?? Model.TeamBExternalName ?? "Đối thủ ngoài")</h2><span class="small">ĐỘI KHÁCH</span></div>
            </div>
'@
$source = $source.Substring(0,$start) + $header + $source.Substring($end)
SaveView 'Matches\Detail' ($source.Replace('col-12 col-lg-8','col-12'))

$source = ReadView 'Teams\PublicProfile'
$start = $source.IndexOf('<!-- 1.')
$end = $source.IndexOf('<!-- 2.')
$header = @'
<section class="public-passport">
    <div class="passport-art"><span>THE CLUB</span><i class="bi bi-shield-shaded"></i><span>EST. @(Model.EstablishedDate?.ToString("yyyy") ?? "—")</span></div>
    <div class="passport-body"><div class="d-flex align-items-center gap-3 mb-4"><div class="club-logo">@if (!string.IsNullOrEmpty(Model.LogoUrl)) { <img src="@Model.LogoUrl" alt="Logo @Model.Name" /> } else { <i class="bi bi-shield"></i> }</div><span class="eyebrow">HỒ SƠ CÂU LẠC BỘ</span></div>
        <h1>@Model.Name</h1><p class="passport-description">@(string.IsNullOrWhiteSpace(Model.Description) ? "Cùng một niềm đam mê. Cùng một màu áo. Hẹn gặp nhau trên sân bóng." : Model.Description)</p>
        <div class="passport-facts"><div><span>Khu vực</span><strong>@(Model.ActivityArea ?? "Chưa cập nhật")</strong></div><div><span>Sân nhà</span><strong>@(Model.HomePitch ?? "Chưa cập nhật")</strong></div><div><span>Thành lập</span><strong>@(Model.EstablishedDate?.ToString("dd/MM/yyyy") ?? "Chưa cập nhật")</strong></div><div><span>Uy tín giao lưu</span><strong><i class="bi bi-star-fill"></i> @Model.AverageRating.ToString("0.0") <small>(@Model.ValidReviewCount)</small></strong></div></div>
        <div class="d-flex align-items-center gap-3 flex-wrap mt-4">
            @if (Model.IsRestrictedFromMatchmaking) { <span class="badge bg-danger-subtle text-danger">Tạm ngưng bắt đối</span> }
            else { <span class="availability"><i></i> Sẵn sàng giao lưu</span> }
            @if (!Model.IsRestrictedFromMatchmaking && !isManager) {
                if (User.Identity?.IsAuthenticated != true) { <a asp-controller="Account" asp-action="Login" asp-route-returnUrl="@($"/doi/{Model.Slug}")" class="btn btn-arena-primary">Mời giao hữu <i class="bi bi-arrow-up-right"></i></a> }
                else { <button class="btn btn-arena-primary" data-bs-toggle="modal" data-bs-target="#inviteFriendlyModal">Mời giao hữu <i class="bi bi-arrow-up-right"></i></button> }
            }
            @if (isManager) { <a asp-action="Dashboard" asp-route-id="@Model.Id" class="btn btn-arena-dark">Không gian đội <i class="bi bi-arrow-right"></i></a> }
        </div>
    </div>
</section>

'@
SaveView 'Teams\PublicProfile' ($source.Substring(0,$start) + $header + $source.Substring($end))
