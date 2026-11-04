using System.ComponentModel.DataAnnotations;
using AloBall.Web.Data.Entities;

namespace AloBall.Web.Models;

public class TeamProfileInput
{
    public Guid Id { get; set; }
    [Required(ErrorMessage = "Nhập tên đội bóng."), StringLength(150)] public string Name { get; set; } = "";
    [DataType(DataType.Date)] public DateTime? EstablishedDate { get; set; }
    [StringLength(1000)] public string? LogoUrl { get; set; }
    [StringLength(100)] public string? ActivityArea { get; set; }
    [StringLength(200)] public string? HomePitch { get; set; }
    [StringLength(2000)] public string? Description { get; set; }
    public bool IsPublic { get; set; } = true;
}

public class PlayerProfileInput
{
    public Guid? Id { get; set; }
    public Guid TeamId { get; set; }
    [Required(ErrorMessage = "Nhập họ tên cầu thủ."), StringLength(100)] public string FullName { get; set; } = "";
    [Range(0, 999)] public int? KitNumber { get; set; }
    [StringLength(50)] public string? Position { get; set; }
    [Range(50, 250)] public double? HeightCm { get; set; }
    [Range(15, 250)] public double? WeightKg { get; set; }
    [StringLength(50)] public string? ShirtName { get; set; }
    [StringLength(20)] public string? ShirtSize { get; set; }
    [StringLength(20)] public string? ShortSize { get; set; }
    [StringLength(20)] public string? PreferredFoot { get; set; }
    [StringLength(50)] public string? Experience { get; set; }
    [StringLength(100)] public string? Region { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsPublicProfile { get; set; } = true;
    public bool PublicKitNumber { get; set; } = true;
    public bool PublicPosition { get; set; } = true;
    public bool PublicHeightWeight { get; set; } = false;
    public bool PublicPreferredFoot { get; set; } = false;
    public bool PublicExperience { get; set; } = false;
}

public class AchievementInput
{
    public Guid? Id { get; set; }
    public Guid TeamId { get; set; }
    [Required, StringLength(150)] public string TournamentName { get; set; } = "";
    [Required, StringLength(40)] public string SeasonOrYear { get; set; } = "";
    [Required, StringLength(150)] public string TitleOrRank { get; set; } = "";
    public bool IsFeatured { get; set; }
}

public class ScheduleMatchInput
{
    public Guid TeamId { get; set; }
    public MatchCategory Category { get; set; } = MatchCategory.Tournament;
    [Required, StringLength(150)] public string Opponent { get; set; } = "";
    public DateTime MatchDateTime { get; set; }
    [Required, StringLength(200)] public string PitchName { get; set; } = "";
    public string PitchType { get; set; } = "7 người";
    [StringLength(1000)] public string? Notes { get; set; }
    public bool IsPublic { get; set; } = true;
}
