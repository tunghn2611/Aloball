using System.ComponentModel.DataAnnotations;
using AloBall.Web.Data.Entities;

namespace AloBall.Web.Models;

public class CreateTournamentInput
{
    [Required(ErrorMessage = "Vui lòng nhập tên giải đấu.")]
    [StringLength(150, MinimumLength = 3, ErrorMessage = "Tên giải đấu phải từ 3 đến 150 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập khu vực thi đấu.")]
    [StringLength(100)]
    public string Region { get; set; } = string.Empty;

    public TournamentFormat Format { get; set; } = TournamentFormat.RoundRobinLeague;

    [Required(ErrorMessage = "Vui lòng chọn ngày khai mạc.")]
    public DateTime StartDate { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập sân thi đấu.")]
    [StringLength(200)]
    public string PitchVenue { get; set; } = string.Empty;

    [Range(0, 100_000_000, ErrorMessage = "Lệ phí giải phải từ 0đ đến 100.000.000đ.")]
    public decimal EntryFeeVnd { get; set; }
}

public class TournamentCardDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string PitchVenue { get; set; } = string.Empty;
    public TournamentFormat Format { get; set; }
    public string FormatLabel { get; set; } = string.Empty;
    public TournamentStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public decimal EntryFeeVnd { get; set; }
    public string InviteCode { get; set; } = string.Empty;
    public string OrganizerName { get; set; } = string.Empty;
    public bool IsOrganizer { get; set; }
    public int TeamCount { get; set; }
    public int MatchCount { get; set; }
    public int CompletedMatchCount { get; set; }
}

public class TournamentIndexViewModel
{
    public List<TournamentCardDto> Tournaments { get; set; } = new();
    public List<string> Regions { get; set; } = new();
    public string? RegionFilter { get; set; }
    public TournamentStatus? StatusFilter { get; set; }
    public List<Team> ManagedTeams { get; set; } = new();
    public string? CurrentUserId { get; set; }
}

public class TournamentStandingRowDto
{
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string TeamSlug { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? GroupName { get; set; }
    public int Rank { get; set; }
    public int Played { get; set; }
    public int Wins { get; set; }
    public int Draws { get; set; }
    public int Losses { get; set; }
    public int GoalsFor { get; set; }
    public int GoalsAgainst { get; set; }
    public int GoalDiff { get; set; }
    public int Points { get; set; }
    public int YellowCards { get; set; }
    public int RedCards { get; set; }
}

public class TournamentStandingGroupDto
{
    public string Title { get; set; } = "Bảng xếp hạng tổng";
    public string? GroupName { get; set; }
    public List<TournamentStandingRowDto> Rows { get; set; } = new();
}

public class TournamentFixtureDto
{
    public Guid MatchId { get; set; }
    public string RoundLabel { get; set; } = string.Empty;
    public DateTime MatchDateTime { get; set; }
    public string PitchName { get; set; } = string.Empty;
    public Guid TeamAId { get; set; }
    public string TeamAName { get; set; } = string.Empty;
    public string TeamASlug { get; set; } = string.Empty;
    public Guid? TeamBId { get; set; }
    public string TeamBName { get; set; } = string.Empty;
    public string TeamBSlug { get; set; } = string.Empty;
    public MatchStatus Status { get; set; }
    public int? ScoreA { get; set; }
    public int? ScoreB { get; set; }
    public bool ConfirmedByTeamA { get; set; }
    public bool ConfirmedByTeamB { get; set; }
    public bool IsConfirmedResult { get; set; }
    public int YellowCardCount { get; set; }
    public int RedCardCount { get; set; }
}

public class TournamentRoundGroupDto
{
    public string RoundLabel { get; set; } = string.Empty;
    public List<TournamentFixtureDto> Matches { get; set; } = new();
}

public class TournamentScorerRowDto
{
    public int Rank { get; set; }
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int? KitNumber { get; set; }
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int Goals { get; set; }
    public int Assists { get; set; }
    public int GoalContributions => Goals + Assists;
    public int YellowCards { get; set; }
    public int RedCards { get; set; }
}

public class TournamentTeamDto
{
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string TeamSlug { get; set; } = string.Empty;
    public string? ActivityArea { get; set; }
    public string? GroupName { get; set; }
    public int SeedOrder { get; set; }
}

public class TournamentDetailsViewModel
{
    public Tournament Tournament { get; set; } = null!;
    public string FormatLabel { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public string OrganizerName { get; set; } = string.Empty;
    public bool IsOrganizer { get; set; }
    public string ActiveTab { get; set; } = "standings";
    public List<TournamentTeamDto> ParticipatingTeams { get; set; } = new();
    public List<TournamentStandingGroupDto> StandingGroups { get; set; } = new();
    public List<TournamentRoundGroupDto> FixtureRounds { get; set; } = new();
    public List<TournamentScorerRowDto> Scorers { get; set; } = new();
    public List<Team> AvailablePublicTeams { get; set; } = new();
    public List<Team> MyEligibleTeams { get; set; } = new();
    public string ZaloStandingsSummary { get; set; } = string.Empty;
    public bool CanGenerateFixtures { get; set; }
    public bool CanGenerateNextRound { get; set; }
    public string? NextRoundButtonLabel { get; set; }
}
