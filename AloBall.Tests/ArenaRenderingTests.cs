using System.Net;
using System.Text.RegularExpressions;

namespace AloBall.Tests;

public class ArenaRenderingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InternalDivision_SavedAssignmentDoesNotBecomeALock_AndInitialRosterIsSelected(bool savedLineup)
    {
        using var app = new RequirementsWebFactory();
        await app.Seed();
        await app.WithDb(async db =>
        {
            var match = await db.Matches.FindAsync(app.Match);
            match!.Category = AloBall.Web.Data.Entities.MatchCategory.Internal;
            match.Status = AloBall.Web.Data.Entities.MatchStatus.Scheduled;
            if (savedLineup)
                db.MatchPlayerLineups.Add(new AloBall.Web.Data.Entities.MatchPlayerLineup
                {
                    MatchId = app.Match, PlayerId = app.PlayerA, TeamId = app.TeamA,
                    TeamSide = "B", IsStarting = true, AssignedSlotCode = "GK"
                });
            await db.SaveChangesAsync();
        });
        using var client = app.Client();
        var html = await client.GetStringAsync($"/Matches/Detail/{app.Match}");
        Assert.Matches("class=\"form-check-input chk-participant\"[^>]*checked", html);
        var locks = Regex.Matches(html, "<select[^>]*class=\"[^\"]*sel-lock-(?:side|slot)[^\"]*\"[^>]*>(.*?)</select>", RegexOptions.Singleline);
        Assert.Equal(2, locks.Count);
        foreach (Match control in locks) Assert.DoesNotContain("selected", control.Groups[1].Value);
    }

    [Fact]
    public async Task RedesignedScreens_RenderWithNavigationAndWorkingTabTargets()
    {
        using var app = new RequirementsWebFactory();
        await app.Seed();
        using var client = app.Client();
        var routes = new[] { "/", "/Teams", "/Teams/Create", $"/Teams/Dashboard/{app.TeamA}",
            $"/Teams/Players?teamId={app.TeamA}", $"/Teams/PlayerEditor?teamId={app.TeamA}",
            $"/Teams/Edit/{app.TeamA}", "/doi/audit-a", "/Matchmaking",
            $"/Attendance/Sessions?teamId={app.TeamA}", $"/Attendance/Detail/{app.Session}",
            $"/Matches?teamId={app.TeamA}", $"/Matches/Schedule?teamId={app.TeamA}",
            $"/Matches/Detail/{app.Match}", $"/FireKeeping?teamId={app.TeamA}", "/Home/Privacy" };
        foreach (var route in routes)
        {
            var response = await client.GetAsync(route);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{route}: {response.StatusCode}");
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("/css/arena.css", html);
            Assert.Contains("/js/arena.js", html);
            Assert.Contains("id=\"main-content\"", html);
            Assert.Contains("aria-controls=\"arenaSidebar\"", html);
            Assert.DoesNotContain("/css/site.css", html);
            Assert.DoesNotContain("/css/club.css", html);
            foreach (Match target in Regex.Matches(html, "data-bs-target=\"#([^\"]+)\""))
                Assert.Contains($"id=\"{target.Groups[1].Value}\"", html);
            if (html.Contains("role=\"tablist\"")) Assert.Contains("tab-pane", html);
        }
    }

    [Theory]
    [InlineData("/Account/Login")]
    [InlineData("/Account/Register")]
    public async Task AuthenticationScreens_KeepValidationDependenciesAndAntiforgery(string route)
    {
        using var app = new RequirementsWebFactory();
        await app.Seed();
        using var client = app.Client(null);
        var html = await client.GetStringAsync(route);
        Assert.Contains("auth-layout", html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.True(html.IndexOf("jquery.min.js", StringComparison.Ordinal) < html.IndexOf("jquery.validate.min.js", StringComparison.Ordinal));
        Assert.Contains("jquery.min.js", html);
    }
}
