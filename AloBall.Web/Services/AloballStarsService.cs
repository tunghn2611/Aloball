using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Services;

public interface IAloballStarsService
{
    Task<Dictionary<Guid, int>> ComputeTeamStarsMapAsync(IEnumerable<Guid> teamIds);
    Task<int> GetTeamStarsAsync(Guid teamId);
    Task<LeaderboardIndexViewModel> BuildLeaderboardAsync(
        string? region,
        bool onlyRankedMatches = true,
        bool showPreview = false,
        string? userId = null,
        Guid? myTeamId = null);
    Task<HeadToHeadDto?> GetHeadToHeadAsync(Guid myTeamId, Guid rivalTeamId);
}

public class AloballStarsService : IAloballStarsService
{
    public const int MinRankedMatchesToUnlockRegion = 10;
    public const int MaxRankedMatchesPerPairPerMonth = 2;

    private readonly ApplicationDbContext _db;

    public AloballStarsService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Dictionary<Guid, int>> ComputeTeamStarsMapAsync(IEnumerable<Guid> teamIds)
    {
        var ids = teamIds.Distinct().ToList();
        var map = ids.ToDictionary(id => id, _ => 0);
        if (ids.Count == 0) return map;

        var eval = await EvaluateAllMatchesAsync(onlyRankedMatches: true, targetRegion: null, includeNonPublicTeams: true);
        foreach (var id in ids)
        {
            if (eval.TeamAccumulators.TryGetValue(id, out var acc))
                map[id] = acc.Stars;
        }
        return map;
    }

    public async Task<int> GetTeamStarsAsync(Guid teamId)
    {
        var map = await ComputeTeamStarsMapAsync([teamId]);
        return map.GetValueOrDefault(teamId, 0);
    }

    public async Task<LeaderboardIndexViewModel> BuildLeaderboardAsync(
        string? region,
        bool onlyRankedMatches = true,
        bool showPreview = false,
        string? userId = null,
        Guid? myTeamId = null)
    {
        string? cleanRegion = string.IsNullOrWhiteSpace(region) ? null : region.Trim();
        var eval = await EvaluateAllMatchesAsync(onlyRankedMatches, cleanRegion, includeNonPublicTeams: false);

        var teams = eval.PublicTeams;
        var regions = teams
            .Where(t => !string.IsNullOrWhiteSpace(t.ActivityArea))
            .Select(t => t.ActivityArea!.Trim())
            .Concat(await _db.PitchVenues.AsNoTracking().Where(v => v.IsActive).Select(v => v.District).ToListAsync())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r)
            .ToList();

        var filteredTeams = string.IsNullOrEmpty(cleanRegion)
            ? teams
            : teams.Where(t => MatchConciergeService.RegionsMatch(t.ActivityArea, cleanRegion)).ToList();

        var filteredIds = filteredTeams.Select(t => t.Id).ToList();
        var vibesMap = await MatchmakingService.LoadTeamVibesMapAsync(_db, filteredIds);

        var rows = new List<TeamStandingRowDto>();
        foreach (var t in filteredTeams)
        {
            eval.TeamAccumulators.TryGetValue(t.Id, out var acc);
            vibesMap.TryGetValue(t.Id, out var vibe);

            rows.Add(new TeamStandingRowDto
            {
                TeamId = t.Id,
                TeamName = t.Name,
                TeamSlug = t.Slug,
                LogoUrl = t.LogoUrl,
                Region = t.ActivityArea ?? "Chưa cập nhật",
                PlanTier = t.PlanTier,
                IsPaidPlanActive = t.IsPaidPlanActive,
                AloballStars = acc?.Stars ?? 0,
                MatchesPlayed = acc?.MatchesPlayed ?? 0,
                Wins = acc?.Wins ?? 0,
                Draws = acc?.Draws ?? 0,
                Losses = acc?.Losses ?? 0,
                GoalsFor = acc?.GoalsFor ?? 0,
                GoalsAgainst = acc?.GoalsAgainst ?? 0,
                RecentForm = acc != null ? acc.FormHistory.TakeLast(5).ToList() : [],
                AverageRating = t.AverageRating,
                ValidReviewCount = t.ValidReviewCount,
                TopVibes = vibe?.TopVibes ?? [],
                IsHighHardTackle = vibe?.IsHighHardTackle ?? false
            });
        }

        var rankedRows = rows
            .OrderByDescending(r => r.AloballStars)
            .ThenByDescending(r => r.GoalDiff)
            .ThenByDescending(r => r.GoalsFor)
            .ThenByDescending(r => r.AverageRating)
            .ThenBy(r => r.TeamName)
            .ToList();

        for (int i = 0; i < rankedRows.Count; i++)
        {
            rankedRows[i].Rank = i + 1;
        }

        var managedTeams = new List<Team>();
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var mIds = await _db.TeamMembers
                .AsNoTracking()
                .Where(m => m.UserId == userId)
                .Select(m => m.TeamId)
                .ToListAsync();

            managedTeams = await _db.Teams
                .AsNoTracking()
                .Where(t => mIds.Contains(t.Id) && !t.IsDraft)
                .OrderBy(t => t.Name)
                .ToListAsync();
        }

        Guid? selectedMyTeamId = (myTeamId.HasValue && managedTeams.Any(t => t.Id == myTeamId.Value))
            ? myTeamId.Value
            : managedTeams.FirstOrDefault()?.Id;

        return new LeaderboardIndexViewModel
        {
            SelectedRegion = cleanRegion,
            OnlyRankedMatches = onlyRankedMatches,
            ShowPreview = showPreview,
            AvailableRegions = regions,
            RegionProgress = new RegionUnlockProgressDto
            {
                Region = cleanRegion,
                CurrentRankedMatches = eval.RankedMatchesInScope,
                TargetMatches = MinRankedMatchesToUnlockRegion
            },
            Standings = rankedRows,
            ManagedTeams = managedTeams,
            SelectedMyTeamId = selectedMyTeamId
        };
    }

    public async Task<HeadToHeadDto?> GetHeadToHeadAsync(Guid myTeamId, Guid rivalTeamId)
    {
        if (myTeamId == Guid.Empty || rivalTeamId == Guid.Empty || myTeamId == rivalTeamId)
            return null;

        var myTeam = await _db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == myTeamId);
        var rivalTeam = await _db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == rivalTeamId);
        if (myTeam == null || rivalTeam == null) return null;

        var eval = await EvaluateAllMatchesAsync(onlyRankedMatches: true, targetRegion: null, includeNonPublicTeams: true);
        var vibesMap = await MatchmakingService.LoadTeamVibesMapAsync(_db, [rivalTeamId]);

        var h2hMatches = eval.EvaluatedH2HMatches
            .Where(x => (x.TeamAId == myTeamId && x.TeamBId == rivalTeamId) || (x.TeamAId == rivalTeamId && x.TeamBId == myTeamId))
            .OrderByDescending(x => x.KickoffLocal)
            .ToList();

        int wins = 0, draws = 0, losses = 0, gf = 0, ga = 0;
        var items = new List<HeadToHeadMatchItemDto>();

        foreach (var em in h2hMatches)
        {
            bool myIsA = em.TeamAId == myTeamId;
            int myScore = myIsA ? em.ScoreA : em.ScoreB;
            int rivalScore = myIsA ? em.ScoreB : em.ScoreA;

            gf += myScore;
            ga += rivalScore;

            string code;
            if (myScore > rivalScore) { wins++; code = "W"; }
            else if (myScore == rivalScore) { draws++; code = "D"; }
            else { losses++; code = "L"; }

            int awardedStars = em.WasCappedByMonthlyLimit ? 0 : (code == "W" ? 3 : code == "D" ? 1 : 0);

            items.Add(new HeadToHeadMatchItemDto
            {
                MatchId = em.MatchId,
                KickoffLocal = em.KickoffLocal,
                PitchName = em.PitchName,
                MyScore = myScore,
                RivalScore = rivalScore,
                ResultCode = code,
                AwardedStars = awardedStars,
                WasCappedByMonthlyLimit = em.WasCappedByMonthlyLimit,
                VerificationLabel = em.VerificationLabel
            });
        }

        return new HeadToHeadDto
        {
            MyTeamId = myTeam.Id,
            MyTeamName = myTeam.Name,
            MyTeamStars = eval.TeamAccumulators.TryGetValue(myTeam.Id, out var mAcc) ? mAcc.Stars : 0,
            RivalTeamId = rivalTeam.Id,
            RivalTeamName = rivalTeam.Name,
            RivalTeamSlug = rivalTeam.Slug,
            RivalTeamStars = eval.TeamAccumulators.TryGetValue(rivalTeam.Id, out var rAcc) ? rAcc.Stars : 0,
            RivalAverageRating = rivalTeam.AverageRating,
            RivalTopVibes = vibesMap.TryGetValue(rivalTeam.Id, out var rv) ? rv.TopVibes : [],
            TotalMatches = h2hMatches.Count,
            Wins = wins,
            Draws = draws,
            Losses = losses,
            GoalsFor = gf,
            GoalsAgainst = ga,
            Matches = items
        };
    }

    private sealed class TeamAccumulator
    {
        public int Stars { get; set; }
        public int MatchesPlayed { get; set; }
        public int Wins { get; set; }
        public int Draws { get; set; }
        public int Losses { get; set; }
        public int GoalsFor { get; set; }
        public int GoalsAgainst { get; set; }
        public List<string> FormHistory { get; } = [];
    }

    private sealed class EvaluatedH2HMatch
    {
        public Guid MatchId { get; init; }
        public Guid TeamAId { get; init; }
        public Guid TeamBId { get; init; }
        public int ScoreA { get; init; }
        public int ScoreB { get; init; }
        public DateTime KickoffLocal { get; init; }
        public string PitchName { get; init; } = string.Empty;
        public bool WasCappedByMonthlyLimit { get; init; }
        public string VerificationLabel { get; init; } = string.Empty;
    }

    private sealed class EvaluationResult
    {
        public List<Team> PublicTeams { get; init; } = [];
        public Dictionary<Guid, TeamAccumulator> TeamAccumulators { get; } = new();
        public List<EvaluatedH2HMatch> EvaluatedH2HMatches { get; } = [];
        public int RankedMatchesInScope { get; set; }
    }

    private async Task<EvaluationResult> EvaluateAllMatchesAsync(
        bool onlyRankedMatches,
        string? targetRegion,
        bool includeNonPublicTeams)
    {
        var allTeams = await _db.Teams
            .AsNoTracking()
            .Where(t => !t.IsDraft)
            .ToListAsync();

        var teamsById = allTeams.ToDictionary(t => t.Id);

        var refereeVerifiedMatchIds = (await _db.RefereeAssignments
            .AsNoTracking()
            .Where(a => a.Proposal.Status == RefereeProposalStatus.Accepted
                        && (a.Proposal.Request.Status == RefereeRequestStatus.Confirmed || a.Proposal.Request.Status == RefereeRequestStatus.Completed)
                        && a.Proposal.Request.MatchId.HasValue)
            .Select(a => a.Proposal.Request.MatchId!.Value)
            .Distinct()
            .ToListAsync())
            .ToHashSet();

        // Only official inter-club completed matches (exclude internal A/B splits & self-matches)
        var rawMatches = await _db.Matches
            .AsNoTracking()
            .Where(m => m.Category != MatchCategory.Internal
                        && m.TeamBId.HasValue
                        && m.TeamBId.Value != m.TeamAId
                        && m.Status == MatchStatus.Completed
                        && m.ScoreA.HasValue
                        && m.ScoreB.HasValue)
            .ToListAsync();

        var orderedMatches = rawMatches
            .Select(m => (Match: m, LocalDt: CompetitionRules.ToLocal(m.MatchDateTime)))
            .OrderBy(x => x.LocalDt)
            .ThenBy(x => x.Match.Id)
            .ToList();

        var result = new EvaluationResult
        {
            PublicTeams = allTeams.Where(t => includeNonPublicTeams || t.IsPublic).ToList()
        };

        TeamAccumulator GetOrCreate(Guid tid)
        {
            if (!result.TeamAccumulators.TryGetValue(tid, out var acc))
            {
                acc = new TeamAccumulator();
                result.TeamAccumulators[tid] = acc;
            }
            return acc;
        }

        var pairMonthlyCounts = new Dictionary<(Guid, Guid, string), int>();

        foreach (var (m, localDt) in orderedMatches)
        {
            Guid teamAId = m.TeamAId;
            Guid teamBId = m.TeamBId!.Value;
            if (!teamsById.TryGetValue(teamAId, out var teamA) || !teamsById.TryGetValue(teamBId, out var teamB))
                continue;

            bool hasDualConfirm = m.ConfirmedByTeamA && m.ConfirmedByTeamB;
            bool hasAssignedReferee = refereeVerifiedMatchIds.Contains(m.Id);
            bool isVerified = hasDualConfirm || hasAssignedReferee;

            if (!isVerified)
            {
                if (onlyRankedMatches || (!m.ConfirmedByTeamA && !m.ConfirmedByTeamB))
                    continue;

                // Preview/unfiltered match (single-confirmed) -> no ranking stars, counts only when preview filter is off
                RecordStats(GetOrCreate(teamAId), m.ScoreA!.Value, m.ScoreB!.Value, starsAwarded: 0);
                RecordStats(GetOrCreate(teamBId), m.ScoreB!.Value, m.ScoreA!.Value, starsAwarded: 0);
                continue;
            }

            var minId = teamAId < teamBId ? teamAId : teamBId;
            var maxId = teamAId < teamBId ? teamBId : teamAId;
            string monthKey = $"{localDt:yyyy-MM}";
            var pairKey = (minId, maxId, monthKey);
            int monthlyCount = pairMonthlyCounts.GetValueOrDefault(pairKey, 0) + 1;
            pairMonthlyCounts[pairKey] = monthlyCount;

            bool isCappedOut = monthlyCount > MaxRankedMatchesPerPairPerMonth;
            int sA = m.ScoreA!.Value;
            int sB = m.ScoreB!.Value;

            result.EvaluatedH2HMatches.Add(new EvaluatedH2HMatch
            {
                MatchId = m.Id,
                TeamAId = teamAId,
                TeamBId = teamBId,
                ScoreA = sA,
                ScoreB = sB,
                KickoffLocal = localDt,
                PitchName = m.PitchName ?? "Sân đấu",
                WasCappedByMonthlyLimit = isCappedOut,
                VerificationLabel = hasDualConfirm ? "Xác nhận 2 đội" : "Trọng tài xác nhận"
            });

            if (!isCappedOut)
            {
                int starsA = sA > sB ? 3 : sA == sB ? 1 : 0;
                int starsB = sB > sA ? 3 : sB == sA ? 1 : 0;

                RecordStats(GetOrCreate(teamAId), sA, sB, starsA);
                RecordStats(GetOrCreate(teamBId), sB, sA, starsB);

                bool inRegionScope = string.IsNullOrEmpty(targetRegion)
                    || MatchConciergeService.RegionsMatch(teamA.ActivityArea, targetRegion)
                    || MatchConciergeService.RegionsMatch(teamB.ActivityArea, targetRegion);

                if (inRegionScope)
                    result.RankedMatchesInScope++;
            }
            else if (!onlyRankedMatches)
            {
                // Unfiltered view shows excess rematch in match counts with 0 ranking stars
                RecordStats(GetOrCreate(teamAId), sA, sB, starsAwarded: 0);
                RecordStats(GetOrCreate(teamBId), sB, sA, starsAwarded: 0);
            }
        }

        return result;
    }

    private static void RecordStats(TeamAccumulator acc, int goalsFor, int goalsAgainst, int starsAwarded)
    {
        acc.MatchesPlayed++;
        acc.GoalsFor += goalsFor;
        acc.GoalsAgainst += goalsAgainst;
        acc.Stars += starsAwarded;

        if (goalsFor > goalsAgainst)
        {
            acc.Wins++;
            acc.FormHistory.Add("W");
        }
        else if (goalsFor == goalsAgainst)
        {
            acc.Draws++;
            acc.FormHistory.Add("D");
        }
        else
        {
            acc.Losses++;
            acc.FormHistory.Add("L");
        }
    }
}
