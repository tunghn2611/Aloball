using AloBall.Web.Data.Entities;

namespace AloBall.Web.Models;

public class TeamReputationStats
{
    public int TotalOfficialMatches { get; set; }
    public int Wins { get; set; }
    public int Draws { get; set; }
    public int Losses { get; set; }
    public double WinPercentage { get; set; }
    public int GoalsScored { get; set; }
    public int GoalsConceded { get; set; }
    public int GoalDifference => GoalsScored - GoalsConceded;

    // Invitation metrics (12 months)
    public int TotalEligibleInvitations { get; set; }
    public int RespondedInvitations { get; set; }
    public int AcceptedInvitations { get; set; }
    public double ResponseRatePercentage { get; set; }
    public double AcceptanceRatePercentage { get; set; }
    public bool HasInvitationData => TotalEligibleInvitations > 0;
}

public class PlayerPublicProfileViewModel
{
    public Guid PlayerId { get; set; }
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string TeamSlug { get; set; } = string.Empty;
    public string? TeamLogoUrl { get; set; }
    public bool CanShowTeam { get; set; } = true;

    // Public display fields (filtered according to privacy)
    public string DisplayName { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public bool ShowPhoto { get; set; }
    public int? KitNumber { get; set; }
    public bool ShowKitNumber { get; set; }
    public string? Position { get; set; }
    public bool ShowPosition { get; set; }
    public string? PreferredFoot { get; set; }
    public bool ShowPreferredFoot { get; set; }
    public string? Experience { get; set; }
    public bool ShowExperience { get; set; }
    public string? Region { get; set; }
    public double? HeightCm { get; set; }
    public double? WeightKg { get; set; }
    public bool ShowHeightWeight { get; set; }
    public bool IsLinkedUser { get; set; }

    // Privacy settings (for owner/manager editing)
    public bool IsPublicProfile { get; set; } = true;
    public bool PublicKitNumber { get; set; } = true;
    public bool PublicPosition { get; set; } = true;
    public bool PublicHeightWeight { get; set; } = false;
    public bool PublicPhoto { get; set; } = true;
    public bool PublicPreferredFoot { get; set; } = false;
    public bool PublicExperience { get; set; } = false;

    // Official Performance Stats (from verified matches only)
    public int OfficialAppearances { get; set; }
    public int OfficialGoals { get; set; }
    public int OfficialAssists { get; set; }
    public double AttendanceRatePercentage { get; set; }
    public int TotalAttendanceSessions { get; set; }
    public int AttendedSessions { get; set; }
    public bool HasAttendanceData => TotalAttendanceSessions > 0;

    // Recent official match contributions
    public List<PlayerMatchContributionItem> RecentMatches { get; set; } = new();

    // Permissions
    public bool IsSelf { get; set; }
    public bool IsManager { get; set; }
}

public class PlayerMatchContributionItem
{
    public Guid MatchId { get; set; }
    public DateTime MatchDateTime { get; set; }
    public string OpponentName { get; set; } = string.Empty;
    public string PitchName { get; set; } = string.Empty;
    public int TeamScore { get; set; }
    public int OpponentScore { get; set; }
    public bool HasActuallyPlayed { get; set; }
    public bool IsStarting { get; set; }
    public int GoalsInMatch { get; set; }
    public int AssistsInMatch { get; set; }
}

public class PlayerPrivacyUpdateInput
{
    public Guid PlayerId { get; set; }
    public bool IsPublicProfile { get; set; }
    public bool PublicKitNumber { get; set; }
    public bool PublicPosition { get; set; }
    public bool PublicHeightWeight { get; set; }
    public bool PublicPhoto { get; set; }
    public bool PublicPreferredFoot { get; set; }
    public bool PublicExperience { get; set; }
}
