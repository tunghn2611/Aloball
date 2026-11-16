using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Controllers;

[AllowAnonymous]
public class ShareCardController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITeamAuthorizationService _auth;
    private readonly IHttpClientFactory _httpClientFactory;

    public ShareCardController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ITeamAuthorizationService auth,
        IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _userManager = userManager;
        _auth = auth;
        _httpClientFactory = httpClientFactory;
    }

    // 1. GET /api/share-card/player/{id}
    [HttpGet("api/share-card/player/{id:guid}")]
    public async Task<IActionResult> GetPlayerCard(Guid id)
    {
        var card = await BuildPlayerCardDtoAsync(id);
        if (card == null) return NotFound(new { error = "Không tìm thấy cầu thủ hoặc hồ sơ đang để riêng tư." });
        return Ok(card);
    }

    // 2. GET /api/share-card/match/{id}
    [HttpGet("api/share-card/match/{id:guid}")]
    public async Task<IActionResult> GetMatchPoster(Guid id)
    {
        var poster = await BuildMatchPosterDtoAsync(id);
        if (poster == null) return BadRequest(new { error = "Trận đấu chưa có kết quả chính thức hoặc không tồn tại." });
        return Ok(poster);
    }

    // 3. GET /api/share-card/img-proxy?url=...
    [HttpGet("api/share-card/img-proxy")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> ProxyImage([FromQuery] string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return BadRequest("URL required");
        Response.Headers["Access-Control-Allow-Origin"] = "*";

        if (url.StartsWith("/") || url.StartsWith("~/"))
        {
            var localPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", url.TrimStart('~', '/').Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(localPath))
            {
                var ext = Path.GetExtension(localPath).ToLowerInvariant();
                var ctype = ext switch { ".png" => "image/png", ".webp" => "image/webp", ".svg" => "image/svg+xml", _ => "image/jpeg" };
                return PhysicalFile(localPath, ctype);
            }
            return NotFound();
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            return BadRequest("Invalid URL");
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var response = await client.GetAsync(uri);
            if (!response.IsSuccessStatusCode) return StatusCode((int)response.StatusCode);

            var ctype = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return File(bytes, ctype);
        }
        catch
        {
            return StatusCode(502);
        }
    }

    // 4. GET /share/player/{id} (OG Social Share Page)
    [HttpGet("share/player/{id:guid}")]
    public async Task<IActionResult> PlayerShare(Guid id)
    {
        var card = await BuildPlayerCardDtoAsync(id);
        if (card == null) return NotFound("Hồ sơ cầu thủ không tồn tại hoặc đã bị ẩn.");

        var req = HttpContext.Request;
        var baseUrl = $"{req.Scheme}://{req.Host}";
        var shareImg = !string.IsNullOrEmpty(card.PhotoUrl) 
            ? (card.PhotoUrl.StartsWith("http") ? card.PhotoUrl : $"{baseUrl}{card.PhotoUrl}") 
            : $"{baseUrl}/images/aloball-og-card.png";

        var vm = new PlayerShareViewModel
        {
            Card = card,
            OgTitle = $"{card.FullName} ({card.Stats.Ovr} OVR - {card.PosCode}) | AloBall FUT 27",
            OgDescription = $"Khám phá bộ chỉ số thi đấu FIFA của {card.FullName} tại CLB {card.TeamName} trên nền tảng AloBall.",
            OgImage = shareImg,
            OgUrl = $"{baseUrl}/share/player/{id}"
        };

        return View("~/Views/ShareCard/PlayerShare.cshtml", vm);
    }

    // 5. GET /share/match/{id} (OG Social Share Page)
    [HttpGet("share/match/{id:guid}")]
    public async Task<IActionResult> MatchShare(Guid id)
    {
        var poster = await BuildMatchPosterDtoAsync(id);
        if (poster == null) return NotFound("Trận đấu chưa có kết quả chính thức hoặc không tồn tại.");

        var req = HttpContext.Request;
        var baseUrl = $"{req.Scheme}://{req.Host}";
        var shareImg = !string.IsNullOrEmpty(poster.TeamA.CoverUrl)
            ? (poster.TeamA.CoverUrl.StartsWith("http") ? poster.TeamA.CoverUrl : $"{baseUrl}{poster.TeamA.CoverUrl}")
            : $"{baseUrl}/images/aloball-og-poster.png";

        var vm = new MatchShareViewModel
        {
            Poster = poster,
            OgTitle = $"Kết quả: {poster.TeamA.Name} {poster.ScoreA} - {poster.ScoreB} {poster.TeamB.Name} | AloBall",
            OgDescription = $"Trận đấu {poster.Category} tại {poster.Venue} ({poster.MatchDate}). Cầu thủ xuất sắc: {poster.Motm?.PlayerName ?? "N/A"}.",
            OgImage = shareImg,
            OgUrl = $"{baseUrl}/share/match/{id}"
        };

        return View("~/Views/ShareCard/MatchShare.cshtml", vm);
    }

    #region Helper Calculation Methods

    private async Task<PlayerCardDto?> BuildPlayerCardDtoAsync(Guid id)
    {
        var player = await _db.Players
            .Include(p => p.Team)
            .Include(p => p.LinkedUser)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (player == null) return null;

        var currentUser = User != null ? await _userManager.GetUserAsync(User) : null;
        string? currentUserId = currentUser?.Id;
        bool isSelf = currentUserId != null && player.LinkedUserId == currentUserId;
        bool isManager = currentUserId != null && await _auth.HasManagerAccessAsync(player.TeamId, currentUserId);
        bool isPlatformAdmin = currentUserId != null && await _auth.IsPlatformAdminAsync(currentUserId);

        if (!player.IsPublicProfile && !isSelf && !isManager && !isPlatformAdmin) return null;

        var matches = await _db.Matches
            .Where(m => (m.TeamAId == player.TeamId || m.TeamBId == player.TeamId)
                && m.Status == MatchStatus.Completed
                && m.ConfirmedByTeamA
                && (m.ConfirmedByTeamB || !m.TeamBId.HasValue)
                && m.ScoreA.HasValue && m.ScoreB.HasValue)
            .ToListAsync();

        var matchIds = matches.Select(m => m.Id).ToHashSet();
        var lineups = await _db.MatchPlayerLineups
            .Where(l => l.PlayerId == player.Id && matchIds.Contains(l.MatchId))
            .ToListAsync();

        int appearances = lineups.Count(l => l.HasActuallyPlayed || l.IsStarting);

        var goalEvents = await _db.MatchGoalEvents
            .Where(g => matchIds.Contains(g.MatchId) && (g.ScorerPlayerId == player.Id || g.AssistPlayerId == player.Id))
            .ToListAsync();

        int goalsCount = goalEvents.Count(g => g.ScorerPlayerId == player.Id);
        int assistsCount = goalEvents.Count(g => g.AssistPlayerId == player.Id);

        int winsCount = 0;
        foreach (var l in lineups.Where(x => x.HasActuallyPlayed || x.IsStarting))
        {
            var m = matches.FirstOrDefault(x => x.Id == l.MatchId);
            if (m == null || !m.ScoreA.HasValue || !m.ScoreB.HasValue) continue;
            if (m.Category == MatchCategory.Internal)
            {
                if (l.TeamSide == "A" && m.ScoreA > m.ScoreB) winsCount++;
                else if (l.TeamSide == "B" && m.ScoreB > m.ScoreA) winsCount++;
            }
            else
            {
                if (m.TeamAId == player.TeamId && m.ScoreA > m.ScoreB) winsCount++;
                else if (m.TeamBId == player.TeamId && m.ScoreB > m.ScoreA) winsCount++;
            }
        }
        double winRate = appearances > 0 ? (double)winsCount / appearances : 0.5;

        var attendances = await _db.AttendanceRecords
            .Where(r => r.PlayerId == player.Id && r.Actual != ActualStatus.NotChecked)
            .ToListAsync();
        int totalAtt = attendances.Count;
        int presentAtt = attendances.Count(r => r.Actual == ActualStatus.Present || r.IsExempt);
        double attRate = totalAtt > 0 ? (double)presentAtt / totalAtt : 0.8;

        var recentAward = await _db.MonthlyFireAwards
            .AnyAsync(a => a.PlayerId == player.Id && a.DecidedAt >= DateTime.UtcNow.AddDays(-60));
        var totalAbPoints = await _db.PlayerAbJournalEntries
            .Where(e => e.PlayerId == player.Id && e.ReversedEntryId == null)
            .SumAsync(e => (int?)e.Points) ?? 0;
        bool isMotm = recentAward || totalAbPoints >= 30;

        string rawPos = (player.Position ?? player.StandardPrimaryPosition ?? "Tiền đạo").Trim();
        string pUpper = rawPos.ToUpperInvariant();
        bool isGk = pUpper.Contains("GK") || pUpper.Contains("THỦ MÔN");
        bool isDef = pUpper.Contains("CB") || pUpper.Contains("LB") || pUpper.Contains("RB") || pUpper.Contains("HẬU VỆ") || pUpper.Contains("DF");
        bool isMid = pUpper.Contains("CAM") || pUpper.Contains("CM") || pUpper.Contains("CDM") || pUpper.Contains("TIỀN VỆ") || pUpper.Contains("MF");
        bool isFwd = !isGk && !isDef && !isMid;

        string posCode = isGk ? "GK" : isDef ? (pUpper.Contains("CB") ? "CB" : pUpper.Contains("LB") ? "LB" : "RB") : isMid ? (pUpper.Contains("CAM") ? "CAM" : pUpper.Contains("CDM") ? "CDM" : "CM") : "ST";

        int att = Math.Clamp(60 + (goalsCount * 4) + (isFwd ? 10 : isMid ? 5 : 0), 55, 99);
        int cre = Math.Clamp(60 + (assistsCount * 4) + (isMid ? 10 : isFwd ? 4 : 0), 55, 99);
        int sta = Math.Clamp((int)Math.Round(55 + (attRate * 35) + Math.Min(9, presentAtt)), 55, 99);
        int win = Math.Clamp((int)Math.Round(58 + (winRate * 36) + Math.Min(5, winsCount)), 55, 99);
        double teamRating = player.Team?.AverageRating ?? 5.0;
        int rep = Math.Clamp((int)Math.Round(55 + (teamRating / 5.0) * 44), 55, 99);
        int expBase = player.Experience?.Contains("5") == true || player.Experience?.Contains("Phủi cứng") == true ? 82 :
                      player.Experience?.Contains("3") == true ? 75 :
                      player.Experience?.Contains("1") == true ? 68 : 62;
        int exp = Math.Clamp(expBase + Math.Min(15, appearances * 2), 55, 99);

        int ovr;
        if (isFwd) ovr = (int)Math.Round(att * 0.35 + cre * 0.20 + win * 0.15 + sta * 0.10 + exp * 0.10 + rep * 0.10);
        else if (isMid) ovr = (int)Math.Round(cre * 0.35 + att * 0.20 + sta * 0.15 + win * 0.15 + exp * 0.08 + rep * 0.07);
        else if (isDef) ovr = (int)Math.Round(sta * 0.25 + win * 0.25 + exp * 0.20 + rep * 0.15 + cre * 0.10 + att * 0.05);
        else if (isGk) ovr = (int)Math.Round(win * 0.30 + rep * 0.25 + exp * 0.20 + sta * 0.15 + cre * 0.10);
        else ovr = (int)Math.Round(att * 0.25 + cre * 0.20 + sta * 0.20 + win * 0.15 + rep * 0.10 + exp * 0.10);
        ovr = Math.Clamp(ovr, 55, 99);

        string tier = isMotm ? "motm" : ovr >= 80 ? "gold" : ovr >= 70 ? "silver" : "bronze";

        return new PlayerCardDto
        {
            Id = player.Id,
            FullName = player.FullName,
            DisplayName = !string.IsNullOrWhiteSpace(player.ShirtName) ? player.ShirtName : player.FullName,
            KitNumber = player.KitNumber,
            PosCode = posCode,
            Position = rawPos,
            PreferredFoot = player.PreferredFoot ?? "Phải",
            Region = player.Region ?? player.Team?.ActivityArea ?? "Hà Nội",
            Experience = player.Experience ?? "Phong trào",
            PhotoUrl = player.PhotoUrl ?? player.LinkedUser?.AvatarUrl,
            TeamName = player.Team?.Name ?? "AloBall FC",
            TeamSlug = player.Team?.Slug,
            TeamLogoUrl = player.Team?.LogoUrl,
            Tier = tier,
            IsMotm = isMotm,
            Appearances = appearances,
            Goals = goalsCount,
            Assists = assistsCount,
            Stats = new PlayerCardStatsDto
            {
                Att = att,
                Cre = cre,
                Sta = sta,
                Win = win,
                Rep = rep,
                Exp = exp,
                Ovr = ovr
            }
        };
    }

    private async Task<MatchPosterDto?> BuildMatchPosterDtoAsync(Guid id)
    {
        var match = await _db.Matches
            .Include(m => m.TeamA)
            .Include(m => m.TeamB)
            .Include(m => m.GoalEvents).ThenInclude(g => g.Scorer)
            .Include(m => m.GoalEvents).ThenInclude(g => g.Assist)
            .Include(m => m.Lineups).ThenInclude(l => l.Player)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (match == null) return null;
        if (match.Status != MatchStatus.Completed || !match.ConfirmedByTeamA || (match.TeamBId.HasValue && !match.ConfirmedByTeamB))
        {
            return null;
        }

        var teamAName = match.Category == MatchCategory.Internal ? $"{match.TeamA.Name} (Bên A)" : match.TeamA.Name;
        var teamBName = match.Category == MatchCategory.Internal ? $"{match.TeamA.Name} (Bên B)" : (match.TeamB?.Name ?? match.TeamBExternalName ?? "Đối thủ ngoài");

        var teamA = new MatchPosterTeamDto
        {
            Id = match.TeamAId,
            Name = teamAName,
            LogoUrl = match.TeamA.LogoUrl,
            CoverUrl = match.TeamA.CoverImageUrl,
            CoverOpacity = match.TeamA.CoverOpacity ?? 0.7
        };

        var teamB = new MatchPosterTeamDto
        {
            Id = match.TeamBId,
            Name = teamBName,
            LogoUrl = match.TeamB?.LogoUrl ?? match.TeamA.LogoUrl,
            CoverUrl = match.TeamB?.CoverImageUrl ?? match.TeamA.CoverImageUrl,
            CoverOpacity = match.TeamB?.CoverOpacity ?? 0.7
        };

        var goalEvents = match.GoalEvents.OrderBy(g => g.Minute ?? 0).ToList();
        var scorersA = goalEvents
            .Where(g => g.TeamId == match.TeamAId)
            .GroupBy(g => g.ScorerPlayerId)
            .Select(grp => new MatchPosterScorerDto
            {
                PlayerName = grp.First().Scorer?.FullName ?? "Cầu thủ ẩn",
                Goals = grp.Count(),
                Minutes = string.Join("', ", grp.Where(x => x.Minute.HasValue).Select(x => x.Minute.ToString())) + (grp.Any(x => x.Minute.HasValue) ? "'" : ""),
                AssistName = grp.FirstOrDefault(x => x.Assist != null)?.Assist?.FullName
            }).ToList();

        var scorersB = goalEvents
            .Where(g => g.TeamId != match.TeamAId)
            .GroupBy(g => g.ScorerPlayerId)
            .Select(grp => new MatchPosterScorerDto
            {
                PlayerName = grp.First().Scorer?.FullName ?? "Cầu thủ ẩn",
                Goals = grp.Count(),
                Minutes = string.Join("', ", grp.Where(x => x.Minute.HasValue).Select(x => x.Minute.ToString())) + (grp.Any(x => x.Minute.HasValue) ? "'" : ""),
                AssistName = grp.FirstOrDefault(x => x.Assist != null)?.Assist?.FullName
            }).ToList();

        // Calculate MOTM
        MatchPosterMotmDto? motm = null;
        var playerContributions = new Dictionary<Guid, (Player player, int points, int goals, int assists, string teamName)>();
        foreach (var g in goalEvents)
        {
            if (g.Scorer != null)
            {
                if (!playerContributions.ContainsKey(g.ScorerPlayerId))
                    playerContributions[g.ScorerPlayerId] = (g.Scorer, 0, 0, 0, g.TeamId == match.TeamAId ? teamAName : teamBName);
                var c = playerContributions[g.ScorerPlayerId];
                playerContributions[g.ScorerPlayerId] = (c.player, c.points + 3, c.goals + 1, c.assists, c.teamName);
            }
            if (g.Assist != null && g.AssistPlayerId.HasValue)
            {
                if (!playerContributions.ContainsKey(g.AssistPlayerId.Value))
                    playerContributions[g.AssistPlayerId.Value] = (g.Assist, 0, 0, 0, g.TeamId == match.TeamAId ? teamAName : teamBName);
                var c = playerContributions[g.AssistPlayerId.Value];
                playerContributions[g.AssistPlayerId.Value] = (c.player, c.points + 2, c.goals, c.assists + 1, c.teamName);
            }
        }

        if (playerContributions.Any())
        {
            var best = playerContributions.Values.OrderByDescending(x => x.points).ThenByDescending(x => x.goals).First();
            motm = new MatchPosterMotmDto
            {
                PlayerId = best.player.Id,
                PlayerName = best.player.FullName,
                TeamName = best.teamName,
                PhotoUrl = best.player.PhotoUrl,
                Highlight = $"{(best.goals > 0 ? $"{best.goals} Bàn thắng " : "")}{(best.assists > 0 ? $"· {best.assists} Kiến tạo" : "")}".Trim(' ', '·')
            };
        }
        else if (match.Lineups.Any())
        {
            var p = match.Lineups.First().Player;
            motm = new MatchPosterMotmDto
            {
                PlayerId = p.Id,
                PlayerName = p.FullName,
                TeamName = teamAName,
                PhotoUrl = p.PhotoUrl,
                Highlight = "Cầu thủ thi đấu xuất sắc"
            };
        }

        return new MatchPosterDto
        {
            Id = match.Id,
            MatchDate = match.MatchDateTime.ToString("HH:mm · dd/MM/yyyy"),
            Venue = string.IsNullOrEmpty(match.PitchName) ? "Sân bóng đá phong trào" : match.PitchName,
            PitchType = match.PitchType ?? "Sân 7",
            Category = match.Category switch
            {
                MatchCategory.Internal => "Trận đấu Nội bộ CLB",
                MatchCategory.Tournament => "Trận đấu Giải",
                _ => "Giao hữu đối ngoại"
            },
            TeamA = teamA,
            TeamB = teamB,
            ScoreA = match.ScoreA ?? 0,
            ScoreB = match.ScoreB ?? 0,
            ScorersA = scorersA,
            ScorersB = scorersB,
            Motm = motm,
            BrandTag = "aloball.vercel.app"
        };
    }

    #endregion
}
