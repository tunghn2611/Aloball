using AloBall.Web.Data;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Controllers;

[Authorize]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<AloBall.Web.Data.Entities.ApplicationUser> _userManager;
    private readonly ITeamAuthorizationService _auth;
    private readonly IMatchmakingService _matchmaking;

    public AdminController(
        ApplicationDbContext db,
        UserManager<AloBall.Web.Data.Entities.ApplicationUser> userManager,
        ITeamAuthorizationService auth,
        IMatchmakingService matchmaking)
    {
        _db = db;
        _userManager = userManager;
        _auth = auth;
        _matchmaking = matchmaking;
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

        ViewBag.BlacklistedTeams = blacklistedTeams;
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
}
