using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AloBall.Tests;

public sealed class RequirementsWebFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    public Guid TeamA { get; } = Guid.NewGuid();
    public Guid TeamB { get; } = Guid.NewGuid();
    public Guid TeamC { get; } = Guid.NewGuid();
    public Guid PlayerA { get; } = Guid.NewGuid();
    public Guid PlayerB { get; } = Guid.NewGuid();
    public Guid Match { get; } = Guid.NewGuid();
    public Guid Session { get; } = Guid.NewGuid();
    public Guid Record { get; } = Guid.NewGuid();
    public string Period => CompetitionRules.LocalNow.ToString("yyyy-MM");

    public RequirementsWebFactory() => _connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(_connection));
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.AddAuthentication(o => { o.DefaultAuthenticateScheme = "AuditTest"; o.DefaultChallengeScheme = "AuditTest"; o.DefaultForbidScheme = "AuditTest"; })
                .AddScheme<AuthenticationSchemeOptions, AuditAuthenticationHandler>("AuditTest", _ => { });
        });
    }

    public async Task Seed()
    {
        await WithDb(async db =>
        {
            await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(new ApplicationUser { Id = "owner", UserName = "owner@test.local" }, new ApplicationUser { Id = "member", UserName = "member@test.local" }, new ApplicationUser { Id = "outsider", UserName = "outsider@test.local" });
            db.Teams.AddRange(new Team { Id = TeamA, Name = "Đội A", Slug = "audit-a" }, new Team { Id = TeamB, Name = "Đội B", Slug = "audit-b" }, new Team { Id = TeamC, Name = "Đội C", Slug = "audit-c" });
            db.TeamMembers.AddRange(new TeamMember { TeamId = TeamA, UserId = "owner", Role = TeamRole.Owner }, new TeamMember { TeamId = TeamA, UserId = "member", Role = TeamRole.Member }, new TeamMember { TeamId = TeamC, UserId = "outsider", Role = TeamRole.Owner });
            db.Players.AddRange(new Player { Id = PlayerA, TeamId = TeamA, FullName = "Cầu thủ A", LinkedUserId = "member" }, new Player { Id = PlayerB, TeamId = TeamB, FullName = "Cầu thủ B" });
            var date = CompetitionRules.LocalNow.Date.AddHours(-1);
            db.Matches.Add(new MatchGame { Id = Match, TeamAId = TeamA, TeamBId = TeamB, Category = MatchCategory.Friendly, Status = MatchStatus.Completed, MatchDateTime = date, ScoreA = 2, ScoreB = 1, ConfirmedByTeamA = true, ConfirmedByTeamB = true });
            db.AttendanceSessions.Add(new AttendanceSession { Id = Session, TeamId = TeamA, Title = "Buổi tuần", SessionDateTime = date, StatisticalPeriod = date.ToString("yyyy-MM"), Status = SessionStatus.Finalized });
            db.AttendanceRecords.Add(new AttendanceRecord { Id = Record, SessionId = Session, PlayerId = PlayerA, Actual = ActualStatus.Present });
            await db.SaveChangesAsync();
        });
    }

    public async Task WithDb(Func<ApplicationDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    public HttpClient Client(string? user = "owner")
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (user != null) client.DefaultRequestHeaders.Add("X-Test-User", user);
        return client;
    }

    public async Task<HttpResponseMessage> Post(HttpClient client, string path, Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync("/Teams");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value);
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) _connection.Dispose(); }
}

public sealed class AuditAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = Request.Headers["X-Test-User"].ToString();
        if (string.IsNullOrEmpty(user)) return Task.FromResult(AuthenticateResult.NoResult());
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user), new Claim(ClaimTypes.Name, user) }, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}

public class RequirementsWorkflowTests
{
    [Theory]
    [InlineData("member")]
    [InlineData("outsider")]
    public async Task Import_IsRejectedForAnyoneExceptLeadership(string user)
    {
        using var app = new RequirementsWebFactory(); await app.Seed(); using var client = app.Client(user);
        var result = await app.Post(client, "/Sheets/ApplyImport", new() { ["teamId"] = app.TeamA.ToString(), ["approvedChanges[0].PlayerId"] = app.PlayerA.ToString(), ["approvedChanges[0].FieldName"] = "Size áo", ["approvedChanges[0].NewValue"] = "XL" });
        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        await app.WithDb(async db => Assert.Null((await db.Players.FindAsync(app.PlayerA))!.ShirtSize));
        if (user == "outsider") Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/Sheets/ExportPlayers?teamId={app.TeamA}")).StatusCode);
    }

    [Fact]
    public async Task Goal_CannotUseAnotherTeamsPlayer_OrUnrelatedMatch()
    {
        using var app = new RequirementsWebFactory(); await app.Seed(); using var client = app.Client();
        var result = await app.Post(client, "/Matches/AddGoalEvent", new() { ["matchId"] = app.Match.ToString(), ["teamId"] = app.TeamA.ToString(), ["scorerPlayerId"] = app.PlayerB.ToString() });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        using var outsider = app.Client("outsider");
        result = await app.Post(outsider, "/Matches/UpdateLineup", new() { ["matchId"] = app.Match.ToString(), ["teamId"] = app.TeamC.ToString(), ["playerIds[0]"] = app.PlayerA.ToString() });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        await app.WithDb(async db => { Assert.Empty(await db.MatchGoalEvents.ToListAsync()); Assert.Empty(await db.MatchPlayerLineups.ToListAsync()); });
    }

    [Fact]
    public async Task ClosedPeriod_RejectsScoreAndAttendanceWithoutChangingSource()
    {
        using var app = new RequirementsWebFactory(); await app.Seed(); using var client = app.Client();
        await app.WithDb(async db => { var match = await db.Matches.FindAsync(app.Match); db.FireCompetitionPeriods.Add(new FireCompetitionPeriod { TeamId = app.TeamA, StatisticalPeriod = match!.MatchDateTime.ToString("yyyy-MM"), Status = PeriodStatus.Closed }); await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.Conflict, (await app.Post(client, "/Matches/UpdateScore", new() { ["matchId"] = app.Match.ToString(), ["scoreA"] = "9", ["scoreB"] = "0" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await app.Post(client, "/Attendance/UpdateRecordStatus", new() { ["recordId"] = app.Record.ToString(), ["actualStatus"] = "2" })).StatusCode);
        await app.WithDb(async db => { Assert.Equal(2, (await db.Matches.FindAsync(app.Match))!.ScoreA); Assert.Equal(ActualStatus.Present, (await db.AttendanceRecords.FindAsync(app.Record))!.Actual); });
    }

    [Fact]
    public async Task EditingFinalizedAttendance_ReversesAb_AndMemberCannotEdit()
    {
        using var app = new RequirementsWebFactory(); await app.Seed(); using var client = app.Client();
        await app.WithDb(db => new FireKeepingService(db).SyncAttendanceSessionPointsAsync(app.Session));
        Assert.Equal(HttpStatusCode.OK, (await app.Post(client, "/Attendance/UpdateRecordStatus", new() { ["recordId"] = app.Record.ToString(), ["actualStatus"] = "2" })).StatusCode);
        await app.WithDb(async db => { Assert.Equal(0, await db.PlayerAbJournalEntries.Where(e => e.PlayerId == app.PlayerA).SumAsync(e => e.Points)); Assert.Equal(ActualStatus.Absent, (await db.AttendanceRecords.FindAsync(app.Record))!.Actual); });
        using var member = app.Client("member");
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Post(member, "/Attendance/UpdateRecordStatus", new() { ["recordId"] = app.Record.ToString(), ["responseStatus"] = "1" })).StatusCode);
    }

    [Fact]
    public async Task PrivateProfilesAndMatches_AreNotVisibleToAnonymous()
    {
        using var app = new RequirementsWebFactory(); await app.Seed();
        await app.WithDb(async db => { (await db.Teams.FindAsync(app.TeamA))!.IsPublic = false; (await db.Matches.FindAsync(app.Match))!.IsPublic = false; await db.SaveChangesAsync(); });
        using var anonymous = app.Client(null);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/doi/audit-a")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/Matches/Detail/{app.Match}")).StatusCode);
        using var member = app.Client("member");
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/Matches/Detail/{app.Match}")).StatusCode);
    }

    [Fact]
    public async Task PlayerEditor_PersistsKitAndBodyInformation_WithMemberReadOnly()
    {
        using var app = new RequirementsWebFactory(); await app.Seed(); using var client = app.Client();
        var response = await app.Post(client, "/Teams/PlayerEditor", new() { ["Id"] = app.PlayerA.ToString(), ["TeamId"] = app.TeamA.ToString(), ["FullName"] = "Nguyễn An", ["HeightCm"] = "178", ["WeightKg"] = "72", ["ShirtSize"] = "L", ["KitNumber"] = "10", ["IsActive"] = "true", ["IsPublicProfile"] = "true", ["PublicHeightWeight"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await app.WithDb(async db => { var p = await db.Players.FindAsync(app.PlayerA); Assert.Equal(178, p!.HeightCm); Assert.Equal(72, p.WeightKg); Assert.Equal("L", p.ShirtSize); });
        var html = await app.Client(null).GetStringAsync("/doi/audit-a"); Assert.Contains("178cm", html);
        using var member = app.Client("member"); Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync($"/Teams/PlayerEditor?teamId={app.TeamA}&id={app.PlayerA}")).StatusCode);
    }

    [Fact]
    public async Task TournamentAndFeaturedAchievement_CanBeCreatedByLeader()
    {
        using var app = new RequirementsWebFactory(); await app.Seed(); using var client = app.Client();
        var response = await app.Post(client, "/Matches/Schedule", new() { ["TeamId"] = app.TeamA.ToString(), ["Category"] = "2", ["Opponent"] = "FC Đối thủ", ["MatchDateTime"] = CompetitionRules.LocalNow.ToString("yyyy-MM-ddTHH:mm"), ["PitchName"] = "Sân A", ["PitchType"] = "7 người", ["Notes"] = "Cúp mùa thu", ["IsPublic"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        response = await app.Post(client, "/Teams/SaveAchievement", new() { ["TeamId"] = app.TeamA.ToString(), ["TournamentName"] = "Cúp mùa thu", ["SeasonOrYear"] = "2026", ["TitleOrRank"] = "Vô địch", ["IsFeatured"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var html = await app.Client(null).GetStringAsync("/doi/audit-a"); Assert.Contains("Cúp mùa thu", WebUtility.HtmlDecode(html)); Assert.Contains("THÀNH TÍCH TỐT NHẤT", html);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task RatingOutsideOneToFive_IsRejected(int stars)
    {
        using var app = new RequirementsWebFactory(); await app.Seed();
        await app.WithDb(async db => { var service = new MatchmakingService(db, new TeamAuthorizationService(db)); Assert.False((await service.SubmitReviewAsync(app.Match, app.TeamA, "owner", stars, "Test")).Success); Assert.Empty(await db.TeamMatchReviews.ToListAsync()); });
    }

    [Fact]
    public async Task MonthlyAward_RejectsLowerRankedPlayer()
    {
        using var app = new RequirementsWebFactory(); await app.Seed();
        await app.WithDb(async db =>
        {
            var second = new Player { TeamId = app.TeamA, FullName = "Hạng hai" }; db.Players.Add(second);
            var date = (await db.Matches.FindAsync(app.Match))!.MatchDateTime;
            foreach (var (player, points) in new[] { (app.PlayerA, 20), (second.Id, 10) }) db.PlayerAbJournalEntries.Add(new PlayerAbJournalEntry { TeamId = app.TeamA, PlayerId = player, StatisticalPeriod = date.ToString("yyyy-MM"), Points = points, OperationKey = Guid.NewGuid().ToString(), EventDate = date });
            await db.SaveChangesAsync();
            var service = new FireKeepingService(db);
            Assert.False((await service.FinalizePeriodAndAwardAsync(app.TeamA, date.ToString("yyyy-MM"), new() { second.Id }, "owner", null)).Success);
            Assert.True((await service.FinalizePeriodAndAwardAsync(app.TeamA, date.ToString("yyyy-MM"), new() { app.PlayerA }, "owner", null)).Success);
            Assert.Equal(app.PlayerA, (await db.MonthlyFireAwards.SingleAsync()).PlayerId);
        });
    }

    [Fact]
    public async Task JournalFailure_RollsBackGoalAndMatchRevision()
    {
        using var app = new RequirementsWebFactory(); await app.Seed(); using var client = app.Client();
        var revision = 0;
        await app.WithDb(async db =>
        {
            revision = (await db.Matches.FindAsync(app.Match))!.Revision;
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER audit_fail_journal BEFORE INSERT ON PlayerAbJournalEntries BEGIN SELECT RAISE(ABORT, 'simulated journal failure'); END;");
        });
        var response = await app.Post(client, "/Matches/AddGoalEvent", new() { ["matchId"] = app.Match.ToString(), ["teamId"] = app.TeamA.ToString(), ["scorerPlayerId"] = app.PlayerA.ToString() });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await app.WithDb(async db =>
        {
            Assert.Empty(await db.MatchGoalEvents.ToListAsync());
            Assert.Empty(await db.PlayerAbJournalEntries.ToListAsync());
            Assert.Equal(revision, (await db.Matches.FindAsync(app.Match))!.Revision);
        });
    }

    [Fact]
    public async Task OneStarReview_BlocksMatchmakingButKeepsInternalWorkspace()
    {
        using var app = new RequirementsWebFactory(); await app.Seed(); using var client = app.Client();
        await app.WithDb(async db =>
        {
            db.TeamMembers.Add(new TeamMember { TeamId = app.TeamB, UserId = "outsider", Role = TeamRole.Owner });
            db.MatchmakingPosts.Add(new MatchmakingPost { TeamId = app.TeamB, MatchDate = CompetitionRules.LocalNow.AddDays(7), Area = "Hà Nội" });
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Redirect, (await app.Post(client, "/Matches/SubmitReview", new() { ["matchId"] = app.Match.ToString(), ["reviewerTeamId"] = app.TeamA.ToString(), ["stars"] = "1", ["comment"] = "Lý do đánh giá trong dữ liệu kiểm thử" })).StatusCode);
        await app.WithDb(async db =>
        {
            Assert.True((await db.Teams.FindAsync(app.TeamB))!.IsRestrictedFromMatchmaking);
            Assert.Equal(MatchmakingStatus.Suspended, (await db.MatchmakingPosts.SingleAsync()).Status);
        });
        using var restricted = app.Client("outsider");
        Assert.Equal(HttpStatusCode.OK, (await restricted.GetAsync($"/Teams/Dashboard/{app.TeamB}")).StatusCode);
        await app.Post(restricted, "/Matchmaking/CreatePost", new() { ["teamId"] = app.TeamB.ToString(), ["matchDate"] = CompetitionRules.LocalNow.AddDays(8).ToString("yyyy-MM-dd"), ["startTime"] = "19:00", ["endTime"] = "20:30", ["area"] = "Hà Nội", ["pitchType"] = "7 người", ["desiredLevel"] = "Trung bình", ["costSharing"] = "50-50" });
        await app.WithDb(async db => Assert.Single(await db.MatchmakingPosts.ToListAsync()));
    }

    [Fact]
    public async Task HomePitchPost_UsesTeamsPitch_AndInvalidTimeDoesNotCreatePost()
    {
        using var app = new RequirementsWebFactory(); await app.Seed(); using var client = app.Client();
        await app.WithDb(async db => { (await db.Teams.FindAsync(app.TeamA))!.HomePitch = "Sân nhà A"; await db.SaveChangesAsync(); });
        var fields = new Dictionary<string, string> { ["teamId"] = app.TeamA.ToString(), ["matchDate"] = CompetitionRules.LocalNow.AddDays(7).ToString("yyyy-MM-dd"), ["startTime"] = "19:00", ["endTime"] = "20:30", ["area"] = "Hà Nội", ["pitchType"] = "7 người", ["desiredLevel"] = "Trung bình", ["costSharing"] = "50-50", ["pitchArrangement"] = "HomePitch" };
        Assert.Equal(HttpStatusCode.Redirect, (await app.Post(client, "/Matchmaking/CreatePost", fields)).StatusCode);
        await app.WithDb(async db => { var post = await db.MatchmakingPosts.SingleAsync(); Assert.Equal(PitchArrangement.HomePitch, post.PitchArrangement); Assert.Equal("Sân nhà A", post.PitchName); });
        fields["endTime"] = "18:00";
        await app.Post(client, "/Matchmaking/CreatePost", fields);
        await app.WithDb(async db => Assert.Single(await db.MatchmakingPosts.ToListAsync()));
    }

    [Fact]
    public async Task WorkspacePages_RenderForLeaderAndMember()
    {
        using var app = new RequirementsWebFactory(); await app.Seed();
        foreach (var user in new[] { "owner", "member" })
        {
            using var client = app.Client(user);
            foreach (var path in new[] { $"/Teams/Dashboard/{app.TeamA}", $"/Teams/Players?teamId={app.TeamA}", $"/Matches/Index?teamId={app.TeamA}", $"/Attendance/Detail/{app.Session}", $"/FireKeeping?teamId={app.TeamA}" })
            {
                var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("club-nav", await response.Content.ReadAsStringAsync());
            }
        }
    }
}
