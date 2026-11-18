using System.Net;
using System.Net.Sockets;
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
    private const int MaxProxyImageBytes = 5 * 1024 * 1024; // 5 MB
    private static readonly HashSet<string> AllowedLocalImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".svg"
    };

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
        if (string.IsNullOrWhiteSpace(url)) return BadRequest("URL required.");

        string trimmed = url.Trim();

        // Safe relative path inside wwwroot (blocks path traversal & non-image files)
        if (trimmed.StartsWith('/') || trimmed.StartsWith("~/"))
        {
            if (trimmed.StartsWith("//") || trimmed.StartsWith("/\\") || trimmed.Contains(".."))
                return BadRequest("Invalid relative path.");

            var wwwroot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"));
            var relative = trimmed.TrimStart('~', '/').Replace('/', Path.DirectorySeparatorChar);
            var localFull = Path.GetFullPath(Path.Combine(wwwroot, relative));
            if (!localFull.StartsWith(wwwroot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return BadRequest("Invalid relative path.");

            var ext = Path.GetExtension(localFull);
            if (!AllowedLocalImageExtensions.Contains(ext))
                return BadRequest("Unsupported file type.");

            if (!System.IO.File.Exists(localFull))
                return NotFound();

            var localCtype = ext.ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                _ => "image/jpeg"
            };
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            return PhysicalFile(localFull, localCtype);
        }

        var validation = await ValidateExternalImageUriAsync(trimmed);
        if (!validation.IsValid || validation.Uri == null)
        {
            return BadRequest(validation.Error ?? "Blocked or invalid image URL.");
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            using var response = await client.GetAsync(validation.Uri, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode);

            // Re-validate final URI in case an HTTP redirect occurred
            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri != null && !Uri.Equals(finalUri, validation.Uri))
            {
                var redirectCheck = await ValidateExternalImageUriAsync(finalUri.AbsoluteUri);
                if (!redirectCheck.IsValid)
                    return BadRequest("Redirect to restricted host is not allowed.");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Target resource is not an image.");

            if (response.Content.Headers.ContentLength > MaxProxyImageBytes)
                return BadRequest("Image exceeds maximum allowed size.");

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length > MaxProxyImageBytes)
                return BadRequest("Image exceeds maximum allowed size.");

            Response.Headers["Access-Control-Allow-Origin"] = "*";
            return File(bytes, contentType);
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
        var shareImg = ResolveAbsoluteShareImage(card.PhotoUrl ?? card.TeamLogoUrl, baseUrl, "/images/aloball-og-card.png");

        var vm = new PlayerShareViewModel
        {
            Card = card,
            OgTitle = $"{card.FullName} ({card.Stats.Ovr} OVR - {card.PosCode}) | AloBall EA FC 27",
            OgDescription = $"Khám phá thẻ chỉ số EA FC 27 ({card.Stats.Ovr} OVR) của {card.FullName} tại CLB {card.TeamName} trên AloBall.",
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
        var shareImg = ResolveAbsoluteShareImage(poster.TeamA.CoverUrl ?? poster.TeamA.LogoUrl, baseUrl, "/images/aloball-og-poster.png");

        var vm = new MatchShareViewModel
        {
            Poster = poster,
            OgTitle = $"Kết quả: {poster.TeamA.Name} {poster.ScoreA} - {poster.ScoreB} {poster.TeamB.Name} | AloBall",
            OgDescription = $"Trận đấu {poster.Category} tại {poster.Venue} ({poster.MatchDate}). Cầu thủ xuất sắc: {poster.Motm?.PlayerName ?? "Đang cập nhật"}.",
            OgImage = shareImg,
            OgUrl = $"{baseUrl}/share/match/{id}"
        };

        return View("~/Views/ShareCard/MatchShare.cshtml", vm);
    }

    #region Helper & Security Methods

    public static string CalculateCardTier(int ovr, bool isMotm) =>
        isMotm ? "motm" : ovr >= 80 ? "gold" : ovr >= 70 ? "silver" : "bronze";

    public static string ToProxyAwareImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        var trimmed = url.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return $"/api/share-card/img-proxy?url={Uri.EscapeDataString(trimmed)}";
        }
        return trimmed;
    }

    public static async Task<(bool IsValid, Uri? Uri, string? Error)> ValidateExternalImageUriAsync(
        string? rawUrl,
        Func<string, Task<IPAddress[]>>? dnsResolver = null)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
            return (false, null, "URL required.");

        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
            return (false, null, "Invalid URL format.");

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return (false, null, "Only HTTP and HTTPS protocols are permitted.");

        if (!string.IsNullOrEmpty(uri.UserInfo))
            return (false, null, "Credentials in URL are not permitted.");

        string host = uri.DnsSafeHost.Trim('[', ']').TrimEnd('.');
        if (string.IsNullOrWhiteSpace(host))
            return (false, null, "Missing host.");

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            ulong.TryParse(host, out _))
        {
            return (false, null, "Internal or localhost hosts are blocked.");
        }

        if (IPAddress.TryParse(host, out var literalIp))
        {
            if (IsBlockedIpAddress(literalIp))
                return (false, null, "Private, loopback, or link-local IP addresses are blocked.");
            return (true, uri, null);
        }

        try
        {
            var resolve = dnsResolver ?? (h => Dns.GetHostAddressesAsync(h));
            var addresses = await resolve(host);
            if (addresses == null || addresses.Length == 0)
                return (false, null, "Unable to resolve remote host.");

            if (addresses.Any(IsBlockedIpAddress))
                return (false, null, "Resolved IP belongs to a private, loopback, or restricted range.");

            return (true, uri, null);
        }
        catch
        {
            return (false, null, "DNS resolution failed.");
        }
    }

    public static bool IsBlockedIpAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
            return true;

        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] b = ip.GetAddressBytes();
            if (b[0] == 0) return true;                                  // 0.0.0.0/8
            if (b[0] == 10) return true;                                 // 10.0.0.0/8 (RFC1918)
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true;   // 100.64.0.0/10 (CGNAT)
            if (b[0] == 127) return true;                                // 127.0.0.0/8 (Loopback)
            if (b[0] == 169 && b[1] == 254) return true;                 // 169.254.0.0/16 (Link-local / Cloud Metadata)
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;    // 172.16.0.0/12 (RFC1918)
            if (b[0] == 192 && b[1] == 0 && (b[2] == 0 || b[2] == 2)) return true;
            if (b[0] == 192 && b[1] == 168) return true;                 // 192.168.0.0/16 (RFC1918)
            if (b[0] == 198 && (b[1] == 18 || b[1] == 19)) return true;  // 198.18.0.0/15
            if (b[0] >= 224) return true;                                // Multicast & Reserved
            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || ip.IsIPv6Teredo)
                return true;

            byte[] b = ip.GetAddressBytes();
            // fc00::/7 Unique Local IPv6 (fc00..fdff)
            if ((b[0] & 0xFE) == 0xFC) return true;
        }

        return false;
    }

    private static string ResolveAbsoluteShareImage(string? candidateUrl, string baseUrl, string defaultPath)
    {
        if (string.IsNullOrWhiteSpace(candidateUrl)) return $"{baseUrl}{defaultPath}";
        if (candidateUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            candidateUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return candidateUrl;
        if (candidateUrl.StartsWith("/"))
            return $"{baseUrl}{candidateUrl}";
        return $"{baseUrl}/{candidateUrl.TrimStart('~', '/')}";
    }

    private async Task<PlayerCardDto?> BuildPlayerCardDtoAsync(Guid id)
    {
        var player = await _db.Players
            .Include(p => p.Team)
            .Include(p => p.LinkedUser)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (player == null) return null;

        var currentUser = (User?.Identity?.IsAuthenticated == true) ? await _userManager.GetUserAsync(User) : null;
        string? currentUserId = currentUser?.Id;
        bool isSelf = currentUserId != null && player.LinkedUserId == currentUserId;
        bool isManager = currentUserId != null && await _auth.HasManagerAccessAsync(player.TeamId, currentUserId);
        bool isPlatformAdmin = currentUserId != null && await _auth.IsPlatformAdminAsync(currentUserId);
        bool canSeePrivateFields = isSelf || isManager || isPlatformAdmin;

        if (!player.IsPublicProfile && !canSeePrivateFields) return null;

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

        // MOTM eligibility: recent MonthlyFireAward OR top weekly club AB Coin OR >= 30 AB Coin total
        var recentAward = await _db.MonthlyFireAwards
            .AnyAsync(a => a.PlayerId == player.Id && a.DecidedAt >= DateTime.UtcNow.AddDays(-60));

        var totalAbPoints = await _db.PlayerAbJournalEntries
            .Where(e => e.PlayerId == player.Id && e.ReversedEntryId == null)
            .SumAsync(e => (int?)e.Points) ?? 0;

        var weekAgoLocal = CompetitionRules.LocalNow.AddDays(-7);
        var weekAgoUtc = DateTime.UtcNow.AddDays(-7);
        var teamWeeklyTotals = await _db.PlayerAbJournalEntries
            .Where(e => e.TeamId == player.TeamId && e.ReversedEntryId == null &&
                        (e.EventDate >= weekAgoLocal || e.CreatedAt >= weekAgoUtc))
            .GroupBy(e => e.PlayerId)
            .Select(g => new { PlayerId = g.Key, WeeklyPoints = g.Sum(x => x.Points) })
            .ToListAsync();

        int maxWeeklyAb = teamWeeklyTotals.Count > 0 ? teamWeeklyTotals.Max(x => x.WeeklyPoints) : 0;
        bool isTopWeeklyAb = maxWeeklyAb > 0 && teamWeeklyTotals.Any(x => x.PlayerId == player.Id && x.WeeklyPoints == maxWeeklyAb);
        bool isMotm = recentAward || isTopWeeklyAb || totalAbPoints >= 30;

        string rawPos = (player.Position ?? player.StandardPrimaryPosition ?? "Tiền đạo").Trim();
        string pUpper = rawPos.ToUpperInvariant();
        bool isGk = pUpper.Contains("GK") || pUpper.Contains("THỦ MÔN");
        bool isDef = pUpper.Contains("CB") || pUpper.Contains("LB") || pUpper.Contains("RB") || pUpper.Contains("HẬU VỆ") || pUpper.Contains("DF");
        bool isMid = pUpper.Contains("CAM") || pUpper.Contains("CM") || pUpper.Contains("CDM") || pUpper.Contains("TIỀN VỆ") || pUpper.Contains("MF");
        bool isFwd = !isGk && !isDef && !isMid;

        string posCode = isGk ? "GK" : isDef ? (pUpper.Contains("CB") ? "CB" : pUpper.Contains("LB") ? "LB" : "RB") : isMid ? (pUpper.Contains("CAM") ? "CAM" : pUpper.Contains("CDM") ? "CDM" : "CM") : "ST";
        if (!canSeePrivateFields && !player.PublicPosition)
        {
            rawPos = "Cầu thủ";
        }

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

        string tier = CalculateCardTier(ovr, isMotm);

        var rawPlayStyles = player.PlayStyles ?? player.LinkedUser?.PlayStyles;
        var parsedPlayStyles = PlayStyleCatalog.Parse(rawPlayStyles);
        var playStyleItems = parsedPlayStyles.Select(ps => new PlayerCardPlayStyleDto
        {
            Code = ps.Definition.Code,
            NameVi = ps.Definition.NameVi,
            NameEn = ps.Definition.NameEn,
            SvgPath = ps.Definition.SvgPath,
            IsPlus = ps.IsPlus
        }).ToList();

        return new PlayerCardDto
        {
            Id = player.Id,
            FullName = player.FullName,
            DisplayName = !string.IsNullOrWhiteSpace(player.ShirtName) ? player.ShirtName : player.FullName,
            KitNumber = (canSeePrivateFields || player.PublicKitNumber) ? player.KitNumber : null,
            PosCode = posCode,
            Position = rawPos,
            PreferredFoot = (canSeePrivateFields || player.PublicPreferredFoot) ? (player.PreferredFoot ?? "Phải") : null,
            Region = player.Region ?? player.Team?.ActivityArea ?? "Hà Nội",
            Experience = (canSeePrivateFields || player.PublicExperience) ? (player.Experience ?? "Phong trào") : null,
            PhotoUrl = (canSeePrivateFields || player.PublicPhoto) ? (player.PhotoUrl ?? player.LinkedUser?.AvatarUrl) : null,
            PlayStyles = PlayStyleCatalog.NormalizeRaw(rawPlayStyles),
            PlayStyleItems = playStyleItems,
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
        if (match.Status != MatchStatus.Completed
            || !match.ConfirmedByTeamA
            || (match.TeamBId.HasValue && !match.ConfirmedByTeamB)
            || !match.ScoreA.HasValue
            || !match.ScoreB.HasValue)
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
            CoverOpacity = Math.Clamp(match.TeamA.CoverOpacity ?? 0.7, 0.1, 1.0)
        };

        var teamB = new MatchPosterTeamDto
        {
            Id = match.TeamBId,
            Name = teamBName,
            LogoUrl = match.TeamB?.LogoUrl ?? match.TeamA.LogoUrl,
            CoverUrl = match.TeamB?.CoverImageUrl ?? match.TeamA.CoverImageUrl,
            CoverOpacity = Math.Clamp(match.TeamB?.CoverOpacity ?? match.TeamA.CoverOpacity ?? 0.7, 0.1, 1.0)
        };

        var goalEvents = match.GoalEvents.OrderBy(g => g.Minute ?? 0).ToList();
        var internalSideByPlayer = match.Lineups
            .GroupBy(l => l.PlayerId)
            .ToDictionary(g => g.Key, g => g.First().TeamSide ?? "A");

        bool IsSideAEvent(MatchGoalEvent ev)
        {
            if (match.Category == MatchCategory.Internal)
            {
                return !internalSideByPlayer.TryGetValue(ev.ScorerPlayerId, out var side) || side != "B";
            }
            return ev.TeamId == match.TeamAId;
        }

        var sideAEvents = goalEvents.Where(IsSideAEvent).ToList();
        var sideBEvents = goalEvents.Where(ev => !IsSideAEvent(ev)).ToList();

        var scorersA = BuildGroupedContributors(sideAEvents);
        var scorersB = BuildGroupedContributors(sideBEvents);

        // Calculate MOTM (Goals * 3 + Assists * 2)
        MatchPosterMotmDto? motm = null;
        var playerContributions = new Dictionary<Guid, (Player player, int points, int goals, int assists, string teamName)>();
        foreach (var g in goalEvents)
        {
            string sideTeamName = IsSideAEvent(g) ? teamAName : teamBName;
            if (g.Scorer != null)
            {
                if (!playerContributions.ContainsKey(g.ScorerPlayerId))
                    playerContributions[g.ScorerPlayerId] = (g.Scorer, 0, 0, 0, sideTeamName);
                var c = playerContributions[g.ScorerPlayerId];
                playerContributions[g.ScorerPlayerId] = (c.player, c.points + 3, c.goals + 1, c.assists, c.teamName);
            }
            if (g.Assist != null && g.AssistPlayerId.HasValue)
            {
                if (!playerContributions.ContainsKey(g.AssistPlayerId.Value))
                    playerContributions[g.AssistPlayerId.Value] = (g.Assist, 0, 0, 0, sideTeamName);
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
                PhotoUrl = best.player.PublicPhoto ? best.player.PhotoUrl : null,
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
                PhotoUrl = p.PublicPhoto ? p.PhotoUrl : null,
                Highlight = "Cầu thủ thi đấu xuất sắc"
            };
        }

        var localKickoff = CompetitionRules.ToLocal(match.MatchDateTime);

        return new MatchPosterDto
        {
            Id = match.Id,
            MatchDate = localKickoff.ToString("HH:mm · dd/MM/yyyy"),
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

    private static List<MatchPosterScorerDto> BuildGroupedContributors(List<MatchGoalEvent> events)
    {
        var map = new Dictionary<Guid, (string Name, int Goals, int Assists, List<int> Minutes, HashSet<string> Assisters)>();

        foreach (var ev in events)
        {
            var scorerId = ev.ScorerPlayerId;
            var scorerName = ev.Scorer?.FullName ?? "Cầu thủ ẩn";
            if (!map.TryGetValue(scorerId, out var sc))
                sc = (scorerName, 0, 0, new List<int>(), new HashSet<string>());

            sc.Goals++;
            if (ev.Minute.HasValue) sc.Minutes.Add(ev.Minute.Value);
            if (ev.Assist != null && !string.IsNullOrWhiteSpace(ev.Assist.FullName))
                sc.Assisters.Add(ev.Assist.FullName);
            map[scorerId] = sc;

            if (ev.AssistPlayerId.HasValue)
            {
                var assistId = ev.AssistPlayerId.Value;
                var assistName = ev.Assist?.FullName ?? "Đồng đội";
                if (!map.TryGetValue(assistId, out var ast))
                    ast = (assistName, 0, 0, new List<int>(), new HashSet<string>());
                ast.Assists++;
                map[assistId] = ast;
            }
        }

        return map.Values
            .OrderByDescending(v => v.Goals)
            .ThenByDescending(v => v.Assists)
            .ThenBy(v => v.Name)
            .Select(v =>
            {
                var parts = new List<string>();
                if (v.Goals > 0) parts.Add($"{v.Goals}⚽");
                if (v.Assists > 0) parts.Add($"{v.Assists}👟");
                string summary = parts.Count > 0 ? $"{v.Name} ({string.Join(", ", parts)})" : v.Name;
                string? minutesStr = v.Minutes.Count > 0
                    ? string.Join("', ", v.Minutes.OrderBy(m => m)) + "'"
                    : null;

                return new MatchPosterScorerDto
                {
                    PlayerName = v.Name,
                    Goals = v.Goals,
                    Assists = v.Assists,
                    Minutes = minutesStr,
                    AssistName = v.Assisters.FirstOrDefault(),
                    SummaryText = summary
                };
            })
            .ToList();
    }

    #endregion
}
