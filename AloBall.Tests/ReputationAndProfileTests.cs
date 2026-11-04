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

public class ReputationAndProfileTests
{
    private class TestTempDataProvider : Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider
    {
        private readonly Dictionary<string, object> _data = new();
        public IDictionary<string, object> LoadTempData(HttpContext context) => _data;
        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
            _data.Clear();
            if (values != null)
                foreach (var kvp in values) _data[kvp.Key] = kvp.Value;
        }
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

    private void SetUserContext(Controller controller, ApplicationUser? user)
    {
        var claims = new List<Claim>();
        if (user != null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, user.Id));
            claims.Add(new Claim(ClaimTypes.Name, user.UserName ?? user.Email ?? "testuser"));
        }
        var identity = user != null ? new ClaimsIdentity(claims, "TestAuthType") : new ClaimsIdentity();
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
            httpContext,
            new TestTempDataProvider());
    }

    private async Task<(Team teamA, Team teamB, ApplicationUser managerA, ApplicationUser playerUser, Player player1, Player player2, ApplicationUser outsider)>
        SeedReputationDataAsync(ApplicationDbContext db)
    {
        var managerA = new ApplicationUser { Id = "mgr_a_" + Guid.NewGuid(), UserName = "managerA@aloball.vn", Email = "managerA@aloball.vn", FullName = "Manager A" };
        var playerUser = new ApplicationUser { Id = "player_u_" + Guid.NewGuid(), UserName = "player1@aloball.vn", Email = "player1@aloball.vn", FullName = "Nguyễn Văn A", PhoneNumber = "0987654321" };
        var outsider = new ApplicationUser { Id = "outsider_" + Guid.NewGuid(), UserName = "outsider@aloball.vn", Email = "outsider@aloball.vn", FullName = "Khách ngoài" };

        db.Users.AddRange(managerA, playerUser, outsider);

        var teamA = new Team { Id = Guid.NewGuid(), Name = "Hanoi Stars FC", Slug = "hanoi-stars-fc", IsPublic = true, IsDraft = false, AverageRating = 4.8, ValidReviewCount = 5 };
        var teamB = new Team { Id = Guid.NewGuid(), Name = "Saigon Phoenix FC", Slug = "saigon-phoenix-fc", IsPublic = true, IsDraft = false, AverageRating = 4.5, ValidReviewCount = 3 };
        db.Teams.AddRange(teamA, teamB);

        db.TeamMembers.Add(new TeamMember { TeamId = teamA.Id, UserId = managerA.Id, Role = TeamRole.Manager });
        db.TeamMembers.Add(new TeamMember { TeamId = teamA.Id, UserId = playerUser.Id, Role = TeamRole.Member });

        var player1 = new Player
        {
            Id = Guid.NewGuid(),
            TeamId = teamA.Id,
            LinkedUserId = playerUser.Id,
            FullName = "Nguyễn Văn A",
            KitNumber = 10,
            Position = "Tiền đạo",
            PreferredFoot = "Phải",
            Experience = "Bán chuyên",
            HeightCm = 175,
            WeightKg = 70,
            IsActive = true,
            IsPublicProfile = true,
            PublicKitNumber = true,
            PublicPosition = true,
            PublicHeightWeight = true,
            PublicPreferredFoot = true,
            PublicExperience = true,
            PublicPhoto = true
        };

        var player2 = new Player
        {
            Id = Guid.NewGuid(),
            TeamId = teamA.Id,
            FullName = "Trần Văn B",
            KitNumber = 7,
            Position = "Tiền vệ",
            IsActive = true,
            IsPublicProfile = true
        };

        db.Players.AddRange(player1, player2);
        await db.SaveChangesAsync();

        return (teamA, teamB, managerA, playerUser, player1, player2, outsider);
    }

    [Fact]
    public async Task PublicProfile_ComputesOfficialMatchStatistics_ExcludesUnconfirmedMatches()
    {
        using var db = CreateInMemoryDbContext();
        var (teamA, teamB, managerA, playerUser, p1, p2, outsider) = await SeedReputationDataAsync(db);

        // Trận 1: Hoàn tất, đối soát 2 bên -> Team A thắng 3 - 1
        db.Matches.Add(new MatchGame
        {
            Id = Guid.NewGuid(),
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            Status = MatchStatus.Completed,
            ConfirmedByTeamA = true,
            ConfirmedByTeamB = true,
            ScoreA = 3,
            ScoreB = 1,
            MatchDateTime = DateTime.UtcNow.AddDays(-10),
            IsPublic = true
        });

        // Trận 2: Hoàn tất, đối soát 2 bên -> Team A hòa 2 - 2
        db.Matches.Add(new MatchGame
        {
            Id = Guid.NewGuid(),
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            Status = MatchStatus.Completed,
            ConfirmedByTeamA = true,
            ConfirmedByTeamB = true,
            ScoreA = 2,
            ScoreB = 2,
            MatchDateTime = DateTime.UtcNow.AddDays(-5),
            IsPublic = true
        });

        // Trận 3: Hoàn tất nhưng chưa đối soát (TeamB chưa xác nhận) -> Phải bị loại khỏi thống kê chính thức
        db.Matches.Add(new MatchGame
        {
            Id = Guid.NewGuid(),
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            Status = MatchStatus.Completed,
            ConfirmedByTeamA = true,
            ConfirmedByTeamB = false,
            ScoreA = 5,
            ScoreB = 0,
            MatchDateTime = DateTime.UtcNow.AddDays(-2),
            IsPublic = true
        });

        // Trận 4: Chưa đấu (Scheduled) -> Loại khỏi thống kê
        db.Matches.Add(new MatchGame
        {
            Id = Guid.NewGuid(),
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            Status = MatchStatus.Scheduled,
            ConfirmedByTeamA = false,
            ConfirmedByTeamB = false,
            MatchDateTime = DateTime.UtcNow.AddDays(3),
            IsPublic = true
        });

        await db.SaveChangesAsync();

        var userManager = CreateMockUserManager(db);
        var auth = new TeamAuthorizationService(db);
        var controller = new TeamsController(db, userManager, auth);
        SetUserContext(controller, null);

        var result = await controller.PublicProfile(teamA.Slug);
        var viewResult = Assert.IsType<ViewResult>(result);

        var stats = Assert.IsType<TeamReputationStats>(controller.ViewBag.ReputationStats);
        Assert.Equal(2, stats.TotalOfficialMatches);
        Assert.Equal(1, stats.Wins);
        Assert.Equal(1, stats.Draws);
        Assert.Equal(0, stats.Losses);
        Assert.Equal(50.0, stats.WinPercentage);
        Assert.Equal(5, stats.GoalsScored); // 3 + 2
        Assert.Equal(3, stats.GoalsConceded); // 1 + 2
        Assert.Equal(2, stats.GoalDifference); // 5 - 3
    }

    [Fact]
    public async Task PublicProfile_ComputesInvitationResponseAndAcceptanceRates_Past12Months()
    {
        using var db = CreateInMemoryDbContext();
        var (teamA, teamB, managerA, playerUser, p1, p2, outsider) = await SeedReputationDataAsync(db);

        // 1. Thách đấu trực tiếp tới teamA -> Đã chấp nhận (Accepted)
        var post1 = new MatchmakingPost
        {
            Id = Guid.NewGuid(),
            PostType = MatchmakingPostType.DirectChallenge,
            TeamId = teamB.Id,
            TargetTeamId = teamA.Id,
            MatchDate = DateTime.UtcNow.AddDays(-30),
            StartTime = TimeSpan.FromHours(18),
            EndTime = TimeSpan.FromHours(20),
            Status = MatchmakingStatus.Locked,
            CreatedAt = DateTime.UtcNow.AddDays(-35)
        };
        db.MatchmakingPosts.Add(post1);
        db.MatchmakingProposals.Add(new MatchmakingProposal
        {
            PostId = post1.Id,
            ChallengingTeamId = teamB.Id,
            Status = ProposalStatus.Accepted,
            CreatedAt = DateTime.UtcNow.AddDays(-35),
            RespondedAt = DateTime.UtcNow.AddDays(-34)
        });

        // 2. Thách đấu trực tiếp tới teamA -> Đã từ chối (Rejected)
        var post2 = new MatchmakingPost
        {
            Id = Guid.NewGuid(),
            PostType = MatchmakingPostType.DirectChallenge,
            TeamId = teamB.Id,
            TargetTeamId = teamA.Id,
            MatchDate = DateTime.UtcNow.AddDays(-20),
            StartTime = TimeSpan.FromHours(19),
            EndTime = TimeSpan.FromHours(21),
            Status = MatchmakingStatus.Open,
            CreatedAt = DateTime.UtcNow.AddDays(-25)
        };
        db.MatchmakingPosts.Add(post2);
        db.MatchmakingProposals.Add(new MatchmakingProposal
        {
            PostId = post2.Id,
            ChallengingTeamId = teamB.Id,
            Status = ProposalStatus.Rejected,
            CreatedAt = DateTime.UtcNow.AddDays(-25),
            RespondedAt = DateTime.UtcNow.AddDays(-24)
        });

        // 3. Đề nghị bị rút/hủy trước khi phản hồi (Cancelled) -> Bỏ qua không tính vào chỉ số
        var post3 = new MatchmakingPost
        {
            Id = Guid.NewGuid(),
            PostType = MatchmakingPostType.DirectChallenge,
            TeamId = teamB.Id,
            TargetTeamId = teamA.Id,
            MatchDate = DateTime.UtcNow.AddDays(-15),
            StartTime = TimeSpan.FromHours(18),
            EndTime = TimeSpan.FromHours(20),
            Status = MatchmakingStatus.Cancelled,
            CreatedAt = DateTime.UtcNow.AddDays(-18)
        };
        db.MatchmakingPosts.Add(post3);
        db.MatchmakingProposals.Add(new MatchmakingProposal
        {
            PostId = post3.Id,
            ChallengingTeamId = teamB.Id,
            Status = ProposalStatus.Cancelled,
            CreatedAt = DateTime.UtcNow.AddDays(-18)
        });

        // 4. Lời mời đang mở nhưng ngày thi đấu đã qua -> Hết hạn không phản hồi (Expired)
        var post4 = new MatchmakingPost
        {
            Id = Guid.NewGuid(),
            PostType = MatchmakingPostType.DirectChallenge,
            TeamId = teamB.Id,
            TargetTeamId = teamA.Id,
            MatchDate = DateTime.UtcNow.AddDays(-5),
            StartTime = TimeSpan.FromHours(18),
            EndTime = TimeSpan.FromHours(20),
            Status = MatchmakingStatus.Open,
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        };
        db.MatchmakingPosts.Add(post4);
        db.MatchmakingProposals.Add(new MatchmakingProposal
        {
            PostId = post4.Id,
            ChallengingTeamId = teamB.Id,
            Status = ProposalStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        });

        // 5. Lời mời đang mở và ngày thi đấu ở tương lai (Active pending) -> Chưa đến hạn, bỏ qua
        var post5 = new MatchmakingPost
        {
            Id = Guid.NewGuid(),
            PostType = MatchmakingPostType.DirectChallenge,
            TeamId = teamB.Id,
            TargetTeamId = teamA.Id,
            MatchDate = DateTime.UtcNow.AddDays(5),
            StartTime = TimeSpan.FromHours(18),
            EndTime = TimeSpan.FromHours(20),
            Status = MatchmakingStatus.Open,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        db.MatchmakingPosts.Add(post5);
        db.MatchmakingProposals.Add(new MatchmakingProposal
        {
            PostId = post5.Id,
            ChallengingTeamId = teamB.Id,
            Status = ProposalStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        });

        await db.SaveChangesAsync();

        var userManager = CreateMockUserManager(db);
        var auth = new TeamAuthorizationService(db);
        var controller = new TeamsController(db, userManager, auth);
        SetUserContext(controller, null);

        var result = await controller.PublicProfile(teamA.Slug);
        var viewResult = Assert.IsType<ViewResult>(result);

        var stats = Assert.IsType<TeamReputationStats>(controller.ViewBag.ReputationStats);
        // Eligible: 1 accepted + 1 rejected + 1 expired = 3
        Assert.Equal(3, stats.TotalEligibleInvitations);
        // Responded: 1 accepted + 1 rejected = 2
        Assert.Equal(2, stats.RespondedInvitations);
        // Accepted: 1
        Assert.Equal(1, stats.AcceptedInvitations);
        // Response Rate: 2 / 3 = 66.7%
        Assert.Equal(66.7, stats.ResponseRatePercentage);
        // Acceptance Rate: 1 / 2 = 50.0%
        Assert.Equal(50.0, stats.AcceptanceRatePercentage);
    }

    [Fact]
    public async Task PublicProfile_DisplaysVerifiedReviews_ExcludesInvalidatedReviews()
    {
        using var db = CreateInMemoryDbContext();
        var (teamA, teamB, managerA, playerUser, p1, p2, outsider) = await SeedReputationDataAsync(db);

        var match = new MatchGame
        {
            Id = Guid.NewGuid(),
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            Status = MatchStatus.Completed,
            ConfirmedByTeamA = true,
            ConfirmedByTeamB = true,
            ScoreA = 2,
            ScoreB = 1,
            MatchDateTime = DateTime.UtcNow.AddDays(-7),
            IsPublic = true
        };
        db.Matches.Add(match);

        // Đánh giá 1: Hợp lệ (5 sao)
        db.TeamMatchReviews.Add(new TeamMatchReview
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            ReviewerTeamId = teamB.Id,
            TargetTeamId = teamA.Id,
            RatingStars = 5,
            Comment = "Đội bạn đá rất đẹp, đúng hẹn và lịch sự.",
            IsValid = true,
            CreatedAt = DateTime.UtcNow.AddDays(-6)
        });

        // Đánh giá 2: Đã bị Admin vô hiệu hóa (IsValid = false) do tranh chấp
        db.TeamMatchReviews.Add(new TeamMatchReview
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            ReviewerTeamId = teamB.Id,
            TargetTeamId = teamA.Id,
            RatingStars = 1,
            Comment = "Spam review tiêu cực",
            IsValid = false,
            InvalidationReason = "Admin xử lý khiếu nại thành công",
            CreatedAt = DateTime.UtcNow.AddDays(-5)
        });

        await db.SaveChangesAsync();

        var userManager = CreateMockUserManager(db);
        var auth = new TeamAuthorizationService(db);
        var controller = new TeamsController(db, userManager, auth);
        SetUserContext(controller, null);

        var result = await controller.PublicProfile(teamA.Slug);
        var viewResult = Assert.IsType<ViewResult>(result);

        var reviews = (List<TeamMatchReview>)controller.ViewBag.VerifiedReviews;
        Assert.Single(reviews);
        Assert.Equal(5, reviews[0].RatingStars);
    }

    [Fact]
    public async Task PlayerPublicProfile_ComputesOfficialAppearances_GoalsAndAssists_NoDoubleCounting()
    {
        using var db = CreateInMemoryDbContext();
        var (teamA, teamB, managerA, playerUser, p1, p2, outsider) = await SeedReputationDataAsync(db);

        // Trận 1 (Chính thức, đối soát cả 2 đội)
        var match1 = new MatchGame
        {
            Id = Guid.NewGuid(),
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            Status = MatchStatus.Completed,
            ConfirmedByTeamA = true,
            ConfirmedByTeamB = true,
            ScoreA = 3,
            ScoreB = 2,
            MatchDateTime = DateTime.UtcNow.AddDays(-10),
            IsPublic = true
        };
        db.Matches.Add(match1);

        // Lineup trận 1: P1 đá chính, P2 vào sân
        db.MatchPlayerLineups.Add(new MatchPlayerLineup
        {
            MatchId = match1.Id,
            TeamId = teamA.Id,
            PlayerId = p1.Id,
            IsStarting = true,
            HasActuallyPlayed = true
        });
        db.MatchPlayerLineups.Add(new MatchPlayerLineup
        {
            MatchId = match1.Id,
            TeamId = teamA.Id,
            PlayerId = p2.Id,
            IsStarting = false,
            HasActuallyPlayed = true
        });

        // Sự kiện bàn thắng trận 1: P1 ghi 2 bàn, P2 kiến tạo 1 bàn
        db.MatchGoalEvents.Add(new MatchGoalEvent
        {
            MatchId = match1.Id,
            TeamId = teamA.Id,
            ScorerPlayerId = p1.Id,
            AssistPlayerId = p2.Id,
            Minute = 15
        });
        db.MatchGoalEvents.Add(new MatchGoalEvent
        {
            MatchId = match1.Id,
            TeamId = teamA.Id,
            ScorerPlayerId = p1.Id,
            Minute = 60
        });

        // Trận 2 (Chính thức, đối soát cả 2 đội)
        var match2 = new MatchGame
        {
            Id = Guid.NewGuid(),
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            Status = MatchStatus.Completed,
            ConfirmedByTeamA = true,
            ConfirmedByTeamB = true,
            ScoreA = 1,
            ScoreB = 1,
            MatchDateTime = DateTime.UtcNow.AddDays(-5),
            IsPublic = true
        };
        db.Matches.Add(match2);

        // Lineup trận 2: P1 vào sân
        db.MatchPlayerLineups.Add(new MatchPlayerLineup
        {
            MatchId = match2.Id,
            TeamId = teamA.Id,
            PlayerId = p1.Id,
            IsStarting = false,
            HasActuallyPlayed = true
        });

        // Buổi điểm danh của trận 2 cũng ghi nhận P1 có mặt (tránh đếm trùng thành 2 lần ra sân)
        var session2 = new AttendanceSession
        {
            Id = Guid.NewGuid(),
            TeamId = teamA.Id,
            MatchId = match2.Id,
            SessionDateTime = DateTime.UtcNow.AddDays(-5)
        };
        db.AttendanceSessions.Add(session2);
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            SessionId = session2.Id,
            PlayerId = p1.Id,
            Actual = ActualStatus.Present
        });

        // Trận 3: Chưa đối soát (chưa xác nhận) -> bàn thắng và ra sân không được tính vào chỉ số chính thức
        var match3 = new MatchGame
        {
            Id = Guid.NewGuid(),
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            Status = MatchStatus.Completed,
            ConfirmedByTeamA = true,
            ConfirmedByTeamB = false,
            ScoreA = 2,
            ScoreB = 0,
            MatchDateTime = DateTime.UtcNow.AddDays(-1),
            IsPublic = true
        };
        db.Matches.Add(match3);
        db.MatchGoalEvents.Add(new MatchGoalEvent
        {
            MatchId = match3.Id,
            TeamId = teamA.Id,
            ScorerPlayerId = p1.Id,
            Minute = 30
        });

        await db.SaveChangesAsync();

        var userManager = CreateMockUserManager(db);
        var auth = new TeamAuthorizationService(db);
        var controller = new TeamsController(db, userManager, auth);
        SetUserContext(controller, null);

        var resultP1 = await controller.PlayerPublicProfile(p1.Id);
        var viewResultP1 = Assert.IsType<ViewResult>(resultP1);
        var modelP1 = Assert.IsType<PlayerPublicProfileViewModel>(viewResultP1.Model);

        Assert.Equal(2, modelP1.OfficialAppearances); // 2 trận (không đếm trùng trận 2)
        Assert.Equal(2, modelP1.OfficialGoals); // 2 bàn ở trận 1 (bàn ở trận 3 bị loại)
        Assert.Equal(0, modelP1.OfficialAssists);

        var resultP2 = await controller.PlayerPublicProfile(p2.Id);
        var viewResultP2 = Assert.IsType<ViewResult>(resultP2);
        var modelP2 = Assert.IsType<PlayerPublicProfileViewModel>(viewResultP2.Model);

        Assert.Equal(1, modelP2.OfficialAppearances);
        Assert.Equal(0, modelP2.OfficialGoals);
        Assert.Equal(1, modelP2.OfficialAssists);
    }

    [Fact]
    public async Task PlayerPublicProfile_AttendanceRate_CalculatesCorrectly()
    {
        using var db = CreateInMemoryDbContext();
        var (teamA, teamB, managerA, playerUser, p1, p2, outsider) = await SeedReputationDataAsync(db);

        var session1 = new AttendanceSession { Id = Guid.NewGuid(), TeamId = teamA.Id, SessionDateTime = DateTime.UtcNow.AddDays(-20) };
        var session2 = new AttendanceSession { Id = Guid.NewGuid(), TeamId = teamA.Id, SessionDateTime = DateTime.UtcNow.AddDays(-15) };
        var session3 = new AttendanceSession { Id = Guid.NewGuid(), TeamId = teamA.Id, SessionDateTime = DateTime.UtcNow.AddDays(-10) };
        var session4 = new AttendanceSession { Id = Guid.NewGuid(), TeamId = teamA.Id, SessionDateTime = DateTime.UtcNow.AddDays(-5) };
        db.AttendanceSessions.AddRange(session1, session2, session3, session4);

        // Session 1: Có mặt
        db.AttendanceRecords.Add(new AttendanceRecord { SessionId = session1.Id, PlayerId = p1.Id, Actual = ActualStatus.Present });
        // Session 2: Miễn trừ hợp lệ theo quy chế (tính là tham gia)
        db.AttendanceRecords.Add(new AttendanceRecord { SessionId = session2.Id, PlayerId = p1.Id, Actual = ActualStatus.Absent, IsExempt = true });
        // Session 3: Nghỉ không phép
        db.AttendanceRecords.Add(new AttendanceRecord { SessionId = session3.Id, PlayerId = p1.Id, Actual = ActualStatus.Absent });
        // Session 4: Chưa điểm danh -> Loại khỏi tính toán
        db.AttendanceRecords.Add(new AttendanceRecord { SessionId = session4.Id, PlayerId = p1.Id, Actual = ActualStatus.NotChecked });

        await db.SaveChangesAsync();

        var userManager = CreateMockUserManager(db);
        var auth = new TeamAuthorizationService(db);
        var controller = new TeamsController(db, userManager, auth);
        SetUserContext(controller, null);

        var result = await controller.PlayerPublicProfile(p1.Id);
        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<PlayerPublicProfileViewModel>(viewResult.Model);

        Assert.True(model.HasAttendanceData);
        Assert.Equal(3, model.TotalAttendanceSessions);
        Assert.Equal(2, model.AttendedSessions);
        Assert.Equal(66.7, model.AttendanceRatePercentage);
    }

    [Fact]
    public async Task PlayerPublicProfile_PrivacySettings_ProtectsPrivateFieldsAndHandlesNonPublic()
    {
        using var db = CreateInMemoryDbContext();
        var (teamA, teamB, managerA, playerUser, p1, p2, outsider) = await SeedReputationDataAsync(db);

        // Đặt một số trường thành riêng tư
        p1.PublicKitNumber = false;
        p1.PublicPosition = false;
        p1.PublicHeightWeight = false;
        p1.PublicPreferredFoot = false;
        p1.PublicExperience = false;
        p1.PublicPhoto = false;
        await db.SaveChangesAsync();

        var userManager = CreateMockUserManager(db);
        var auth = new TeamAuthorizationService(db);
        var controller = new TeamsController(db, userManager, auth);
        SetUserContext(controller, outsider);

        var result = await controller.PlayerPublicProfile(p1.Id);
        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<PlayerPublicProfileViewModel>(viewResult.Model);

        Assert.False(model.ShowKitNumber);
        Assert.False(model.ShowPosition);
        Assert.False(model.ShowHeightWeight);
        Assert.False(model.ShowPreferredFoot);
        Assert.False(model.ShowExperience);
        Assert.False(model.ShowPhoto);
        Assert.False(model.IsSelf);
        Assert.False(model.IsManager);

        // Đặt toàn bộ hồ sơ thành riêng tư (IsPublicProfile = false)
        p1.IsPublicProfile = false;
        await db.SaveChangesAsync();

        // 1. Khách ngoài truy cập -> 404 NotFound
        var outsiderResult = await controller.PlayerPublicProfile(p1.Id);
        Assert.IsType<NotFoundObjectResult>(outsiderResult);

        // 2. Chính chủ truy cập -> Xem được hồ sơ của chính mình
        SetUserContext(controller, playerUser);
        var selfResult = await controller.PlayerPublicProfile(p1.Id);
        var selfView = Assert.IsType<ViewResult>(selfResult);
        var selfModel = Assert.IsType<PlayerPublicProfileViewModel>(selfView.Model);
        Assert.True(selfModel.IsSelf);

        // 3. Quản lý đội bóng truy cập -> Xem được
        SetUserContext(controller, managerA);
        var mgrResult = await controller.PlayerPublicProfile(p1.Id);
        var mgrView = Assert.IsType<ViewResult>(mgrResult);
        var mgrModel = Assert.IsType<PlayerPublicProfileViewModel>(mgrView.Model);
        Assert.True(mgrModel.IsManager);
    }

    [Fact]
    public async Task UpdatePlayerPrivacy_EnforcesIDORProtection()
    {
        using var db = CreateInMemoryDbContext();
        var (teamA, teamB, managerA, playerUser, p1, p2, outsider) = await SeedReputationDataAsync(db);

        var userManager = CreateMockUserManager(db);
        var auth = new TeamAuthorizationService(db);
        var controller = new TeamsController(db, userManager, auth);

        var updateInput = new PlayerPrivacyUpdateInput
        {
            PlayerId = p1.Id,
            IsPublicProfile = false,
            PublicKitNumber = false,
            PublicPosition = true,
            PublicHeightWeight = false,
            PublicPhoto = false,
            PublicPreferredFoot = true,
            PublicExperience = false
        };

        // 1. Người ngoài (outsider) cố gắng thay đổi quyền riêng tư của P1 -> 403 Forbid
        SetUserContext(controller, outsider);
        var forbidResult = await controller.UpdatePlayerPrivacy(updateInput);
        Assert.IsType<ForbidResult>(forbidResult);

        // Kiểm tra trong DB không bị thay đổi
        var playerCheck = await db.Players.FindAsync(p1.Id);
        Assert.True(playerCheck!.IsPublicProfile);

        // 2. Chính chủ (playerUser) cập nhật -> Thành công
        SetUserContext(controller, playerUser);
        var selfUpdateResult = await controller.UpdatePlayerPrivacy(updateInput);
        var redirectSelf = Assert.IsType<RedirectToActionResult>(selfUpdateResult);
        Assert.Equal(nameof(TeamsController.PlayerPublicProfile), redirectSelf.ActionName);

        playerCheck = await db.Players.FindAsync(p1.Id);
        Assert.False(playerCheck!.IsPublicProfile);
        Assert.False(playerCheck.PublicKitNumber);
        Assert.True(playerCheck.PublicPreferredFoot);

        // 3. Lãnh đội (managerA) cập nhật lại -> Thành công
        updateInput.IsPublicProfile = true;
        updateInput.PublicKitNumber = true;
        SetUserContext(controller, managerA);
        var mgrUpdateResult = await controller.UpdatePlayerPrivacy(updateInput);
        var redirectMgr = Assert.IsType<RedirectToActionResult>(mgrUpdateResult);
        Assert.Equal(nameof(TeamsController.PlayerPublicProfile), redirectMgr.ActionName);

        playerCheck = await db.Players.FindAsync(p1.Id);
        Assert.True(playerCheck!.IsPublicProfile);
        Assert.True(playerCheck.PublicKitNumber);
    }
}
