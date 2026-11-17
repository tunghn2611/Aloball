namespace AloBall.Web.Models;

public class PlayerCardStatsDto
{
    public int Att { get; set; }
    public int Cre { get; set; }
    public int Sta { get; set; }
    public int Win { get; set; }
    public int Rep { get; set; }
    public int Exp { get; set; }
    public int Ovr { get; set; }
}

public class PlayerCardDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int? KitNumber { get; set; }
    public string PosCode { get; set; } = "ST";
    public string Position { get; set; } = "Tiền đạo";
    public string? PreferredFoot { get; set; }
    public string? Region { get; set; }
    public string? Experience { get; set; }
    public string? PhotoUrl { get; set; }
    public string? PlayStyles { get; set; }
    public string? TeamName { get; set; }
    public string? TeamSlug { get; set; }
    public string? TeamLogoUrl { get; set; }
    public PlayerCardStatsDto Stats { get; set; } = new();
    public string Tier { get; set; } = "gold"; // bronze, silver, gold, motm
    public bool IsMotm { get; set; }
    public int Appearances { get; set; }
    public int Goals { get; set; }
    public int Assists { get; set; }
}

public class MatchPosterTeamDto
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? CoverUrl { get; set; }
    public double CoverOpacity { get; set; } = 0.7;
}

public class MatchPosterScorerDto
{
    public string PlayerName { get; set; } = string.Empty;
    public int Goals { get; set; } = 1;
    public string? Minutes { get; set; }
    public string? AssistName { get; set; }
}

public class MatchPosterMotmDto
{
    public Guid? PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string Highlight { get; set; } = string.Empty;
}

public class MatchPosterDto
{
    public Guid Id { get; set; }
    public string MatchDate { get; set; } = string.Empty;
    public string Venue { get; set; } = string.Empty;
    public string PitchType { get; set; } = "Sân 7";
    public string Category { get; set; } = "Giao hữu";
    public MatchPosterTeamDto TeamA { get; set; } = new();
    public MatchPosterTeamDto TeamB { get; set; } = new();
    public int ScoreA { get; set; }
    public int ScoreB { get; set; }
    public List<MatchPosterScorerDto> ScorersA { get; set; } = new();
    public List<MatchPosterScorerDto> ScorersB { get; set; } = new();
    public MatchPosterMotmDto? Motm { get; set; }
    public string BrandTag { get; set; } = "aloball.vercel.app";
}

public class PlayerShareViewModel
{
    public PlayerCardDto Card { get; set; } = new();
    public string OgTitle { get; set; } = string.Empty;
    public string OgDescription { get; set; } = string.Empty;
    public string OgImage { get; set; } = string.Empty;
    public string OgUrl { get; set; } = string.Empty;
}

public class MatchShareViewModel
{
    public MatchPosterDto Poster { get; set; } = new();
    public string OgTitle { get; set; } = string.Empty;
    public string OgDescription { get; set; } = string.Empty;
    public string OgImage { get; set; } = string.Empty;
    public string OgUrl { get; set; } = string.Empty;
}
