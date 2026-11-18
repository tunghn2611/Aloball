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

public class PlayStylePhase2ATests
{
    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext db)
    {
        var store = new Microsoft.AspNetCore.Identity.EntityFrameworkCore.UserStore<ApplicationUser>(db);
        return new UserManager<ApplicationUser>(
            store, null!, new PasswordHasher<ApplicationUser>(), null!, null!, null!,
            new IdentityErrorDescriber(), null!, null!);
    }

    private static void SetUser(Controller controller, ApplicationUser user)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.UserName ?? user.Id)
        }, "TestAuth"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
    }

    [Fact]
    public void Parse_OrdersPlayStylePlusFirst_AndCapsAtOnePlusThreeRegular()
    {
        var parsed = PlayStyleCatalog.Parse("FINESSE,CLINICAL,RAPID+,TRICKSTER,POWER_SHOT,MAESTRO+,UNKNOWN");

        Assert.Equal(4, parsed.Count);
        Assert.True(parsed[0].IsPlus);
        Assert.Equal("RAPID", parsed[0].Definition.Code);
        Assert.All(parsed.Skip(1), s => Assert.False(s.IsPlus));
        Assert.Equal(new[] { "FINESSE", "CLINICAL", "TRICKSTER" }, parsed.Skip(1).Select(s => s.Definition.Code));
    }

    [Fact]
    public void ValidateAndFormat_RejectsMultiplePlus_MoreThan3Regular_AndUnknownCode()
    {
        Assert.False(PlayStyleCatalog.ValidateAndFormat("FINESSE+,RAPID+").IsValid);
        Assert.False(PlayStyleCatalog.ValidateAndFormat("FINESSE,CLINICAL,RAPID,TRICKSTER").IsValid);
        Assert.False(PlayStyleCatalog.ValidateAndFormat("NOT_A_REAL_SKILL").IsValid);

        var valid = PlayStyleCatalog.ValidateAndFormat("TRICKSTER,FINESSE+,CLINICAL,RAPID");
        Assert.True(valid.IsValid);
        Assert.Equal("FINESSE+,TRICKSTER,CLINICAL,RAPID", valid.Formatted);
    }

    [Fact]
    public void HardTackleWarning_TriggersOnlyWhenCasualTeamFacesAtLeast35PercentHardTackle()
    {
        // Build casual (Đá dưỡng sinh) team vibe
        var casualReviews = new List<TeamMatchReview>
        {
            new() { IsValid = true, PlayStyleVibeTags = $"{PlayStyleCatalog.VibeDaoDuongSinh},{PlayStyleCatalog.VibeVuiVe}" }
        };
        var casualVibe = PlayStyleCatalog.AnalyzeTeamVibe(casualReviews);

        // Build non-casual team vibe
        var intenseOwnVibe = PlayStyleCatalog.AnalyzeTeamVibe(new[]
        {
            new TeamMatchReview { IsValid = true, PlayStyleVibeTags = PlayStyleCatalog.VibeTheLuc }
        });

        // Opponent at 30% hard tackle (3/10) -> below 35% threshold -> NO warning
        var belowThresholdOpponent = PlayStyleCatalog.AnalyzeTeamVibe(
            Enumerable.Range(0, 3).Select(_ => new TeamMatchReview { IsValid = true, PlayStyleVibeTags = PlayStyleCatalog.VibeDaRat })
            .Concat(Enumerable.Range(0, 7).Select(_ => new TeamMatchReview { IsValid = true, PlayStyleVibeTags = PlayStyleCatalog.VibeBanBat })));

        // Opponent at exact 35.0% hard tackle (7/20) -> triggers warning ONLY for Đá dưỡng sinh team
        var atThresholdOpponent = PlayStyleCatalog.AnalyzeTeamVibe(
            Enumerable.Range(0, 7).Select(_ => new TeamMatchReview { IsValid = true, PlayStyleVibeTags = PlayStyleCatalog.VibeDaRat })
            .Concat(Enumerable.Range(0, 13).Select(_ => new TeamMatchReview { IsValid = true, PlayStyleVibeTags = PlayStyleCatalog.VibeBanBat })));

        Assert.False(belowThresholdOpponent.IsHighHardTackle);
        Assert.False(PlayStyleCatalog.ShouldShowHardTackleWarning(casualVibe, belowThresholdOpponent));

        Assert.Equal(35.0, atThresholdOpponent.HardTacklePercentage);
        Assert.True(atThresholdOpponent.IsHighHardTackle);
        Assert.True(PlayStyleCatalog.ShouldShowHardTackleWarning(casualVibe, atThresholdOpponent));
        Assert.False(PlayStyleCatalog.ShouldShowHardTackleWarning(intenseOwnVibe, atThresholdOpponent));
    }

    [Fact]
    public async Task SubmitReviewAsync_PersistsMultiSelectVibeTags_AndLoadsTeamVibeMap()
    {
        await using var db = CreateDb();
        var auth = new TeamAuthorizationService(db);
        var mmService = new MatchmakingService(db, auth);

        var captain = new ApplicationUser { Id = "cap-1", UserName = "cap@aloball.vn" };
        db.Users.Add(captain);

        var teamA = new Team { Name = "FC Dưỡng Sinh", Slug = "fc-duong-sinh" };
        var teamB = new Team { Name = "FC Phủi", Slug = "fc-phui" };
        db.Teams.AddRange(teamA, teamB);
        db.TeamMembers.Add(new TeamMember { TeamId = teamA.Id, UserId = captain.Id, Role = TeamRole.Owner });

        var match = new MatchGame
        {
            Category = MatchCategory.Friendly,
            Status = MatchStatus.Completed,
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            ConfirmedByTeamA = true,
            ConfirmedByTeamB = true,
            MatchDateTime = CompetitionRules.LocalNow.AddHours(-2)
        };
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var joinedVibes = $"{PlayStyleCatalog.VibeDaRat},{PlayStyleCatalog.VibeTheLuc},InvalidTagInjection";
        var (success, _) = await mmService.SubmitReviewAsync(match.Id, teamA.Id, captain.Id, 4, "Đá rất quyết liệt", joinedVibes);

        Assert.True(success);
        var savedReview = await db.TeamMatchReviews.SingleAsync(r => r.TargetTeamId == teamB.Id);
        Assert.Equal($"{PlayStyleCatalog.VibeDaRat},{PlayStyleCatalog.VibeTheLuc}", savedReview.PlayStyleVibeTags);

        var map = await MatchmakingService.LoadTeamVibesMapAsync(db, new[] { teamB.Id });
        Assert.True(map.TryGetValue(teamB.Id, out var vibeB));
        Assert.Equal(100.0, vibeB.HardTacklePercentage);
        Assert.True(vibeB.IsHighHardTackle);
    }

    [Fact]
    public async Task UpdatePlayStyles_RequiresManagerAccess_AndSupportsUnclaimedRosterSlots()
    {
        await using var db = CreateDb();
        var captain = new ApplicationUser { Id = "cap-u", UserName = "captain@club.vn" };
        var regularMember = new ApplicationUser { Id = "mem-u", UserName = "member@club.vn" };
        var outsider = new ApplicationUser { Id = "out-u", UserName = "outsider@club.vn" };
        db.Users.AddRange(captain, regularMember, outsider);

        var team = new Team { Name = "FC Cầu Giấy", Slug = "fc-cau-giay" };
        db.Teams.Add(team);
        db.TeamMembers.AddRange(
            new TeamMember { TeamId = team.Id, UserId = captain.Id, Role = TeamRole.Manager },
            new TeamMember { TeamId = team.Id, UserId = regularMember.Id, Role = TeamRole.Member });

        // Unclaimed player (LinkedUserId == null)
        var unclaimedPlayer = new Player
        {
            TeamId = team.Id,
            LinkedUserId = null,
            FullName = "Minh Khách 10",
            KitNumber = 10,
            IsActive = true
        };
        db.Players.Add(unclaimedPlayer);
        await db.SaveChangesAsync();

        var controller = new TeamsController(db, CreateUserManager(db), new TeamAuthorizationService(db));

        // Regular member cannot assign PlayStyles
        SetUser(controller, regularMember);
        var memberResult = await controller.UpdatePlayStyles(new TeamsController.UpdatePlayStylesDto
        {
            PlayerId = unclaimedPlayer.Id,
            PlayStyles = "FINESSE+"
        });
        var memberStatus = Assert.IsType<ObjectResult>(memberResult);
        Assert.Equal(403, memberStatus.StatusCode);

        // Outsider cannot assign PlayStyles
        SetUser(controller, outsider);
        var outsiderResult = await controller.UpdatePlayStyles(new TeamsController.UpdatePlayStylesDto
        {
            PlayerId = unclaimedPlayer.Id,
            PlayStyles = "FINESSE+"
        });
        var outsiderStatus = Assert.IsType<ObjectResult>(outsiderResult);
        Assert.Equal(403, outsiderStatus.StatusCode);

        // Captain/Manager CAN assign PlayStyles to unclaimed player
        SetUser(controller, captain);
        var captainResult = await controller.UpdatePlayStyles(new TeamsController.UpdatePlayStylesDto
        {
            PlayerId = unclaimedPlayer.Id,
            PlayStyles = "TRICKSTER,FINESSE+,RAPID,CLINICAL"
        });
        Assert.IsType<OkObjectResult>(captainResult);

        var updated = await db.Players.FindAsync(unclaimedPlayer.Id);
        Assert.Equal("FINESSE+,TRICKSTER,RAPID,CLINICAL", updated?.PlayStyles);
    }
}
