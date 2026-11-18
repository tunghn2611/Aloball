using AloBall.Web.Data.Entities;

namespace AloBall.Web.Models;

public class ConciergeOrderInput
{
    public Guid TeamId { get; set; }
    public DateTime PlayDate { get; set; }
    public TimeSpan KickoffTime { get; set; } = new(19, 0, 0);
    public string Region { get; set; } = string.Empty;
    public double MinStars { get; set; } = 0;
    public double MaxStars { get; set; } = 100;
    public double MinFriendlyRating { get; set; } = 3.5;
    public bool IsProWithReferee { get; set; } = false;
    public bool AvoidHardTackle { get; set; } = true;
}

public class RecurringScheduleInput
{
    public Guid TeamId { get; set; }
    public bool IsAutoPairingEnabled { get; set; } = true;
    public int[]? SelectedDays { get; set; }
    public int DaysOfWeekMask { get; set; }
    public TimeSpan PreferredKickoffTime { get; set; } = new(19, 0, 0);
    public string Region { get; set; } = string.Empty;
    public double MinAloballStars { get; set; } = 0;
    public double MaxAloballStars { get; set; } = 100;
    public double MinFriendlyRating { get; set; } = 3.5;
    public bool AvoidHardTackle { get; set; } = true;
}

public class ConciergeOrderCardDto
{
    public Guid Id { get; set; }
    public ConciergeOrderSource Source { get; set; }
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public DateTime KickoffAt { get; set; }
    public double MinStars { get; set; }
    public double MaxStars { get; set; }
    public double MinFriendlyRating { get; set; }
    public bool IsProWithReferee { get; set; }
    public ConciergeOrderStatus Status { get; set; }
    public decimal EstimatedTotalFee { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }

    public Guid? MatchedOpponentTeamId { get; set; }
    public string? MatchedOpponentName { get; set; }
    public string? MatchedOpponentSlug { get; set; }
    public int MatchedOpponentStars { get; set; }
    public double MatchedOpponentRating { get; set; }
    public double MatchedOpponentHardTacklePct { get; set; }

    public Guid? BookedPitchSlotId { get; set; }
    public string? BookedPitchName { get; set; }
    public string? BookedPitchAddress { get; set; }
    public string? BookedFieldCode { get; set; }
    public decimal PitchFeeVnd { get; set; }

    public Guid? AssignedRefereeId { get; set; }
    public string? AssignedRefereeName { get; set; }
    public decimal RefereeFeeVnd { get; set; }

    public Guid? ResultMatchId { get; set; }
    public string? ResultMatchRsvpToken { get; set; }

    public string ZaloSummaryMessage { get; set; } = string.Empty;
}

public class ConciergeWorkspaceViewModel
{
    public string ActiveTab { get; set; } = "concierge"; // "concierge" | "autopair"
    public bool IsAuthenticated { get; set; }
    public List<Team> ManagedTeams { get; set; } = new();
    public Guid? SelectedTeamId { get; set; }
    public Team? SelectedTeam { get; set; }
    public int SelectedTeamStars { get; set; }
    public double SelectedTeamHardTacklePct { get; set; }

    public TeamRecurringSchedule? RecurringSchedule { get; set; }
    public ConciergeOrderInput OrderInput { get; set; } = new();
    public RecurringScheduleInput ScheduleInput { get; set; } = new();

    public List<ConciergeOrderCardDto> ConciergeOrders { get; set; } = new();
    public List<ConciergeOrderCardDto> AutoPairingOrders { get; set; } = new();
    public List<string> SuggestedRegions { get; set; } = new();
}
