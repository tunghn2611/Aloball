using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AloBall.Web.Data.Entities;

public enum LoanListingType
{
    PlayerAvailable = 0, // Cầu thủ rảnh lịch muốn đá ké / đá thuê
    TeamRecruiting = 1   // Đội bóng sắp thi đấu nhưng thiếu người, đăng tuyển vị trí cụ thể
}

public enum LoanFeeType
{
    SplitPitch = 0,      // Share tiền sân (0đ)
    PaidMercenary = 1    // Phí mượn + share/miễn tiền sân
}

public enum LoanListingStatus
{
    Open = 0,            // Đang mở
    Matched = 1,         // Đã chốt kèo mượn
    Completed = 2,       // Hoàn thành & đã giải phóng cọc AB Coin
    Cancelled = 3        // Đã hủy
}

public enum LoanApplicationStatus
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2,
    Cancelled = 3
}

public class PlayerLoanListing
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public LoanListingType ListingType { get; set; } = LoanListingType.TeamRecruiting;

    // Nullable if team-posted, hoặc cầu thủ đăng tin rảnh lịch
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public Guid? PlayerId { get; set; }
    public Player? Player { get; set; }

    // Nullable if free-agent player, hoặc đội bóng đăng tuyển
    public Guid? TeamId { get; set; }
    public Team? Team { get; set; }

    // Nullable link to an existing Matchday
    public Guid? MatchId { get; set; }
    public MatchGame? Match { get; set; }

    [Required, MaxLength(100)]
    public string Region { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? PitchName { get; set; }

    public DateTime PlayDate { get; set; }

    [Required, MaxLength(50)]
    public string TimeSlot { get; set; } = "19:00 - 20:30";

    [Required, MaxLength(50)]
    public string PositionsNeeded { get; set; } = "MID"; // GK, DEF, MID, FWD

    public LoanFeeType FeeType { get; set; } = LoanFeeType.SplitPitch;

    [Column(TypeName = "decimal(18,2)")]
    public decimal LoanFeeAmount { get; set; } = 0m; // 0 to 1,000,000 VND

    public int EscrowAbCoin { get; set; } = 0; // Optional AB Coin deposit to prevent last-minute no-shows

    public Guid? EscrowJournalEntryId { get; set; }
    public bool IsEscrowReleased { get; set; } = false;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public LoanListingStatus Status { get; set; } = LoanListingStatus.Open;

    public Guid? AcceptedApplicationId { get; set; }
    public Guid? LoanedGuestPlayerId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? MatchedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ICollection<PlayerLoanApplication> Applications { get; set; } = new List<PlayerLoanApplication>();
}

public class PlayerLoanApplication
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ListingId { get; set; }
    public PlayerLoanListing Listing { get; set; } = null!;

    [Required]
    public string ApplicantUserId { get; set; } = string.Empty;
    public ApplicationUser ApplicantUser { get; set; } = null!;

    public Guid? ApplicantPlayerId { get; set; }
    public Player? ApplicantPlayer { get; set; }

    public Guid? ApplicantTeamId { get; set; }
    public Team? ApplicantTeam { get; set; }

    public Guid? TargetMatchId { get; set; }
    public MatchGame? TargetMatch { get; set; }

    [Required, MaxLength(50)]
    public string Position { get; set; } = "MID"; // GK, DEF, MID, FWD

    [MaxLength(500)]
    public string? Message { get; set; }

    public LoanApplicationStatus Status { get; set; } = LoanApplicationStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }
}
