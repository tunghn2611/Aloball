using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AloBall.Web.Data.Entities;

public enum AbSourceDomain
{
    AttendanceSession = 1,
    MatchGoalEvent = 2,
    MatchWin = 3
}

public enum PeriodStatus
{
    Open = 0,
    Closed = 1
}

public class TeamFireRegulation
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    public int Version { get; set; } = 1;

    // Thời điểm bắt đầu có hiệu lực
    public DateTime EffectiveFrom { get; set; } = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Mức điểm AB theo quy chế
    public int PointsPerAttendance { get; set; } = 2;
    public int PointsPerGoal { get; set; } = 3;
    public int PointsPerAssist { get; set; } = 1;
    public int PointsPerWin { get; set; } = 1;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class FireCompetitionPeriod
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    [Required, MaxLength(7)]
    public string StatisticalPeriod { get; set; } = string.Empty; // "yyyy-MM"

    public PeriodStatus Status { get; set; } = PeriodStatus.Open;
    public int Revision { get; set; } = 1; // Tăng lên mỗi lần mở lại / chốt lại

    public DateTime? ClosedAt { get; set; }
    public string? ClosedByUserId { get; set; }

    public DateTime? ReopenedAt { get; set; }
    public string? ReopenedByUserId { get; set; }
    public string? ReopenReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<MonthlyFireAward> Awards { get; set; } = new List<MonthlyFireAward>();
}

public class PlayerAbJournalEntry
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    public Guid PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    [Required, MaxLength(7)]
    public string StatisticalPeriod { get; set; } = string.Empty; // "yyyy-MM"

    public AbSourceDomain SourceDomain { get; set; }
    public Guid SourceId { get; set; }

    [Required, MaxLength(50)]
    public string SourceRole { get; set; } = string.Empty; // "Attendance", "Scorer", "Assist", "Winner"

    public int SourceRevision { get; set; } = 1;

    // OperationKey duy nhất: TeamId_Domain_SourceId_Role_rev_Revision
    [Required, MaxLength(150)]
    public string OperationKey { get; set; } = string.Empty;

    // Điểm có dấu (+ hoặc -)
    public int Points { get; set; }

    public int RegulationVersion { get; set; } = 1;

    // Bút toán đảo: trỏ tới phát sinh gốc bị thu hồi
    public Guid? ReversedEntryId { get; set; }
    public PlayerAbJournalEntry? ReversedEntry { get; set; }

    public string? AdjustmentReason { get; set; }

    public DateTime EventDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class MonthlyFireAward
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PeriodId { get; set; }
    public FireCompetitionPeriod Period { get; set; } = null!;

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    public Guid PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    [Required, MaxLength(7)]
    public string StatisticalPeriod { get; set; } = string.Empty;

    public int PeriodRevision { get; set; } = 1;

    public int TotalAbPoints { get; set; }
    public int AttendedSessions { get; set; }
    public int Goals { get; set; }
    public int Assists { get; set; }
    public int Wins { get; set; }

    public int Rank { get; set; } = 1;
    public string AwardTitle { get; set; } = "Cầu thủ xuất sắc nhất tháng";

    public bool IsArchived { get; set; } = false; // Đánh dấu nếu kỳ bị mở lại

    public string? DecidedByUserId { get; set; }
    public DateTime DecidedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
}
