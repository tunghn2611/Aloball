using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace AloBall.Web.Data.Entities;

public class ApplicationUser : IdentityUser
{
    [MaxLength(100)]
    public string FullName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Profile & Onboarding
    [MaxLength(20)]
    public string? PreferredFoot { get; set; }
    [MaxLength(50)]
    public string? Experience { get; set; }
    [MaxLength(100)]
    public string? Region { get; set; }
    [MaxLength(50)]
    public string? PrimaryPosition { get; set; }
    public bool HasCompletedOnboarding { get; set; } = false;
    public DateTime? OnboardingCompletedAt { get; set; }

    // Quan hệ với các đội bóng
    public ICollection<TeamMember> TeamMemberships { get; set; } = new List<TeamMember>();
    // Quan hệ với hồ sơ cầu thủ (1 user có thể liên kết hồ sơ cầu thủ)
    public ICollection<Player> LinkedPlayers { get; set; } = new List<Player>();
}

public enum TeamRole
{
    Member = 0,
    Manager = 1,
    Owner = 2
}

public class Team
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Slug { get; set; } = string.Empty; // unique slug cho URL công khai

    public string? LogoUrl { get; set; }
    public string? CoverImageUrl { get; set; }
    public double? CoverOpacity { get; set; } = 0.7; // Default 70% opacity for newly created covers
    public DateTime? EstablishedDate { get; set; }
    public string? Description { get; set; }
    public string? ActivityArea { get; set; }
    public string? HomePitch { get; set; }

    public bool IsPublic { get; set; } = true;
    public bool IsDraft { get; set; } = false;

    // Bắt đối & Đánh giá
    public bool IsRestrictedFromMatchmaking { get; set; } = false;
    public string? MatchmakingRestrictionReason { get; set; }
    public double AverageRating { get; set; } = 5.0;
    public int ValidReviewCount { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<TeamMember> Members { get; set; } = new List<TeamMember>();
    public ICollection<Player> Players { get; set; } = new List<Player>();
    public ICollection<TeamRegulation> Regulations { get; set; } = new List<TeamRegulation>();
    public ICollection<AttendanceSession> AttendanceSessions { get; set; } = new List<AttendanceSession>();
    public ICollection<KitOrderBatch> KitOrderBatches { get; set; } = new List<KitOrderBatch>();
    public ICollection<MatchmakingPost> MatchmakingPosts { get; set; } = new List<MatchmakingPost>();
    public ICollection<CompetitionAchievement> Achievements { get; set; } = new List<CompetitionAchievement>();
    public ICollection<TeamMessage> Messages { get; set; } = new List<TeamMessage>();
}

public class TeamMember
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public TeamRole Role { get; set; } = TeamRole.Member;
    public bool IsBtc { get; set; } = false; // Ban Tổ Chức (miễn nghĩa vụ quy chế chuyên cần)

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

public class Player
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    // Liên kết tài khoản hệ thống (tùy chọn)
    public string? LinkedUserId { get; set; }
    public ApplicationUser? LinkedUser { get; set; }

    [Required, MaxLength(100)]
    public string FullName { get; set; } = string.Empty;
    public int? KitNumber { get; set; }
    [MaxLength(50)]
    public string? Position { get; set; }
    [MaxLength(20)]
    public string? StandardPrimaryPosition { get; set; }
    public string? AllowedSecondaryPositionsJson { get; set; }
    public string? PhotoUrl { get; set; }

    public double? HeightCm { get; set; }
    public double? WeightKg { get; set; }

    [MaxLength(50)]
    public string? ShirtName { get; set; }
    [MaxLength(20)]
    public string? ShirtSize { get; set; }
    [MaxLength(20)]
    public string? ShortSize { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime JoinedDate { get; set; } = DateTime.UtcNow;
    public DateTime? InactiveDate { get; set; }

    public string? InternalNotes { get; set; }

    [MaxLength(20)]
    public string? PreferredFoot { get; set; }
    [MaxLength(50)]
    public string? Experience { get; set; }
    [MaxLength(100)]
    public string? Region { get; set; }

    // Cấu hình công khai từng trường
    public bool IsPublicProfile { get; set; } = true;
    public bool PublicKitNumber { get; set; } = true;
    public bool PublicPosition { get; set; } = true;
    public bool PublicHeightWeight { get; set; } = false;
    public bool PublicPhoto { get; set; } = true;
    public bool PublicPreferredFoot { get; set; } = false;
    public bool PublicExperience { get; set; } = false;

    // Navigation
    public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();
    public ICollection<MatchPlayerLineup> MatchLineups { get; set; } = new List<MatchPlayerLineup>();
}

public enum TeamMessageType
{
    Direct = 0,        // Tin nhắn liên hệ trực tiếp 1-1 giữa Thành viên và Ban cán sự / Đội trưởng
    Announcement = 1  // Thông báo toàn đội từ Ban cán sự / Đội trưởng
}

public class TeamMessage
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    [Required]
    public string SenderUserId { get; set; } = string.Empty;
    public ApplicationUser SenderUser { get; set; } = null!;

    public string? RecipientUserId { get; set; }
    public ApplicationUser? RecipientUser { get; set; }

    public Guid ConversationId { get; set; } = Guid.NewGuid();

    public TeamMessageType MessageType { get; set; } = TeamMessageType.Direct;

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(2000)]
    public string Content { get; set; } = string.Empty;

    public bool IsRead { get; set; } = false;
    public DateTime? ReadAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

