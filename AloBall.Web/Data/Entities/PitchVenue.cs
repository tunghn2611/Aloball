using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AloBall.Web.Data.Entities;

public class PitchVenue
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Required, MaxLength(100)]
    public string City { get; set; } = "Hà Nội";

    [Required, MaxLength(100)]
    public string District { get; set; } = "Cầu Giấy";

    [Required, MaxLength(500)]
    public string FullAddress { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string ContactPhone { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string SupportedFormats { get; set; } = "7 người";

    [Required, MaxLength(100)]
    public string SurfaceType { get; set; } = "Cỏ nhân tạo";

    [MaxLength(500)]
    public string? Facilities { get; set; }

    [MaxLength(100)]
    public string? OpeningHours { get; set; } = "06:00 - 23:00";

    [Column(TypeName = "decimal(18,2)")]
    public decimal? IndicativePricePerHour { get; set; }

    [MaxLength(500)]
    public string? PriceNotes { get; set; }

    public string? ImageUrl { get; set; }

    public string? MapLink { get; set; }

    public int TotalPitches { get; set; } = 4;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PitchBookingRequest> BookingRequests { get; set; } = new List<PitchBookingRequest>();
}
