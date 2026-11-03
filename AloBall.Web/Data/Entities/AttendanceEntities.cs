using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AloBall.Web.Data.Entities;

public enum ResponseStatus
{
    NotRecorded = 0,   // Chưa ghi nhận
    Attending = 1,     // Xác nhận tham gia
    Excused = 2,       // Báo vắng
    NoResponse = 3     // Không phản hồi
}

public enum ActualStatus
{
    NotChecked = 0,    // Chưa điểm danh
    Present = 1,       // Có mặt
    Absent = 2         // Vắng
}

public enum SessionStatus
{
    Draft = 0,
    Finalized = 1,
    Cancelled = 2
}

public class TeamRegulation
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    public int Version { get; set; } = 1;
    public string Title { get; set; } = "Quy chế chuyên cần";

    // Phạt không xác nhận (mặc định 30,000 VND theo mẫu KLB)
    public decimal FineNoResponse { get; set; } = 30000m;

    // Phạt xác nhận mà vắng (mặc định 50,000 VND theo mẫu KLB)
    public decimal FineAttendingButAbsent { get; set; } = 50000m;

    // Ngưỡng tối thiểu số trận / tháng (mặc định 2 trận)
    public int MinMatchesPerMonth { get; set; } = 2;

    // Phạt thiếu chuyên cần theo tháng (mặc định 200,000 VND)
    public decimal FineUnderMinAttendance { get; set; } = 200000m;

    // Hạn xác nhận mặc định: số giờ trước trận hoặc giờ trong ngày (ví dụ: 12h trưa ngày đấu)
    public TimeSpan DefaultDeadlineTimeOfDay { get; set; } = new TimeSpan(12, 0, 0);

    // Quỹ định kỳ theo quý (tham khảo)
    public decimal QuarterlyFundDue { get; set; } = 300000m;

    // Báo vắng đúng hạn có được miễn không? (mặc định: có miễn vi phạm 30k)
    public bool IsExcusedExemptFromFine { get; set; } = true;

    // Cộng dồn khoản phạt buổi và phạt thiếu chuyên cần? (mặc định: true)
    public bool CombineMonthlyAndSessionFines { get; set; } = true;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AttendanceSession
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    [Required, MaxLength(100)]
    public string Title { get; set; } = "Buổi tập / Thi đấu";

    public DateTime SessionDateTime { get; set; }
    public DateTime ResponseDeadline { get; set; }

    [Required, MaxLength(7)]
    public string StatisticalPeriod { get; set; } = "2026-09"; // YYYY-MM (Ví dụ 2026-09)

    public SessionStatus Status { get; set; } = SessionStatus.Draft;

    // Địa điểm sân đấu
    [MaxLength(150)]
    public string PitchName { get; set; } = string.Empty;
    [MaxLength(50)]
    public string PitchType { get; set; } = "7 người";

    // Liên kết trận đấu (nếu có)
    public Guid? MatchId { get; set; }
    public MatchGame? Match { get; set; }

    public string? Notes { get; set; }
    public int Revision { get; set; } = 1; // Phiên bản cập nhật cho Idempotency và Sổ AB
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AttendanceRecord> Records { get; set; } = new List<AttendanceRecord>();
}

public class AttendanceRecord
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SessionId { get; set; }
    public AttendanceSession Session { get; set; } = null!;

    public Guid PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    // 1. Phản hồi
    public ResponseStatus Response { get; set; } = ResponseStatus.NotRecorded;
    public DateTime? ResponseTime { get; set; }
    public bool IsResponseLate { get; set; } = false;

    // 2. Thực tế
    public ActualStatus Actual { get; set; } = ActualStatus.NotChecked;

    // Tính toán theo quy chế
    public decimal FineAmount { get; set; } = 0m;
    public bool NeedsConfirmation { get; set; } = false; // "Cần xác nhận" khi có tình huống chưa rõ
    public string? PenaltyReason { get; set; }

    // Miễn giảm / Điều chỉnh
    public bool IsExempt { get; set; } = false; // BTC hoặc duyệt miễn
    public string? ExemptionReason { get; set; }
    public string? AdjustedByUserId { get; set; }
    public DateTime? AdjustedAt { get; set; }

    // Đối soát nộp quỹ
    public bool IsPaid { get; set; } = false;
    public DateTime? PaidAt { get; set; }
}

public class MonthlyAttendanceClosing
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    [Required, MaxLength(7)]
    public string StatisticalPeriod { get; set; } = string.Empty; // YYYY-MM

    public bool IsClosed { get; set; } = false;
    public DateTime? ClosedAt { get; set; }
    public string? ClosedByUserId { get; set; }

    // Snapshot version quy chế áp dụng lúc chốt
    public int RegulationVersion { get; set; } = 1;
    public decimal RegulationMinMatches { get; set; } = 2;
    public decimal RegulationUnderAttendanceFine { get; set; } = 200000m;

    // Lịch sử mở lại
    public string? ReopenReason { get; set; }
    public DateTime? ReopenedAt { get; set; }
    public string? ReopenedByUserId { get; set; }

    public ICollection<MonthlyPlayerSummary> Summaries { get; set; } = new List<MonthlyPlayerSummary>();
}

public class MonthlyPlayerSummary
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MonthlyClosingId { get; set; }
    public MonthlyAttendanceClosing MonthlyClosing { get; set; } = null!;

    public Guid PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    public int TotalSessionsInMonth { get; set; }
    public int AttendedSessionsCount { get; set; }
    public bool IsBtc { get; set; } = false;

    public decimal TotalSessionFines { get; set; } = 0m;
    public decimal UnderAttendanceFine { get; set; } = 0m;
    public decimal TotalDue { get; set; } = 0m;
    public decimal TotalPaid { get; set; } = 0m;

    public string? Notes { get; set; }
}
