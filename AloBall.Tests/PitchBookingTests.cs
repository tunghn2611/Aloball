using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AloBall.Tests;

public class PitchBookingTests
{
    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private async Task<(ApplicationUser admin, ApplicationUser owner, ApplicationUser member, ApplicationUser outsider, Team team, PitchVenue venue1, PitchVenue venue2)>
        SeedBasicPitchDataAsync(ApplicationDbContext db)
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

        var venue1 = new PitchVenue
        {
            Id = Guid.NewGuid(),
            Name = "Sân bóng Thành Phát",
            City = "Hà Nội",
            District = "Cầu Giấy",
            FullAddress = "Số 2 Hoàng Minh Giám, Hà Nội",
            ContactPhone = "0912.345.888",
            SupportedFormats = "5 người, 7 người",
            SurfaceType = "Cỏ nhân tạo",
            TotalPitches = 4,
            IndicativePricePerHour = 500000,
            IsActive = true
        };

        var venue2 = new PitchVenue
        {
            Id = Guid.NewGuid(),
            Name = "Sân bóng Thủy Lợi",
            City = "Hà Nội",
            District = "Đống Đa",
            FullAddress = "Ngõ 95 Chùa Bộc, Hà Nội",
            ContactPhone = "0988.777.666",
            SupportedFormats = "7 người, 11 người",
            SurfaceType = "Cỏ nhân tạo",
            TotalPitches = 2,
            IndicativePricePerHour = 600000,
            IsActive = true
        };

        db.PitchVenues.AddRange(venue1, venue2);
        await db.SaveChangesAsync();

        return (admin, owner, member, outsider, team, venue1, venue2);
    }

    [Fact]
    public async Task VenueListingAndFiltering_WorksAccurately()
    {
        using var db = CreateInMemoryDbContext();
        var auth = new TeamAuthorizationService(db);
        var service = new PitchBookingService(db, auth);
        var data = await SeedBasicPitchDataAsync(db);

        // Filter by district Cầu Giấy
        var cauGiayVenues = await service.GetActiveVenuesAsync(district: "Cầu Giấy");
        Assert.Single(cauGiayVenues);
        Assert.Equal("Sân bóng Thành Phát", cauGiayVenues.First().Name);

        // Filter by supported format 11 người
        var format11Venues = await service.GetActiveVenuesAsync(format: "11 người");
        Assert.Single(format11Venues);
        Assert.Equal("Sân bóng Thủy Lợi", format11Venues.First().Name);

        // Filter by search keyword
        var searchVenues = await service.GetActiveVenuesAsync(search: "Chùa Bộc");
        Assert.Single(searchVenues);
        Assert.Equal("Sân bóng Thủy Lợi", searchVenues.First().Name);
    }

    [Fact]
    public async Task InactiveVenue_IsHiddenFromPublic_AndPreservesHistory()
    {
        using var db = CreateInMemoryDbContext();
        var auth = new TeamAuthorizationService(db);
        var service = new PitchBookingService(db, auth);
        var data = await SeedBasicPitchDataAsync(db);

        // Toggle venue status to inactive
        await service.ToggleVenueStatusAsync(data.venue1.Id, data.admin.Id);

        // Public discovery should now only return venue2
        var activeList = await service.GetActiveVenuesAsync();
        Assert.Single(activeList);
        Assert.Equal(data.venue2.Id, activeList.First().Id);

        // Admin list should still return both venues
        var adminList = await service.GetAllVenuesAsync();
        Assert.Equal(2, adminList.Count);
    }

    [Fact]
    public async Task CreateBookingRequest_Validation_RejectsPastDateAndInvalidTimes()
    {
        using var db = CreateInMemoryDbContext();
        var auth = new TeamAuthorizationService(db);
        var service = new PitchBookingService(db, auth);
        var data = await SeedBasicPitchDataAsync(db);

        // Past date
        var pastReq = new PitchBookingRequest
        {
            TeamId = data.team.Id,
            PitchVenueId = data.venue1.Id,
            ServiceDate = DateTime.Today.AddDays(-1),
            StartTime = new TimeSpan(19, 0, 0),
            EndTime = new TimeSpan(20, 30, 0),
            PitchFormat = "7 người",
            PitchCount = 1
        };
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateBookingRequestAsync(pastReq, data.owner.Id));

        // End time before start time
        var invalidTimeReq = new PitchBookingRequest
        {
            TeamId = data.team.Id,
            PitchVenueId = data.venue1.Id,
            ServiceDate = DateTime.Today.AddDays(2),
            StartTime = new TimeSpan(21, 0, 0),
            EndTime = new TimeSpan(19, 0, 0),
            PitchFormat = "7 người",
            PitchCount = 1
        };
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateBookingRequestAsync(invalidTimeReq, data.owner.Id));
    }

    [Fact]
    public async Task Authorization_OnlyOwnerOrManagerCanRequest_MemberAndOutsiderDenied()
    {
        using var db = CreateInMemoryDbContext();
        var auth = new TeamAuthorizationService(db);
        var service = new PitchBookingService(db, auth);
        var data = await SeedBasicPitchDataAsync(db);

        var validReq = new PitchBookingRequest
        {
            TeamId = data.team.Id,
            PitchVenueId = data.venue1.Id,
            ServiceDate = DateTime.Today.AddDays(2),
            StartTime = new TimeSpan(19, 0, 0),
            EndTime = new TimeSpan(20, 30, 0),
            PitchFormat = "7 người",
            PitchCount = 1
        };

        // Member denied
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateBookingRequestAsync(validReq, data.member.Id));

        // Outsider denied
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateBookingRequestAsync(validReq, data.outsider.Id));

        // Owner succeeds
        var created = await service.CreateBookingRequestAsync(validReq, data.owner.Id);
        Assert.NotNull(created);
        Assert.Equal(PitchBookingStatus.Pending, created.Status);
    }

    [Fact]
    public async Task NotificationDelivered_WhenBookingRequestSubmitted()
    {
        using var db = CreateInMemoryDbContext();
        var auth = new TeamAuthorizationService(db);
        var service = new PitchBookingService(db, auth);
        var data = await SeedBasicPitchDataAsync(db);

        var req = new PitchBookingRequest
        {
            TeamId = data.team.Id,
            PitchVenueId = data.venue1.Id,
            ServiceDate = DateTime.Today.AddDays(3),
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(19, 30, 0),
            PitchFormat = "7 người",
            PitchCount = 1
        };
        await service.CreateBookingRequestAsync(req, data.owner.Id);

        // Check in-app team message was delivered
        var msg = await db.TeamMessages.FirstOrDefaultAsync(m => m.TeamId == data.team.Id && m.Title.Contains("đặt sân"));
        Assert.NotNull(msg);
        Assert.Contains(data.venue1.Name, msg.Content);
    }

    [Fact]
    public async Task SuperAdmin_SubmitsOffer_AndTeamAccepts_TransitionsToConfirmed()
    {
        using var db = CreateInMemoryDbContext();
        var auth = new TeamAuthorizationService(db);
        var service = new PitchBookingService(db, auth);
        var data = await SeedBasicPitchDataAsync(db);

        var req = await service.CreateBookingRequestAsync(new PitchBookingRequest
        {
            TeamId = data.team.Id,
            PitchVenueId = data.venue1.Id,
            ServiceDate = DateTime.Today.AddDays(2),
            StartTime = new TimeSpan(19, 0, 0),
            EndTime = new TimeSpan(20, 30, 0),
            PitchFormat = "7 người",
            PitchCount = 1
        }, data.owner.Id);

        // Non-admin cannot submit offer
        var offerData = new PitchBookingOffer
        {
            ConfirmedVenueName = data.venue1.Name,
            ConfirmedAddress = data.venue1.FullAddress,
            ConfirmedDate = req.ServiceDate,
            ConfirmedStartTime = req.StartTime,
            ConfirmedEndTime = req.EndTime,
            ConfirmedPitchFormat = "7 người",
            ConfirmedPitchCount = 1,
            SpecificFieldCode = "Sân 1",
            TotalPriceVND = 650000,
            DepositInstructions = "Cọc 200k"
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateBookingOfferAsync(req.Id, data.owner.Id, offerData));

        // Admin submits offer
        var offer = await service.CreateBookingOfferAsync(req.Id, data.admin.Id, offerData);
        Assert.Equal(PitchBookingOfferStatus.Pending, offer.Status);

        var checkReq = await db.PitchBookingRequests.FindAsync(req.Id);
        Assert.Equal(PitchBookingStatus.AwaitingTeamDecision, checkReq!.Status);

        // Team Owner accepts offer
        var acceptResult = await service.AcceptBookingOfferAsync(offer.Id, data.owner.Id);
        Assert.True(acceptResult);

        var confirmedReq = await db.PitchBookingRequests.FindAsync(req.Id);
        Assert.Equal(PitchBookingStatus.Confirmed, confirmedReq!.Status);
        Assert.Equal("Sân 1", confirmedReq.ConfirmedFieldCode);

        var acceptedOffer = await db.PitchBookingOffers.FindAsync(offer.Id);
        Assert.Equal(PitchBookingOfferStatus.Accepted, acceptedOffer!.Status);
    }

    [Fact]
    public async Task OverlappingConfirmedBookings_ArePrevented()
    {
        using var db = CreateInMemoryDbContext();
        var auth = new TeamAuthorizationService(db);
        var service = new PitchBookingService(db, auth);
        var data = await SeedBasicPitchDataAsync(db);

        var bookingDate = DateTime.Today.AddDays(4);

        // Booking 1: Sân bóng Thủy Lợi (TotalPitches = 2), 19:00 - 20:30, Sân 1
        var req1 = await service.CreateBookingRequestAsync(new PitchBookingRequest
        {
            TeamId = data.team.Id,
            PitchVenueId = data.venue2.Id,
            ServiceDate = bookingDate,
            StartTime = new TimeSpan(19, 0, 0),
            EndTime = new TimeSpan(20, 30, 0),
            PitchFormat = "7 người",
            PitchCount = 1
        }, data.owner.Id);

        var offer1 = await service.CreateBookingOfferAsync(req1.Id, data.admin.Id, new PitchBookingOffer
        {
            ConfirmedVenueName = data.venue2.Name,
            ConfirmedAddress = data.venue2.FullAddress,
            ConfirmedDate = bookingDate,
            ConfirmedStartTime = new TimeSpan(19, 0, 0),
            ConfirmedEndTime = new TimeSpan(20, 30, 0),
            ConfirmedPitchFormat = "7 người",
            ConfirmedPitchCount = 1,
            SpecificFieldCode = "Sân 1",
            TotalPriceVND = 600000
        });
        await service.AcceptBookingOfferAsync(offer1.Id, data.owner.Id);

        // Booking 2 on same venue, same date, overlapping time slot (19:30 - 21:00) attempting to book Sân 1
        var req2 = await service.CreateBookingRequestAsync(new PitchBookingRequest
        {
            TeamId = data.team.Id,
            PitchVenueId = data.venue2.Id,
            ServiceDate = bookingDate,
            StartTime = new TimeSpan(19, 30, 0), // Overlaps!
            EndTime = new TimeSpan(21, 0, 0),
            PitchFormat = "7 người",
            PitchCount = 1
        }, data.owner.Id);

        // Admin attempting to offer Sân 1 for req2 must throw conflict exception
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateBookingOfferAsync(req2.Id, data.admin.Id, new PitchBookingOffer
        {
            ConfirmedVenueName = data.venue2.Name,
            ConfirmedAddress = data.venue2.FullAddress,
            ConfirmedDate = bookingDate,
            ConfirmedStartTime = new TimeSpan(19, 30, 0),
            ConfirmedEndTime = new TimeSpan(21, 0, 0),
            ConfirmedPitchFormat = "7 người",
            ConfirmedPitchCount = 1,
            SpecificFieldCode = "Sân 1", // Specific field conflict!
            TotalPriceVND = 600000
        }));

        Assert.Contains("Sân con 'Sân 1'", ex.Message);
    }

    [Fact]
    public async Task CannotCancelConfirmedOrCompletedBooking()
    {
        using var db = CreateInMemoryDbContext();
        var auth = new TeamAuthorizationService(db);
        var service = new PitchBookingService(db, auth);
        var data = await SeedBasicPitchDataAsync(db);

        var req = await service.CreateBookingRequestAsync(new PitchBookingRequest
        {
            TeamId = data.team.Id,
            PitchVenueId = data.venue1.Id,
            ServiceDate = DateTime.Today.AddDays(2),
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(19, 30, 0),
            PitchFormat = "7 người",
            PitchCount = 1
        }, data.owner.Id);

        var offer = await service.CreateBookingOfferAsync(req.Id, data.admin.Id, new PitchBookingOffer
        {
            ConfirmedVenueName = data.venue1.Name,
            ConfirmedAddress = data.venue1.FullAddress,
            ConfirmedDate = req.ServiceDate,
            ConfirmedStartTime = req.StartTime,
            ConfirmedEndTime = req.EndTime,
            ConfirmedPitchFormat = "7 người",
            ConfirmedPitchCount = 1,
            TotalPriceVND = 500000
        });
        await service.AcceptBookingOfferAsync(offer.Id, data.owner.Id);

        // Attempt to cancel confirmed booking -> must fail
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelBookingRequestAsync(req.Id, data.owner.Id));

        // Mark completed by admin
        await service.CompleteBookingAsync(req.Id, data.admin.Id);

        // Attempt to cancel completed booking -> must fail
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelBookingRequestAsync(req.Id, data.owner.Id));
    }
}
