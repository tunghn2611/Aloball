using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Controllers;

public class LeaderboardController : Controller
{
    private readonly IAloballStarsService _stars;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITeamAuthorizationService _auth;

    public LeaderboardController(
        IAloballStarsService stars,
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ITeamAuthorizationService auth)
    {
        _stars = stars;
        _db = db;
        _userManager = userManager;
        _auth = auth;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index(
        string? region = null,
        bool onlyRanked = true,
        bool preview = false,
        Guid? myTeamId = null)
    {
        var user = User?.Identity?.IsAuthenticated == true ? await _userManager.GetUserAsync(User) : null;
        var vm = await _stars.BuildLeaderboardAsync(region, onlyRanked, preview, user?.Id, myTeamId);
        return View(vm);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> HeadToHead(Guid myTeamId, Guid rivalTeamId)
    {
        if (myTeamId == Guid.Empty || rivalTeamId == Guid.Empty || myTeamId == rivalTeamId)
            return BadRequest(new { error = "Cặp đội bóng đối đầu không hợp lệ." });

        var rival = await _db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == rivalTeamId);
        if (rival == null || rival.IsDraft)
            return NotFound(new { error = "Không tìm thấy đội đối thủ." });

        if (!rival.IsPublic)
        {
            var user = User?.Identity?.IsAuthenticated == true ? await _userManager.GetUserAsync(User) : null;
            if (user == null || !await _auth.HasMemberAccessAsync(rivalTeamId, user.Id))
                return NotFound(new { error = "Hồ sơ đối thủ không công khai." });
        }

        var h2h = await _stars.GetHeadToHeadAsync(myTeamId, rivalTeamId);
        if (h2h == null)
            return NotFound(new { error = "Không tìm thấy dữ liệu đối đầu." });

        return Json(h2h);
    }
}
