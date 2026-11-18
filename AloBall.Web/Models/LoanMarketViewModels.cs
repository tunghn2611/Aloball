using AloBall.Web.Data.Entities;

namespace AloBall.Web.Models;

public class PlayerScoutProfileDto
{
    public Guid? PlayerId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string PrimaryPosition { get; set; } = "MID";
    public string PreferredFoot { get; set; } = "Phải";
    public string Experience { get; set; } = "Phong trào";
    public string Region { get; set; } = "Hà Nội";
    public int Ovr { get; set; } = 72;
    public string Tier { get; set; } = "silver";
    public int RepStat { get; set; } = 85;
    public double ReputationStars { get; set; } = 5.0;
    public int AvailableAbCoin { get; set; } = 0;
    public string? HomeTeamName { get; set; }
    public string? HomeTeamSlug { get; set; }
    public List<AssignedPlayStyle> PlayStyles { get; set; } = new();
}

public class LoanApplicationCardDto
{
    public Guid Id { get; set; }
    public Guid ListingId { get; set; }
    public string Position { get; set; } = "MID";
    public string? Message { get; set; }
    public LoanApplicationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ApplicantTeamId { get; set; }
    public string? ApplicantTeamName { get; set; }
    public Guid? TargetMatchId { get; set; }
    public string? TargetMatchLabel { get; set; }
    public PlayerScoutProfileDto Scout { get; set; } = new();
}

public class LoanListingCardDto
{
    public Guid Id { get; set; }
    public LoanListingType ListingType { get; set; }
    public string? UserId { get; set; }
    public Guid? PlayerId { get; set; }
    public Guid? TeamId { get; set; }
    public string? TeamName { get; set; }
    public string? TeamSlug { get; set; }
    public string? TeamLogoUrl { get; set; }
    public double TeamRating { get; set; } = 5.0;
    public Guid? MatchId { get; set; }
    public string? MatchLabel { get; set; }
    public string Region { get; set; } = string.Empty;
    public string? PitchName { get; set; }
    public DateTime PlayDate { get; set; }
    public string TimeSlot { get; set; } = "19:00 - 20:30";
    public string PositionsNeeded { get; set; } = "MID";
    public List<string> PositionBadges { get; set; } = new();
    public LoanFeeType FeeType { get; set; }
    public decimal LoanFeeAmount { get; set; }
    public int EscrowAbCoin { get; set; }
    public bool IsEscrowHeld { get; set; }
    public bool IsEscrowReleased { get; set; }
    public string? Notes { get; set; }
    public LoanListingStatus Status { get; set; }
    public Guid? AcceptedApplicationId { get; set; }
    public Guid? LoanedGuestPlayerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? MatchedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public PlayerScoutProfileDto? PlayerScout { get; set; }
    public PlayerScoutProfileDto? MatchedPlayerScout { get; set; }
    public string? MatchedTeamName { get; set; }
    public Guid? EffectiveMatchId { get; set; }
    public List<LoanApplicationCardDto> Applications { get; set; } = new();
    public string ZaloShareMessage { get; set; } = string.Empty;
    public bool CanManage { get; set; }
    public bool HasApplied { get; set; }
}

public class CreateLoanListingInput
{
    public LoanListingType ListingType { get; set; } = LoanListingType.TeamRecruiting;
    public Guid? TeamId { get; set; }
    public Guid? MatchId { get; set; }
    public string Region { get; set; } = string.Empty;
    public string? PitchName { get; set; }
    public DateTime PlayDate { get; set; }
    public string TimeSlot { get; set; } = "19:00 - 20:30";
    public string? PositionsNeeded { get; set; }
    public List<string>? Positions { get; set; }
    public LoanFeeType FeeType { get; set; } = LoanFeeType.SplitPitch;
    public decimal LoanFeeAmount { get; set; } = 0m;
    public int EscrowAbCoin { get; set; } = 0;
    public string? Notes { get; set; }
}

public class ApplyLoanInput
{
    public Guid ListingId { get; set; }
    public string Position { get; set; } = "MID";
    public Guid? ApplicantTeamId { get; set; }
    public Guid? TargetMatchId { get; set; }
    public string? Message { get; set; }
}

public class LoanMarketIndexViewModel
{
    public string ActiveTab { get; set; } = "teams"; // "teams" | "players"
    public string? Region { get; set; }
    public DateTime? PlayDate { get; set; }
    public string? Position { get; set; }
    public string FeeFilter { get; set; } = "all"; // "all" | "split" | "paid"

    public List<LoanListingCardDto> TeamRecruitingListings { get; set; } = new();
    public List<LoanListingCardDto> PlayerAvailableListings { get; set; } = new();

    public List<Team> ManagedTeams { get; set; } = new();
    public List<MatchGame> UpcomingManagedMatches { get; set; } = new();
    public int CurrentUserAbCoin { get; set; }
    public PlayerScoutProfileDto? CurrentUserScout { get; set; }
}
