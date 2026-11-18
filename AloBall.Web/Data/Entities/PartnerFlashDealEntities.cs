using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AloBall.Web.Data.Entities;

public enum PitchFlashDealStatus
{
    Active = 0,
    Claimed = 1,
    Expired = 2
}

public class PitchFlashDeal
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PitchId { get; set; }
    public PitchVenue Pitch { get; set; } = null!;

    [MaxLength(50)]
    public string? FieldCode { get; set; }

    [Required, MaxLength(100)]
    public string Region { get; set; } = string.Empty;

    public DateTime KickoffAt { get; set; } // Asia/Ho_Chi_Minh local wall-clock time

    [Range(30, 240)]
    public int DurationMinutes { get; set; } = 90;

    [Column(TypeName = "decimal(18,2)")]
    public decimal OriginalPriceVnd { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal DealPriceVnd { get; set; }

    public int DiscountPercent { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public Guid? ClaimedByTeamId { get; set; }
    public Team? ClaimedByTeam { get; set; }

    public Guid? ClaimedMatchId { get; set; }
    public MatchGame? ClaimedMatch { get; set; }

    public PitchFlashDealStatus Status { get; set; } = PitchFlashDealStatus.Active;

    [Required]
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClaimedAt { get; set; }

    [ConcurrencyCheck]
    public int ClaimVersion { get; set; } = 0;
}
