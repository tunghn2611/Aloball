using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AloBall.Web.Data.Entities;

public enum PlanTier
{
    Free = 0,
    Plus = 1,
    Pro = 2
}

public enum ConciergeOrderSource
{
    Concierge = 0,   // Thi đấu trọn gói A–Z
    AutoPairing = 1  // Xếp cặp tự động lịch cố định
}

public enum ConciergeOrderStatus
{
    Proposed = 0,    // Kèo tự động chờ xác nhận
    Fulfilled = 1,   // Đã tạo trận trên Matchday Center
    Failed = 2,      // Không tìm được đối / sân / trọng tài
    Cancelled = 3
}

// Lịch cố định & tiêu chuẩn đối thủ cho xếp cặp tự động (1 đội = 1 lịch)
public class TeamRecurringSchedule
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    public bool IsAutoPairingEnabled { get; set; }

    // Bit i = DayOfWeek i (Chủ nhật = 0 ... Thứ 7 = 6)
    public int DaysOfWeekMask { get; set; }

    // Giờ bóng lăn theo giờ Việt Nam (Asia/Ho_Chi_Minh)
    public TimeSpan PreferredKickoffTime { get; set; } = new(19, 0, 0);

    [Required, MaxLength(100)]
    public string Region { get; set; } = string.Empty;

    public double MinAloballStars { get; set; } = 0;
    public double MaxAloballStars { get; set; } = 100;
    public double MinFriendlyRating { get; set; } = 3.5;
    public bool AvoidHardTackle { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// Đơn "Thi đấu trọn gói" / kèo ghép cặp tự động
public class MatchConciergeOrder
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public ConciergeOrderSource Source { get; set; } = ConciergeOrderSource.Concierge;

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    [Required, MaxLength(100)]
    public string Region { get; set; } = string.Empty;

    public DateTime KickoffAt { get; set; } // Giờ Việt Nam (đồng bộ MatchGame.MatchDateTime)

    public double MinStars { get; set; }
    public double MaxStars { get; set; } = 100;
    public double MinFriendlyRating { get; set; } = 3.5;
    public bool IsProWithReferee { get; set; }

    public Guid? MatchedOpponentTeamId { get; set; }
    public Team? MatchedOpponentTeam { get; set; }

    public Guid? BookedPitchSlotId { get; set; }   // PitchBookingRequest.Id
    public Guid? AssignedRefereeId { get; set; }   // Referee.Id
    public Guid? ResultMatchId { get; set; }       // MatchGame.Id

    [Column(TypeName = "decimal(18,2)")]
    public decimal EstimatedTotalFee { get; set; }

    public ConciergeOrderStatus Status { get; set; } = ConciergeOrderStatus.Proposed;

    [MaxLength(500)]
    public string? FailureReason { get; set; }

    public string? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
