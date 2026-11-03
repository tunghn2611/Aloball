using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AloBall.Web.Data.Entities;

public enum PitchArrangement
{
    HomePitch = 0,    // Đã có sân nhà
    AwayPitch = 1,    // Đi khách
    Flexible = 2      // Linh hoạt thương lượng
}

public enum MatchmakingStatus
{
    Open = 0,         // Đang tìm đối
    Locked = 1,       // Đã chốt đối
    Cancelled = 2,    // Đã hủy
    Suspended = 3     // Bị tạm ngưng do đội bị hạn chế (Blacklist)
}

public enum MatchmakingPostType
{
    Public = 0,       // Kèo công khai trên sàn tìm đối chung
    DirectChallenge = 1 // Thách đấu trực tiếp đích danh 1 đội bóng
}

public class MatchmakingPost
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public MatchmakingPostType PostType { get; set; } = MatchmakingPostType.Public;

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    // Nếu là thách đấu trực tiếp thì có đội mục tiêu
    public Guid? TargetTeamId { get; set; }
    public Team? TargetTeam { get; set; }

    public DateTime MatchDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }

    [Required, MaxLength(100)]
    public string Area { get; set; } = string.Empty; // Khu vực: Cầu Giấy, Nam Từ Liêm...

    public PitchArrangement PitchArrangement { get; set; } = PitchArrangement.Flexible;
    public string? PitchName { get; set; }
    public bool IsPitchBooked { get; set; } = false;

    public string PitchType { get; set; } = "7 người"; // 5, 7, 11
    public string DesiredLevel { get; set; } = "Trung bình - Khá"; // Mới đá, Trung bình, Khá, Phủi cứng

    public string CostSharing { get; set; } = "50-50"; // 50-50, Thua trả, Tiền sân chia đôi tiền nước ai nấy trả
    public string? Notes { get; set; }

    public MatchmakingStatus Status { get; set; } = MatchmakingStatus.Open;

    // Khi đã chốt
    public Guid? AcceptedProposalId { get; set; }
    public Guid? CreatedMatchId { get; set; }
    public MatchGame? CreatedMatch { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Concurrency check
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    public ICollection<MatchmakingProposal> Proposals { get; set; } = new List<MatchmakingProposal>();
}

public enum ProposalStatus
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2,
    Cancelled = 3
}

public class MatchmakingProposal
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PostId { get; set; }
    public MatchmakingPost Post { get; set; } = null!;

    public Guid ChallengingTeamId { get; set; }
    public Team ChallengingTeam { get; set; } = null!;

    public string ProposedByUserId { get; set; } = string.Empty;
    public string? Message { get; set; }
    public ProposalStatus Status { get; set; } = ProposalStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }
}

public class TeamMatchReview
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MatchId { get; set; }
    public MatchGame Match { get; set; } = null!;

    public Guid ReviewerTeamId { get; set; }
    public Team ReviewerTeam { get; set; } = null!;

    public Guid TargetTeamId { get; set; }
    public Team TargetTeam { get; set; } = null!;

    public string SubmittedByUserId { get; set; } = string.Empty;

    [Range(1, 5)]
    public int RatingStars { get; set; } // 1..5 sao

    [Required]
    public string Comment { get; set; } = string.Empty; // Nếu 1 sao thì bắt buộc có lý do chi tiết

    public bool IsValid { get; set; } = true; // Trạng thái hiệu lực (true = kích hoạt blacklist nếu 1 sao)
    public string? InvalidationReason { get; set; }
    public string? ModeratedByAdminId { get; set; }
    public DateTime? ModeratedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ReviewDisputeAppeal
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ReviewId { get; set; }
    public TeamMatchReview Review { get; set; } = null!;

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    [Required]
    public string AppealReason { get; set; } = string.Empty;
    public string? EvidenceUrl { get; set; }

    public bool IsResolved { get; set; } = false;
    public string? AdminResolutionNote { get; set; }
    public string? ResolvedByAdminId { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
