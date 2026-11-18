using System.ComponentModel.DataAnnotations;
using AloBall.Web.Data.Entities;

namespace AloBall.Web.Models;

public class CreateFlashDealInput
{
    [Required]
    public Guid PitchId { get; set; }

    [MaxLength(50)]
    public string? FieldCode { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn ngày và giờ bóng lăn.")]
    public DateTime KickoffAt { get; set; }

    [Range(30, 240, ErrorMessage = "Thời lượng trận phải từ 30 đến 240 phút.")]
    public int DurationMinutes { get; set; } = 90;

    [Range(1, 50_000_000, ErrorMessage = "Giá gốc sân phải lớn hơn 0.")]
    public decimal OriginalPriceVnd { get; set; } = 700_000m;

    [Range(1, 50_000_000, ErrorMessage = "Giá Flash Deal phải lớn hơn 0.")]
    public decimal DealPriceVnd { get; set; } = 450_000m;

    [MaxLength(500)]
    public string? Note { get; set; }
}

public class RegisterPartnerVenueInput
{
    [Required(ErrorMessage = "Vui lòng nhập tên cụm sân.")]
    [StringLength(200, MinimumLength = 3)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tỉnh/thành phố.")]
    [MaxLength(100)]
    public string City { get; set; } = "Hà Nội";

    [Required(ErrorMessage = "Vui lòng nhập quận/huyện.")]
    [MaxLength(100)]
    public string District { get; set; } = "Cầu Giấy";

    [Required(ErrorMessage = "Vui lòng nhập địa chỉ sân.")]
    [MaxLength(500)]
    public string FullAddress { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại liên hệ.")]
    [MaxLength(50)]
    public string ContactPhone { get; set; } = string.Empty;

    [Range(100_000, 10_000_000)]
    public decimal IndicativePricePerHour { get; set; } = 600_000m;

    [Range(1, 30)]
    public int TotalPitches { get; set; } = 4;
}

public class RegisterRefereePartnerInput
{
    [Required(ErrorMessage = "Vui lòng nhập họ tên trọng tài.")]
    [StringLength(150, MinimumLength = 3)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại / liên hệ.")]
    [MaxLength(250)]
    public string ContactInfo { get; set; } = string.Empty;

    [MaxLength(500)]
    public string ExperienceSummary { get; set; } = "Trọng tài bóng đá phủi sân 7 / 11.";

    [Range(100_000, 5_000_000)]
    public decimal DefaultPriceVND { get; set; } = 350_000m;
}

public class PitchFlashDealCardDto
{
    public Guid Id { get; set; }
    public Guid PitchId { get; set; }
    public string PitchName { get; set; } = string.Empty;
    public string PitchAddress { get; set; } = string.Empty;
    public string? FieldCode { get; set; }
    public string Region { get; set; } = string.Empty;
    public DateTime KickoffAt { get; set; }
    public int DurationMinutes { get; set; }
    public decimal OriginalPriceVnd { get; set; }
    public decimal DealPriceVnd { get; set; }
    public int DiscountPercent { get; set; }
    public string? Note { get; set; }
    public PitchFlashDealStatus Status { get; set; }
    public Guid? ClaimedByTeamId { get; set; }
    public string? ClaimedByTeamName { get; set; }
    public Guid? ClaimedMatchId { get; set; }
    public bool IsUserRegionMatch { get; set; }
    public string ZaloShareText { get; set; } = string.Empty;
}

public class PartnerBookingRowDto
{
    public Guid RequestId { get; set; }
    public string PitchName { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public DateTime ServiceDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string PitchFormat { get; set; } = string.Empty;
    public PitchBookingStatus Status { get; set; }
    public string? ConfirmedFieldCode { get; set; }
    public decimal? AcceptedPriceVnd { get; set; }
}

public class RefereeAssignmentItemDto
{
    public Guid AssignmentId { get; set; }
    public Guid RequestId { get; set; }
    public Guid? MatchId { get; set; }
    public string SourceTag { get; set; } = "Điều phối";
    public string MatchTitle { get; set; } = string.Empty;
    public string VenueName { get; set; } = string.Empty;
    public DateTime KickoffAt { get; set; }
    public decimal FeeVnd { get; set; }
    public bool IsCompleted { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
}

public class FlashDealBannerViewModel
{
    public List<PitchFlashDealCardDto> Deals { get; set; } = new();
    public List<Team> ManagedTeams { get; set; } = new();
    public string? PreferredRegion { get; set; }
    public bool IsAuthenticated { get; set; }
}

public class PartnerPortalViewModel
{
    public string ActiveTab { get; set; } = "pitch"; // "pitch" | "referee"
    public bool IsAuthenticated { get; set; }
    public bool IsPlatformAdmin { get; set; }

    // Pitch Owner Section
    public List<PitchVenue> OwnedVenues { get; set; } = new();
    public Guid? SelectedPitchId { get; set; }
    public int WeeklyBookingCount { get; set; }
    public decimal GrossRevenueVnd { get; set; }
    public int ActiveDealsCount { get; set; }
    public List<PartnerBookingRowDto> Bookings { get; set; } = new();
    public List<PitchFlashDealCardDto> OwnerFlashDeals { get; set; } = new();
    public List<PitchFlashDealCardDto> MarketplaceFlashDeals { get; set; } = new();
    public CreateFlashDealInput CreateDealInput { get; set; } = new();

    // Referee Partner Section
    public List<Referee> RefereeProfiles { get; set; } = new();
    public Referee? ActiveRefereeProfile { get; set; }
    public int CompletedRefereeMatches { get; set; }
    public decimal RefereeTotalEarningsVnd { get; set; }
    public List<RefereeAssignmentItemDto> UpcomingAssignments { get; set; } = new();
    public List<RefereeAssignmentItemDto> PastAssignments { get; set; } = new();

    // Captain claiming support
    public List<Team> ManagedTeams { get; set; } = new();
}
