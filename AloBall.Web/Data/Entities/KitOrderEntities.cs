using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AloBall.Web.Data.Entities;

public enum KitBatchStatus
{
    Open = 0,       // Đang mở đăng ký
    Closed = 1,     // Đã chốt danh sách
    Ordered = 2,    // Đã đặt xưởng
    Delivered = 3   // Đã phát xong
}

public class KitOrderBatch
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    [Required, MaxLength(100)]
    public string BatchName { get; set; } = "Đợt đặt áo đấu mới";

    public string? Description { get; set; }
    public decimal UnitPrice { get; set; } = 0m;

    public KitBatchStatus Status { get; set; } = KitBatchStatus.Open;
    public DateTime? ClosedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<KitOrderItem> Items { get; set; } = new List<KitOrderItem>();
}

public class KitOrderItem
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BatchId { get; set; }
    public KitOrderBatch Batch { get; set; } = null!;

    public Guid? PlayerId { get; set; }
    public Player? Player { get; set; }

    // Snapshot thông tin đặt tại thời điểm chốt (không bị đổi khi Player profile đổi)
    [Required, MaxLength(100)]
    public string PlayerName { get; set; } = string.Empty;
    public string? ShirtName { get; set; }
    public int? KitNumber { get; set; }
    public string? ShirtSize { get; set; }
    public string? ShortSize { get; set; }
    public int Quantity { get; set; } = 1;

    public decimal Amount { get; set; } = 0m;
    public bool IsPaid { get; set; } = false;
    public string? Notes { get; set; }
}
