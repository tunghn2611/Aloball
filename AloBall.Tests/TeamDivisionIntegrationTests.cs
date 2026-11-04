using AloBall.Web.Controllers;
using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using AloBall.Web.Services.TeamDivision;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Xunit;

namespace AloBall.Tests;

public class TestUserManager : UserManager<ApplicationUser>
{
    private readonly ApplicationUser _currentUser;

    public TestUserManager(ApplicationUser currentUser)
        : base(new TestUserStore(),
               Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
               new PasswordHasher<ApplicationUser>(),
               new IUserValidator<ApplicationUser>[0],
               new IPasswordValidator<ApplicationUser>[0],
               new UpperInvariantLookupNormalizer(),
               new IdentityErrorDescriber(),
               null!,
               new Logger<UserManager<ApplicationUser>>(new LoggerFactory()))
    {
        _currentUser = currentUser;
    }

    public override Task<ApplicationUser?> GetUserAsync(ClaimsPrincipal principal)
    {
        return Task.FromResult<ApplicationUser?>(_currentUser);
    }

    private class TestUserStore : IUserStore<ApplicationUser>
    {
        public void Dispose() { }
        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.Id);
        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.UserName);
        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken ct) => Task.CompletedTask;
        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.NormalizedUserName);
        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken ct) => Task.CompletedTask;
        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(IdentityResult.Success);
        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken ct) => Task.FromResult<ApplicationUser?>(null);
        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken ct) => Task.FromResult<ApplicationUser?>(null);
    }
}

public class TestMatchmakingService : IMatchmakingService
{
    public Task<(bool Success, string Message, Guid? MatchId)> AcceptProposalAndLockMatchAsync(Guid postId, Guid proposalId, string managerUserId) => Task.FromResult<(bool, string, Guid?)>((true, "", null));
    public Task<(bool Success, string Message)> SubmitReviewAsync(Guid matchId, Guid reviewerTeamId, string userId, int stars, string comment) => Task.FromResult((true, ""));
    public Task UpdateTeamRatingAndBlacklistStatusAsync(Guid teamId) => Task.CompletedTask;
    public Task<(bool Success, string Message)> ResolveAppealAsync(Guid appealId, bool invalidateReview, string adminNote, string adminUserId) => Task.FromResult((true, ""));
    public Task<(bool Success, string Message)> SetTeamBlacklistStatusAsync(Guid teamId, bool isRestricted, string? reason, string adminUserId) => Task.FromResult((true, ""));
}

public class TeamDivisionIntegrationTests
{
    [Theory]
    [InlineData(true, "InsufficientPlayersCount")]
    [InlineData(false, "UnconfirmedSubstitutes")]
    public async Task Preview_EmptySelectionDoesNotSilentlySelectEntireRoster(bool explicitlyEmpty, string expectedCode)
    {
        using var db = CreateInMemoryDbContext();
        var (controller, _, managerId, _) = SetupController(db);
        var team = new Team { Name = "Test roster", Slug = "test-roster" };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = managerId, Role = TeamRole.Manager });
        db.Players.AddRange(Enumerable.Range(1, 16).Select(i => new Player
        {
            TeamId = team.Id, FullName = $"Player {i}", IsActive = true, Position = "Tiền vệ"
        }));
        var match = new MatchGame { TeamAId = team.Id, Category = MatchCategory.Internal, Status = MatchStatus.Scheduled };
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var response = Assert.IsType<JsonResult>(await controller.PreviewAutoDivision(new MatchesController.PreviewDivisionRequestDto
        {
            MatchId = match.Id, FormationCode = "7_231",
            ParticipantPlayerIds = explicitlyEmpty ? new List<Guid>() : null
        }));
        var json = System.Text.Json.JsonSerializer.SerializeToElement(response.Value);
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal(expectedCode, json.GetProperty("data").GetProperty("DiagnosticErrorCode").GetString());
        Assert.Empty(await db.MatchPlayerLineups.ToListAsync());
    }

    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private (MatchesController controller, ApplicationDbContext db, string managerId, string memberId) SetupController(ApplicationDbContext db)
    {
        string managerId = Guid.NewGuid().ToString();
        string memberId = Guid.NewGuid().ToString();

        var user = new ApplicationUser { Id = managerId, UserName = "manager@aloball.vn" };
        var testUserManager = new TestUserManager(user);
        var authService = new TeamAuthorizationService(db);
        var matchmaking = new TestMatchmakingService();
        var fireKeeping = new FireKeepingService(db);
        var divisionEngine = new TeamDivisionEngine();

        var controller = new MatchesController(
            db,
            testUserManager,
            authService,
            matchmaking,
            fireKeeping,
            divisionEngine);

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, managerId) };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };

        return (controller, db, managerId, memberId);
    }

    [Fact]
    public async Task NonManagerOrForeignUser_DeniedAtPreviewAndConfirm()
    {
        using var db = CreateInMemoryDbContext();
        var (controller, _, managerId, memberId) = SetupController(db);

        var team = new Team { Name = "CLB Thử Nghiệm", Slug = "test-team" };
        db.Teams.Add(team);
        // User managerId là Member (không phải Manager)
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = managerId, Role = TeamRole.Member });

        var match = new MatchGame { TeamAId = team.Id, Category = MatchCategory.Internal };
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        // 1. Preview bị Forbid
        var previewRes = await controller.PreviewAutoDivision(new MatchesController.PreviewDivisionRequestDto
        {
            MatchId = match.Id,
            FormationCode = "7_231"
        });
        Assert.IsType<ForbidResult>(previewRes);

        // 2. Confirm bị Forbid
        var confirmRes = await controller.ConfirmInternalDivision(new MatchesController.ConfirmDivisionRequestDto
        {
            MatchId = match.Id,
            FormationCode = "7_231",
            ExpectedRevision = 1
        });
        Assert.IsType<ForbidResult>(confirmRes);
    }

    [Fact]
    public async Task OptimisticConcurrencyConflict_WhenTwoManagersSaveSimultaneously()
    {
        using var db = CreateInMemoryDbContext();
        var (controller, _, managerId, _) = SetupController(db);

        var team = new Team { Name = "CLB Sài Gòn", Slug = "sg-fc" };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = managerId, Role = TeamRole.Manager });

        var match = new MatchGame { TeamAId = team.Id, Category = MatchCategory.Internal, LineupRevision = 2 };
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        // Manager gửi ExpectedRevision = 1 trong khi DB đã lên Revision = 2
        var confirmRes = await controller.ConfirmInternalDivision(new MatchesController.ConfirmDivisionRequestDto
        {
            MatchId = match.Id,
            FormationCode = "7_231",
            ExpectedRevision = 1
        });

        var conflictRes = Assert.IsType<ConflictObjectResult>(confirmRes);
        Assert.Equal(409, conflictRes.StatusCode);
    }

    [Fact]
    public async Task CompletedOrCancelledMatch_RejectsLineupModification()
    {
        using var db = CreateInMemoryDbContext();
        var (controller, _, managerId, _) = SetupController(db);

        var team = new Team { Name = "CLB Hà Nội", Slug = "hn-fc" };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = managerId, Role = TeamRole.Manager });

        // Trận đã hoàn thành
        var match = new MatchGame { TeamAId = team.Id, Category = MatchCategory.Internal, Status = MatchStatus.Completed, LineupRevision = 1 };
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var confirmRes = await controller.ConfirmInternalDivision(new MatchesController.ConfirmDivisionRequestDto
        {
            MatchId = match.Id,
            FormationCode = "7_231",
            ExpectedRevision = 1
        });

        var badReq = Assert.IsType<BadRequestObjectResult>(confirmRes);
        Assert.Equal(400, badReq.StatusCode);
    }

    [Fact]
    public async Task ConfirmDivision_StoresLineupsWithCorrectPositions_AndDoesNotAwardAbOrMarkPlayed()
    {
        using var db = CreateInMemoryDbContext();
        var (controller, _, managerId, _) = SetupController(db);

        var team = new Team { Name = "CLB Chiến Thắng", Slug = "win-fc" };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = managerId, Role = TeamRole.Manager });

        var match = new MatchGame { TeamAId = team.Id, Category = MatchCategory.Internal, Status = MatchStatus.Scheduled, LineupRevision = 1 };
        db.Matches.Add(match);

        // Tạo 14 cầu thủ với vị trí tương ứng 7 slots (GK, CB_L, CB_R, MF_L, MF_C, MF_R, FW)
        var pA_GK = new Player { TeamId = team.Id, FullName = "GK A", Position = "Thủ môn" };
        var pA_DF1 = new Player { TeamId = team.Id, FullName = "DF1 A", Position = "Hậu vệ" };
        var pA_DF2 = new Player { TeamId = team.Id, FullName = "DF2 A", Position = "Hậu vệ" };
        var pA_MF1 = new Player { TeamId = team.Id, FullName = "MF1 A", Position = "Tiền vệ" };
        var pA_MF2 = new Player { TeamId = team.Id, FullName = "MF2 A", Position = "Tiền vệ" };
        var pA_MF3 = new Player { TeamId = team.Id, FullName = "MF3 A", Position = "Tiền vệ" };
        var pA_FW = new Player { TeamId = team.Id, FullName = "FW A", Position = "Tiền đạo" };

        var pB_GK = new Player { TeamId = team.Id, FullName = "GK B", Position = "Thủ môn" };
        var pB_DF1 = new Player { TeamId = team.Id, FullName = "DF1 B", Position = "Hậu vệ" };
        var pB_DF2 = new Player { TeamId = team.Id, FullName = "DF2 B", Position = "Hậu vệ" };
        var pB_MF1 = new Player { TeamId = team.Id, FullName = "MF1 B", Position = "Tiền vệ" };
        var pB_MF2 = new Player { TeamId = team.Id, FullName = "MF2 B", Position = "Tiền vệ" };
        var pB_MF3 = new Player { TeamId = team.Id, FullName = "MF3 B", Position = "Tiền vệ" };
        var pB_FW = new Player { TeamId = team.Id, FullName = "FW B", Position = "Tiền đạo" };

        var sideAPlayers = new List<Player> { pA_GK, pA_DF1, pA_DF2, pA_MF1, pA_MF2, pA_MF3, pA_FW };
        var sideBPlayers = new List<Player> { pB_GK, pB_DF1, pB_DF2, pB_MF1, pB_MF2, pB_MF3, pB_FW };

        db.Players.AddRange(sideAPlayers);
        db.Players.AddRange(sideBPlayers);
        await db.SaveChangesAsync();

        var slots7 = FormationRegistry.S7_231.Slots;

        var assignments = new List<MatchesController.ConfirmSlotAssignmentDto>();
        for (int i = 0; i < 7; i++)
        {
            assignments.Add(new MatchesController.ConfirmSlotAssignmentDto
            {
                PlayerId = sideAPlayers[i].Id,
                TeamSide = "A",
                SlotCode = slots7[i].SlotCode
            });
            assignments.Add(new MatchesController.ConfirmSlotAssignmentDto
            {
                PlayerId = sideBPlayers[i].Id,
                TeamSide = "B",
                SlotCode = slots7[i].SlotCode
            });
        }

        var res = await controller.ConfirmInternalDivision(new MatchesController.ConfirmDivisionRequestDto
        {
            MatchId = match.Id,
            FormationCode = "7_231",
            ExpectedRevision = 1,
            Assignments = assignments,
            Substitutes = new List<MatchesController.ConfirmSubstituteDto>()
        });

        var json = Assert.IsType<JsonResult>(res);

        // Kiểm tra database:
        var lineupsInDb = await db.MatchPlayerLineups.Where(l => l.MatchId == match.Id).ToListAsync();
        Assert.Equal(14, lineupsInDb.Count);

        // Toàn bộ phải có HasActuallyPlayed == false và IsStarting == true
        Assert.All(lineupsInDb, l => Assert.False(l.HasActuallyPlayed));
        Assert.All(lineupsInDb, l => Assert.True(l.IsStarting));

        // Không có bất kỳ điểm AB nào được ghi vào sổ
        var abEntries = await db.PlayerAbJournalEntries.Where(j => j.SourceId == match.Id).ToListAsync();
        Assert.Empty(abEntries);

        // Revision tăng lên 2
        var updatedMatch = await db.Matches.FindAsync(match.Id);
        Assert.Equal(2, updatedMatch!.LineupRevision);
    }

    [Fact]
    public async Task CopyLineupFromPreviousMatch_CreatesIndependentSnapshot()
    {
        using var db = CreateInMemoryDbContext();
        var (controller, _, managerId, _) = SetupController(db);

        var team = new Team { Name = "CLB Đồng Đội", Slug = "dd-fc" };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = managerId, Role = TeamRole.Manager });

        var match1 = new MatchGame { TeamAId = team.Id, Category = MatchCategory.Internal, FormationCode = "7_231" };
        var match2 = new MatchGame { TeamAId = team.Id, Category = MatchCategory.Internal };
        db.Matches.AddRange(match1, match2);

        var p = new Player { TeamId = team.Id, FullName = "Nguyễn Văn Độc Lập" };
        db.Players.Add(p);
        await db.SaveChangesAsync();

        db.MatchPlayerLineups.Add(new MatchPlayerLineup
        {
            MatchId = match1.Id,
            TeamId = team.Id,
            PlayerId = p.Id,
            TeamSide = "A",
            AssignedSlotCode = "GK",
            IsStarting = true
        });
        await db.SaveChangesAsync();

        // Nạp đội hình từ Trận 1 sang Trận 2
        var copyRes = await controller.CopyLineupFromPreviousMatch(match2.Id, match1.Id);
        Assert.IsType<JsonResult>(copyRes);

        // Kiểm tra Trận 2 đã có bản ghi mới
        var match2Lineups = await db.MatchPlayerLineups.Where(l => l.MatchId == match2.Id).ToListAsync();
        Assert.Single(match2Lineups);

        // Sửa bản ghi ở Trận 2
        match2Lineups[0].TeamSide = "B";
        await db.SaveChangesAsync();

        // Kiểm tra Trận 1 vẫn nguyên vẹn
        var match1Lineup = await db.MatchPlayerLineups.FirstAsync(l => l.MatchId == match1.Id);
        Assert.Equal("A", match1Lineup.TeamSide);
    }
}
