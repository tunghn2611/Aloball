using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AloBall.Web.Data.Entities;

public enum RefereeProposalStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
    NotSelected = 3,
    Withdrawn = 4
}

public class RefereeProposal
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    // Request this proposal belongs to
    public Guid RequestId { get; set; }
    public RefereeRequest Request { get; set; } = null!;

    // Admin who created the proposal (SuperAdmin user id)
    public string AdminId { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalPriceVND { get; set; }

    public string? ServiceNote { get; set; }

    public RefereeProposalStatus Status { get; set; } = RefereeProposalStatus.Pending;

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RefereeAssignment> Assignments { get; set; } = new List<RefereeAssignment>();
}
