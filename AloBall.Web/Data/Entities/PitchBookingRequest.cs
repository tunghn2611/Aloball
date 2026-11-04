using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AloBall.Web.Data.Entities;

public enum PitchBookingStatus
{
    Pending = 0,
    UnderReview = 1,
    AwaitingTeamDecision = 2,
    Confirmed = 3,
    Cancelled = 4,
    UnableToFulfill = 5,
    Completed = 6
}

public class PitchBookingRequest
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    public Guid PitchVenueId { get; set; }
    public PitchVenue PitchVenue { get; set; } = null!;

    public Guid? MatchId { get; set; }
    public MatchGame? Match { get; set; }

    public DateTime ServiceDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }

    [Required, MaxLength(100)]
    public string PitchFormat { get; set; } = "7 người";

    [Range(1, int.MaxValue)]
    public int PitchCount { get; set; } = 1;

    [Column(TypeName = "decimal(18,2)")]
    public decimal? BudgetVND { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public PitchBookingStatus Status { get; set; } = PitchBookingStatus.Pending;

    [MaxLength(50)]
    public string? ConfirmedFieldCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<PitchBookingOffer> Offers { get; set; } = new List<PitchBookingOffer>();
}
