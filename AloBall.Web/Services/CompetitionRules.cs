using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Services;

public static class CompetitionRules
{
    // Match/session timestamps entered by clubs use Vietnam local time.
    public static DateTime LocalNow => DateTime.UtcNow.AddHours(7);

    public static Task<bool> IsClosedAsync(ApplicationDbContext db, Guid teamId, string period) =>
        db.FireCompetitionPeriods.AnyAsync(p => p.TeamId == teamId && p.StatisticalPeriod == period && p.Status == PeriodStatus.Closed);

    public static async Task<bool> IsMatchClosedAsync(ApplicationDbContext db, MatchGame match)
    {
        var period = match.MatchDateTime.ToString("yyyy-MM");
        return await db.FireCompetitionPeriods.AnyAsync(p =>
            (p.TeamId == match.TeamAId || p.TeamId == match.TeamBId) && p.StatisticalPeriod == period && p.Status == PeriodStatus.Closed);
    }

    public static bool SafeImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return true;
        if (url.StartsWith("/") && !url.StartsWith("//") && !url.StartsWith("/\\")) return true;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    }
}

