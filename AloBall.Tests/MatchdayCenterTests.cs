using System.Security.Claims;
using AloBall.Web.Controllers;
using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using AloBall.Web.Services.TeamDivision;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AloBall.Tests;

public class MatchdayCenterTests
{
    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private (MatchesController controller, ApplicationDbContext db, ApplicationUser managerUser, ApplicationUser playerUser1, ApplicationUser playerUser2, ApplicationUser outsiderUser, Team teamA, Team teamB, MatchGame match, AttendanceSession session, AttendanceRecord rec1, AttendanceRecord rec2)
        SetupTestEnvironment(ApplicationDbContext db, string? asUserId = null)
    {
        var existingManager = db.Users.Find("manager_1");
        if (existingManager == null)
        {
            var managerUser = new ApplicationUser { Id = "manager_1", UserName = "manager@aloball.vn", FullName = "Nguyễn Văn Đội Trưởng" };
            var playerUser1 = new ApplicationUser { Id = "player_1", UserName = "player1@aloball.vn", FullName = "Trần Cầu Thủ 1" };
            var playerUser2 = new ApplicationUser { Id = "player_2", UserName = "player2@aloball.vn", FullName = "Lê Cầu Thủ 2" };
            var outsiderUser = new ApplicationUser { Id = "outsider_1", UserName = "outsider@aloball.vn", FullName = "Người Ngoài" };

            db.Users.AddRange(managerUser, playerUser1, playerUser2, outsiderUser);

            var teamA = new Team
            {
                Id = Guid.NewGuid(),
                Name = "KLB Football Club",
                Slug = "klb-fc",
                IsPublic = true,
                CreatedAt = DateTime.UtcNow
            };
            var teamB = new Team
            {
                Id = Guid.NewGuid(),
                Name = "Sao Vàng FC",
                Slug = "sao-vang-fc",
                IsPublic = true,
                CreatedAt = DateTime.UtcNow
            };

            db.Teams.AddRange(teamA, teamB);

            db.TeamMembers.AddRange(
                new TeamMember { TeamId = teamA.Id, UserId = managerUser.Id, Role = TeamRole.Manager },
                new TeamMember { TeamId = teamA.Id, UserId = playerUser1.Id, Role = TeamRole.Member },
                new TeamMember { TeamId = teamA.Id, UserId = playerUser2.Id, Role = TeamRole.Member }
            );

            var player1 = new Player
            {
                Id = Guid.NewGuid(),
                TeamId = teamA.Id,
                LinkedUserId = playerUser1.Id,
                FullName = "Trần Cầu Thủ 1",
                KitNumber = 7,
                IsActive = true
            };

            var player2 = new Player
            {
                Id = Guid.NewGuid(),
                TeamId = teamA.Id,
                LinkedUserId = playerUser2.Id,
                FullName = "Lê Cầu Thủ 2",
                KitNumber = 10,
                IsActive = true
            };

            db.Players.AddRange(player1, player2);

            var matchDate = DateTime.Today.AddDays(2).AddHours(19);
            var match = new MatchGame
            {
                Id = Guid.NewGuid(),
                TeamAId = teamA.Id,
                TeamBId = teamB.Id,
                MatchDateTime = matchDate,
                PitchName = "Sân bóng PVV",
                PitchType = "7 người",
                Category = MatchCategory.Friendly,
                Status = MatchStatus.Scheduled,
                IsPublic = true
            };
            db.Matches.Add(match);

            var session = new AttendanceSession
            {
                Id = Guid.NewGuid(),
                TeamId = teamA.Id,
                MatchId = match.Id,
                Title = "Buổi thi đấu sân PVV",
                SessionDateTime = matchDate,
                ResponseDeadline = matchDate.AddHours(-6),
                StatisticalPeriod = matchDate.ToString("yyyy-MM"),
                PitchName = "Sân bóng PVV",
                PitchType = "7 người",
                Status = SessionStatus.Draft
            };
            db.AttendanceSessions.Add(session);

            var rec1 = new AttendanceRecord
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                PlayerId = player1.Id,
                Response = ResponseStatus.NotRecorded,
                Actual = ActualStatus.NotChecked
            };
            var rec2 = new AttendanceRecord
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                PlayerId = player2.Id,
                Response = ResponseStatus.NotRecorded,
                Actual = ActualStatus.NotChecked
            };
            db.AttendanceRecords.AddRange(rec1, rec2);

            db.SaveChanges();
        }

        var mUser = db.Users.Find("manager_1")!;
        var pUser1 = db.Users.Find("player_1")!;
        var pUser2 = db.Users.Find("player_2")!;
        var oUser = db.Users.Find("outsider_1")!;
        var tA = db.Teams.First(t => t.Slug == "klb-fc");
        var tB = db.Teams.First(t => t.Slug == "sao-vang-fc");
        var m = db.Matches.First(x => x.TeamAId == tA.Id);
        var s = db.AttendanceSessions.First(x => x.TeamId == tA.Id);
        var r1 = db.AttendanceRecords.Include(r => r.Player).First(r => r.SessionId == s.Id && r.Player.LinkedUserId == pUser1.Id);
        var r2 = db.AttendanceRecords.Include(r => r.Player).First(r => r.SessionId == s.Id && r.Player.LinkedUserId == pUser2.Id);

        string currentUserId = asUserId ?? mUser.Id;
        var currentUser = db.Users.Find(currentUserId)!;
        var testUserManager = new TestUserManager(currentUser);
        var authService = new TeamAuthorizationService(db);
        var matchmaking = new TestMatchmakingService();
        var fireKeeping = new FireKeepingService(db);
        var divisionEngine = new TeamDivisionEngine();
        var teamMessaging = new TeamMessagingService(db, authService);
        var calcEngine = new AttendanceCalculationEngine(db);

        var controller = new MatchesController(
            db,
            testUserManager,
            authService,
            matchmaking,
            fireKeeping,
            divisionEngine,
            teamMessaging,
            calcEngine);

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, currentUserId) };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());

        return (controller, db, mUser, pUser1, pUser2, oUser, tA, tB, m, s, r1, r2);
    }

    private class TestTempDataProvider : ITempDataProvider
    {
        private IDictionary<string, object> _data = new Dictionary<string, object>();
        public IDictionary<string, object> LoadTempData(HttpContext context) => _data;
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) => _data = values ?? new Dictionary<string, object>();
    }

    [Fact]
    public async Task TeamAccessAndIdorProtection_OutsiderDeniedForPrivateMatch()
    {
        using var db = CreateInMemoryDbContext();
        var (controller, _, _, _, _, outsider, teamA, _, match, _, _, _) = SetupTestEnvironment(db);

        // Đặt trận đấu và đội là private
        match.IsPublic = false;
        teamA.IsPublic = false;
        await db.SaveChangesAsync();

        // Switch user context to outsider
        var testUserManager = new TestUserManager(outsider);
        var authService = new TeamAuthorizationService(db);
        var divisionEngine = new TeamDivisionEngine();
        var fireKeeping = new FireKeepingService(db);
        var matchmaking = new TestMatchmakingService();

        var outsiderController = new MatchesController(
            db,
            testUserManager,
            authService,
            matchmaking,
            fireKeeping,
            divisionEngine);

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, outsider.Id) };
        outsiderController.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) }
        };

        var result = await outsiderController.Detail(match.Id);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task RsvpPrivacy_PlayerCanUpdateOwnRecord_OtherPlayerDenied()
    {
        using var db = CreateInMemoryDbContext();
        // Setup with Player 1
        var (controllerP1, _, _, p1, p2, _, _, _, match, _, rec1, rec2) = SetupTestEnvironment(db, "player_1");

        // Player 1 cập nhật record của chính mình -> OK
        var res1 = await controllerP1.SubmitMatchRsvp(match.Id, rec1.Id, (int)ResponseStatus.Attending);
        var redir1 = Assert.IsType<RedirectToActionResult>(res1);
        Assert.Equal("Detail", redir1.ActionName);

        var updatedRec1 = await db.AttendanceRecords.FindAsync(rec1.Id);
        Assert.Equal(ResponseStatus.Attending, updatedRec1!.Response);

        // Player 1 cố ý cập nhật record của Player 2 -> FORBID
        var resForbidden = await controllerP1.SubmitMatchRsvp(match.Id, rec2.Id, (int)ResponseStatus.Excused);
        Assert.IsType<ForbidResult>(resForbidden);
    }

    [Fact]
    public async Task RsvpAuthorization_ManagerCanUpdateAnyPlayerRecord()
    {
        using var db = CreateInMemoryDbContext();
        var (managerController, _, _, _, _, _, _, _, match, _, _, rec2) = SetupTestEnvironment(db, "manager_1");

        // Manager cập nhật record của Player 2 -> OK
        var res = await managerController.SubmitMatchRsvp(match.Id, rec2.Id, (int)ResponseStatus.Excused);
        var redir = Assert.IsType<RedirectToActionResult>(res);
        Assert.Equal("Detail", redir.ActionName);

        var updatedRec2 = await db.AttendanceRecords.FindAsync(rec2.Id);
        Assert.Equal(ResponseStatus.Excused, updatedRec2!.Response);
    }

    [Fact]
    public async Task MatchReminders_ManagerCanSend_RegularMemberDenied()
    {
        using var db = CreateInMemoryDbContext();
        var (managerController, _, _, p1, _, _, teamA, _, match, _, _, _) = SetupTestEnvironment(db, "manager_1");

        // 1. Regular player cố gửi nhắc nhở -> FORBID
        var (playerController, _, _, _, _, _, _, _, _, _, _, _) = SetupTestEnvironment(db, "player_1");
        var resForbid = await playerController.SendMatchReminder(match.Id, teamA.Id, "Nhắc nhở từ mem");
        Assert.IsType<ForbidResult>(resForbid);

        // 2. Manager gửi nhắc nhở -> OK
        var resSuccess = await managerController.SendMatchReminder(match.Id, teamA.Id, "Nhớ mang đủ áo");
        var redir = Assert.IsType<RedirectToActionResult>(resSuccess);
        Assert.Equal("Detail", redir.ActionName);

        // Kiểm tra thông báo đã được lưu trong TeamMessages
        var message = await db.TeamMessages.FirstOrDefaultAsync(m => m.TeamId == teamA.Id && m.Content.Contains($"[MATCH_REMINDER:{match.Id}]"));
        Assert.NotNull(message);
        Assert.Contains("THÔNG BÁO NHẮC ĐIỂM DANH", message.Content);

        // 3. Rate-limit check: Gửi lại ngay lập tức bị chặn để tránh spam
        var resSpam = await managerController.SendMatchReminder(match.Id, teamA.Id, "Spam lần 2");
        Assert.IsType<RedirectToActionResult>(resSpam);
        Assert.Equal("Lời nhắc đã được gửi gần đây. Vui lòng đợi ít nhất 5 phút giữa các lần gửi để tránh làm phiền thành viên.", managerController.TempData["Error"]);
    }

    [Fact]
    public async Task DiscrepancyHandling_DetectsDifferenceAndSyncsExplicitly()
    {
        using var db = CreateInMemoryDbContext();
        var (managerController, _, _, _, _, _, teamA, _, match, session, _, _) = SetupTestEnvironment(db, "manager_1");

        // Tạo PitchVenue và PitchBookingRequest đã confirmed với giờ/sân khác
        var venue = new PitchVenue
        {
            Id = Guid.NewGuid(),
            Name = "Sân bóng Đại học Thủy Lợi",
            City = "Hà Nội",
            District = "Đống Đa",
            FullAddress = "Số 175 Tây Sơn, Đống Đa, Hà Nội",
            ContactPhone = "0988.111.222",
            SupportedFormats = "7 người",
            SurfaceType = "Cỏ nhân tạo",
            IndicativePricePerHour = 600000,
            IsActive = true
        };
        db.PitchVenues.Add(venue);

        var bookingRequest = new PitchBookingRequest
        {
            Id = Guid.NewGuid(),
            TeamId = teamA.Id,
            PitchVenueId = venue.Id,
            MatchId = match.Id,
            ServiceDate = match.MatchDateTime.Date,
            StartTime = new TimeSpan(20, 0, 0),
            EndTime = new TimeSpan(21, 30, 0),
            PitchFormat = "7 người",
            Status = PitchBookingStatus.Confirmed
        };
        db.PitchBookingRequests.Add(bookingRequest);

        var acceptedOffer = new PitchBookingOffer
        {
            Id = Guid.NewGuid(),
            RequestId = bookingRequest.Id,
            AdminId = "admin_1",
            ConfirmedVenueName = "Sân bóng Thủy Lợi - Sân 2",
            ConfirmedAddress = "175 Tây Sơn, Hà Nội",
            ConfirmedDate = match.MatchDateTime.Date,
            ConfirmedStartTime = new TimeSpan(20, 0, 0), // Lệch 1 tiếng so với 19:00
            ConfirmedEndTime = new TimeSpan(21, 30, 0),
            TotalPriceVND = 800000,
            Status = PitchBookingOfferStatus.Accepted
        };
        db.PitchBookingOffers.Add(acceptedOffer);
        await db.SaveChangesAsync();

        // Kiểm tra qua Detail: phát hiện lệch
        var detailResult = await managerController.Detail(match.Id);
        var viewResult = Assert.IsType<ViewResult>(detailResult);
        Assert.True((bool)managerController.ViewBag.HasPitchDiscrepancy);

        // Manager gọi đồng bộ explicitly
        var syncResult = await managerController.SyncMatchWithConfirmedPitch(match.Id);
        var redir = Assert.IsType<RedirectToActionResult>(syncResult);
        Assert.Equal("Detail", redir.ActionName);

        // Kiểm tra match và session đã được đồng bộ chuẩn
        var updatedMatch = await db.Matches.FindAsync(match.Id);
        Assert.Equal("Sân bóng Thủy Lợi - Sân 2", updatedMatch!.PitchName);
        Assert.Equal(new TimeSpan(20, 0, 0), updatedMatch.MatchDateTime.TimeOfDay);

        var updatedSession = await db.AttendanceSessions.FindAsync(session.Id);
        Assert.Equal("Sân bóng Thủy Lợi - Sân 2", updatedSession!.PitchName);
        Assert.Equal(new TimeSpan(20, 0, 0), updatedSession.SessionDateTime.TimeOfDay);
    }

    [Fact]
    public async Task VenueAndRefereeStatus_NoFalseConfirmationWhenPending()
    {
        using var db = CreateInMemoryDbContext();
        var (managerController, _, _, _, _, _, teamA, _, match, _, _, _) = SetupTestEnvironment(db, "manager_1");

        // 1. Tạo RefereeRequest đang ở trạng thái Pending (chưa confirm)
        var refReq = new RefereeRequest
        {
            Id = Guid.NewGuid(),
            TeamId = teamA.Id,
            MatchId = match.Id,
            ServiceDate = match.MatchDateTime.Date,
            StartTime = new TimeSpan(19, 0, 0),
            EndTime = new TimeSpan(21, 0, 0),
            VenueName = "Sân PVV",
            VenueAddress = "72 Trần Thái Tông",
            RefereeCount = 1,
            Status = RefereeRequestStatus.Pending
        };
        db.RefereeRequests.Add(refReq);
        await db.SaveChangesAsync();

        // 2. Load Detail
        await managerController.Detail(match.Id);

        // Phải đảm bảo confirmed referee request là null (không báo ảo là đã chốt)
        Assert.Null(managerController.ViewBag.ConfirmedRefereeRequest);
        Assert.Null(managerController.ViewBag.AcceptedRefereeProposal);
    }

    [Fact]
    public async Task OfficialStatistics_OnlyConfirmedMatchesCount()
    {
        using var db = CreateInMemoryDbContext();
        var (managerController, _, _, _, _, _, teamA, teamB, match, _, _, _) = SetupTestEnvironment(db, "manager_1");

        // Tạo 1 trận quá khứ đã Completed NHƯNG CHƯA ĐỐI SOÁT (chỉ ConfirmedByTeamA)
        var unconfirmedMatch = new MatchGame
        {
            Id = Guid.NewGuid(),
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            MatchDateTime = DateTime.Today.AddDays(-10),
            PitchName = "Sân PVV",
            Category = MatchCategory.Friendly,
            Status = MatchStatus.Completed,
            ScoreA = 5,
            ScoreB = 1,
            ConfirmedByTeamA = true,
            ConfirmedByTeamB = false // Chưa xác nhận bởi Team B!
        };

        // Tạo 1 trận quá khứ đã Completed VÀ ĐÃ ĐỐI SOÁT CẢ 2 BÊN
        var fullyConfirmedMatch = new MatchGame
        {
            Id = Guid.NewGuid(),
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            MatchDateTime = DateTime.Today.AddDays(-5),
            PitchName = "Sân PVV",
            Category = MatchCategory.Friendly,
            Status = MatchStatus.Completed,
            ScoreA = 3,
            ScoreB = 2,
            ConfirmedByTeamA = true,
            ConfirmedByTeamB = true,
            ConfirmedAt = DateTime.UtcNow.AddDays(-5)
        };

        // Trận hiện tại đã kết thúc
        match.Status = MatchStatus.Completed;
        match.ScoreA = 1;
        match.ScoreB = 1;
        match.ConfirmedByTeamA = true;
        match.ConfirmedByTeamB = true;

        db.Matches.AddRange(unconfirmedMatch, fullyConfirmedMatch);
        await db.SaveChangesAsync();

        await managerController.Detail(match.Id);

        // Chỉ trận fullyConfirmedMatch (Thắng 3-2) và match hiện tại (Hòa 1-1) được tính vào thống kê chính thức!
        // Trận unconfirmedMatch (5-1) không được tính vì chưa xác nhận 2 bên!
        Assert.Equal(1, (int)managerController.ViewBag.TeamAWins);
        Assert.Equal(1, (int)managerController.ViewBag.TeamADraws);
        Assert.Equal(0, (int)managerController.ViewBag.TeamALosses);
    }

    [Fact]
    public async Task LineupReadiness_EvaluatesCorrectCapacity()
    {
        using var db = CreateInMemoryDbContext();
        var (managerController, _, _, _, _, _, teamA, _, match, _, _, _) = SetupTestEnvironment(db, "manager_1");

        // Trận 7 người cần 7 cầu thủ đá chính
        match.PitchType = "7 người";
        await db.SaveChangesAsync();

        await managerController.Detail(match.Id);
        Assert.Equal(7, (int)managerController.ViewBag.RequiredStarters);
        Assert.False((bool)managerController.ViewBag.IsLineupReady); // Chưa có cầu thủ trong đội hình

        // Thêm 7 cầu thủ đá chính cho Team A
        for (int i = 0; i < 7; i++)
        {
            var p = new Player { Id = Guid.NewGuid(), TeamId = teamA.Id, FullName = $"Cầu thủ #{i}", IsActive = true };
            db.Players.Add(p);
            db.MatchPlayerLineups.Add(new MatchPlayerLineup
            {
                MatchId = match.Id,
                TeamId = teamA.Id,
                PlayerId = p.Id,
                IsStarting = true
            });
        }
        await db.SaveChangesAsync();

        await managerController.Detail(match.Id);
        Assert.True((bool)managerController.ViewBag.IsLineupReady);
    }
}
