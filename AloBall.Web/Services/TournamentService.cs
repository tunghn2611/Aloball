using System.Security.Cryptography;
using System.Text;
using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Services;

public interface ITournamentService
{
    Task<TournamentIndexViewModel> BuildIndexAsync(string? region, TournamentStatus? status, string? currentUserId);
    Task<TournamentDetailsViewModel?> BuildDetailsAsync(Guid tournamentId, string? currentUserId, string? activeTab, string baseAppUrl);
    Task<(bool Success, string Message, Tournament? Tournament)> CreateTournamentAsync(CreateTournamentInput input, string organizerUserId);
    Task<(bool Success, string Message)> AddTeamByOrganizerAsync(Guid tournamentId, Guid teamId, string? groupName, string organizerUserId);
    Task<(bool Success, string Message, Guid? TournamentId)> JoinByInviteCodeAsync(string inviteCode, Guid teamId, string captainUserId);
    Task<(bool Success, string Message)> RemoveTeamAsync(Guid tournamentId, Guid teamId, string organizerUserId);
    Task<(bool Success, string Message, List<MatchGame> GeneratedMatches)> GenerateFixturesAsync(Guid tournamentId, string organizerUserId, TimeSpan? defaultKickoffTime = null);
    Task<(bool Success, string Message, List<MatchGame> GeneratedMatches)> GenerateNextRoundAsync(Guid tournamentId, string organizerUserId);
    Task RecalculateStandingsAsync(Guid tournamentId);
    Task SyncTournamentForMatchAsync(Guid matchId);
    Task<(bool Success, string Message)> CompleteTournamentAsync(Guid tournamentId, string organizerUserId);
}

public class TournamentService : ITournamentService
{
    private readonly ApplicationDbContext _db;
    private readonly ITeamAuthorizationService _auth;

    public TournamentService(ApplicationDbContext db, ITeamAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public static string GetFormatLabel(TournamentFormat format) => format switch
    {
        TournamentFormat.KnockoutCup => "Loại trực tiếp (Cup)",
        TournamentFormat.GroupPlusKnockout => "Chia bảng + Loại trực tiếp",
        _ => "Vòng tròn tính điểm (League)"
    };

    public static string GetStatusLabel(TournamentStatus status) => status switch
    {
        TournamentStatus.Ongoing => "Đang thi đấu",
        TournamentStatus.Completed => "Đã kết thúc",
        _ => "Đang mở đăng ký"
    };

    public async Task<TournamentIndexViewModel> BuildIndexAsync(string? region, TournamentStatus? status, string? currentUserId)
    {
        string? cleanRegion = string.IsNullOrWhiteSpace(region) ? null : region.Trim();

        var query = _db.Tournaments
            .AsNoTracking()
            .Include(t => t.OrganizerUser)
            .Include(t => t.Teams)
            .Include(t => t.Matches)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        var allTournaments = await query
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        var regions = await _db.Tournaments
            .AsNoTracking()
            .Where(t => !string.IsNullOrWhiteSpace(t.Region))
            .Select(t => t.Region.Trim())
            .Distinct()
            .OrderBy(r => r)
            .ToListAsync();

        var filtered = string.IsNullOrEmpty(cleanRegion)
            ? allTournaments
            : allTournaments.Where(t => MatchConciergeService.RegionsMatch(t.Region, cleanRegion)).ToList();

        var cards = filtered.Select(t => new TournamentCardDto
        {
            Id = t.Id,
            Name = t.Name,
            Region = t.Region,
            PitchVenue = t.PitchVenue,
            Format = t.Format,
            FormatLabel = GetFormatLabel(t.Format),
            Status = t.Status,
            StatusLabel = GetStatusLabel(t.Status),
            StartDate = t.StartDate,
            EntryFeeVnd = t.EntryFeeVnd,
            InviteCode = t.InviteCode,
            OrganizerName = t.OrganizerUser?.FullName ?? "BTC AloBall",
            IsOrganizer = !string.IsNullOrEmpty(currentUserId) && t.OrganizerUserId == currentUserId,
            TeamCount = t.Teams.Count,
            MatchCount = t.Matches.Count,
            CompletedMatchCount = t.Matches.Count(m => m.Status == MatchStatus.Completed && m.ConfirmedByTeamA && m.ConfirmedByTeamB)
        }).ToList();

        var managedTeams = new List<Team>();
        if (!string.IsNullOrEmpty(currentUserId))
        {
            managedTeams = await _db.TeamMembers
                .AsNoTracking()
                .Where(tm => tm.UserId == currentUserId && (tm.Role == TeamRole.Owner || tm.Role == TeamRole.Manager))
                .Select(tm => tm.Team)
                .OrderBy(t => t.Name)
                .ToListAsync();
        }

        return new TournamentIndexViewModel
        {
            Tournaments = cards,
            Regions = regions,
            RegionFilter = cleanRegion,
            StatusFilter = status,
            ManagedTeams = managedTeams,
            CurrentUserId = currentUserId
        };
    }

    public async Task<TournamentDetailsViewModel?> BuildDetailsAsync(Guid tournamentId, string? currentUserId, string? activeTab, string baseAppUrl)
    {
        var tournament = await _db.Tournaments
            .AsNoTracking()
            .Include(t => t.OrganizerUser)
            .Include(t => t.Teams).ThenInclude(tt => tt.Team)
            .FirstOrDefaultAsync(t => t.Id == tournamentId);

        if (tournament == null) return null;

        bool isOrganizer = !string.IsNullOrEmpty(currentUserId) && tournament.OrganizerUserId == currentUserId;

        var matches = await _db.Matches
            .AsNoTracking()
            .Include(m => m.TeamA)
            .Include(m => m.TeamB)
            .Include(m => m.CardEvents)
            .Where(m => m.TournamentId == tournamentId)
            .OrderBy(m => m.MatchDateTime)
            .ThenBy(m => m.Id)
            .ToListAsync();

        var matchIds = matches.Select(m => m.Id).ToList();
        var refVerifiedIds = await GetRefereeVerifiedMatchIdsAsync(matchIds);

        // Compute synced standings snapshot in DB if standings rows are out-of-sync in count
        var persistedStandings = await _db.TournamentStandings
            .AsNoTracking()
            .Include(s => s.Team)
            .Where(s => s.TournamentId == tournamentId)
            .OrderBy(s => s.Rank)
            .ThenBy(s => s.Team.Name)
            .ToListAsync();

        if (persistedStandings.Count != tournament.Teams.Count && tournament.Teams.Count > 0)
        {
            await RecalculateStandingsAsync(tournamentId);
            persistedStandings = await _db.TournamentStandings
                .AsNoTracking()
                .Include(s => s.Team)
                .Where(s => s.TournamentId == tournamentId)
                .OrderBy(s => s.Rank)
                .ThenBy(s => s.Team.Name)
                .ToListAsync();
        }

        var standingRows = persistedStandings.Select(s => new TournamentStandingRowDto
        {
            TeamId = s.TeamId,
            TeamName = s.Team.Name,
            TeamSlug = s.Team.Slug,
            LogoUrl = s.Team.LogoUrl,
            GroupName = s.GroupName,
            Rank = s.Rank,
            Played = s.Played,
            Wins = s.Wins,
            Draws = s.Draws,
            Losses = s.Losses,
            GoalsFor = s.GoalsFor,
            GoalsAgainst = s.GoalsAgainst,
            GoalDiff = s.GoalDiff,
            Points = s.Points,
            YellowCards = s.YellowCards,
            RedCards = s.RedCards
        }).ToList();

        List<TournamentStandingGroupDto> standingGroups;
        if (tournament.Format == TournamentFormat.GroupPlusKnockout && standingRows.Any(r => !string.IsNullOrWhiteSpace(r.GroupName)))
        {
            standingGroups = standingRows
                .GroupBy(r => string.IsNullOrWhiteSpace(r.GroupName) ? "Chưa phân bảng" : r.GroupName!)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new TournamentStandingGroupDto
                {
                    Title = g.Key,
                    GroupName = g.Key,
                    Rows = g.OrderBy(r => r.Rank).ToList()
                })
                .ToList();
        }
        else
        {
            standingGroups =
            [
                new TournamentStandingGroupDto
                {
                    Title = "Bảng xếp hạng tổng",
                    Rows = standingRows.OrderBy(r => r.Rank).ToList()
                }
            ];
        }

        var fixtureRounds = matches
            .GroupBy(m => string.IsNullOrWhiteSpace(m.RoundLabel) ? "Lịch thi đấu" : m.RoundLabel!)
            .Select(g => new TournamentRoundGroupDto
            {
                RoundLabel = g.Key,
                Matches = g.Select(m =>
                {
                    bool confirmed = IsMatchResultConfirmed(m, refVerifiedIds);
                    return new TournamentFixtureDto
                    {
                        MatchId = m.Id,
                        RoundLabel = g.Key,
                        MatchDateTime = CompetitionRules.ToLocal(m.MatchDateTime),
                        PitchName = m.PitchName,
                        TeamAId = m.TeamAId,
                        TeamAName = m.TeamA?.Name ?? "Đội A",
                        TeamASlug = m.TeamA?.Slug ?? string.Empty,
                        TeamBId = m.TeamBId,
                        TeamBName = m.TeamB?.Name ?? m.TeamBExternalName ?? "Đội B",
                        TeamBSlug = m.TeamB?.Slug ?? string.Empty,
                        Status = m.Status,
                        ScoreA = m.ScoreA,
                        ScoreB = m.ScoreB,
                        ConfirmedByTeamA = m.ConfirmedByTeamA,
                        ConfirmedByTeamB = m.ConfirmedByTeamB,
                        IsConfirmedResult = confirmed,
                        YellowCardCount = m.CardEvents.Count(c => c.CardType == MatchCardType.Yellow),
                        RedCardCount = m.CardEvents.Count(c => c.CardType == MatchCardType.Red)
                    };
                }).ToList()
            })
            .ToList();

        var scorers = await BuildScorerLeaderboardAsync(tournamentId, matches, refVerifiedIds);

        var joinedIds = tournament.Teams.Select(tt => tt.TeamId).ToHashSet();
        var availablePublicTeams = isOrganizer
            ? await _db.Teams.AsNoTracking()
                .Where(t => !t.IsDraft && t.IsPublic && !joinedIds.Contains(t.Id))
                .OrderBy(t => t.Name)
                .ToListAsync()
            : [];

        var myEligibleTeams = !string.IsNullOrEmpty(currentUserId) && tournament.Status == TournamentStatus.Registration
            ? await _db.TeamMembers.AsNoTracking()
                .Where(tm => tm.UserId == currentUserId && (tm.Role == TeamRole.Owner || tm.Role == TeamRole.Manager) && !joinedIds.Contains(tm.TeamId))
                .Select(tm => tm.Team)
                .OrderBy(t => t.Name)
                .ToListAsync()
            : [];

        int minTeams = tournament.Format == TournamentFormat.GroupPlusKnockout ? 4 : 2;
        bool canGenerateFixtures = isOrganizer && matches.Count == 0 && tournament.Teams.Count >= minTeams;

        var (canNextRound, nextLabel) = EvaluateNextRoundAvailability(tournament, matches, refVerifiedIds, isOrganizer);

        string cleanTab = (activeTab ?? "standings").ToLowerInvariant() switch
        {
            "fixtures" => "fixtures",
            "scorers" => "scorers",
            _ => "standings"
        };

        return new TournamentDetailsViewModel
        {
            Tournament = tournament,
            FormatLabel = GetFormatLabel(tournament.Format),
            StatusLabel = GetStatusLabel(tournament.Status),
            OrganizerName = tournament.OrganizerUser?.FullName ?? "BTC AloBall",
            IsOrganizer = isOrganizer,
            ActiveTab = cleanTab,
            ParticipatingTeams = tournament.Teams
                .OrderBy(tt => tt.GroupName)
                .ThenBy(tt => tt.SeedOrder)
                .ThenBy(tt => tt.Team.Name)
                .Select(tt => new TournamentTeamDto
                {
                    TeamId = tt.TeamId,
                    TeamName = tt.Team.Name,
                    TeamSlug = tt.Team.Slug,
                    ActivityArea = tt.Team.ActivityArea,
                    GroupName = tt.GroupName,
                    SeedOrder = tt.SeedOrder
                }).ToList(),
            StandingGroups = standingGroups,
            FixtureRounds = fixtureRounds,
            Scorers = scorers,
            AvailablePublicTeams = availablePublicTeams,
            MyEligibleTeams = myEligibleTeams,
            ZaloStandingsSummary = BuildZaloStandingsSummary(tournament, standingGroups, scorers, baseAppUrl),
            CanGenerateFixtures = canGenerateFixtures,
            CanGenerateNextRound = canNextRound,
            NextRoundButtonLabel = nextLabel
        };
    }

    public async Task<(bool Success, string Message, Tournament? Tournament)> CreateTournamentAsync(
        CreateTournamentInput input, string organizerUserId)
    {
        if (string.IsNullOrWhiteSpace(organizerUserId))
            return (false, "Vui lòng đăng nhập để tạo giải đấu.", null);

        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length < 3)
            return (false, "Tên giải đấu phải có ít nhất 3 ký tự.", null);

        if (string.IsNullOrWhiteSpace(input.Region))
            return (false, "Vui lòng nhập khu vực thi đấu.", null);

        if (string.IsNullOrWhiteSpace(input.PitchVenue))
            return (false, "Vui lòng nhập sân thi đấu.", null);

        if (input.EntryFeeVnd < 0m || input.EntryFeeVnd > 100_000_000m)
            return (false, "Lệ phí giải phải từ 0đ đến 100.000.000đ.", null);

        DateTime startDate = input.StartDate == default ? CompetitionRules.LocalNow.Date.AddDays(3) : input.StartDate.Date;

        string inviteCode = await GenerateUniqueInviteCodeAsync();

        var tournament = new Tournament
        {
            Name = input.Name.Trim(),
            OrganizerUserId = organizerUserId,
            Region = input.Region.Trim(),
            Format = input.Format,
            StartDate = startDate,
            PitchVenue = input.PitchVenue.Trim(),
            EntryFeeVnd = input.EntryFeeVnd,
            Status = TournamentStatus.Registration,
            InviteCode = inviteCode,
            CreatedAt = DateTime.UtcNow
        };

        _db.Tournaments.Add(tournament);
        await _db.SaveChangesAsync();

        return (true, $"Đã tạo giải đấu '{tournament.Name}'! Mã mời tham dự: {tournament.InviteCode}", tournament);
    }

    public async Task<(bool Success, string Message)> AddTeamByOrganizerAsync(
        Guid tournamentId, Guid teamId, string? groupName, string organizerUserId)
    {
        var tournament = await _db.Tournaments.Include(t => t.Teams).FirstOrDefaultAsync(t => t.Id == tournamentId);
        if (tournament == null)
            return (false, "Giải đấu không tồn tại.");

        if (!string.Equals(tournament.OrganizerUserId, organizerUserId, StringComparison.Ordinal))
            return (false, "Chỉ Ban tổ chức tạo giải mới có quyền thêm hoặc điều chỉnh đội bóng.");

        if (await _db.Matches.AnyAsync(m => m.TournamentId == tournamentId))
            return (false, "Giải đã sinh lịch thi đấu, không thể thêm đội mới.");

        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == teamId && !t.IsDraft);
        if (team == null)
            return (false, "Đội bóng không tồn tại.");

        if (tournament.Teams.Any(tt => tt.TeamId == teamId))
            return (false, $"{team.Name} đã có tên trong danh sách tham dự.");

        string? resolvedGroup = tournament.Format == TournamentFormat.GroupPlusKnockout
            ? (string.IsNullOrWhiteSpace(groupName)
                ? (tournament.Teams.Count % 2 == 0 ? "Bảng A" : "Bảng B")
                : groupName.Trim())
            : null;

        _db.TournamentTeams.Add(new TournamentTeam
        {
            TournamentId = tournamentId,
            TeamId = teamId,
            GroupName = resolvedGroup,
            SeedOrder = tournament.Teams.Count + 1,
            JoinedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        await RecalculateStandingsAsync(tournamentId);

        return (true, $"Đã thêm {team.Name} vào giải đấu.");
    }

    public async Task<(bool Success, string Message, Guid? TournamentId)> JoinByInviteCodeAsync(
        string inviteCode, Guid teamId, string captainUserId)
    {
        if (string.IsNullOrWhiteSpace(inviteCode))
            return (false, "Vui lòng nhập mã mời giải đấu.", null);

        if (!await _auth.HasManagerAccessAsync(teamId, captainUserId))
            return (false, "Chỉ Đội trưởng / Quản lý đội mới có quyền đăng ký cho đội tham dự giải.", null);

        var code = inviteCode.Trim().ToUpperInvariant();
        var tournament = await _db.Tournaments
            .Include(t => t.Teams)
            .FirstOrDefaultAsync(t => t.InviteCode == code);

        if (tournament == null)
            return (false, "Không tìm thấy giải đấu với mã mời này.", null);

        if (tournament.Status != TournamentStatus.Registration || await _db.Matches.AnyAsync(m => m.TournamentId == tournament.Id))
            return (false, "Giải đấu đã chốt danh sách và bắt đầu thi đấu.", tournament.Id);

        if (tournament.Teams.Any(tt => tt.TeamId == teamId))
            return (false, "Đội bóng của bạn đã tham dự giải đấu này.", tournament.Id);

        string? groupName = tournament.Format == TournamentFormat.GroupPlusKnockout
            ? (tournament.Teams.Count % 2 == 0 ? "Bảng A" : "Bảng B")
            : null;

        _db.TournamentTeams.Add(new TournamentTeam
        {
            TournamentId = tournament.Id,
            TeamId = teamId,
            GroupName = groupName,
            SeedOrder = tournament.Teams.Count + 1,
            JoinedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        await RecalculateStandingsAsync(tournament.Id);

        return (true, $"Đăng ký tham gia giải '{tournament.Name}' thành công!", tournament.Id);
    }

    public async Task<(bool Success, string Message)> RemoveTeamAsync(Guid tournamentId, Guid teamId, string organizerUserId)
    {
        var tournament = await _db.Tournaments.FindAsync(tournamentId);
        if (tournament == null)
            return (false, "Giải đấu không tồn tại.");

        if (!string.Equals(tournament.OrganizerUserId, organizerUserId, StringComparison.Ordinal))
            return (false, "Chỉ Ban tổ chức tạo giải mới được xóa đội.");

        if (await _db.Matches.AnyAsync(m => m.TournamentId == tournamentId))
            return (false, "Giải đã có lịch thi đấu, không thể gỡ đội.");

        var entry = await _db.TournamentTeams.FirstOrDefaultAsync(tt => tt.TournamentId == tournamentId && tt.TeamId == teamId);
        if (entry == null)
            return (false, "Đội không nằm trong giải đấu.");

        _db.TournamentTeams.Remove(entry);
        var standing = await _db.TournamentStandings.FirstOrDefaultAsync(s => s.TournamentId == tournamentId && s.TeamId == teamId);
        if (standing != null)
            _db.TournamentStandings.Remove(standing);

        await _db.SaveChangesAsync();
        return (true, "Đã xóa đội khỏi danh sách tham dự.");
    }

    public async Task<(bool Success, string Message, List<MatchGame> GeneratedMatches)> GenerateFixturesAsync(
        Guid tournamentId, string organizerUserId, TimeSpan? defaultKickoffTime = null)
    {
        var tournament = await _db.Tournaments
            .Include(t => t.Teams).ThenInclude(tt => tt.Team)
            .FirstOrDefaultAsync(t => t.Id == tournamentId);

        if (tournament == null)
            return (false, "Giải đấu không tồn tại.", []);

        if (!string.Equals(tournament.OrganizerUserId, organizerUserId, StringComparison.Ordinal))
            return (false, "Chỉ Ban tổ chức tạo giải mới được phép sinh lịch thi đấu.", []);

        if (await _db.Matches.AnyAsync(m => m.TournamentId == tournamentId))
            return (false, "Lịch thi đấu cho giải này đã được sinh trước đó.", []);

        var orderedTeams = tournament.Teams
            .OrderBy(tt => tt.SeedOrder)
            .ThenBy(tt => tt.JoinedAt)
            .ToList();

        int minRequired = tournament.Format == TournamentFormat.GroupPlusKnockout ? 4 : 2;
        if (orderedTeams.Count < minRequired)
            return (false, $"Cần ít nhất {minRequired} đội đăng ký để sinh lịch theo thể thức {GetFormatLabel(tournament.Format)}.", []);

        var kickoffBase = defaultKickoffTime ?? new TimeSpan(18, 0, 0);
        var startDay = tournament.StartDate == default ? CompetitionRules.LocalNow.Date.AddDays(1) : tournament.StartDate.Date;
        var created = new List<MatchGame>();

        if (tournament.Format == TournamentFormat.RoundRobinLeague)
        {
            var rrMatches = BuildRoundRobinPairings(orderedTeams.Select(t => t.TeamId).ToList(), roundPrefix: "Vòng", startDay, kickoffBase, tournament);
            created.AddRange(rrMatches);
        }
        else if (tournament.Format == TournamentFormat.KnockoutCup)
        {
            int count = orderedTeams.Count;
            string roundLabel = count <= 2 ? "Chung kết" : count <= 4 ? "Bán kết" : count <= 8 ? "Tứ kết" : "Vòng 1";
            int slotIndex = 0;
            for (int i = 0; i + 1 < count; i += 2)
            {
                created.Add(CreateTournamentMatch(
                    tournament,
                    orderedTeams[i].TeamId,
                    orderedTeams[i + 1].TeamId,
                    roundLabel,
                    ResolveSlotTime(startDay, 0, slotIndex++, kickoffBase)));
            }
        }
        else
        {
            // GroupPlusKnockout: ensure balanced Bảng A / Bảng B
            for (int i = 0; i < orderedTeams.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(orderedTeams[i].GroupName))
                    orderedTeams[i].GroupName = i % 2 == 0 ? "Bảng A" : "Bảng B";
            }

            var groups = orderedTeams
                .GroupBy(t => t.GroupName!)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (groups.Any(g => g.Count() < 2))
            {
                // Rebalance evenly if organizer placed <2 teams in a group
                for (int i = 0; i < orderedTeams.Count; i++)
                    orderedTeams[i].GroupName = i % 2 == 0 ? "Bảng A" : "Bảng B";

                groups = orderedTeams.GroupBy(t => t.GroupName!).OrderBy(g => g.Key).ToList();
            }

            int groupOffsetDays = 0;
            foreach (var grp in groups)
            {
                var grpMatches = BuildRoundRobinPairings(
                    grp.Select(x => x.TeamId).ToList(),
                    $"{grp.Key} - Vòng",
                    startDay.AddDays(groupOffsetDays),
                    kickoffBase,
                    tournament);
                created.AddRange(grpMatches);
                groupOffsetDays += 1;
            }
        }

        _db.Matches.AddRange(created);
        tournament.Status = TournamentStatus.Ongoing;
        await _db.SaveChangesAsync();

        await RecalculateStandingsAsync(tournamentId);
        return (true, $"Đã sinh tự động {created.Count} trận đấu cho giải '{tournament.Name}'!", created);
    }

    public async Task<(bool Success, string Message, List<MatchGame> GeneratedMatches)> GenerateNextRoundAsync(
        Guid tournamentId, string organizerUserId)
    {
        var tournament = await _db.Tournaments
            .Include(t => t.Teams)
            .FirstOrDefaultAsync(t => t.Id == tournamentId);

        if (tournament == null)
            return (false, "Giải đấu không tồn tại.", []);

        if (!string.Equals(tournament.OrganizerUserId, organizerUserId, StringComparison.Ordinal))
            return (false, "Chỉ Ban tổ chức tạo giải mới được phép bốc thăm vòng tiếp theo.", []);

        var matches = await _db.Matches
            .AsNoTracking()
            .Where(m => m.TournamentId == tournamentId)
            .OrderBy(m => m.MatchDateTime)
            .ToListAsync();

        var refIds = await GetRefereeVerifiedMatchIdsAsync(matches.Select(m => m.Id).ToList());
        var (canAdvance, nextLabel) = EvaluateNextRoundAvailability(tournament, matches, refIds, isOrganizer: true);
        if (!canAdvance || string.IsNullOrEmpty(nextLabel))
            return (false, "Chưa đủ điều kiện tạo vòng tiếp theo (cần hoàn tất & xác nhận toàn bộ trận của vòng hiện tại).", []);

        var latestMatchDay = matches.Max(m => m.MatchDateTime.Date).AddDays(7);
        var kickoff = new TimeSpan(19, 0, 0);
        var generated = new List<MatchGame>();

        if (tournament.Format == TournamentFormat.GroupPlusKnockout &&
            !matches.Any(m => m.RoundLabel == "Bán kết" || m.RoundLabel == "Chung kết"))
        {
            await RecalculateStandingsAsync(tournamentId);
            var standings = await _db.TournamentStandings
                .AsNoTracking()
                .Where(s => s.TournamentId == tournamentId)
                .OrderBy(s => s.GroupName)
                .ThenBy(s => s.Rank)
                .ToListAsync();

            var grpA = standings.Where(s => s.GroupName == "Bảng A").Take(2).ToList();
            var grpB = standings.Where(s => s.GroupName == "Bảng B").Take(2).ToList();

            if (grpA.Count == 2 && grpB.Count == 2)
            {
                generated.Add(CreateTournamentMatch(tournament, grpA[0].TeamId, grpB[1].TeamId, "Bán kết", latestMatchDay.Add(kickoff)));
                generated.Add(CreateTournamentMatch(tournament, grpB[0].TeamId, grpA[1].TeamId, "Bán kết", latestMatchDay.Add(kickoff).AddMinutes(90)));
            }
            else if (grpA.Count >= 1 && grpB.Count >= 1)
            {
                generated.Add(CreateTournamentMatch(tournament, grpA[0].TeamId, grpB[0].TeamId, "Chung kết", latestMatchDay.Add(kickoff)));
            }
        }
        else
        {
            var lastRoundLabel = matches.Last().RoundLabel;
            var lastRoundMatches = matches.Where(m => m.RoundLabel == lastRoundLabel).ToList();
            var winners = new List<Guid>();
            foreach (var rm in lastRoundMatches)
            {
                if (!rm.ScoreA.HasValue || !rm.ScoreB.HasValue || !rm.TeamBId.HasValue)
                    return (false, "Vòng hiện tại vẫn còn trận chưa có tỉ số hợp lệ.", []);
                if (rm.ScoreA.Value == rm.ScoreB.Value)
                    return (false, $"Trận đấu vòng {lastRoundLabel} đang hòa ({rm.ScoreA}-{rm.ScoreB}); cần cập nhật tỉ số phân định thắng thua (hoặc cộng bàn luân lưu) trước khi sang vòng mới.", []);
                winners.Add(rm.ScoreA.Value > rm.ScoreB.Value ? rm.TeamAId : rm.TeamBId.Value);
            }

            if (winners.Count < 2)
                return (false, "Không đủ đội thắng để ghép cặp vòng tiếp theo.", []);

            string newLabel = winners.Count == 2 ? "Chung kết" : winners.Count <= 4 ? "Bán kết" : "Tứ kết";
            for (int i = 0; i + 1 < winners.Count; i += 2)
            {
                generated.Add(CreateTournamentMatch(tournament, winners[i], winners[i + 1], newLabel, latestMatchDay.Add(kickoff).AddMinutes((i / 2) * 90)));
            }
        }

        if (generated.Count == 0)
            return (false, "Không thể tạo thêm cặp đấu hợp lệ.", []);

        _db.Matches.AddRange(generated);
        await _db.SaveChangesAsync();
        await RecalculateStandingsAsync(tournamentId);

        return (true, $"Đã tạo {generated.Count} trận đấu cho vòng {generated[0].RoundLabel}!", generated);
    }

    public async Task RecalculateStandingsAsync(Guid tournamentId)
    {
        var tournament = await _db.Tournaments
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tournamentId);
        if (tournament == null) return;

        var regTeams = await _db.TournamentTeams
            .AsNoTracking()
            .Include(tt => tt.Team)
            .Where(tt => tt.TournamentId == tournamentId)
            .ToListAsync();

        var matches = await _db.Matches
            .AsNoTracking()
            .Include(m => m.CardEvents)
            .Where(m => m.TournamentId == tournamentId)
            .ToListAsync();

        var refVerifiedIds = await GetRefereeVerifiedMatchIdsAsync(matches.Select(m => m.Id).ToList());
        var confirmedMatches = matches.Where(m => IsMatchResultConfirmed(m, refVerifiedIds)).ToList();

        var teamGroupMap = regTeams.ToDictionary(tt => tt.TeamId, tt => tt.GroupName);
        var accMap = regTeams.ToDictionary(tt => tt.TeamId, tt => new StandingAccumulator
        {
            TeamId = tt.TeamId,
            TeamName = tt.Team.Name,
            GroupName = tt.GroupName
        });

        foreach (var cm in confirmedMatches)
        {
            // Cards across all confirmed tournament matches contribute to fair-play tracking
            foreach (var ce in cm.CardEvents)
            {
                if (accMap.TryGetValue(ce.TeamId, out var cAcc))
                {
                    if (ce.CardType == MatchCardType.Yellow) cAcc.YellowCards++;
                    else if (ce.CardType == MatchCardType.Red) cAcc.RedCards++;
                }
            }

            if (!cm.ScoreA.HasValue || !cm.ScoreB.HasValue || !cm.TeamBId.HasValue)
                continue;

            Guid teamA = cm.TeamAId;
            Guid teamB = cm.TeamBId.Value;

            if (!accMap.TryGetValue(teamA, out var accA) || !accMap.TryGetValue(teamB, out var accB))
                continue;

            // For Group + Knockout, only group-stage matches count toward group standings table
            if (tournament.Format == TournamentFormat.GroupPlusKnockout)
            {
                var gA = teamGroupMap.GetValueOrDefault(teamA);
                var gB = teamGroupMap.GetValueOrDefault(teamB);
                if (string.IsNullOrWhiteSpace(gA) || !string.Equals(gA, gB, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            int sA = cm.ScoreA.Value;
            int sB = cm.ScoreB.Value;

            accA.Played++;
            accB.Played++;
            accA.GoalsFor += sA;
            accA.GoalsAgainst += sB;
            accB.GoalsFor += sB;
            accB.GoalsAgainst += sA;

            if (sA > sB)
            {
                accA.Wins++;
                accA.Points += 3;
                accB.Losses++;
            }
            else if (sB > sA)
            {
                accB.Wins++;
                accB.Points += 3;
                accA.Losses++;
            }
            else
            {
                accA.Draws++;
                accA.Points += 1;
                accB.Draws++;
                accB.Points += 1;
            }
        }

        // Compute rank per group (or overall table), tie-breaker: Points -> GD -> GF -> Discipline -> Name
        var sortedByGroup = accMap.Values
            .GroupBy(a => tournament.Format == TournamentFormat.GroupPlusKnockout ? (a.GroupName ?? string.Empty) : string.Empty);

        foreach (var grp in sortedByGroup)
        {
            var ordered = grp
                .OrderByDescending(a => a.Points)
                .ThenByDescending(a => a.GoalDiff)
                .ThenByDescending(a => a.GoalsFor)
                .ThenBy(a => a.YellowCards + a.RedCards * 3)
                .ThenBy(a => a.TeamName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
                ordered[i].Rank = i + 1;
        }

        var existingRows = await _db.TournamentStandings
            .Where(s => s.TournamentId == tournamentId)
            .ToListAsync();

        var existingByTeam = existingRows.ToDictionary(s => s.TeamId);
        var activeIds = accMap.Keys.ToHashSet();

        foreach (var stale in existingRows.Where(s => !activeIds.Contains(s.TeamId)))
            _db.TournamentStandings.Remove(stale);

        var nowUtc = DateTime.UtcNow;
        foreach (var acc in accMap.Values)
        {
            if (existingByTeam.TryGetValue(acc.TeamId, out var entity))
            {
                entity.GroupName = acc.GroupName;
                entity.Rank = acc.Rank;
                entity.Played = acc.Played;
                entity.Wins = acc.Wins;
                entity.Draws = acc.Draws;
                entity.Losses = acc.Losses;
                entity.GoalsFor = acc.GoalsFor;
                entity.GoalsAgainst = acc.GoalsAgainst;
                entity.GoalDiff = acc.GoalDiff;
                entity.Points = acc.Points;
                entity.YellowCards = acc.YellowCards;
                entity.RedCards = acc.RedCards;
                entity.UpdatedAt = nowUtc;
            }
            else
            {
                _db.TournamentStandings.Add(new TournamentStanding
                {
                    TournamentId = tournamentId,
                    TeamId = acc.TeamId,
                    GroupName = acc.GroupName,
                    Rank = acc.Rank,
                    Played = acc.Played,
                    Wins = acc.Wins,
                    Draws = acc.Draws,
                    Losses = acc.Losses,
                    GoalsFor = acc.GoalsFor,
                    GoalsAgainst = acc.GoalsAgainst,
                    GoalDiff = acc.GoalDiff,
                    Points = acc.Points,
                    YellowCards = acc.YellowCards,
                    RedCards = acc.RedCards,
                    UpdatedAt = nowUtc
                });
            }
        }

        await _db.SaveChangesAsync();
    }

    public async Task SyncTournamentForMatchAsync(Guid matchId)
    {
        var tournamentId = await _db.Matches
            .AsNoTracking()
            .Where(m => m.Id == matchId)
            .Select(m => m.TournamentId)
            .FirstOrDefaultAsync();

        if (tournamentId.HasValue)
            await RecalculateStandingsAsync(tournamentId.Value);
    }

    public async Task<(bool Success, string Message)> CompleteTournamentAsync(Guid tournamentId, string organizerUserId)
    {
        var tournament = await _db.Tournaments.FindAsync(tournamentId);
        if (tournament == null)
            return (false, "Giải đấu không tồn tại.");

        if (!string.Equals(tournament.OrganizerUserId, organizerUserId, StringComparison.Ordinal))
            return (false, "Chỉ Ban tổ chức tạo giải mới được phép kết thúc giải đấu.");

        tournament.Status = TournamentStatus.Completed;
        await _db.SaveChangesAsync();
        await RecalculateStandingsAsync(tournamentId);

        return (true, $"Đã khép lại giải đấu '{tournament.Name}'!");
    }

    private async Task<List<TournamentScorerRowDto>> BuildScorerLeaderboardAsync(
        Guid tournamentId, List<MatchGame> matches, HashSet<Guid> refVerifiedIds)
    {
        var confirmedMatchIds = matches
            .Where(m => IsMatchResultConfirmed(m, refVerifiedIds))
            .Select(m => m.Id)
            .ToHashSet();

        if (confirmedMatchIds.Count == 0) return [];

        var goals = await _db.MatchGoalEvents
            .AsNoTracking()
            .Include(g => g.Scorer)
            .Include(g => g.Assist)
            .Include(g => g.Team)
            .Where(g => confirmedMatchIds.Contains(g.MatchId))
            .ToListAsync();

        var cards = await _db.MatchCardEvents
            .AsNoTracking()
            .Include(c => c.Player)
            .Include(c => c.Team)
            .Where(c => confirmedMatchIds.Contains(c.MatchId))
            .ToListAsync();

        var stats = new Dictionary<Guid, TournamentScorerRowDto>();

        TournamentScorerRowDto GetOrCreate(Player player, Team team)
        {
            if (!stats.TryGetValue(player.Id, out var row))
            {
                row = new TournamentScorerRowDto
                {
                    PlayerId = player.Id,
                    PlayerName = player.FullName,
                    KitNumber = player.KitNumber,
                    TeamId = team.Id,
                    TeamName = team.Name
                };
                stats[player.Id] = row;
            }
            return row;
        }

        foreach (var g in goals)
        {
            if (g.Scorer != null && g.Team != null)
                GetOrCreate(g.Scorer, g.Team).Goals++;

            if (g.Assist != null && g.Team != null)
                GetOrCreate(g.Assist, g.Team).Assists++;
        }

        foreach (var c in cards)
        {
            if (c.Player != null && c.Team != null)
            {
                var row = GetOrCreate(c.Player, c.Team);
                if (c.CardType == MatchCardType.Yellow) row.YellowCards++;
                else if (c.CardType == MatchCardType.Red) row.RedCards++;
            }
        }

        var ordered = stats.Values
            .OrderByDescending(r => r.Goals)
            .ThenByDescending(r => r.Assists)
            .ThenBy(r => r.YellowCards + r.RedCards * 3)
            .ThenBy(r => r.PlayerName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
            ordered[i].Rank = i + 1;

        return ordered;
    }

    private List<MatchGame> BuildRoundRobinPairings(
        List<Guid> teamIds, string roundPrefix, DateTime startDay, TimeSpan kickoffBase, Tournament tournament)
    {
        var pool = new List<Guid>(teamIds);
        if (pool.Count % 2 != 0)
            pool.Add(Guid.Empty); // Bye

        int n = pool.Count;
        int numRounds = n - 1;
        var results = new List<MatchGame>();

        for (int round = 0; round < numRounds; round++)
        {
            string label = $"{roundPrefix} {round + 1}";
            int slotInRound = 0;

            for (int i = 0; i < n / 2; i++)
            {
                Guid t1 = pool[i];
                Guid t2 = pool[n - 1 - i];
                if (t1 == Guid.Empty || t2 == Guid.Empty) continue;

                // Alternate home/away between rounds for fairness
                Guid home = (round % 2 == 1 && i == 0) || (i > 0 && round % 2 == 1) ? t2 : t1;
                Guid away = home == t1 ? t2 : t1;

                results.Add(CreateTournamentMatch(
                    tournament,
                    home,
                    away,
                    label,
                    ResolveSlotTime(startDay, round * 7, slotInRound++, kickoffBase)));
            }

            // Berger rotation: keep pool[0] fixed, rotate pool[1..n-1] right
            var last = pool[n - 1];
            for (int k = n - 1; k > 1; k--)
                pool[k] = pool[k - 1];
            pool[1] = last;
        }

        return results;
    }

    private static MatchGame CreateTournamentMatch(
        Tournament tournament, Guid teamAId, Guid teamBId, string roundLabel, DateTime kickoffLocal) => new()
        {
            Category = MatchCategory.Tournament,
            Status = MatchStatus.Scheduled,
            TeamAId = teamAId,
            TeamBId = teamBId,
            TournamentId = tournament.Id,
            RoundLabel = roundLabel,
            MatchDateTime = kickoffLocal,
            PitchName = tournament.PitchVenue,
            PitchType = "7 người",
            RsvpToken = Guid.NewGuid().ToString("N")[..8],
            IsPublic = true,
            Notes = $"[{tournament.Name}] {roundLabel}"
        };

    private static DateTime ResolveSlotTime(DateTime startDay, int dayOffset, int slotIndex, TimeSpan kickoffBase)
    {
        int extraDays = slotIndex / 3;
        int slotInDay = slotIndex % 3;
        return startDay.AddDays(dayOffset + extraDays).Add(kickoffBase).AddMinutes(slotInDay * 90);
    }

    private static bool IsMatchResultConfirmed(MatchGame m, HashSet<Guid> refVerifiedIds) =>
        m.Status == MatchStatus.Completed
        && m.ScoreA.HasValue
        && m.ScoreB.HasValue
        && ((m.ConfirmedByTeamA && (m.ConfirmedByTeamB || !m.TeamBId.HasValue)) || refVerifiedIds.Contains(m.Id));

    private async Task<HashSet<Guid>> GetRefereeVerifiedMatchIdsAsync(List<Guid> matchIds)
    {
        if (matchIds.Count == 0) return [];

        return (await _db.RefereeAssignments
            .AsNoTracking()
            .Where(a => a.Proposal.Status == RefereeProposalStatus.Accepted
                        && (a.Proposal.Request.Status == RefereeRequestStatus.Confirmed || a.Proposal.Request.Status == RefereeRequestStatus.Completed)
                        && a.Proposal.Request.MatchId.HasValue
                        && matchIds.Contains(a.Proposal.Request.MatchId.Value))
            .Select(a => a.Proposal.Request.MatchId!.Value)
            .Distinct()
            .ToListAsync())
            .ToHashSet();
    }

    private static (bool CanAdvance, string? Label) EvaluateNextRoundAvailability(
        Tournament tournament, List<MatchGame> matches, HashSet<Guid> refIds, bool isOrganizer)
    {
        if (!isOrganizer || matches.Count == 0 || tournament.Status == TournamentStatus.Completed)
            return (false, null);

        if (tournament.Format == TournamentFormat.RoundRobinLeague)
            return (false, null);

        if (matches.Any(m => !IsMatchResultConfirmed(m, refIds)))
            return (false, null);

        if (matches.Any(m => m.RoundLabel == "Chung kết"))
            return (false, null);

        if (tournament.Format == TournamentFormat.GroupPlusKnockout &&
            !matches.Any(m => m.RoundLabel == "Bán kết"))
        {
            return (true, "Bốc thăm vòng Knockout (Bán kết)");
        }

        var lastRoundLabel = matches.Last().RoundLabel;
        var lastRoundCount = matches.Count(m => m.RoundLabel == lastRoundLabel);
        if (lastRoundCount <= 1) return (false, null);

        string nextTitle = lastRoundCount == 2 ? "Chung kết" : lastRoundCount <= 4 ? "Bán kết" : "Vòng tiếp theo";
        return (true, $"Tạo vòng {nextTitle}");
    }

    private async Task<string> GenerateUniqueInviteCodeAsync()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var chars = new char[6];
            for (int i = 0; i < 6; i++)
                chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

            var code = $"AB{new string(chars)}";
            if (!await _db.Tournaments.AnyAsync(t => t.InviteCode == code))
                return code;
        }
        return $"AB{Guid.NewGuid():N}"[..8].ToUpperInvariant();
    }

    private static string BuildZaloStandingsSummary(
        Tournament tournament,
        List<TournamentStandingGroupDto> groups,
        List<TournamentScorerRowDto> scorers,
        string baseAppUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🏆 BXH GIẢI ĐẤU {tournament.Name.ToUpperInvariant()}");
        sb.AppendLine($"🏟 {tournament.PitchVenue} · {tournament.Region}");
        sb.AppendLine();

        foreach (var grp in groups)
        {
            if (groups.Count > 1)
                sb.AppendLine($"📌 {grp.Title.ToUpperInvariant()}:");

            foreach (var r in grp.Rows)
            {
                string sign = r.GoalDiff > 0 ? $"+{r.GoalDiff}" : r.GoalDiff.ToString();
                sb.AppendLine($"{r.Rank}. {r.TeamName} | {r.Played}T ({r.Wins}-{r.Draws}-{r.Losses}) | HS {sign} | {r.Points}đ");
            }
            sb.AppendLine();
        }

        var topScorers = scorers.Where(s => s.Goals > 0).Take(3).ToList();
        if (topScorers.Count > 0)
        {
            sb.AppendLine("⚽ TOP VUA PHÁ LƯỚI:");
            foreach (var s in topScorers)
                sb.AppendLine($"- {s.PlayerName} ({s.TeamName}): {s.Goals} bàn / {s.Assists} KT");
            sb.AppendLine();
        }

        sb.AppendLine($"👉 Xem lịch & chi tiết tại AloBall: {baseAppUrl.TrimEnd('/')}/Tournaments/Details/{tournament.Id}");
        return sb.ToString();
    }

    private sealed class StandingAccumulator
    {
        public Guid TeamId { get; init; }
        public string TeamName { get; init; } = string.Empty;
        public string? GroupName { get; init; }
        public int Rank { get; set; }
        public int Played { get; set; }
        public int Wins { get; set; }
        public int Draws { get; set; }
        public int Losses { get; set; }
        public int GoalsFor { get; set; }
        public int GoalsAgainst { get; set; }
        public int GoalDiff => GoalsFor - GoalsAgainst;
        public int Points { get; set; }
        public int YellowCards { get; set; }
        public int RedCards { get; set; }
    }
}
