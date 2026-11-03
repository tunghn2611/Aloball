using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Controllers;

[Authorize]
public class FireKeepingController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITeamAuthorizationService _auth;
    private readonly IFireKeepingService _fireService;

    public FireKeepingController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ITeamAuthorizationService auth,
        IFireKeepingService fireService)
    {
        _db = db;
        _userManager = userManager;
        _auth = auth;
        _fireService = fireService;
    }

    // 1. Trang trung tâm Giữ Lửa: Tab Bảng xếp hạng, Tab Danh hiệu, Tab Minigame Penalty
    [HttpGet]
    public async Task<IActionResult> Index(Guid teamId, string? period = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasMemberAccessAsync(teamId, user.Id))
            return Forbid();

        var team = await _db.Teams
            .Include(t => t.Players)
            .FirstOrDefaultAsync(t => t.Id == teamId);
        if (team == null) return NotFound();

        period ??= DateTime.UtcNow.ToString("yyyy-MM");

        var leaderboard = await _fireService.GetMonthlyLeaderboardAsync(teamId, period);
        var effectiveReg = await _fireService.GetEffectiveRegulationAsync(teamId, DateTime.UtcNow);

        // Lấy danh sách danh hiệu cúp tháng đã trao của các kỳ
        var allAwards = await _db.MonthlyFireAwards
            .Include(a => a.Player)
            .Where(a => a.TeamId == teamId && !a.IsArchived)
            .OrderByDescending(a => a.StatisticalPeriod)
            .ToListAsync();

        ViewBag.Team = team;
        ViewBag.Period = period;
        ViewBag.Leaderboard = leaderboard;
        ViewBag.Regulation = effectiveReg;
        ViewBag.AllAwards = allAwards;
        ViewBag.IsManager = await _auth.HasManagerAccessAsync(teamId, user.Id);

        return View(team);
    }

    // 2. Chốt kỳ thi đua và trao cúp xuất sắc nhất tháng (Lãnh đạo)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FinalizePeriod(Guid teamId, string period, List<Guid> chosenPlayerIds, string? notes)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasManagerAccessAsync(teamId, user.Id))
            return Forbid();

        var result = await _fireService.FinalizePeriodAndAwardAsync(teamId, period, chosenPlayerIds, user.Id, notes);
        if (result.Success)
        {
            TempData["Success"] = result.Message;
        }
        else
        {
            TempData["Error"] = result.Message;
        }

        return RedirectToAction(nameof(Index), new { teamId, period });
    }

    // 3. Mở lại kỳ thi đua đã chốt (Bắt buộc có lý do)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReopenPeriod(Guid teamId, string period, string reason)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasManagerAccessAsync(teamId, user.Id))
            return Forbid();

        var result = await _fireService.ReopenPeriodAsync(teamId, period, reason, user.Id);
        if (result.Success)
        {
            TempData["Success"] = result.Message;
        }
        else
        {
            TempData["Error"] = result.Message;
        }

        return RedirectToAction(nameof(Index), new { teamId, period });
    }

    // 4. API Lấy hồ sơ AB cá nhân cầu thủ (Xem popup lịch sử điểm, cúp)
    [HttpGet]
    public async Task<IActionResult> PlayerSummary(Guid teamId, Guid playerId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasMemberAccessAsync(teamId, user.Id))
            return Forbid();

        try
        {
            var summary = await _fireService.GetPlayerAbSummaryAsync(teamId, playerId);
            return Json(new
            {
                success = true,
                playerName = summary.PlayerName,
                kitNumber = summary.KitNumber,
                position = summary.Position,
                currentMonthAb = summary.CurrentMonthAb,
                totalCumulativeAb = summary.TotalCumulativeAb,
                awards = summary.Awards.Select(a => new { a.AwardTitle, a.StatisticalPeriod, a.TotalAbPoints }),
                history = summary.JournalEntries.Select(j => new
                {
                    date = j.EventDate.ToString("dd/MM/yyyy"),
                    role = j.SourceRole,
                    points = j.Points,
                    reason = j.AdjustmentReason,
                    period = j.StatisticalPeriod
                })
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }
}
