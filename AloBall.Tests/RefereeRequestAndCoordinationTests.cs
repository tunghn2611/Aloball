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

public class RefereeRequestAndCoordinationTests
{
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

    private void SetUserContext(Controller controller, ApplicationUser user)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.UserName ?? user.Email ?? "testuser")
        };
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
        controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
            httpContext,
            new TestTempDataProvider());
    }

    private class TestTempDataProvider : Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider
    {
        private readonly Dictionary<string, object> _data = new();
        public IDictionary<string, object> LoadTempData(HttpContext context) => _data;
        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
            foreach (var kvp in values) _data[kvp.Key] = kvp.Value;
        }
    }

    private async Task<(ApplicationUser admin, ApplicationUser owner, ApplicationUser member, ApplicationUser outsider, Team team, Referee referee1, Referee referee2)>
        SeedBasicDataAsync(ApplicationDbContext db)
    {
        var admin = new ApplicationUser { Id = "admin_user", UserName = "admin@aloball.vn", Email = "admin@aloball.vn", FullName = "Platform Admin" };
        var owner = new ApplicationUser { Id = "owner_user", UserName = "owner@aloball.vn", Email = "owner@aloball.vn", FullName = "Team Owner" };
        var member = new ApplicationUser { Id = "member_user", UserName = "member@aloball.vn", Email = "member@aloball.vn", FullName = "Team Member" };
        var outsider = new ApplicationUser { Id = "outsider_user", UserName = "outsider@aloball.vn", Email = "outsider@aloball.vn", FullName = "Outsider" };

        db.Users.AddRange(admin, owner, member, outsider);

        var team = new Team
        {
            Id = Guid.NewGuid(),
            Name = "Hanoi Phoenix FC",
            Slug = "hanoi-phoenix-fc",
            CreatedAt = DateTime.UtcNow
        };
        db.Teams.Add(team);

        db.TeamMembers.AddRange(
            new TeamMember { TeamId = team.Id, UserId = owner.Id, Role = TeamRole.Owner },
            new TeamMember { TeamId = team.Id, UserId = member.Id, Role = TeamRole.Member }
        );

        var ref1 = new Referee
        {
            Id = Guid.NewGuid(),
            Name = "Trọng tài Nguyễn Văn Tuấn",
            ContactInfo = "0912.345.678",
            ExperienceSummary = "Kinh nghiệm 5 năm bắt sân 7",
            SupportedFormats = "7 người",
            DefaultPriceVND = 350000,
            IsActive = true
        };

        var ref2 = new Referee
        {
            Id = Guid.NewGuid(),
            Name = "Trọng tài Trần Đình Trọng",
            ContactInfo = "0987.654.321",
            ExperienceSummary = "Trọng tài VFF cấp 2",
            SupportedFormats = "7 người, 11 người",
            DefaultPriceVND = 400000,
            IsActive = true
        };

        db.Referees.AddRange(ref1, ref2);
        await db.SaveChangesAsync();

        return (admin, owner, member, outsider, team, ref1, ref2);
    }

    [Fact]
    public async Task CreateRequest_WithPastDate_ThrowsArgumentException()
    {
        using var db = CreateInMemoryDbContext();
        var auth = new TeamAuthorizationService(db);
        var service = new RefereeCoordinationService(db, auth);
        var data = await SeedBasicDataAsync(db);

        var req = new RefereeRequest
        {
            TeamId = data.team.Id,
            ServiceDate = DateTime.Today.AddDays(-1), // Past date
            StartTime = new TimeSpan(19, 0, 0),
            EndTime = new TimeSpan(20, 30, 0),
            VenueName = "Sân Cầu Giấy",
            VenueAddress = "Hà Nội",
            MatchFormat = "7 người",
            RefereeCount = 1
        };

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRequestAsync(req, data.owner.Id));
    }

    [Fact]
    public async Task CreateRequest_WithEndTimeBeforeStartTime_ThrowsArgumentException()
    {
        using var db = CreateInMemoryDbContext();
        var auth = new TeamAuthorizationService(db);
        var service = new RefereeCoordinationService(db, auth);
        var data = await SeedBasicDataAsync(db);

        var req = new RefereeRequest
        {
            TeamId = data.team.Id,
            ServiceDate = DateTime.Today.AddDays(1),
            StartTime = new TimeSpan(20, 0, 0),
            EndTime = new TimeSpan(19, 0, 0), // End before start
            VenueName = "Sân Cầu Giấy",
            VenueAddress = "Hà Nội",
            MatchFormat = "7 người",
            RefereeCount = 1
        };

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRequestAsync(req, data.owner.Id));
    }

    [Fact]
    public async Task CreateRequest_ByNonManagerMember_ThrowsUnauthorizedAccessException()
    {
        using var db = CreateInMemoryDbContext();
        var auth = new TeamAuthorizationService(db);
        var service = new RefereeCoordinationService(db, auth);
        var data = await SeedBasicDataAsync(db);

        var req = new RefereeRequest
        {
            TeamId = data.team.Id,
            ServiceDate = DateTime.Today.AddDays(1),
            StartTime = new TimeSpan(19, 0, 0),
            EndTime = new TimeSpan(20, 30, 0),
            VenueName = "Sân Cầu Giấy",
            VenueAddress = "Hà Nội",
            MatchFormat = "7 người",
            RefereeCount = 1
        };

        // Member is not Manager or Owner
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateRequestAsync(req, data.member.Id));
    }

    [Fact]
    public async Task CreateRequest_ByOwner_SucceedsWithPendingStatus()
    {
        using var db = CreateInMemoryDbContext();
        var auth = new TeamAuthorizationService(db);
        var service = new RefereeCoordinationService(db, auth);
        var data = await SeedBasicDataAsync(db);

        var req = new RefereeRequest
        {
            TeamId = data.team.Id,
            ServiceDate = DateTime.Today.AddDays(2),
            StartTime = new TimeSpan(19, 0, 0),
            EndTime = new TimeSpan(20, 30, 0),
            VenueName = "Sân Thành Phát",
            VenueAddress = "Số 2 Hoàng Minh Giám, Hà Nội",
            MatchFormat = "7 người",
            RefereeCount = 1,
            BudgetVND = 350000
        };

        var created = await service.CreateRequestAsync(req, data.owner.Id);

        Assert.NotNull(created);
        Assert.Equal(RefereeRequestStatus.Pending, created.Status);
        Assert.Equal("Sân Thành Phát", created.VenueName);
    }

    [Fact]
    public async Task CancelRequest_WhenPendingOrUnderReview_Succeeds()
    {
        using var db = CreateInMemoryDbContext();
        var auth = new TeamAuthorizationService(db);
        var service = new RefereeCoordinationService(db, auth);
        var data = await SeedBasicDataAsync(db);

        var req = new RefereeRequest
        {
            TeamId = data.team.Id,
            ServiceDate = DateTime.Today.AddDays(2),
            StartTime = new TimeSpan(19, 0, 0),
            EndTime = new TimeSpan(20, 30, 0),
            VenueName = "Sân Thành Phát",
            VenueAddress = "Số 2 Hoàng Minh Giám, Hà Nội",
            MatchFormat = "7 người",
            RefereeCount = 1
        };
        var created = await service.CreateRequestAsync(req, data.owner.Id);

        var cancelResult = await service.CancelRequestAsync(created.Id, data.owner.Id);
        Assert.True(cancelResult);

        var updated = await db.RefereeRequests.FindAsync(created.Id);
        Assert.Equal(RefereeRequestStatus.Cancelled, updated!.Status);
    }

    [Fact]
    public async Task SuperAdmin_CanSubmitProposal_AndRequesterAccepts_RevealsRefereeContact()
    {
        using var db = CreateInMemoryDbContext();
        var auth = new TeamAuthorizationService(db);
        var service = new RefereeCoordinationService(db, auth);
        var data = await SeedBasicDataAsync(db);

        // 1. Team creates request for 1 referee
        var req = new RefereeRequest
        {
            TeamId = data.team.Id,
            ServiceDate = DateTime.Today.AddDays(3),
            StartTime = new TimeSpan(19, 0, 0),
            EndTime = new TimeSpan(20, 30, 0),
            VenueName = "Sân Bách Khoa",
            VenueAddress = "Hai Bà Trưng, Hà Nội",
            MatchFormat = "7 người",
            RefereeCount = 1
        };
        var created = await service.CreateRequestAsync(req, data.owner.Id);

        // 2. Admin submits proposal
        var proposal = await service.CreateProposalAsync(
            created.Id,
            data.admin.Id,
            350000,
            "Trọng tài cấp 2 có mặt trước 15 phút",
            new List<Guid> { data.referee1.Id });

        Assert.Equal(RefereeProposalStatus.Pending, proposal.Status);

        // Check request status updated to AwaitingTeamDecision
        var checkReq = await db.RefereeRequests.FindAsync(created.Id);
        Assert.Equal(RefereeRequestStatus.AwaitingTeamDecision, checkReq!.Status);

        // 3. Before acceptance: member views details -> contact info must be masked
        var detailsBefore = await service.GetRequestDetailsAsync(created.Id, data.member.Id, false);
        var assignedRefBefore = detailsBefore!.Proposals.First().Assignments.First().Referee;
        Assert.Contains("Bảo mật", assignedRefBefore.ContactInfo);

        // 4. Owner accepts proposal
        var acceptResult = await service.AcceptProposalAsync(proposal.Id, data.owner.Id);
        Assert.True(acceptResult);

        var confirmedReq = await db.RefereeRequests.FindAsync(created.Id);
        Assert.Equal(RefereeRequestStatus.Confirmed, confirmedReq!.Status);

        var acceptedProposal = await db.RefereeProposals.FindAsync(proposal.Id);
        Assert.Equal(RefereeProposalStatus.Accepted, acceptedProposal!.Status);

        // 5. After acceptance: member views details -> contact info is revealed!
        var detailsAfter = await service.GetRequestDetailsAsync(created.Id, data.member.Id, false);
        var assignedRefAfter = detailsAfter!.Proposals.First().Assignments.First().Referee;
        Assert.Equal("0912.345.678", assignedRefAfter.ContactInfo);
    }

    [Fact]
    public async Task AcceptingProposal_MarksOtherProposalsAsNotSelected()
    {
        using var db = CreateInMemoryDbContext();
        var auth = new TeamAuthorizationService(db);
        var service = new RefereeCoordinationService(db, auth);
        var data = await SeedBasicDataAsync(db);

        var req = new RefereeRequest
        {
            TeamId = data.team.Id,
            ServiceDate = DateTime.Today.AddDays(4),
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(19, 30, 0),
            VenueName = "Sân Thủy Lợi",
            VenueAddress = "Hà Nội",
            MatchFormat = "7 người",
            RefereeCount = 1
        };
        var created = await service.CreateRequestAsync(req, data.owner.Id);

        // Admin submits 2 competing proposals
        var prop1 = await service.CreateProposalAsync(created.Id, data.admin.Id, 300000, "Gói cơ bản", new List<Guid> { data.referee1.Id });
        var prop2 = await service.CreateProposalAsync(created.Id, data.admin.Id, 400000, "Gói VIP", new List<Guid> { data.referee2.Id });

        // Owner accepts prop1
        await service.AcceptProposalAsync(prop1.Id, data.owner.Id);

        var p1 = await db.RefereeProposals.FindAsync(prop1.Id);
        var p2 = await db.RefereeProposals.FindAsync(prop2.Id);

        Assert.Equal(RefereeProposalStatus.Accepted, p1!.Status);
        Assert.Equal(RefereeProposalStatus.NotSelected, p2!.Status);

        // Attempting to accept prop2 now should fail
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptProposalAsync(prop2.Id, data.owner.Id));
    }

    [Fact]
    public async Task CannotOfferAlreadyConfirmedReferee_ToOverlappingRequest()
    {
        using var db = CreateInMemoryDbContext();
        var auth = new TeamAuthorizationService(db);
        var service = new RefereeCoordinationService(db, auth);
        var data = await SeedBasicDataAsync(db);

        var targetDate = DateTime.Today.AddDays(5);

        // Request 1: 18:00 - 19:30
        var req1 = await service.CreateRequestAsync(new RefereeRequest
        {
            TeamId = data.team.Id,
            ServiceDate = targetDate,
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(19, 30, 0),
            VenueName = "Sân A",
            VenueAddress = "HN",
            MatchFormat = "7 người",
            RefereeCount = 1
        }, data.owner.Id);

        var prop1 = await service.CreateProposalAsync(req1.Id, data.admin.Id, 350000, "Note", new List<Guid> { data.referee1.Id });
        await service.AcceptProposalAsync(prop1.Id, data.owner.Id);

        // Request 2 on same day overlapping hours: 19:00 - 20:30
        var req2 = await service.CreateRequestAsync(new RefereeRequest
        {
            TeamId = data.team.Id,
            ServiceDate = targetDate,
            StartTime = new TimeSpan(19, 0, 0), // Overlaps with 18:00 - 19:30!
            EndTime = new TimeSpan(20, 30, 0),
            VenueName = "Sân B",
            VenueAddress = "HN",
            MatchFormat = "7 người",
            RefereeCount = 1
        }, data.owner.Id);

        // Attempting to propose referee1 for req2 should throw InvalidOperationException
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateProposalAsync(req2.Id, data.admin.Id, 350000, "Overlap attempt", new List<Guid> { data.referee1.Id }));

        Assert.Contains("trùng giờ", ex.Message);
    }

    [Fact]
    public async Task UnauthorizedUser_CannotAccessOtherTeamsRequestDetails()
    {
        using var db = CreateInMemoryDbContext();
        var auth = new TeamAuthorizationService(db);
        var service = new RefereeCoordinationService(db, auth);
        var data = await SeedBasicDataAsync(db);

        var req = await service.CreateRequestAsync(new RefereeRequest
        {
            TeamId = data.team.Id,
            ServiceDate = DateTime.Today.AddDays(2),
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(19, 30, 0),
            VenueName = "Sân Riêng",
            VenueAddress = "HN",
            MatchFormat = "7 người",
            RefereeCount = 1
        }, data.owner.Id);

        // Outsider attempts to access
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetRequestDetailsAsync(req.Id, data.outsider.Id, false));
    }

    [Fact]
    public async Task CannotCancelCompletedOrConfirmedRequest()
    {
        using var db = CreateInMemoryDbContext();
        var auth = new TeamAuthorizationService(db);
        var service = new RefereeCoordinationService(db, auth);
        var data = await SeedBasicDataAsync(db);

        var req = await service.CreateRequestAsync(new RefereeRequest
        {
            TeamId = data.team.Id,
            ServiceDate = DateTime.Today.AddDays(2),
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(19, 30, 0),
            VenueName = "Sân C",
            VenueAddress = "HN",
            MatchFormat = "7 người",
            RefereeCount = 1
        }, data.owner.Id);

        var prop = await service.CreateProposalAsync(req.Id, data.admin.Id, 300000, "Note", new List<Guid> { data.referee1.Id });
        await service.AcceptProposalAsync(prop.Id, data.owner.Id);

        // Attempt to cancel confirmed request
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelRequestAsync(req.Id, data.owner.Id));

        // Complete request by admin
        await service.CompleteRequestAsync(req.Id, data.admin.Id);

        // Attempt to cancel completed request
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelRequestAsync(req.Id, data.owner.Id));
    }

    [Fact]
    public async Task CannotCreateDuplicateActiveRequest_ForSameMatch()
    {
        using var db = CreateInMemoryDbContext();
        var auth = new TeamAuthorizationService(db);
        var service = new RefereeCoordinationService(db, auth);
        var data = await SeedBasicDataAsync(db);

        var matchId = Guid.NewGuid();

        var req1 = new RefereeRequest
        {
            TeamId = data.team.Id,
            MatchId = matchId,
            ServiceDate = DateTime.Today.AddDays(2),
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(19, 30, 0),
            VenueName = "Sân Đôi",
            VenueAddress = "HN",
            MatchFormat = "7 người",
            RefereeCount = 1
        };
        await service.CreateRequestAsync(req1, data.owner.Id);

        // Duplicate active request for same match
        var req2 = new RefereeRequest
        {
            TeamId = data.team.Id,
            MatchId = matchId,
            ServiceDate = DateTime.Today.AddDays(2),
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(19, 30, 0),
            VenueName = "Sân Đôi",
            VenueAddress = "HN",
            MatchFormat = "7 người",
            RefereeCount = 1
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRequestAsync(req2, data.owner.Id));
    }

    [Fact]
    public async Task RefereeDirectory_SuperAdminCRUD_WorksCorrectly()
    {
        using var db = CreateInMemoryDbContext();
        var auth = new TeamAuthorizationService(db);
        var service = new RefereeCoordinationService(db, auth);
        var data = await SeedBasicDataAsync(db);

        // 1. Non-admin cannot create referee
        var newRef = new Referee
        {
            Name = "Hoàng Hải",
            ContactInfo = "0933.222.111",
            ExperienceSummary = "Trọng tài phong trào",
            SupportedFormats = "7 người",
            DefaultPriceVND = 300000,
            IsActive = true
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateRefereeAsync(newRef, data.owner.Id));

        // 2. Admin creates referee
        var createdRef = await service.CreateRefereeAsync(newRef, data.admin.Id);
        Assert.NotNull(createdRef);
        Assert.Equal("Hoàng Hải", createdRef.Name);

        // 3. Admin toggles status
        await service.ToggleRefereeStatusAsync(createdRef.Id, data.admin.Id);
        var fetched = await service.GetRefereeByIdAsync(createdRef.Id);
        Assert.False(fetched!.IsActive);

        // 4. Inactive referee cannot be included in proposal
        var req = await service.CreateRequestAsync(new RefereeRequest
        {
            TeamId = data.team.Id,
            ServiceDate = DateTime.Today.AddDays(2),
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(19, 30, 0),
            VenueName = "Sân test",
            VenueAddress = "HN",
            MatchFormat = "7 người",
            RefereeCount = 1
        }, data.owner.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateProposalAsync(req.Id, data.admin.Id, 300000, "Test", new List<Guid> { createdRef.Id }));
    }
}
