using AloBall.Web.Controllers;
using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace AloBall.Tests;

public class ShareCardTests
{
    private class DummyHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }

    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private UserManager<ApplicationUser> CreateMockUserManager(ApplicationDbContext db)
    {
        var userStore = new Microsoft.AspNetCore.Identity.EntityFrameworkCore.UserStore<ApplicationUser>(db);
        return new UserManager<ApplicationUser>(
            userStore,
            null!,
            new PasswordHasher<ApplicationUser>(),
            null!,
            null!,
            null!,
            new IdentityErrorDescriber(),
            null!,
            null!);
    }

    [Fact]
    public async Task GetPlayerCard_ReturnsValidStatsWithin55To99_AndCorrectTier()
    {
        using var db = CreateInMemoryDbContext();
        var userManager = CreateMockUserManager(db);
        var authService = new TeamAuthorizationService(db);
        var httpFactory = new DummyHttpClientFactory();

        var team = new Team
        {
            Id = Guid.NewGuid(),
            Name = "FC Test Champions",
            Slug = "fc-test-champions",
            AverageRating = 4.8
        };
        db.Teams.Add(team);

        var player = new Player
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            FullName = "Nguyễn Văn Sơn",
            ShirtName = "VĂN SƠN",
            KitNumber = 10,
            Position = "Tiền đạo",
            PreferredFoot = "Phải",
            Region = "Cầu Giấy, Hà Nội",
            Experience = "Trên 5 năm",
            IsPublicProfile = true
        };
        db.Players.Add(player);

        // Add confirmed match with player as scorer
        var match = new MatchGame
        {
            Id = Guid.NewGuid(),
            TeamAId = team.Id,
            ScoreA = 3,
            ScoreB = 1,
            Status = MatchStatus.Completed,
            ConfirmedByTeamA = true,
            ConfirmedByTeamB = true,
            MatchDateTime = DateTime.UtcNow.AddDays(-2),
            PitchName = "Sân C500"
        };
        db.Matches.Add(match);

        db.MatchPlayerLineups.Add(new MatchPlayerLineup
        {
            MatchId = match.Id,
            PlayerId = player.Id,
            TeamId = team.Id,
            HasActuallyPlayed = true,
            IsStarting = true
        });

        db.MatchGoalEvents.Add(new MatchGoalEvent
        {
            MatchId = match.Id,
            TeamId = team.Id,
            ScorerPlayerId = player.Id,
            Minute = 25
        });

        await db.SaveChangesAsync();

        var controller = new ShareCardController(db, userManager, authService, httpFactory);
        var result = await controller.GetPlayerCard(player.Id);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var card = Assert.IsType<PlayerCardDto>(okResult.Value);

        Assert.Equal("Nguyễn Văn Sơn", card.FullName);
        Assert.Equal("VĂN SƠN", card.DisplayName);
        Assert.Equal(10, card.KitNumber);
        Assert.Equal("ST", card.PosCode);
        Assert.InRange(card.Stats.Att, 55, 99);
        Assert.InRange(card.Stats.Cre, 55, 99);
        Assert.InRange(card.Stats.Sta, 55, 99);
        Assert.InRange(card.Stats.Win, 55, 99);
        Assert.InRange(card.Stats.Rep, 55, 99);
        Assert.InRange(card.Stats.Exp, 55, 99);
        Assert.InRange(card.Stats.Ovr, 55, 99);
        Assert.Contains(card.Tier, new[] { "bronze", "silver", "gold", "motm" });
    }

    [Fact]
    public async Task GetPlayerCard_WhenPrivateAndAnonymous_ReturnsNotFound()
    {
        using var db = CreateInMemoryDbContext();
        var userManager = CreateMockUserManager(db);
        var authService = new TeamAuthorizationService(db);
        var httpFactory = new DummyHttpClientFactory();

        var team = new Team { Id = Guid.NewGuid(), Name = "FC Private", Slug = "fc-private" };
        db.Teams.Add(team);

        var player = new Player
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            FullName = "Cầu Thủ Ẩn Danh",
            IsPublicProfile = false
        };
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var controller = new ShareCardController(db, userManager, authService, httpFactory);
        var result = await controller.GetPlayerCard(player.Id);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetMatchPoster_UnconfirmedMatch_ReturnsBadRequest()
    {
        using var db = CreateInMemoryDbContext();
        var userManager = CreateMockUserManager(db);
        var authService = new TeamAuthorizationService(db);
        var httpFactory = new DummyHttpClientFactory();

        var teamA = new Team { Id = Guid.NewGuid(), Name = "Đội A", Slug = "doi-a" };
        var teamB = new Team { Id = Guid.NewGuid(), Name = "Đội B", Slug = "doi-b" };
        db.Teams.AddRange(teamA, teamB);

        var match = new MatchGame
        {
            Id = Guid.NewGuid(),
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            ScoreA = 2,
            ScoreB = 2,
            Status = MatchStatus.Completed,
            ConfirmedByTeamA = true,
            ConfirmedByTeamB = false // Not confirmed by Team B
        };
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var controller = new ShareCardController(db, userManager, authService, httpFactory);
        var result = await controller.GetMatchPoster(match.Id);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetMatchPoster_ConfirmedMatch_ReturnsGroupedScorersAndMotm()
    {
        using var db = CreateInMemoryDbContext();
        var userManager = CreateMockUserManager(db);
        var authService = new TeamAuthorizationService(db);
        var httpFactory = new DummyHttpClientFactory();

        var teamA = new Team { Id = Guid.NewGuid(), Name = "AloBall FC", Slug = "aloball-fc" };
        var teamB = new Team { Id = Guid.NewGuid(), Name = "Tràng An FC", Slug = "trang-an-fc" };
        db.Teams.AddRange(teamA, teamB);

        var playerA = new Player { Id = Guid.NewGuid(), TeamId = teamA.Id, FullName = "Hải Long" };
        var playerB = new Player { Id = Guid.NewGuid(), TeamId = teamB.Id, FullName = "Minh Tuấn" };
        db.Players.AddRange(playerA, playerB);

        var match = new MatchGame
        {
            Id = Guid.NewGuid(),
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            ScoreA = 2,
            ScoreB = 1,
            Status = MatchStatus.Completed,
            ConfirmedByTeamA = true,
            ConfirmedByTeamB = true,
            PitchName = "Sân Thành Phát",
            MatchDateTime = DateTime.UtcNow.AddDays(-1)
        };
        db.Matches.Add(match);

        db.MatchGoalEvents.Add(new MatchGoalEvent
        {
            MatchId = match.Id,
            TeamId = teamA.Id,
            ScorerPlayerId = playerA.Id,
            Scorer = playerA,
            Minute = 15
        });
        db.MatchGoalEvents.Add(new MatchGoalEvent
        {
            MatchId = match.Id,
            TeamId = teamA.Id,
            ScorerPlayerId = playerA.Id,
            Scorer = playerA,
            Minute = 40
        });
        db.MatchGoalEvents.Add(new MatchGoalEvent
        {
            MatchId = match.Id,
            TeamId = teamB.Id,
            ScorerPlayerId = playerB.Id,
            Scorer = playerB,
            Minute = 55
        });

        await db.SaveChangesAsync();

        var controller = new ShareCardController(db, userManager, authService, httpFactory);
        var result = await controller.GetMatchPoster(match.Id);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var poster = Assert.IsType<MatchPosterDto>(okResult.Value);

        Assert.Equal(2, poster.ScoreA);
        Assert.Equal(1, poster.ScoreB);
        Assert.Single(poster.ScorersA);
        Assert.Equal("Hải Long", poster.ScorersA[0].PlayerName);
        Assert.Equal(2, poster.ScorersA[0].Goals);
        Assert.NotNull(poster.Motm);
        Assert.Equal("Hải Long", poster.Motm!.PlayerName);
        Assert.Equal("aloball.vercel.app", poster.BrandTag);
    }
}
