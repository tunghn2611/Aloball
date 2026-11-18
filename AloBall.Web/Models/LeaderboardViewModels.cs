using AloBall.Web.Data.Entities;

namespace AloBall.Web.Models;

public class TeamStandingRowDto
{
    public int Rank { get; set; }
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string TeamSlug { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string Region { get; set; } = string.Empty;
    public PlanTier PlanTier { get; set; } = PlanTier.Free;
    public bool IsPaidPlanActive { get; set; }

    public int AloballStars { get; set; }
    public int MatchesPlayed { get; set; }
    public int Wins { get; set; }
    public int Draws { get; set; }
    public int Losses { get; set; }
    public int GoalsFor { get; set; }
    public int GoalsAgainst { get; set; }
    public int GoalDiff => GoalsFor - GoalsAgainst;
    public List<string> RecentForm { get; set; } = new();

    public double AverageRating { get; set; } = 5.0;
    public int ValidReviewCount { get; set; }
    public List<string> TopVibes { get; set; } = new();
    public bool IsHighHardTackle { get; set; }
}

public class RegionUnlockProgressDto
{
    public string? Region { get; set; }
    public int CurrentRankedMatches { get; set; }
    public int TargetMatches { get; set; } = 10;
    public bool IsUnlocked => CurrentRankedMatches >= TargetMatches;
    public int ProgressPercent => TargetMatches > 0
        ? Math.Min(100, (int)Math.Round(CurrentRankedMatches * 100.0 / TargetMatches))
        : 100;
}

public class HeadToHeadMatchItemDto
{
    public Guid MatchId { get; set; }
    public DateTime KickoffLocal { get; set; }
    public string PitchName { get; set; } = string.Empty;
    public int MyScore { get; set; }
    public int RivalScore { get; set; }
    public string ResultCode { get; set; } = "D";
    public int AwardedStars { get; set; }
    public bool WasCappedByMonthlyLimit { get; set; }
    public string VerificationLabel { get; set; } = "Xác nhận 2 đội";
}

public class HeadToHeadDto
{
    public Guid MyTeamId { get; set; }
    public string MyTeamName { get; set; } = string.Empty;
    public int MyTeamStars { get; set; }

    public Guid RivalTeamId { get; set; }
    public string RivalTeamName { get; set; } = string.Empty;
    public string RivalTeamSlug { get; set; } = string.Empty;
    public int RivalTeamStars { get; set; }
    public double RivalAverageRating { get; set; }
    public List<string> RivalTopVibes { get; set; } = new();

    public int TotalMatches { get; set; }
    public int Wins { get; set; }
    public int Draws { get; set; }
    public int Losses { get; set; }
    public int GoalsFor { get; set; }
    public int GoalsAgainst { get; set; }

    public List<HeadToHeadMatchItemDto> Matches { get; set; } = new();
}

public class LeaderboardIndexViewModel
{
    public string? SelectedRegion { get; set; }
    public bool OnlyRankedMatches { get; set; } = true;
    public bool ShowPreview { get; set; } = false;
    public List<string> AvailableRegions { get; set; } = new();
    public RegionUnlockProgressDto RegionProgress { get; set; } = new();
    public List<TeamStandingRowDto> Standings { get; set; } = new();
    public List<TeamStandingRowDto> TopThree => Standings.Take(3).ToList();
    public List<Team> ManagedTeams { get; set; } = new();
    public Guid? SelectedMyTeamId { get; set; }
}
