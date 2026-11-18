using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AloBall.Web.Data.Entities;

public enum TournamentFormat
{
    RoundRobinLeague = 0,
    KnockoutCup = 1,
    GroupPlusKnockout = 2
}

public enum TournamentStatus
{
    Registration = 0,
    Ongoing = 1,
    Completed = 2
}

public enum MatchCardType
{
    Yellow = 1,
    Red = 2
}

public class Tournament
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string OrganizerUserId { get; set; } = string.Empty;
    public ApplicationUser OrganizerUser { get; set; } = null!;

    [Required, MaxLength(100)]
    public string Region { get; set; } = string.Empty;

    public TournamentFormat Format { get; set; } = TournamentFormat.RoundRobinLeague;

    public DateTime StartDate { get; set; }

    [Required, MaxLength(200)]
    public string PitchVenue { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal EntryFeeVnd { get; set; }

    public TournamentStatus Status { get; set; } = TournamentStatus.Registration;

    [Required, MaxLength(16)]
    public string InviteCode { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TournamentTeam> Teams { get; set; } = new List<TournamentTeam>();
    public ICollection<TournamentStanding> Standings { get; set; } = new List<TournamentStanding>();
    public ICollection<MatchGame> Matches { get; set; } = new List<MatchGame>();
}

public class TournamentTeam
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TournamentId { get; set; }
    public Tournament Tournament { get; set; } = null!;

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    [MaxLength(40)]
    public string? GroupName { get; set; }

    public int SeedOrder { get; set; }

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

public class TournamentStanding
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TournamentId { get; set; }
    public Tournament Tournament { get; set; } = null!;

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    [MaxLength(40)]
    public string? GroupName { get; set; }

    public int Rank { get; set; } = 1;
    public int Played { get; set; }
    public int Wins { get; set; }
    public int Draws { get; set; }
    public int Losses { get; set; }
    public int GoalsFor { get; set; }
    public int GoalsAgainst { get; set; }
    public int GoalDiff { get; set; }
    public int Points { get; set; }
    public int YellowCards { get; set; }
    public int RedCards { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class MatchCardEvent
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MatchId { get; set; }
    public MatchGame Match { get; set; } = null!;

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    public Guid PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    public MatchCardType CardType { get; set; } = MatchCardType.Yellow;

    public int? Minute { get; set; }

    [MaxLength(200)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
