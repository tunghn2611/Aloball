using System.ComponentModel.DataAnnotations;

namespace AloBall.Web.Data.Entities;

public class Referee
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    // Contact info (phone/email). Sensitive – only visible after proposal accepted.
    [MaxLength(250)]
    public string ContactInfo { get; set; } = string.Empty;

    // Short summary of experience / qualifications.
    [MaxLength(500)]
    public string ExperienceSummary { get; set; } = string.Empty;

    // Supported match formats, stored as comma‑separated values (e.g., "5‑a‑side,7‑a‑side").
    public string? SupportedFormats { get; set; }

    // Arbitrary notes from admin.
    public string? ServiceNotes { get; set; }

    // Default indicative price in VND. May be null if not set.
    public decimal? DefaultPriceVND { get; set; }

    // Soft‑delete / activation flag.
    public bool IsActive { get; set; } = true;
}
