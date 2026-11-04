using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Controllers;

[ServiceFilter(typeof(DomainMutationFilter))]
public partial class TeamsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITeamAuthorizationService _auth;
    private readonly IWebHostEnvironment? _env;
    private readonly ITeamMessagingService _messaging;

    public TeamsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ITeamAuthorizationService auth)
        : this(db, userManager, auth, null, null)
    {
    }

    public TeamsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ITeamAuthorizationService auth, IWebHostEnvironment? env)
        : this(db, userManager, auth, env, null)
    {
    }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public TeamsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ITeamAuthorizationService auth, IWebHostEnvironment? env, ITeamMessagingService? messaging)
    {
        _db = db;
        _userManager = userManager;
        _auth = auth;
        _env = env;
        _messaging = messaging ?? new TeamMessagingService(db, auth);
    }


    // 1. Hồ sơ công khai của đội bóng (ai cũng xem được)
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> PublicProfile(string slug)
    {
        var team = await _db.Teams
            .Include(t => t.Players.Where(p => p.IsActive && p.IsPublicProfile))
            .Include(t => t.Achievements)
            .FirstOrDefaultAsync(t => t.Slug == slug && t.IsPublic && !t.IsDraft);

        if (team == null) return NotFound("Không tìm thấy thông tin đội bóng.");

        // Lấy các trận sắp diễn ra của đội
        var upcomingMatches = await _db.Matches
            .Include(m => m.TeamA)
            .Include(m => m.TeamB)
            .Where(m => (m.TeamAId == team.Id || m.TeamBId == team.Id) && m.IsPublic && m.TeamA.IsPublic && !m.TeamA.IsDraft && (m.TeamB == null || (m.TeamB.IsPublic && !m.TeamB.IsDraft)) && m.Status == MatchStatus.Scheduled && m.MatchDateTime >= DateTime.Today)
            .OrderBy(m => m.MatchDateTime)
            .Take(5)
            .ToListAsync();

        // Lấy lịch sử các trận đấu công khai đã hoàn tất
        var completedMatches = await _db.Matches
            .Include(m => m.TeamA)
            .Include(m => m.TeamB)
            .Where(m => (m.TeamAId == team.Id || m.TeamBId == team.Id) && m.IsPublic && m.TeamA.IsPublic && !m.TeamA.IsDraft && (m.TeamB == null || (m.TeamB.IsPublic && !m.TeamB.IsDraft)) && m.Status == MatchStatus.Completed)
            .OrderByDescending(m => m.MatchDateTime)
            .Take(10)
            .ToListAsync();

        // Kiểm tra xem User hiện tại có quyền quản lý đội này không (Owner / Manager) hoặc là Thành viên
        var user = User != null ? await _userManager.GetUserAsync(User) : null;
        bool isManager = false;
        bool isMember = false;
        string? currentUserId = null;
        List<Team> userManagedTeams = new();
        if (user != null)
        {
            currentUserId = user.Id;
            isManager = await _auth.HasManagerAccessAsync(team.Id, user.Id);
            isMember = await _auth.HasMemberAccessAsync(team.Id, user.Id);

            var managedTeamIds = await _db.TeamMembers
                .Where(m => m.UserId == user.Id && (m.Role == TeamRole.Manager || m.Role == TeamRole.Owner))
                .Select(m => m.TeamId)
                .ToListAsync();

            userManagedTeams = await _db.Teams
                .Where(t => managedTeamIds.Contains(t.Id) && t.Id != team.Id && !t.IsRestrictedFromMatchmaking)
                .ToListAsync();
        }

        // Lấy danh sách các trận đấu chính thức đã hoàn tất và được xác nhận đối soát
        var officialMatches = await _db.Matches
            .Include(m => m.TeamA)
            .Include(m => m.TeamB)
            .Where(m => (m.TeamAId == team.Id || m.TeamBId == team.Id)
                && m.Status == MatchStatus.Completed
                && m.ConfirmedByTeamA
                && (m.ConfirmedByTeamB || !m.TeamBId.HasValue)
                && m.ScoreA.HasValue && m.ScoreB.HasValue)
            .OrderByDescending(m => m.MatchDateTime)
            .ToListAsync();

        int wins = 0, draws = 0, losses = 0, gf = 0, ga = 0;
        foreach (var m in officialMatches)
        {
            int myScore = m.TeamAId == team.Id ? (m.ScoreA ?? 0) : (m.ScoreB ?? 0);
            int oppScore = m.TeamAId == team.Id ? (m.ScoreB ?? 0) : (m.ScoreA ?? 0);
            gf += myScore;
            ga += oppScore;
            if (myScore > oppScore) wins++;
            else if (myScore == oppScore) draws++;
            else losses++;
        }
        int totalOfficialMatches = wins + draws + losses;
        double winPercentage = totalOfficialMatches > 0 ? Math.Round((double)wins / totalOfficialMatches * 100, 1) : 0;

        // Tỉ lệ phản hồi và nhận lời mời giao lưu (trong vòng 12 tháng gần nhất)
        var twelveMonthsAgo = DateTime.UtcNow.AddMonths(-12);
        var now = DateTime.UtcNow;

        var directChallenges = await _db.MatchmakingPosts
            .Include(p => p.Proposals)
            .Where(p => p.PostType == MatchmakingPostType.DirectChallenge 
                && p.TargetTeamId == team.Id 
                && p.CreatedAt >= twelveMonthsAgo)
            .ToListAsync();

        var publicPosts = await _db.MatchmakingPosts
            .Include(p => p.Proposals)
            .Where(p => p.PostType == MatchmakingPostType.Public 
                && p.TeamId == team.Id 
                && p.CreatedAt >= twelveMonthsAgo)
            .ToListAsync();

        int eligibleInvites = 0;
        int respondedInvites = 0;
        int acceptedInvites = 0;

        void EvaluateInvite(MatchmakingPost post, MatchmakingProposal proposal)
        {
            if (proposal.Status == ProposalStatus.Cancelled) return; // Loại bỏ lời mời bị rút/hủy trước khi phản hồi

            var matchTime = post.MatchDate.Date.Add(post.StartTime);
            bool isExpired = matchTime <= now;

            if (proposal.Status == ProposalStatus.Accepted)
            {
                eligibleInvites++;
                respondedInvites++;
                acceptedInvites++;
            }
            else if (proposal.Status == ProposalStatus.Rejected)
            {
                eligibleInvites++;
                respondedInvites++;
            }
            else if (proposal.Status == ProposalStatus.Pending && isExpired)
            {
                eligibleInvites++; // Hết hạn mà chưa phản hồi
            }
        }

        foreach (var p in directChallenges)
        {
            foreach (var prop in p.Proposals)
            {
                EvaluateInvite(p, prop);
            }
        }

        foreach (var p in publicPosts)
        {
            foreach (var prop in p.Proposals.Where(pr => pr.ChallengingTeamId != team.Id))
            {
                EvaluateInvite(p, prop);
            }
        }

        double responseRate = eligibleInvites > 0 ? Math.Round((double)respondedInvites / eligibleInvites * 100, 1) : 100.0;
        double acceptanceRate = respondedInvites > 0 ? Math.Round((double)acceptedInvites / respondedInvites * 100, 1) : 0.0;

        var reputationStats = new TeamReputationStats
        {
            TotalOfficialMatches = totalOfficialMatches,
            Wins = wins,
            Draws = draws,
            Losses = losses,
            WinPercentage = winPercentage,
            GoalsScored = gf,
            GoalsConceded = ga,
            TotalEligibleInvitations = eligibleInvites,
            RespondedInvitations = respondedInvites,
            AcceptedInvitations = acceptedInvites,
            ResponseRatePercentage = responseRate,
            AcceptanceRatePercentage = acceptanceRate
        };

        // Lịch sử đánh giá sau trận (chỉ lấy review hợp lệ)
        var verifiedReviews = await _db.TeamMatchReviews
            .Include(r => r.ReviewerTeam)
            .Include(r => r.Match)
            .Where(r => r.TargetTeamId == team.Id && r.IsValid)
            .OrderByDescending(r => r.CreatedAt)
            .Take(20)
            .ToListAsync();

        ViewBag.ReputationStats = reputationStats;
        ViewBag.OfficialMatches = officialMatches.Take(10).ToList();
        ViewBag.VerifiedReviews = verifiedReviews;

        ViewBag.UpcomingMatches = upcomingMatches;
        ViewBag.CompletedMatches = completedMatches;
        ViewBag.TournamentMatches = await _db.Matches.Include(m => m.TeamA).Include(m => m.TeamB)
            .Where(m => (m.TeamAId == team.Id || m.TeamBId == team.Id) && m.IsPublic && m.TeamA.IsPublic && !m.TeamA.IsDraft && (m.TeamB == null || (m.TeamB.IsPublic && !m.TeamB.IsDraft)) && m.Category == MatchCategory.Tournament)
            .OrderByDescending(m => m.MatchDateTime).ToListAsync();
        ViewBag.IsManager = isManager;
        ViewBag.IsMember = isMember;
        ViewBag.CurrentUserId = currentUserId;
        ViewBag.UserManagedTeams = userManagedTeams;
        return View(team);
    }

    // 2. Danh sách đội mà User đang tham gia (Nội bộ)
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var memberships = await _db.TeamMembers
            .Include(m => m.Team)
            .Where(m => m.UserId == user.Id)
            .ToListAsync();

        return View(memberships);
    }

    // 3. Bảng điều khiển nội bộ của 1 đội bóng
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Dashboard(Guid id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasMemberAccessAsync(id, user.Id))
            return Forbid();

        var team = await _db.Teams
            .Include(t => t.Players)
            .Include(t => t.Achievements)
            .Include(t => t.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (team == null) return NotFound();

        var role = await _auth.GetUserRoleInTeamAsync(id, user.Id);
        ViewBag.UserRole = role;
        ViewBag.IsManager = await _auth.HasManagerAccessAsync(id, user.Id);
        ViewBag.IsOwner = await _auth.IsOwnerAsync(id, user.Id);
        ViewBag.UnreadMessagesCount = await _messaging.GetUnreadCountAsync(id, user.Id);

        // Thống kê nhanh
        ViewBag.TotalPlayers = team.Players.Count(p => p.IsActive);
        ViewBag.UpcomingSessions = await _db.AttendanceSessions
            .Where(s => s.TeamId == id && s.SessionDateTime >= DateTime.Today && s.Status != SessionStatus.Cancelled)
            .OrderBy(s => s.SessionDateTime)
            .Take(3)
            .ToListAsync();

        // Lời mời thách đấu gửi đến đội này
        var incomingChallenges = await _db.MatchmakingPosts
            .Include(p => p.Team)
            .Include(p => p.Proposals)
            .Where(p => p.PostType == MatchmakingPostType.DirectChallenge && p.TargetTeamId == id && p.Status == MatchmakingStatus.Open)
            .OrderBy(p => p.MatchDate)
            .ToListAsync();

        // Lời mời thách đấu đội này đã gửi đi
        var outgoingChallenges = await _db.MatchmakingPosts
            .Include(p => p.TargetTeam)
            .Include(p => p.Proposals)
            .Where(p => p.PostType == MatchmakingPostType.DirectChallenge && p.TeamId == id && p.Status == MatchmakingStatus.Open)
            .OrderBy(p => p.MatchDate)
            .ToListAsync();

        ViewBag.IncomingChallenges = incomingChallenges;
        ViewBag.OutgoingChallenges = outgoingChallenges;

        return View("Workspace", team);
    }

    // 4. Quản lý danh sách cầu thủ nội bộ
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Players(Guid teamId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasMemberAccessAsync(teamId, user.Id))
            return Forbid();

        var team = await _db.Teams.FindAsync(teamId);
        if (team == null) return NotFound();

        var players = await _db.Players
            .Where(p => p.TeamId == teamId)
            .OrderBy(p => p.KitNumber ?? 999)
            .ToListAsync();

        ViewBag.Team = team;
        ViewBag.IsManager = await _auth.HasManagerAccessAsync(teamId, user.Id);
        return View(players);
    }

    // 5. Thêm cầu thủ mới (chỉ Manager/Owner)
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPlayer(Guid teamId, string fullName, int? kitNumber, string? position, string? shirtName, string? shirtSize, string? shortSize)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasManagerAccessAsync(teamId, user.Id))
            return Forbid();

        if (string.IsNullOrWhiteSpace(fullName))
        {
            TempData["Error"] = "Họ tên cầu thủ không được để trống.";
            return RedirectToAction(nameof(Players), new { teamId });
        }

        var player = new Player
        {
            TeamId = teamId,
            FullName = fullName.Trim(),
            KitNumber = kitNumber,
            Position = position?.Trim(),
            ShirtName = shirtName?.Trim(),
            ShirtSize = shirtSize?.Trim(),
            ShortSize = shortSize?.Trim(),
            IsActive = true,
            JoinedDate = DateTime.UtcNow
        };

        _db.Players.Add(player);
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Đã thêm cầu thủ {player.FullName} thành công!";
        return RedirectToAction(nameof(Players), new { teamId });
    }

    // 6. Tạo đội bóng mới (Bất kỳ user đã đăng nhập nào)
    [HttpGet]
    [Authorize]
    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string? activityArea, string? homePitch, string? description, DateTime? establishedDate = null, string? logoUrl = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (string.IsNullOrWhiteSpace(name) || name.Length > 150 || !CompetitionRules.SafeImageUrl(logoUrl) || establishedDate > CompetitionRules.LocalNow.Date)
        {
            ModelState.AddModelError("", "Kiểm tra tên đội, ngày thành lập và đường dẫn logo HTTPS.");
            return View();
        }

        // Tạo slug an toàn
        string rawSlug = name.Trim().ToLower()
            .Replace(" ", "-")
            .Replace("đ", "d");
        string cleanSlug = System.Text.RegularExpressions.Regex.Replace(rawSlug, @"[^a-z0-9\-]", "");
        if (string.IsNullOrEmpty(cleanSlug)) cleanSlug = "team-" + Guid.NewGuid().ToString().Substring(0, 6);

        // Đảm bảo unique slug
        string finalSlug = cleanSlug;
        int counter = 1;
        while (await _db.Teams.AnyAsync(t => t.Slug == finalSlug))
        {
            finalSlug = $"{cleanSlug}-{counter++}";
        }

        var team = new Team
        {
            Name = name.Trim(),
            Slug = finalSlug,
            ActivityArea = activityArea?.Trim(),
            HomePitch = homePitch?.Trim(),
            Description = description?.Trim(),
            EstablishedDate = establishedDate,
            LogoUrl = logoUrl?.Trim(),
            IsPublic = true,
            AverageRating = 5.0,
            ValidReviewCount = 0
        };

        _db.Teams.Add(team);
        await _db.SaveChangesAsync();

        // Gán người tạo làm Owner và BTC
        _db.TeamMembers.Add(new TeamMember
        {
            TeamId = team.Id,
            UserId = user.Id,
            Role = TeamRole.Owner,
            IsBtc = true
        });

        // Tạo luôn một hồ sơ cầu thủ đại diện cho Owner
        var ownerPlayer = new Player
        {
            TeamId = team.Id,
            FullName = user.FullName ?? user.UserName ?? "Chủ tịch",
            LinkedUserId = user.Id,
            Position = "Đội trưởng",
            IsActive = true
        };
        _db.Players.Add(ownerPlayer);

        // Khởi tạo quy chế mặc định
        _db.TeamRegulations.Add(new TeamRegulation
        {
            TeamId = team.Id,
            Version = 1,
            Title = $"Quy chế chuyên cần {team.Name}",
            FineNoResponse = 30000m,
            FineAttendingButAbsent = 50000m,
            MinMatchesPerMonth = 2,
            FineUnderMinAttendance = 200000m,
            IsExcusedExemptFromFine = true
        });

        await _db.SaveChangesAsync();

        TempData["Success"] = $"Chúc mừng! Đội bóng {team.Name} đã được tạo thành công.";
        return RedirectToAction(nameof(Dashboard), new { id = team.Id });
    }
}

