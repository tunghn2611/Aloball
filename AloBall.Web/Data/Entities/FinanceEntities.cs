using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AloBall.Web.Data.Entities;

public enum SettlementStatus
{
    Draft = 0,
    Confirmed = 1,
    Locked = 2
}

public enum SettlementExpenseCategory
{
    Pitch = 0,        // Sân bóng
    Referee = 1,      // Trọng tài
    Refreshment = 2,  // Nước uống & điện giải
    Media = 3,        // Truyền thông, quay phim, chụp ảnh
    Other = 4         // Chi phí khác
}

public enum PaymentStatus
{
    Unpaid = 0,         // Chưa thanh toán
    PartiallyPaid = 1,  // Đã nộp một phần
    Paid = 2            // Đã hoàn tất
}

public enum PaymentMethod
{
    Cash = 0,          // Tiền mặt
    BankTransfer = 1,  // Chuyển khoản
    Other = 2          // Phương thức khác
}

// Bảng dự toán chi phí phụ cho trận đấu (nhập trước trận để dự báo chi phí)
public class MatchExpenseEstimate
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MatchId { get; set; }
    public MatchGame Match { get; set; } = null!;

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    [Required, MaxLength(200)]
    public string Description { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal EstimatedAmount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string CreatedByUserId { get; set; } = string.Empty;
}

// Quyết toán chi phí trận đấu (Snapshot bất biến sau khi chốt)
public class MatchSettlement
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MatchId { get; set; }
    public MatchGame Match { get; set; } = null!;

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    public DateTime SettlementDate { get; set; } = DateTime.UtcNow;

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalPitchCost { get; set; } = 0m;

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalRefereeCost { get; set; } = 0m;

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalOtherExpenses { get; set; } = 0m;

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; } = 0m;

    public int ParticipantCount { get; set; } = 0;

    [Column(TypeName = "decimal(18,2)")]
    public decimal PerPersonShare { get; set; } = 0m;

    public SettlementStatus Status { get; set; } = SettlementStatus.Confirmed;

    public DateTime? ConfirmedAt { get; set; } = DateTime.UtcNow;

    public string? ConfirmedByUserId { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<MatchSettlementItem> Items { get; set; } = new List<MatchSettlementItem>();
    public ICollection<MatchSettlementMemberAllocation> Allocations { get; set; } = new List<MatchSettlementMemberAllocation>();
}

// Từng khoản chi thực tế trong quyết toán
public class MatchSettlementItem
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SettlementId { get; set; }
    public MatchSettlement Settlement { get; set; } = null!;

    public SettlementExpenseCategory Category { get; set; } = SettlementExpenseCategory.Other;

    [Required, MaxLength(250)]
    public string Description { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    public bool IsFromConfirmedService { get; set; } = false;

    public string? SourceReferenceId { get; set; }
}

// Phân bổ chi phí cho từng thành viên tham gia
public class MatchSettlementMemberAllocation
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SettlementId { get; set; }
    public MatchSettlement Settlement { get; set; } = null!;

    public Guid PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    public string? UserId { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CalculatedAmount { get; set; } // Chia đều cơ sở

    [Column(TypeName = "decimal(18,2)")]
    public decimal AdjustedAmount { get; set; }   // Số tiền thực thu sau làm tròn hoặc điều chỉnh

    [MaxLength(500)]
    public string? AdjustmentReason { get; set; } // Bắt buộc nếu có điều chỉnh khác số gốc

    [Column(TypeName = "decimal(18,2)")]
    public decimal AmountPaid { get; set; } = 0m;

    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

    public DateTime? LastPaymentDate { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    // Navigation
    public ICollection<MatchSettlementPayment> Payments { get; set; } = new List<MatchSettlementPayment>();
    public ICollection<MatchSettlementAdjustmentAudit> AdjustmentAudits { get; set; } = new List<MatchSettlementAdjustmentAudit>();

    [NotMapped]
    public decimal RemainingBalance => Math.Max(0m, AdjustedAmount - AmountPaid);
}

// Bản ghi nhận tiền đóng của cầu thủ
public class MatchSettlementPayment
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AllocationId { get; set; }
    public MatchSettlementMemberAllocation Allocation { get; set; } = null!;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.BankTransfer;

    [MaxLength(100)]
    public string? ReferenceCode { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    [Required]
    public string RecordedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Đảo khoản thu (Auditable Reversal) thay vì xóa âm thầm
    public bool IsReversed { get; set; } = false;

    [MaxLength(500)]
    public string? ReversalReason { get; set; }

    public DateTime? ReversedAt { get; set; }

    public string? ReversedByUserId { get; set; }
}

// Lịch sử kiểm toán khi điều chỉnh số tiền phân bổ thủ công
public class MatchSettlementAdjustmentAudit
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AllocationId { get; set; }
    public MatchSettlementMemberAllocation Allocation { get; set; } = null!;

    [Column(TypeName = "decimal(18,2)")]
    public decimal PreviousAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal NewAmount { get; set; }

    [Required, MaxLength(500)]
    public string MandatoryReason { get; set; } = string.Empty;

    [Required]
    public string AdjustedByUserId { get; set; } = string.Empty;

    public DateTime AdjustedAt { get; set; } = DateTime.UtcNow;
}
