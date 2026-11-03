using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Controllers;

public class MatchmakingController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITeamAuthorizationService _auth;
    private readonly IMatchmakingService _matchmaking;

    public MatchmakingController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ITeamAuthorizationService auth,
        IMatchmakingService matchmaking)
    {
        _db = db;
        _userManager = userManager;
        _auth = auth;
        _matchmaking = matchmaking;
    }

    // 1. Sàn bắt đối công khai (ai cũng xem được)
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index(string? area, DateTime? date, string? pitchType)
    {
        var query = _db.MatchmakingPosts
            .Include(p => p.Team)
            .Where(p => p.PostType == MatchmakingPostType.Public && p.Status == MatchmakingStatus.Open && !p.Team.IsRestrictedFromMatchmaking);

        if (!string.IsNullOrWhiteSpace(area))
        {
            var areaTrim = area.Trim();
            query = query.Where(p => p.Area.Contains(areaTrim) || (p.PitchName != null && p.PitchName.Contains(areaTrim)));
        }

        if (date.HasValue)
        {
            var filterDate = date.Value.Date;
            query = query.Where(p => p.MatchDate.Date == filterDate);
        }

        if (!string.IsNullOrWhiteSpace(pitchType))
        {
            var pitchTypeTrim = pitchType.Trim();
            query = query.Where(p => p.PitchType.Contains(pitchTypeTrim));
        }

        var rawPosts = await query
            .OrderBy(p => p.MatchDate)
            .ToListAsync();

        var posts = rawPosts
            .OrderBy(p => p.MatchDate)
            .ThenBy(p => p.StartTime)
            .ToList();

        var user = await _userManager.GetUserAsync(User);
        List<Team> managedTeams = new();
        if (user != null)
        {
            var teamIds = await _db.TeamMembers
                .Where(m => m.UserId == user.Id && (m.Role == TeamRole.Manager || m.Role == TeamRole.Owner))
                .Select(m => m.TeamId)
                .ToListAsync();

            managedTeams = await _db.Teams
                .Where(t => teamIds.Contains(t.Id) && !t.IsRestrictedFromMatchmaking)
                .ToListAsync();
        }

        ViewBag.ManagedTeams = managedTeams;
        ViewBag.SelectedArea = area;
        ViewBag.SelectedDate = date?.ToString("yyyy-MM-dd");
        ViewBag.SelectedPitchType = pitchType;
        return View(posts);
    }

    // 2. Chi tiết bài đăng tìm đối & danh sách đề nghị
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Details(Guid id)
    {
        var post = await _db.MatchmakingPosts
            .Include(p => p.Team)
            .Include(p => p.TargetTeam)
            .Include(p => p.Proposals).ThenInclude(pr => pr.ChallengingTeam)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (post == null) return NotFound();

        var user = await _userManager.GetUserAsync(User);
        bool isHostManager = false;
        bool isTargetManager = false;
        List<Team> managedTeams = new();

        if (post.PostType == MatchmakingPostType.DirectChallenge &&
            (user == null || !(await _auth.HasMemberAccessAsync(post.TeamId, user.Id) ||
              (post.TargetTeamId.HasValue && await _auth.HasMemberAccessAsync(post.TargetTeamId.Value, user.Id))))) return NotFound();

        if (user != null)
        {
            isHostManager = await _auth.HasManagerAccessAsync(post.TeamId, user.Id);
            if (post.TargetTeamId.HasValue)
            {
                isTargetManager = await _auth.HasManagerAccessAsync(post.TargetTeamId.Value, user.Id);
            }

            // Các đội người dùng đang làm Manager để có thể gửi đề nghị giao lưu
            var teamIds = await _db.TeamMembers
                .Where(m => m.UserId == user.Id && (m.Role == TeamRole.Manager || m.Role == TeamRole.Owner))
                .Select(m => m.TeamId)
                .ToListAsync();

            managedTeams = await _db.Teams
                .Where(t => teamIds.Contains(t.Id) && t.Id != post.TeamId && !t.IsRestrictedFromMatchmaking)
                .ToListAsync();
        }

        ViewBag.IsHostManager = isHostManager;
        ViewBag.IsTargetManager = isTargetManager;
        ViewBag.ManagedTeams = managedTeams;

        return View(post);
    }

    // 3. Đăng tin tìm đối mới (Chỉ Manager/Owner của đội không bị hạn chế)
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePost(Guid teamId, DateTime matchDate, TimeSpan startTime, TimeSpan endTime, string area, string? pitchName, string pitchType, string desiredLevel, string costSharing, string? notes, PitchArrangement pitchArrangement = PitchArrangement.Flexible)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasManagerAccessAsync(teamId, user.Id))
            return Forbid();

        var team = await _db.Teams.FindAsync(teamId);
        if (team == null) return NotFound();

        if (pitchArrangement == PitchArrangement.HomePitch && string.IsNullOrWhiteSpace(pitchName)) pitchName = team.HomePitch;
        if (!Enum.IsDefined(pitchArrangement) || matchDate.Date.Add(startTime) <= CompetitionRules.LocalNow || startTime < TimeSpan.Zero || endTime > TimeSpan.FromDays(1) || endTime <= startTime || string.IsNullOrWhiteSpace(area) ||
            (pitchArrangement == PitchArrangement.HomePitch && string.IsNullOrWhiteSpace(pitchName)))
        {
            TempData["Error"] = "Kiểm tra ngày giờ, khu vực và tên sân nhà. Giờ kết thúc phải sau giờ bắt đầu.";
            return RedirectToAction(nameof(Index));
        }

        if (team.IsRestrictedFromMatchmaking)
        {
            TempData["Error"] = $"Đội của bạn đang bị hạn chế bắt đối: {team.MatchmakingRestrictionReason}";
            return RedirectToAction(nameof(Index));
        }

        var post = new MatchmakingPost
        {
            TeamId = teamId,
            PitchArrangement = pitchArrangement,
            MatchDate = matchDate,
            StartTime = startTime,
            EndTime = endTime,
            Area = area.Trim(),
            PitchName = pitchName?.Trim(),
            PitchType = pitchType,
            DesiredLevel = desiredLevel,
            CostSharing = costSharing,
            Notes = notes,
            Status = MatchmakingStatus.Open
        };

        _db.MatchmakingPosts.Add(post);
        await _db.SaveChangesAsync();

        TempData["Success"] = "Đã đăng tin tìm đối thành công lên Sàn giao lưu!";
        return RedirectToAction(nameof(Details), new { id = post.Id });
    }

    // 4. Gửi đề nghị giao lưu (Thách đấu)
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendProposal(Guid postId, Guid challengingTeamId, string? message)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasManagerAccessAsync(challengingTeamId, user.Id))
            return Forbid();

        var team = await _db.Teams.FindAsync(challengingTeamId);
        if (team == null || team.IsRestrictedFromMatchmaking)
        {
            TempData["Error"] = "Đội của bạn đang bị hạn chế bắt đối hoặc không hợp lệ.";
            return RedirectToAction(nameof(Details), new { id = postId });
        }

        var post = await _db.MatchmakingPosts.FindAsync(postId);
        if (post == null || post.Status != MatchmakingStatus.Open)
        {
            TempData["Error"] = "Tin tìm đối này không còn mở.";
            return RedirectToAction(nameof(Index));
        }

        if (post.PostType != MatchmakingPostType.Public || post.TeamId == challengingTeamId ||
            await _db.MatchmakingProposals.AnyAsync(p => p.PostId == postId && p.ChallengingTeamId == challengingTeamId && p.Status == ProposalStatus.Pending))
            return BadRequest(new { message = "Đề nghị không hợp lệ hoặc đã được gửi." });

        var proposal = new MatchmakingProposal
        {
            PostId = postId,
            ChallengingTeamId = challengingTeamId,
            ProposedByUserId = user.Id,
            Message = message?.Trim(),
            Status = ProposalStatus.Pending
        };

        _db.MatchmakingProposals.Add(proposal);
        await _db.SaveChangesAsync();

        TempData["Success"] = "Đã gửi đề nghị giao lưu tới đội bạn thành công!";
        return RedirectToAction(nameof(Details), new { id = postId });
    }

    // 5. Chốt đối thủ qua Transaction bảo mật
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptProposal(Guid postId, Guid proposalId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var result = await _matchmaking.AcceptProposalAndLockMatchAsync(postId, proposalId, user.Id);

        if (result.Success)
        {
            TempData["Success"] = result.Message;
            return RedirectToAction("Detail", "Matches", new { id = result.MatchId });
        }
        else
        {
            TempData["Error"] = result.Message;
            return RedirectToAction(nameof(Details), new { id = postId });
        }
    }

    // 6. Mời giao hữu trực tiếp với 1 đội bóng từ trang Hồ sơ CLB
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDirectChallenge(
        Guid targetTeamId,
        string targetSlug,
        Guid challengingTeamId,
        DateTime matchDate,
        TimeSpan startTime,
        TimeSpan endTime,
        string area,
        string? pitchName,
        string pitchType,
        string costSharing,
        string? message,
        PitchArrangement pitchArrangement = PitchArrangement.Flexible)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasManagerAccessAsync(challengingTeamId, user.Id))
            return Forbid();

        var challenger = await _db.Teams.FindAsync(challengingTeamId);
        if (challenger == null || challenger.IsRestrictedFromMatchmaking)
        {
            TempData["Error"] = "Đội của bạn đang bị hạn chế bắt đối hoặc không hợp lệ.";
            return Redirect($"/doi/{targetSlug}");
        }

        var targetTeam = await _db.Teams.FindAsync(targetTeamId);
        if (targetTeam == null) return NotFound("Không tìm thấy đội bóng mục tiêu.");

        if (targetTeam.IsRestrictedFromMatchmaking)
        {
            TempData["Error"] = $"Đội {targetTeam.Name} hiện đang tạm ngưng bắt đối giao lưu.";
            return Redirect($"/doi/{targetSlug}");
        }

        if (challengingTeamId == targetTeamId)
        {
            TempData["Error"] = "Không thể gửi lời mời giao hữu cho chính đội của mình.";
            return Redirect($"/doi/{targetSlug}");
        }

        if (pitchArrangement == PitchArrangement.HomePitch && string.IsNullOrWhiteSpace(pitchName)) pitchName = challenger.HomePitch;
        if (!Enum.IsDefined(pitchArrangement) || matchDate.Date.Add(startTime) <= CompetitionRules.LocalNow || startTime < TimeSpan.Zero || endTime > TimeSpan.FromDays(1) || endTime <= startTime || string.IsNullOrWhiteSpace(area) ||
            (pitchArrangement == PitchArrangement.HomePitch && string.IsNullOrWhiteSpace(pitchName))) return BadRequest(new { message = "Kiểm tra ngày giờ, khu vực và sân thi đấu." });

        // Tạo bài đăng thách đấu trực tiếp (KHÔNG đưa lên sàn tìm đối công khai)
        var post = new MatchmakingPost
        {
            PostType = MatchmakingPostType.DirectChallenge,
            PitchArrangement = pitchArrangement,
            TeamId = challengingTeamId,
            TargetTeamId = targetTeamId,
            MatchDate = matchDate,
            StartTime = startTime,
            EndTime = endTime,
            Area = area.Trim(),
            PitchName = pitchName?.Trim(),
            PitchType = pitchType,
            DesiredLevel = "Giao hữu trực tiếp",
            CostSharing = costSharing,
            Notes = $"Lời mời giao hữu trực tiếp gửi đến {targetTeam.Name}: {message}".Trim(),
            Status = MatchmakingStatus.Open
        };

        _db.MatchmakingPosts.Add(post);

        // Tạo sẵn đề nghị thách đấu
        var proposal = new MatchmakingProposal
        {
            PostId = post.Id,
            ChallengingTeamId = targetTeamId, // Đội được mời (khi họ bấm chấp nhận sẽ thành trận đấu giữa 2 bên)
            ProposedByUserId = user.Id,
            Message = message?.Trim() ?? $"Lời mời giao lưu từ {challenger.Name}",
            Status = ProposalStatus.Pending
        };
        _db.MatchmakingProposals.Add(proposal);

        await _db.SaveChangesAsync();

        TempData["Success"] = $"Đã gửi lời mời giao hữu đích danh thành công tới đội {targetTeam.Name}!";
        return RedirectToAction(nameof(Details), new { id = post.Id });
    }
}
