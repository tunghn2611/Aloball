using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AloBall.Web.Data.Entities;

public enum RefereeRequestStatus
{
    Pending = 0,
    UnderReview = 1,
    AwaitingTeamDecision = 2,
    Confirmed = 3,
    Cancelled = 4,
    UnableToFulfill = 5,
    Completed = 6
}

public class RefereeRequest
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    // Team that creates the request
    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    // Associated match (optional but required for pre‑fill)
    public Guid? MatchId { get; set; }
    // Assuming a Match entity exists named MatchGame
    public MatchGame? Match { get; set; }

    public DateTime ServiceDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }

    [Required, MaxLength(200)]
    public string VenueName { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string VenueAddress { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string MatchFormat { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int RefereeCount { get; set; }

    // Optional fields
    [MaxLength(500)]
    public string? PreferredExperience { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? BudgetVND { get; set; }

    public string? Notes { get; set; }

    public RefereeRequestStatus Status { get; set; } = RefereeRequestStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<RefereeProposal> Proposals { get; set; } = new List<RefereeProposal>();
}
