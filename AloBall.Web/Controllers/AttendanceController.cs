using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Controllers;

[Authorize]
[ServiceFilter(typeof(DomainMutationFilter))]
public class AttendanceController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITeamAuthorizationService _auth;
    private readonly IAttendanceCalculationEngine _calcEngine;
    private readonly IFireKeepingService _fireKeeping;

    public AttendanceController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ITeamAuthorizationService auth,
        IAttendanceCalculationEngine calcEngine,
        IFireKeepingService fireKeeping)
    {
        _db = db;
        _userManager = userManager;
        _auth = auth;
        _calcEngine = calcEngine;
        _fireKeeping = fireKeeping;
    }

    // 1. Danh sách các buổi tập / thi đấu
    [HttpGet]
    public async Task<IActionResult> Sessions(Guid teamId, string? period = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasMemberAccessAsync(teamId, user.Id))
            return Forbid();

        var team = await _db.Teams.FindAsync(teamId);
        if (team == null) return NotFound();

        period ??= DateTime.Today.ToString("yyyy-MM");

        var sessions = await _db.AttendanceSessions
            .Include(s => s.Records)
            .Where(s => s.TeamId == teamId && s.StatisticalPeriod == period)
            .OrderByDescending(s => s.SessionDateTime)
            .ToListAsync();

        ViewBag.Team = team;
        ViewBag.Period = period;
        ViewBag.IsManager = await _auth.HasManagerAccessAsync(teamId, user.Id);

        return View(sessions);
    }

    // 2. Chi tiết & Trực tiếp Điểm danh buổi (Mobile-first Touch UI)
    [HttpGet]
    public async Task<IActionResult> Detail(Guid id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var session = await _db.AttendanceSessions
            .Include(s => s.Team)
            .Include(s => s.Match)
            .Include(s => s.Records)
            .ThenInclude(r => r.Player)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (session == null) return NotFound();

        if (!await _auth.HasMemberAccessAsync(session.TeamId, user.Id))
            return Forbid();

        // Đảm bảo mọi cầu thủ đang active của đội đều có record trong session
        var allPlayers = await _db.Players
            .Where(p => p.TeamId == session.TeamId && p.IsActive)
            .ToListAsync();

        bool hasNew = false;
        bool canManage = await _auth.HasManagerAccessAsync(session.TeamId, user.Id);
        bool canPopulate = canManage && session.Status == SessionStatus.Draft && !await CompetitionRules.IsClosedAsync(_db, session.TeamId, session.StatisticalPeriod);
        foreach (var p in allPlayers.Where(_ => canPopulate))
        {
            if (!session.Records.Any(r => r.PlayerId == p.Id))
            {
                session.Records.Add(new AttendanceRecord
                {
                    SessionId = session.Id,
                    PlayerId = p.Id,
                    Response = ResponseStatus.NotRecorded,
                    Actual = ActualStatus.NotChecked
                });
                hasNew = true;
            }
        }

        if (hasNew)
        {
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Xử lý race condition/concurrency khi có request khác đã chèn record đồng thời
                _db.ChangeTracker.Clear();
                session = await _db.AttendanceSessions
                    .Include(s => s.Team)
                    .Include(s => s.Match)
                    .Include(s => s.Records)
                    .ThenInclude(r => r.Player)
                    .FirstOrDefaultAsync(s => s.Id == id);
                if (session == null) return NotFound();
            }
        }

        ViewBag.IsManager = await _auth.HasManagerAccessAsync(session.TeamId, user.Id);
        return View(session);
    }

    // 2.1. Tạo trận đấu nội bộ từ buổi sinh hoạt
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateMatchForSession(Guid sessionId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var session = await _db.AttendanceSessions
            .Include(s => s.Records)
            .FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session == null) return NotFound();

        if (!await _auth.HasManagerAccessAsync(session.TeamId, user.Id))
            return Forbid();

        if (session.MatchId.HasValue)
        {
            return RedirectToAction("Detail", "Matches", new { id = session.MatchId.Value });
        }

        if (session.Status == SessionStatus.Cancelled || await CompetitionRules.IsClosedAsync(_db, session.TeamId, session.StatisticalPeriod))
            return Conflict(new { message = "Buổi đã hủy hoặc kỳ đã chốt. Không thể tạo trận." });

        // Tạo trận đấu nội bộ
        var match = new MatchGame
        {
            TeamAId = session.TeamId,
            MatchDateTime = session.SessionDateTime,
            PitchName = string.IsNullOrEmpty(session.PitchName) ? "Sân bóng sinh hoạt" : session.PitchName,
            PitchType = string.IsNullOrEmpty(session.PitchType) ? "7 người" : session.PitchType,
            Category = MatchCategory.Internal,
            Status = MatchStatus.Scheduled,
            IsPublic = true,
            Notes = $"Trận đấu sinh hoạt thuộc buổi: {session.Title}"
        };

        _db.Matches.Add(match);
        await _db.SaveChangesAsync();

        // Gán vào session
        session.MatchId = match.Id;

        // Tự động đưa các cầu thủ có mặt (Actual == Present) vào đội hình trận (mặc định chia Bên A)
        var presentPlayers = session.Records.Where(r => r.Actual == ActualStatus.Present).ToList();
        int idx = 0;
        foreach (var r in presentPlayers)
        {
            // Chia đều A và B: chẵn vào A, lẻ vào B
            string side = (idx % 2 == 0) ? "A" : "B";
            _db.MatchPlayerLineups.Add(new MatchPlayerLineup
            {
                MatchId = match.Id,
                TeamId = session.TeamId,
                PlayerId = r.PlayerId,
                TeamSide = side,
                IsStarting = true
            });
            idx++;
        }

        await _db.SaveChangesAsync();

        TempData["Success"] = "Đã khởi tạo trận đấu nội bộ và chia đội thành công!";
        return RedirectToAction("Detail", "Matches", new { id = match.Id });
    }

    // 3. Cập nhật trực tiếp trạng thái Phản hồi hoặc Điểm danh thực tế (Ajax / Mobile-first click)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRecordStatus(Guid recordId, int? responseStatus, int? actualStatus)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var record = await _db.AttendanceRecords
            .Include(r => r.Session)
            .Include(r => r.Player)
            .FirstOrDefaultAsync(r => r.Id == recordId);

        if (record == null) return NotFound(new { success = false, message = "Bản ghi không tồn tại." });

        bool isManager = await _auth.HasManagerAccessAsync(record.Session.TeamId, user.Id);

        if (!isManager) return Forbid();
        if (await CompetitionRules.IsClosedAsync(_db, record.Session.TeamId, record.Session.StatisticalPeriod))
            return Conflict(new { success = false, message = "Kỳ đã chốt. Hãy mở lại kỳ trong Giữ Lửa trước khi sửa điểm danh." });
        if (record.Session.Status == SessionStatus.Cancelled ||
            (responseStatus.HasValue && !Enum.IsDefined(typeof(ResponseStatus), responseStatus.Value)) ||
            (actualStatus.HasValue && !Enum.IsDefined(typeof(ActualStatus), actualStatus.Value))) return BadRequest();

        if (responseStatus.HasValue)
        {
            record.Response = (ResponseStatus)responseStatus.Value;
            record.ResponseTime = DateTime.UtcNow;
            record.IsResponseLate = DateTime.UtcNow > record.Session.ResponseDeadline;
        }

        if (actualStatus.HasValue && isManager)
        {
            record.Actual = (ActualStatus)actualStatus.Value;
        }

        // Tự động tính lại quy chế ngay tại server
        await _calcEngine.RecalculateSessionPenaltiesAsync(record.SessionId);
        if (record.Session.Status == SessionStatus.Finalized)
            await _fireKeeping.SyncAttendanceSessionPointsAsync(record.SessionId, user.Id);

        // Lấy lại bản ghi sau tính toán
        var updated = await _db.AttendanceRecords.FindAsync(recordId);

        return Json(new
        {
            success = true,
            response = updated?.Response.ToString(),
            actual = updated?.Actual.ToString(),
            fineAmount = updated?.FineAmount ?? 0,
            penaltyReason = updated?.PenaltyReason,
            needsConfirmation = updated?.NeedsConfirmation ?? false
        });
    }

    // 4. Tạo buổi tập / trận đấu mới
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSession(Guid teamId, string title, DateTime sessionDateTime, DateTime? responseDeadline, string? statisticalPeriod, string? pitchName, string? pitchType, string? notes)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasManagerAccessAsync(teamId, user.Id))
            return Forbid();

        var deadline = responseDeadline ?? sessionDateTime.Date.AddHours(12);
        var period = string.IsNullOrEmpty(statisticalPeriod) ? sessionDateTime.ToString("yyyy-MM") : statisticalPeriod;
        if (period != sessionDateTime.ToString("yyyy-MM")) return BadRequest(new { message = "Kỳ thống kê phải trùng tháng sinh hoạt." });
        if (await CompetitionRules.IsClosedAsync(_db, teamId, period)) return Conflict(new { message = "Kỳ đã chốt. Hãy mở lại kỳ trước khi tạo buổi." });

        var session = new AttendanceSession
        {
            TeamId = teamId,
            Title = string.IsNullOrWhiteSpace(title) ? "Buổi sinh hoạt bóng đá" : title.Trim(),
            SessionDateTime = sessionDateTime,
            ResponseDeadline = deadline,
            StatisticalPeriod = period,
            PitchName = pitchName?.Trim() ?? string.Empty,
            PitchType = string.IsNullOrWhiteSpace(pitchType) ? "7 người" : pitchType.Trim(),
            Status = SessionStatus.Draft,
            Notes = notes
        };

        _db.AttendanceSessions.Add(session);
        await _db.SaveChangesAsync();

        // Tạo sẵn các bản ghi cho toàn bộ cầu thủ active
        var players = await _db.Players.Where(p => p.TeamId == teamId && p.IsActive).ToListAsync();
        foreach (var p in players)
        {
            _db.AttendanceRecords.Add(new AttendanceRecord
            {
                SessionId = session.Id,
                PlayerId = p.Id,
                Response = ResponseStatus.NotRecorded,
                Actual = ActualStatus.NotChecked
            });
        }
        await _db.SaveChangesAsync();

        TempData["Success"] = "Đã tạo buổi sinh hoạt thành công!";
        return RedirectToAction(nameof(Detail), new { id = session.Id });
    }

    // 5. Chốt sổ điểm danh buổi sinh hoạt -> Kích hoạt ghi sổ điểm AB
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FinalizeSession(Guid id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var session = await _db.AttendanceSessions.FindAsync(id);
        if (session == null) return NotFound();

        if (!await _auth.HasManagerAccessAsync(session.TeamId, user.Id))
            return Forbid();

        if (session.SessionDateTime > CompetitionRules.LocalNow) return BadRequest(new { message = "Chỉ chốt điểm danh sau giờ sinh hoạt." });
        if (await CompetitionRules.IsClosedAsync(_db, session.TeamId, session.StatisticalPeriod)) return Conflict(new { message = "Kỳ đã chốt. Hãy mở lại kỳ trước khi chốt buổi." });
        session.Status = SessionStatus.Finalized;
        await _db.SaveChangesAsync();

        // Đồng bộ điểm chuyên cần vào Sổ AB
        await _fireKeeping.SyncAttendanceSessionPointsAsync(session.Id, user.Id);

        TempData["Success"] = "Đã chốt sổ điểm danh và ghi nhận điểm AB giữ lửa cho cầu thủ có mặt!";
        return RedirectToAction(nameof(Detail), new { id = session.Id });
    }

    // 6. Hủy buổi sinh hoạt -> Thu hồi an toàn điểm AB chuyên cần
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelSession(Guid id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var session = await _db.AttendanceSessions.FindAsync(id);
        if (session == null) return NotFound();

        if (!await _auth.HasManagerAccessAsync(session.TeamId, user.Id))
            return Forbid();

        if (await CompetitionRules.IsClosedAsync(_db, session.TeamId, session.StatisticalPeriod)) return Conflict(new { message = "Kỳ đã chốt. Hãy mở lại kỳ trước khi hủy buổi." });
        session.Status = SessionStatus.Cancelled;
        await _db.SaveChangesAsync();

        // Thu hồi điểm trong sổ AB
        await _fireKeeping.SyncAttendanceSessionPointsAsync(session.Id, user.Id);

        TempData["Success"] = "Đã hủy buổi sinh hoạt và thu hồi điểm AB liên quan.";
        return RedirectToAction(nameof(Sessions), new { teamId = session.TeamId });
    }
}
