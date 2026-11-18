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

    [Fact]
    public void CalculateCardTier_MapsBronzeSilverGoldAndMotmCorrectly()
    {
        Assert.Equal("bronze", ShareCardController.CalculateCardTier(55, false));
        Assert.Equal("bronze", ShareCardController.CalculateCardTier(69, false));
        Assert.Equal("silver", ShareCardController.CalculateCardTier(70, false));
        Assert.Equal("silver", ShareCardController.CalculateCardTier(79, false));
        Assert.Equal("gold", ShareCardController.CalculateCardTier(80, false));
        Assert.Equal("gold", ShareCardController.CalculateCardTier(89, false));
        Assert.Equal("motm", ShareCardController.CalculateCardTier(64, true));
        Assert.Equal("motm", ShareCardController.CalculateCardTier(88, true));
    }

    [Fact]
    public async Task GetPlayerCard_ClampsStats55To99_OrdersPlayStylesPlusFirst_AndHonorsPrivacyFlags()
    {
        using var db = CreateInMemoryDbContext();
        var userManager = CreateMockUserManager(db);
        var authService = new TeamAuthorizationService(db);
        var httpFactory = new DummyHttpClientFactory();

        var team = new Team { Id = Guid.NewGuid(), Name = "FC Hà Nội", Slug = "fc-ha-noi", AverageRating = 5.0 };
        db.Teams.Add(team);

        var user = new ApplicationUser
        {
            Id = "u-card-1",
            UserName = "secret@aloball.vn",
            Email = "secret@aloball.vn",
            PhoneNumber = "0909123456",
            AvatarUrl = "https://cdn.example.com/private-avatar.jpg"
        };
        db.Users.Add(user);

        var player = new Player
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            LinkedUserId = user.Id,
            FullName = "Quang Hải",
            Position = "Tiền đạo",
            PreferredFoot = "Trái",
            PhotoUrl = "https://cdn.example.com/photo.jpg",
            PlayStyles = "FINESSE,RAPID+,TRICKSTER",
            IsPublicProfile = true,
            PublicPhoto = false,
            PublicPreferredFoot = false,
            PublicKitNumber = false
        };
        db.Players.Add(player);

        // Give top weekly AB Coin -> qualifies for MOTM tier
        db.PlayerAbJournalEntries.Add(new PlayerAbJournalEntry
        {
            TeamId = team.Id,
            PlayerId = player.Id,
            StatisticalPeriod = CompetitionRules.LocalNow.ToString("yyyy-MM"),
            OperationKey = $"k_{Guid.NewGuid()}",
            Points = 5,
            EventDate = CompetitionRules.LocalNow
        });

        // Extreme goal count to test clamping at 99
        var match = new MatchGame
        {
            Id = Guid.NewGuid(),
            TeamAId = team.Id,
            ScoreA = 20,
            ScoreB = 0,
            Status = MatchStatus.Completed,
            ConfirmedByTeamA = true,
            ConfirmedByTeamB = true,
            MatchDateTime = DateTime.UtcNow.AddDays(-1)
        };
        db.Matches.Add(match);
        for (int i = 0; i < 20; i++)
        {
            db.MatchGoalEvents.Add(new MatchGoalEvent { MatchId = match.Id, TeamId = team.Id, ScorerPlayerId = player.Id });
        }
        await db.SaveChangesAsync();

        var controller = new ShareCardController(db, userManager, authService, httpFactory);
        var result = await controller.GetPlayerCard(player.Id);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<PlayerCardDto>(ok.Value);

        Assert.Equal("motm", dto.Tier);
        Assert.True(dto.IsMotm);
        Assert.Equal(99, dto.Stats.Att);
        Assert.InRange(dto.Stats.Ovr, 55, 99);

        // PlayStyle+ gold sorted first
        Assert.Equal("RAPID+,FINESSE,TRICKSTER", dto.PlayStyles);
        Assert.Equal(3, dto.PlayStyleItems.Count);
        Assert.True(dto.PlayStyleItems[0].IsPlus);
        Assert.Equal("RAPID", dto.PlayStyleItems[0].Code);

        // Privacy flags enforced for anonymous caller (no photo, no preferredFoot, no kitNumber, no email/phone)
        Assert.Null(dto.PhotoUrl);
        Assert.Null(dto.PreferredFoot);
        Assert.Null(dto.KitNumber);
    }

    [Fact]
    public async Task GetMatchPoster_FormatsGroupedGoalsAndAssistsSummary_WithVnTimezone()
    {
        using var db = CreateInMemoryDbContext();
        var controller = new ShareCardController(db, CreateMockUserManager(db), new TeamAuthorizationService(db), new DummyHttpClientFactory());

        var teamA = new Team { Id = Guid.NewGuid(), Name = "Đội Nhà", Slug = "doi-nha", CoverImageUrl = "/img/cover.jpg", CoverOpacity = 0.55 };
        var teamB = new Team { Id = Guid.NewGuid(), Name = "Đội Khách", Slug = "doi-khach" };
        db.Teams.AddRange(teamA, teamB);

        var p1 = new Player { Id = Guid.NewGuid(), TeamId = teamA.Id, FullName = "Tuấn Anh" };
        var p2 = new Player { Id = Guid.NewGuid(), TeamId = teamA.Id, FullName = "Hoàng Đức" };
        db.Players.AddRange(p1, p2);

        // 12:30 UTC == 19:30 Asia/Ho_Chi_Minh (UTC+7)
        var utcKickoff = new DateTime(2026, 11, 15, 12, 30, 0, DateTimeKind.Utc);
        var match = new MatchGame
        {
            Id = Guid.NewGuid(),
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            ScoreA = 3,
            ScoreB = 0,
            Status = MatchStatus.Completed,
            ConfirmedByTeamA = true,
            ConfirmedByTeamB = true,
            PitchName = "Sân Mỹ Đình",
            MatchDateTime = utcKickoff
        };
        db.Matches.Add(match);

        db.MatchGoalEvents.AddRange(
            new MatchGoalEvent { MatchId = match.Id, TeamId = teamA.Id, ScorerPlayerId = p1.Id, Scorer = p1, AssistPlayerId = p2.Id, Assist = p2, Minute = 10 },
            new MatchGoalEvent { MatchId = match.Id, TeamId = teamA.Id, ScorerPlayerId = p1.Id, Scorer = p1, Minute = 25 },
            new MatchGoalEvent { MatchId = match.Id, TeamId = teamA.Id, ScorerPlayerId = p2.Id, Scorer = p2, AssistPlayerId = p1.Id, Assist = p1, Minute = 50 });
        await db.SaveChangesAsync();

        var result = await controller.GetMatchPoster(match.Id);
        var ok = Assert.IsType<OkObjectResult>(result);
        var poster = Assert.IsType<MatchPosterDto>(ok.Value);

        Assert.Equal("19:30 · 15/11/2026", poster.MatchDate);
        Assert.Equal(0.55, poster.TeamA.CoverOpacity);
        Assert.Contains(poster.ScorersA, s => s.SummaryText == "Tuấn Anh (2⚽, 1👟)");
        Assert.Contains(poster.ScorersA, s => s.SummaryText == "Hoàng Đức (1⚽, 1👟)");
    }

    [Theory]
    [InlineData("http://localhost/secret.png")]
    [InlineData("http://127.0.0.1/img.png")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.0.0.5/avatar.png")]
    [InlineData("http://172.16.0.10/avatar.png")]
    [InlineData("http://192.168.1.100/avatar.png")]
    [InlineData("http://[::1]/img.png")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://user@8.8.8.8/img.png")]
    [InlineData("/../../appsettings.json")]
    public async Task ProxyImage_BlocksSsrfAndPathTraversalUrls(string dangerousUrl)
    {
        using var db = CreateInMemoryDbContext();
        var controller = new ShareCardController(db, CreateMockUserManager(db), new TeamAuthorizationService(db), new DummyHttpClientFactory());

        var result = await controller.ProxyImage(dangerousUrl);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ValidateExternalImageUri_BlocksDnsRebindingToPrivateIp_AndAllowsPublicIp()
    {
        var blocked = await ShareCardController.ValidateExternalImageUriAsync(
            "https://evil.example.com/avatar.png",
            _ => Task.FromResult(new[] { System.Net.IPAddress.Parse("10.1.2.3") }));
        Assert.False(blocked.IsValid);

        var allowed = await ShareCardController.ValidateExternalImageUriAsync(
            "https://cdn.example.com/avatar.png",
            _ => Task.FromResult(new[] { System.Net.IPAddress.Parse("93.184.216.34") }));
        Assert.True(allowed.IsValid);
    }
}
