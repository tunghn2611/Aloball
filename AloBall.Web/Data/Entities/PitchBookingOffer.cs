using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AloBall.Web.Data.Entities;

public enum PitchBookingOfferStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
    NotSelected = 3,
    Withdrawn = 4
}

public class PitchBookingOffer
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestId { get; set; }
    public PitchBookingRequest Request { get; set; } = null!;

    [Required]
    public string AdminId { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string ConfirmedVenueName { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string ConfirmedAddress { get; set; } = string.Empty;

    public DateTime ConfirmedDate { get; set; }
    public TimeSpan ConfirmedStartTime { get; set; }
    public TimeSpan ConfirmedEndTime { get; set; }

    [Required, MaxLength(100)]
    public string ConfirmedPitchFormat { get; set; } = "7 người";

    [Range(1, int.MaxValue)]
    public int ConfirmedPitchCount { get; set; } = 1;

    [MaxLength(50)]
    public string? SpecificFieldCode { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalPriceVND { get; set; }

    [MaxLength(1000)]
    public string? DepositInstructions { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public PitchBookingOfferStatus Status { get; set; } = PitchBookingOfferStatus.Pending;

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}
