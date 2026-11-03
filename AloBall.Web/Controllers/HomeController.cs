using System.Diagnostics;
using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Controllers;

public class HomeViewModel
{
    public List<Team> PublicTeams { get; set; } = new();
    public List<MatchmakingPost> RecentPosts { get; set; } = new();
    public List<MatchGame> UpcomingMatches { get; set; } = new();
    public List<MatchGame> RecentCompletedMatches { get; set; } = new();
}

public class HomeController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<HomeController> _logger;

    public HomeController(ApplicationDbContext db, ILogger<HomeController> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        // 1. Chỉ lấy các đội bóng đã công khai và đang hoạt động
        var publicTeams = await _db.Teams
            .Include(t => t.Players.Where(p => p.IsActive && p.IsPublicProfile))
            .Where(t => t.IsPublic && !t.IsDraft)
            .OrderByDescending(t => t.CreatedAt)
            .Take(6)
            .ToListAsync();

        // 2. Chỉ lấy các tin tìm đối đang Mở và từ đội KHÔNG bị hạn chế (Blacklist)
        var recentPostsRaw = await _db.MatchmakingPosts
            .Include(p => p.Team)
            .Where(p => p.PostType == MatchmakingPostType.Public && p.Status == MatchmakingStatus.Open && !p.Team.IsRestrictedFromMatchmaking)
            .OrderBy(p => p.MatchDate)
            .Take(6)
            .ToListAsync();

        var recentPosts = recentPostsRaw
            .OrderBy(p => p.MatchDate)
            .ThenBy(p => p.StartTime)
            .ToList();

        // 3. Trận đấu sắp diễn ra (công khai)
        var upcomingMatches = await _db.Matches
            .Include(m => m.TeamA)
            .Include(m => m.TeamB)
            .Where(m => m.IsPublic && m.TeamA.IsPublic && !m.TeamA.IsDraft && (m.TeamB == null || (m.TeamB.IsPublic && !m.TeamB.IsDraft)) && m.Status == MatchStatus.Scheduled && m.MatchDateTime >= DateTime.Today)
            .OrderBy(m => m.MatchDateTime)
            .Take(4)
            .ToListAsync();

        // 4. Kết quả gần đây (công khai, hoàn tất)
        var recentCompletedMatches = await _db.Matches
            .Include(m => m.TeamA)
            .Include(m => m.TeamB)
            .Where(m => m.IsPublic && m.TeamA.IsPublic && !m.TeamA.IsDraft && (m.TeamB == null || (m.TeamB.IsPublic && !m.TeamB.IsDraft)) && m.Status == MatchStatus.Completed)
            .OrderByDescending(m => m.MatchDateTime)
            .Take(4)
            .ToListAsync();

        var model = new HomeViewModel
        {
            PublicTeams = publicTeams,
            RecentPosts = recentPosts,
            UpcomingMatches = upcomingMatches,
            RecentCompletedMatches = recentCompletedMatches
        };

        return View(model);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
