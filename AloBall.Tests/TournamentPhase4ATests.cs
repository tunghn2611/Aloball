using System.Reflection;
using AloBall.Web.Controllers;
using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AloBall.Tests;

public class TournamentPhase4ATests
{
    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"TournamentPhase4A_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Team MakeTeam(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Slug = name.ToLowerInvariant().Replace(' ', '-') + "-" + Guid.NewGuid().ToString("N")[..5],
        ActivityArea = "Cầu Giấy, Hà Nội",
        IsPublic = true,
        IsDraft = false
    };

    [Fact]
    public async Task RoundRobinFixtureGenerator_GeneratesBalancedNChoose2Fixtures_For4And5Teams()
    {
        using var db = CreateDb();
        var organizer = new ApplicationUser { Id = "org_1", UserName = "org1@aloball.vn", Email = "org1@aloball.vn", FullName = "BTC Giải" };
        db.Users.Add(organizer);

        var t4Teams = Enumerable.Range(1, 4).Select(i => MakeTeam($"FC Bốn {i}")).ToList();
        var t5Teams = Enumerable.Range(1, 5).Select(i => MakeTeam($"FC Năm {i}")).ToList();
        db.Teams.AddRange(t4Teams);
        db.Teams.AddRange(t5Teams);
        await db.SaveChangesAsync();

        var svc = new TournamentService(db, new TeamAuthorizationService(db));
        var startDate = new DateTime(2026, 12, 5);

        // 4 teams -> 6 unique matches (N*(N-1)/2) across 3 rounds
        var c4 = await svc.CreateTournamentAsync(new CreateTournamentInput
        {
            Name = "Cúp Tứ Hùng",
            Region = "Cầu Giấy, Hà Nội",
            Format = TournamentFormat.RoundRobinLeague,
            StartDate = startDate,
            PitchVenue = "Sân Thành Phát",
            EntryFeeVnd = 500_000m
        }, organizer.Id);

        Assert.True(c4.Success);
        foreach (var tm in t4Teams)
            Assert.True((await svc.AddTeamByOrganizerAsync(c4.Tournament!.Id, tm.Id, null, organizer.Id)).Success);

        var gen4 = await svc.GenerateFixturesAsync(c4.Tournament!.Id, organizer.Id);
        Assert.True(gen4.Success);
        Assert.Equal(6, gen4.GeneratedMatches.Count); // 4*3/2
        Assert.Equal(3, gen4.GeneratedMatches.Select(m => m.RoundLabel).Distinct().Count());
        Assert.Equal(6, gen4.GeneratedMatches.Select(m => (m.TeamAId < m.TeamBId!.Value ? m.TeamAId : m.TeamBId!.Value, m.TeamAId < m.TeamBId!.Value ? m.TeamBId!.Value : m.TeamAId)).Distinct().Count());
        Assert.Equal(TournamentStatus.Ongoing, (await db.Tournaments.FindAsync(c4.Tournament.Id))!.Status);

        // Duplicate generation must be blocked
        var dupGen = await svc.GenerateFixturesAsync(c4.Tournament.Id, organizer.Id);
        Assert.False(dupGen.Success);

        // 5 teams (odd count with Bye) -> 10 unique matches across 5 rounds
        var c5 = await svc.CreateTournamentAsync(new CreateTournamentInput
        {
            Name = "Giải Ngũ Hùng",
            Region = "Hà Nội",
            Format = TournamentFormat.RoundRobinLeague,
            StartDate = startDate,
            PitchVenue = "Sân PVV"
        }, organizer.Id);
        foreach (var tm in t5Teams)
            await svc.AddTeamByOrganizerAsync(c5.Tournament!.Id, tm.Id, null, organizer.Id);

        var gen5 = await svc.GenerateFixturesAsync(c5.Tournament!.Id, organizer.Id);
        Assert.True(gen5.Success);
        Assert.Equal(10, gen5.GeneratedMatches.Count); // 5*4/2
        Assert.Equal(5, gen5.GeneratedMatches.Select(m => m.RoundLabel).Distinct().Count());
    }

    [Fact]
    public async Task Standings_TieBreaker_SortsByPoints_GoalDiff_ThenGoalsFor_OnlyCountingConfirmedMatches()
    {
        using var db = CreateDb();
        var organizer = new ApplicationUser { Id = "org_2", UserName = "org2@aloball.vn", Email = "org2@aloball.vn", FullName = "BTC" };
        db.Users.Add(organizer);

        var tAlpha = MakeTeam("FC Alpha");
        var tBravo = MakeTeam("FC Bravo");
        var tCharlie = MakeTeam("FC Charlie");
        var tDelta = MakeTeam("FC Delta");
        db.Teams.AddRange(tAlpha, tBravo, tCharlie, tDelta);
        await db.SaveChangesAsync();

        var svc = new TournamentService(db, new TeamAuthorizationService(db));
        var created = await svc.CreateTournamentAsync(new CreateTournamentInput
        {
            Name = "League TieBreak Cup",
            Region = "Cầu Giấy",
            Format = TournamentFormat.RoundRobinLeague,
            StartDate = new DateTime(2026, 11, 20),
            PitchVenue = "Sân PVV"
        }, organizer.Id);
        var tid = created.Tournament!.Id;

        foreach (var tm in new[] { tAlpha, tBravo, tCharlie, tDelta })
            await svc.AddTeamByOrganizerAsync(tid, tm.Id, null, organizer.Id);

        // Alpha beats Delta 4-1 (+3 pts, +3 GD, 4 GF)
        // Bravo beats Charlie 3-0 (+3 pts, +3 GD, 3 GF) -> Alpha ranks above Bravo via GF (4 > 3)!
        // Charlie loses 0-3 vs Bravo, then beats Delta 1-0 (+3 pts, -2 GD, 1 GF) -> 3rd behind Bravo (+3 GD)!
        // Unconfirmed single-sided Alpha vs Charlie 9-0 MUST BE IGNORED until dual confirmed!
        db.Matches.AddRange(
            new MatchGame { TournamentId = tid, RoundLabel = "Vòng 1", Category = MatchCategory.Tournament, Status = MatchStatus.Completed, TeamAId = tAlpha.Id, TeamBId = tDelta.Id, ScoreA = 4, ScoreB = 1, ConfirmedByTeamA = true, ConfirmedByTeamB = true, MatchDateTime = new DateTime(2026, 11, 20, 18, 0, 0) },
            new MatchGame { TournamentId = tid, RoundLabel = "Vòng 1", Category = MatchCategory.Tournament, Status = MatchStatus.Completed, TeamAId = tBravo.Id, TeamBId = tCharlie.Id, ScoreA = 3, ScoreB = 0, ConfirmedByTeamA = true, ConfirmedByTeamB = true, MatchDateTime = new DateTime(2026, 11, 20, 19, 30, 0) },
            new MatchGame { TournamentId = tid, RoundLabel = "Vòng 2", Category = MatchCategory.Tournament, Status = MatchStatus.Completed, TeamAId = tCharlie.Id, TeamBId = tDelta.Id, ScoreA = 1, ScoreB = 0, ConfirmedByTeamA = true, ConfirmedByTeamB = true, MatchDateTime = new DateTime(2026, 11, 27, 18, 0, 0) },
            new MatchGame { TournamentId = tid, RoundLabel = "Vòng 2", Category = MatchCategory.Tournament, Status = MatchStatus.Completed, TeamAId = tAlpha.Id, TeamBId = tCharlie.Id, ScoreA = 9, ScoreB = 0, ConfirmedByTeamA = true, ConfirmedByTeamB = false, MatchDateTime = new DateTime(2026, 11, 27, 19, 30, 0) }
        );
        await db.SaveChangesAsync();

        await svc.RecalculateStandingsAsync(tid);
        var details = await svc.BuildDetailsAsync(tid, organizer.Id, "standings", "https://aloball.vercel.app");

        var rows = details!.StandingGroups.Single().Rows;
        Assert.Equal([tAlpha.Id, tBravo.Id, tCharlie.Id, tDelta.Id], rows.Select(r => r.TeamId).ToArray());

        Assert.Equal(1, rows[0].Rank); // 3 pts, +3 GD, 4 GF
        Assert.Equal(3, rows[0].Points);
        Assert.Equal(4, rows[0].GoalsFor);

        Assert.Equal(2, rows[1].Rank); // 3 pts, +3 GD, 3 GF
        Assert.Equal(3, rows[1].Points);
        Assert.Equal(3, rows[1].GoalsFor);

        Assert.Equal(3, rows[2].Rank); // 3 pts, -2 GD
        Assert.Equal(-2, rows[2].GoalDiff);

        Assert.Equal(4, rows[3].Rank); // 0 pts
        Assert.Equal(0, rows[3].Points);

        Assert.Contains("FC Alpha", details.ZaloStandingsSummary);
    }

    [Fact]
    public async Task ConfirmingTournamentMatch_UpdatesPersistedStandings_GoldenBoot_AndCardEvents()
    {
        using var db = CreateDb();
        var organizer = new ApplicationUser { Id = "org_3", UserName = "org3@aloball.vn", Email = "org3@aloball.vn", FullName = "BTC" };
        db.Users.Add(organizer);

        var t1 = MakeTeam("KLB FC");
        var t2 = MakeTeam("Sao Vàng FC");
        db.Teams.AddRange(t1, t2);

        var p1 = new Player { Id = Guid.NewGuid(), TeamId = t1.Id, FullName = "Nguyễn Tiến Linh", KitNumber = 22, IsActive = true };
        var p2 = new Player { Id = Guid.NewGuid(), TeamId = t1.Id, FullName = "Nguyễn Quang Hải", KitNumber = 19, IsActive = true };
        var p3 = new Player { Id = Guid.NewGuid(), TeamId = t2.Id, FullName = "Nguyễn Hoàng Đức", KitNumber = 14, IsActive = true };
        db.Players.AddRange(p1, p2, p3);
        await db.SaveChangesAsync();

        var svc = new TournamentService(db, new TeamAuthorizationService(db));
        var created = await svc.CreateTournamentAsync(new CreateTournamentInput
        {
            Name = "Golden Boot Cup",
            Region = "Hà Nội",
            Format = TournamentFormat.RoundRobinLeague,
            StartDate = new DateTime(2026, 11, 20),
            PitchVenue = "Sân Thành Phát"
        }, organizer.Id);

        var tid = created.Tournament!.Id;
        await svc.AddTeamByOrganizerAsync(tid, t1.Id, null, organizer.Id);
        await svc.AddTeamByOrganizerAsync(tid, t2.Id, null, organizer.Id);
        var gen = await svc.GenerateFixturesAsync(tid, organizer.Id);
        var match = gen.GeneratedMatches.Single();

        // Match ends 2-1 in favor of team 1 with goals, assist, yellow & red card
        match.ScoreA = match.TeamAId == t1.Id ? 2 : 1;
        match.ScoreB = match.TeamAId == t1.Id ? 1 : 2;
        match.Status = MatchStatus.Completed;
        match.ConfirmedByTeamA = true;
        match.ConfirmedByTeamB = true;
        match.ConfirmedAt = DateTime.UtcNow;

        db.MatchGoalEvents.AddRange(
            new MatchGoalEvent { MatchId = match.Id, TeamId = t1.Id, ScorerPlayerId = p1.Id, AssistPlayerId = p2.Id, Minute = 12 },
            new MatchGoalEvent { MatchId = match.Id, TeamId = t1.Id, ScorerPlayerId = p1.Id, AssistPlayerId = null, Minute = 44 },
            new MatchGoalEvent { MatchId = match.Id, TeamId = t2.Id, ScorerPlayerId = p3.Id, AssistPlayerId = null, Minute = 60 }
        );
        db.MatchCardEvents.AddRange(
            new MatchCardEvent { MatchId = match.Id, TeamId = t1.Id, PlayerId = p2.Id, CardType = MatchCardType.Yellow, Minute = 35 },
            new MatchCardEvent { MatchId = match.Id, TeamId = t2.Id, PlayerId = p3.Id, CardType = MatchCardType.Red, Minute = 72 }
        );
        await db.SaveChangesAsync();

        await svc.SyncTournamentForMatchAsync(match.Id);

        // Assert persisted TournamentStanding rows updated
        var st1 = await db.TournamentStandings.SingleAsync(s => s.TournamentId == tid && s.TeamId == t1.Id);
        var st2 = await db.TournamentStandings.SingleAsync(s => s.TournamentId == tid && s.TeamId == t2.Id);
        Assert.Equal(1, st1.Rank);
        Assert.Equal(3, st1.Points);
        Assert.Equal(1, st1.YellowCards);
        Assert.Equal(0, st1.RedCards);
        Assert.Equal(2, st2.Rank);
        Assert.Equal(0, st2.Points);
        Assert.Equal(1, st2.RedCards);

        // Assert Golden Boot & Discipline leaderboard
        var hub = await svc.BuildDetailsAsync(tid, organizer.Id, "scorers", "https://aloball.vercel.app");
        Assert.Equal(3, hub!.Scorers.Count);
        Assert.Equal(p1.Id, hub.Scorers[0].PlayerId);
        Assert.Equal(2, hub.Scorers[0].Goals);
        Assert.Equal(p3.Id, hub.Scorers[1].PlayerId);
        Assert.Equal(1, hub.Scorers[1].Goals);
        Assert.Equal(1, hub.Scorers[1].RedCards);
        Assert.Equal(p2.Id, hub.Scorers[2].PlayerId);
        Assert.Equal(1, hub.Scorers[2].Assists);
        Assert.Equal(1, hub.Scorers[2].YellowCards);
    }

    [Fact]
    public async Task GroupPlusKnockout_SplitsGroups_AndAdvancesTop2ToSemiFinals()
    {
        using var db = CreateDb();
        var organizer = new ApplicationUser { Id = "org_4", UserName = "org4@aloball.vn", Email = "org4@aloball.vn", FullName = "BTC" };
        db.Users.Add(organizer);

        var teams = Enumerable.Range(1, 4).Select(i => MakeTeam($"FC Bảng {i}")).ToList();
        db.Teams.AddRange(teams);
        await db.SaveChangesAsync();

        var svc = new TournamentService(db, new TeamAuthorizationService(db));
        var created = await svc.CreateTournamentAsync(new CreateTournamentInput
        {
            Name = "Cup Tứ Kết 2 Bảng",
            Region = "TP.HCM",
            Format = TournamentFormat.GroupPlusKnockout,
            StartDate = new DateTime(2026, 11, 20),
            PitchVenue = "Sân Phú Mỹ Hưng"
        }, organizer.Id);

        var tid = created.Tournament!.Id;
        await svc.AddTeamByOrganizerAsync(tid, teams[0].Id, "Bảng A", organizer.Id);
        await svc.AddTeamByOrganizerAsync(tid, teams[1].Id, "Bảng A", organizer.Id);
        await svc.AddTeamByOrganizerAsync(tid, teams[2].Id, "Bảng B", organizer.Id);
        await svc.AddTeamByOrganizerAsync(tid, teams[3].Id, "Bảng B", organizer.Id);

        var gen = await svc.GenerateFixturesAsync(tid, organizer.Id);
        Assert.True(gen.Success);
        Assert.Equal(2, gen.GeneratedMatches.Count); // 1 match in Bảng A, 1 match in Bảng B

        foreach (var m in gen.GeneratedMatches)
        {
            m.Status = MatchStatus.Completed;
            m.ScoreA = 2;
            m.ScoreB = 0;
            m.ConfirmedByTeamA = true;
            m.ConfirmedByTeamB = true;
            m.ConfirmedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        await svc.RecalculateStandingsAsync(tid);

        var semiGen = await svc.GenerateNextRoundAsync(tid, organizer.Id);
        Assert.True(semiGen.Success);
        Assert.Equal(2, semiGen.GeneratedMatches.Count);
        Assert.All(semiGen.GeneratedMatches, m => Assert.Equal("Bán kết", m.RoundLabel));
    }

    [Fact]
    public async Task Security_OrganizerChecks_InviteCodeJoin_AndValidateAntiForgeryToken()
    {
        using var db = CreateDb();
        var organizer = new ApplicationUser { Id = "org_5", UserName = "org5@aloball.vn", Email = "org5@aloball.vn", FullName = "BTC" };
        var captain = new ApplicationUser { Id = "cap_5", UserName = "cap5@aloball.vn", Email = "cap5@aloball.vn", FullName = "Captain" };
        var outsider = new ApplicationUser { Id = "out_5", UserName = "out5@aloball.vn", Email = "out5@aloball.vn", FullName = "Outsider" };
        db.Users.AddRange(organizer, captain, outsider);

        var t1 = MakeTeam("FC Captain");
        var t2 = MakeTeam("FC B");
        db.Teams.AddRange(t1, t2);
        db.TeamMembers.Add(new TeamMember { TeamId = t1.Id, UserId = captain.Id, Role = TeamRole.Owner });
        await db.SaveChangesAsync();

        var svc = new TournamentService(db, new TeamAuthorizationService(db));
        var created = await svc.CreateTournamentAsync(new CreateTournamentInput
        {
            Name = "Security Cup",
            Region = "Hà Nội",
            Format = TournamentFormat.RoundRobinLeague,
            StartDate = new DateTime(2026, 12, 1),
            PitchVenue = "Sân Bách Khoa"
        }, organizer.Id);

        var tid = created.Tournament!.Id;

        // Outsider cannot add team or generate fixtures
        Assert.False((await svc.AddTeamByOrganizerAsync(tid, t2.Id, null, outsider.Id)).Success);
        Assert.False((await svc.GenerateFixturesAsync(tid, outsider.Id)).Success);

        // Outsider cannot join on behalf of captain's team
        Assert.False((await svc.JoinByInviteCodeAsync(created.Tournament.InviteCode, t1.Id, outsider.Id)).Success);

        // Captain joins using InviteCode cleanly
        var joinOk = await svc.JoinByInviteCodeAsync(created.Tournament.InviteCode.ToLowerInvariant(), t1.Id, captain.Id);
        Assert.True(joinOk.Success);
        Assert.Equal(tid, joinOk.TournamentId);

        // ValidateAntiForgeryToken enforced on all POST endpoints
        var postMethods = typeof(TournamentsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<HttpPostAttribute>() != null)
            .ToList();

        Assert.NotEmpty(postMethods);
        foreach (var pm in postMethods)
            Assert.NotNull(pm.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());

        Assert.NotNull(typeof(MatchesController).GetMethod(nameof(MatchesController.AddCardEvent))!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.NotNull(typeof(MatchesController).GetMethod(nameof(MatchesController.DeleteCardEvent))!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }
}
