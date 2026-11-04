using AloBall.Web.Data;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Controllers;

[Authorize]
public partial class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<AloBall.Web.Data.Entities.ApplicationUser> _userManager;
    private readonly ITeamAuthorizationService _auth;
    private readonly IMatchmakingService _matchmaking;
    private readonly IRefereeCoordinationService _refereeService;
    private readonly IPitchBookingService _pitchService;

    public AdminController(
        ApplicationDbContext db,
        UserManager<AloBall.Web.Data.Entities.ApplicationUser> userManager,
        ITeamAuthorizationService auth,
        IMatchmakingService matchmaking,
        IRefereeCoordinationService refereeService,
        IPitchBookingService pitchService)
    {
        _db = db;
        _userManager = userManager;
        _auth = auth;
        _matchmaking = matchmaking;
        _refereeService = refereeService;
        _pitchService = pitchService;
    }

    // Trang quản trị PlatformAdmin: Xem danh sách khiếu nại đánh giá và danh sách Blacklist
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        var appeals = await _db.ReviewDisputeAppeals
            .Include(a => a.Review).ThenInclude(r => r.Match)
            .Include(a => a.Review).ThenInclude(r => r.ReviewerTeam)
            .Include(a => a.Team)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        var blacklistedTeams = await _db.Teams
            .Where(t => t.IsRestrictedFromMatchmaking)
            .ToListAsync();

        var flaggedReviews = await _db.TeamMatchReviews
            .Include(r => r.Match)
            .Include(r => r.ReviewerTeam)
            .Include(r => r.TargetTeam)
            .Where(r => r.RatingStars == 1 && r.IsValid)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        ViewBag.BlacklistedTeams = blacklistedTeams;
        ViewBag.FlaggedReviews = flaggedReviews;
        return View(appeals);
    }

    // Xử lý khiếu nại
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveAppeal(Guid appealId, bool invalidateReview, string adminNote)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        var result = await _matchmaking.ResolveAppealAsync(appealId, invalidateReview, adminNote, user.Id);
        TempData["Success"] = result.Message;

        return RedirectToAction(nameof(Index));
    }

    // Áp dụng hoặc gỡ bỏ Blacklist thủ công bởi PlatformAdmin
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetTeamBlacklist(Guid teamId, bool isRestricted, string? reason)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        var result = await _matchmaking.SetTeamBlacklistStatusAsync(teamId, isRestricted, reason, user.Id);
        if (result.Success)
            TempData["Success"] = result.Message;
        else
            TempData["Error"] = result.Message;

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ManualUnrestrict(Guid teamId)
    {
        return await SetTeamBlacklist(teamId, false, null);
    }
}
