using System.ComponentModel.DataAnnotations;

namespace AloBall.Web.Data.Entities;

public class RefereeAssignment
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProposalId { get; set; }
    public RefereeProposal Proposal { get; set; } = null!;

    public Guid RefereeId { get; set; }
    public Referee Referee { get; set; } = null!;
}
