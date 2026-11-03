using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AloBall.Web.Data.Entities;

public enum MatchCategory
{
    Internal = 0,     // Nội bộ chia đội trong CLB
    Friendly = 1,     // Giao hữu đối ngoại
    Tournament = 2    // Đá giải
}

public enum MatchStatus
{
    Scheduled = 0,    // Dự kiến
    Completed = 1,    // Hoàn thành
    Cancelled = 2,    // Hủy
    Disputed = 3      // Cần đối soát / Tranh chấp
}

public class MatchGame
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public MatchCategory Category { get; set; } = MatchCategory.Internal;
    public MatchStatus Status { get; set; } = MatchStatus.Scheduled;

    public DateTime MatchDateTime { get; set; }
    public string PitchName { get; set; } = string.Empty;
    public string PitchType { get; set; } = "7 người"; // 5, 7, 11 người

    // Đội A (luôn là đội chủ nhà hoặc đội tạo)
    public Guid TeamAId { get; set; }
    public Team TeamA { get; set; } = null!;

    // Đội B: Nếu là Friendly giữa 2 đội trên app, TeamBId có giá trị. Nếu đá với đối thủ ngoài, TeamBExternalName có giá trị.
    public Guid? TeamBId { get; set; }
    public Team? TeamB { get; set; }
    public string? TeamBExternalName { get; set; }

    // Tỉ số
    public int? ScoreA { get; set; }
    public int? ScoreB { get; set; }
    public bool IsScoreIncomplete { get; set; } = false; // Nhập tỉ số nhưng chưa đủ thông tin bàn thắng

    // Quy trình xác nhận kết quả giữa 2 đội trên hệ thống
    public bool ConfirmedByTeamA { get; set; } = false;
    public bool ConfirmedByTeamB { get; set; } = false;
    public DateTime? ConfirmedAt { get; set; }

    public bool IsPublic { get; set; } = true;
    public string? Notes { get; set; }

    // Đổi kết quả đã xác nhận -> audit
    public string? ScoreChangeReason { get; set; }
    public DateTime? ScoreChangedAt { get; set; }
    public int Revision { get; set; } = 1; // Quản lý version tỉ số / trạng thái trận cho sổ AB

    // Quản lý sơ đồ và concurrency khi chia đội
    public int LineupRevision { get; set; } = 1;
    [MaxLength(20)]
    public string? FormationCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<MatchPlayerLineup> Lineups { get; set; } = new List<MatchPlayerLineup>();
    public ICollection<MatchPositionException> PositionExceptions { get; set; } = new List<MatchPositionException>();
    public ICollection<MatchGoalEvent> GoalEvents { get; set; } = new List<MatchGoalEvent>();
    public ICollection<TeamMatchReview> Reviews { get; set; } = new List<TeamMatchReview>();
}

public class MatchPlayerLineup
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MatchId { get; set; }
    public MatchGame Match { get; set; } = null!;

    public Guid TeamId { get; set; } // Đội mà cầu thủ thuộc về trong trận
    public Guid PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    // Phân biệt bên thi đấu trong trận nội bộ: "A" hoặc "B" (hoặc null nếu là dự bị chung)
    [MaxLength(10)]
    public string? TeamSide { get; set; } = "A";

    public bool IsStarting { get; set; } = true; // Đá chính hoặc dự bị
    [MaxLength(50)]
    public string? AssignedPosition { get; set; }
    [MaxLength(20)]
    public string? AssignedSlotCode { get; set; }
    public int? AssignedKitNumber { get; set; }

    // Đánh dấu cầu thủ thực tế có vào sân thi đấu trong trận hay không (khác với IsStarting)
    public bool HasActuallyPlayed { get; set; } = false;
}

public class MatchPositionException
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MatchId { get; set; }
    public MatchGame Match { get; set; } = null!;

    public Guid PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    [Required, MaxLength(20)]
    public string AllowedSlotCode { get; set; } = string.Empty;

    public string GrantedByUserId { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
}

public class MatchGoalEvent
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MatchId { get; set; }
    public MatchGame Match { get; set; } = null!;

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    public Guid ScorerPlayerId { get; set; }
    public Player Scorer { get; set; } = null!;

    public Guid? AssistPlayerId { get; set; }
    public Player? Assist { get; set; }

    public int? Minute { get; set; }
    public string? Description { get; set; }
    public int Revision { get; set; } = 1; // Quản lý version sửa người ghi bàn / kiến tạo
}

public class CompetitionAchievement
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    [Required, MaxLength(150)]
    public string TournamentName { get; set; } = string.Empty;
    public string SeasonOrYear { get; set; } = string.Empty;
    public string TitleOrRank { get; set; } = string.Empty; // Vô địch, Á quân, Giải Phong cách...
    public string? ProofUrl { get; set; }
    public bool IsFeatured { get; set; } = false; // Nổi bật đưa lên trang công khai
    public bool IsVerifiedOnPlatform { get; set; } = false; // Phân biệt tự khai báo với hệ thống xác nhận

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
