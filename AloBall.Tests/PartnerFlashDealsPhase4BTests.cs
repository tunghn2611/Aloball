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

public class PartnerFlashDealsPhase4BTests
{
    private static ApplicationDbContext CreateDb(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName ?? $"PartnerFlashDeals_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static PitchVenue MakeVenue(string ownerId, string district = "Cầu Giấy", decimal price = 700_000m) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Sân bóng Test B2B",
        City = "Hà Nội",
        District = district,
        FullAddress = $"100 {district}, Hà Nội",
        ContactPhone = "0901234567",
        IndicativePricePerHour = price,
        TotalPitches = 4,
        OwnerUserId = ownerId,
        IsActive = true
    };

    [Fact]
    public async Task FlashDeal_Validation_RejectsInvalidPriceAndTime_AndComputesDiscountPercent()
    {
        using var db = CreateDb();
        var owner = new ApplicationUser { Id = "owner_1", UserName = "owner@san.vn", Email = "owner@san.vn", FullName = "Chủ sân" };
        db.Users.Add(owner);
        var venue = MakeVenue(owner.Id);
        db.PitchVenues.Add(venue);
        await db.SaveChangesAsync();

        var svc = new PartnerPortalService(db, new TeamAuthorizationService(db));
        var now = CompetitionRules.LocalNow;

        // DealPrice >= OriginalPrice -> rejected
        var badPrice = await svc.CreateFlashDealAsync(new CreateFlashDealInput
        {
            PitchId = venue.Id,
            KickoffAt = now.AddHours(4),
            OriginalPriceVnd = 700_000m,
            DealPriceVnd = 700_000m
        }, owner.Id);
        Assert.False(badPrice.Success);

        // Past KickoffAt -> rejected
        var pastTime = await svc.CreateFlashDealAsync(new CreateFlashDealInput
        {
            PitchId = venue.Id,
            KickoffAt = now.AddHours(-1),
            OriginalPriceVnd = 700_000m,
            DealPriceVnd = 450_000m
        }, owner.Id);
        Assert.False(pastTime.Success);

        // Beyond 24 hours -> rejected (strictly last-minute)
        var beyond24h = await svc.CreateFlashDealAsync(new CreateFlashDealInput
        {
            PitchId = venue.Id,
            KickoffAt = now.AddHours(30),
            OriginalPriceVnd = 700_000m,
            DealPriceVnd = 450_000m
        }, owner.Id);
        Assert.False(beyond24h.Success);

        // Valid within 24h & strictly lower price -> auto-computes DiscountPercent = 36%
        var valid = await svc.CreateFlashDealAsync(new CreateFlashDealInput
        {
            PitchId = venue.Id,
            FieldCode = "Sân 7 - Số 2",
            KickoffAt = now.AddHours(5),
            DurationMinutes = 90,
            OriginalPriceVnd = 700_000m,
            DealPriceVnd = 450_000m,
            Note = "Tặng 1 thùng nước"
        }, owner.Id);

        Assert.True(valid.Success);
        Assert.NotNull(valid.Deal);
        Assert.Equal(36, valid.Deal!.DiscountPercent);
        Assert.Equal(PitchFlashDealStatus.Active, valid.Deal.Status);
    }

    [Fact]
    public async Task FlashDeal_AtomicSingleWinnerClaim_PreventsDoubleClaim_AndPrefillsSplitBillPitchFee()
    {
        string dbName = $"RaceClaim_{Guid.NewGuid()}";
        Guid dealId;
        Guid t1Id;
        Guid t2Id;
        string cap1Id = "cap_1";
        string cap2Id = "cap_2";

        using (var setupDb = CreateDb(dbName))
        {
            var owner = new ApplicationUser { Id = "owner_race", UserName = "o@san.vn", Email = "o@san.vn", FullName = "Owner" };
            var cap1 = new ApplicationUser { Id = cap1Id, UserName = "c1@fc.vn", Email = "c1@fc.vn", FullName = "Cap 1" };
            var cap2 = new ApplicationUser { Id = cap2Id, UserName = "c2@fc.vn", Email = "c2@fc.vn", FullName = "Cap 2" };
            setupDb.Users.AddRange(owner, cap1, cap2);

            var venue = MakeVenue(owner.Id);
            var t1 = new Team { Id = Guid.NewGuid(), Name = "FC Alpha", Slug = "fc-alpha-" + Guid.NewGuid().ToString("N")[..5], IsPublic = true };
            var t2 = new Team { Id = Guid.NewGuid(), Name = "FC Bravo", Slug = "fc-bravo-" + Guid.NewGuid().ToString("N")[..5], IsPublic = true };
            setupDb.PitchVenues.Add(venue);
            setupDb.Teams.AddRange(t1, t2);
            setupDb.TeamMembers.AddRange(
                new TeamMember { TeamId = t1.Id, UserId = cap1Id, Role = TeamRole.Owner },
                new TeamMember { TeamId = t2.Id, UserId = cap2Id, Role = TeamRole.Owner });
            setupDb.Players.AddRange(
                new Player { Id = Guid.NewGuid(), TeamId = t1.Id, FullName = "Player 1", IsActive = true },
                new Player { Id = Guid.NewGuid(), TeamId = t1.Id, FullName = "Player 2", IsActive = true },
                new Player { Id = Guid.NewGuid(), TeamId = t2.Id, FullName = "Player 3", IsActive = true },
                new Player { Id = Guid.NewGuid(), TeamId = t2.Id, FullName = "Player 4", IsActive = true });

            var deal = new PitchFlashDeal
            {
                Id = Guid.NewGuid(),
                PitchId = venue.Id,
                Region = "Cầu Giấy, Hà Nội",
                KickoffAt = CompetitionRules.LocalNow.AddHours(6),
                DurationMinutes = 90,
                OriginalPriceVnd = 700_000m,
                DealPriceVnd = 420_000m,
                DiscountPercent = 40,
                CreatedByUserId = owner.Id,
                Status = PitchFlashDealStatus.Active
            };
            setupDb.PitchFlashDeals.Add(deal);
            await setupDb.SaveChangesAsync();

            dealId = deal.Id;
            t1Id = t1.Id;
            t2Id = t2.Id;
        }

        // Fire two competing claims on separate DbContext instances simultaneously
        var task1 = Task.Run(async () =>
        {
            using var db1 = CreateDb(dbName);
            var svc1 = new PartnerPortalService(db1, new TeamAuthorizationService(db1));
            return await svc1.ClaimFlashDealAsync(dealId, t1Id, cap1Id);
        });
        var task2 = Task.Run(async () =>
        {
            using var db2 = CreateDb(dbName);
            var svc2 = new PartnerPortalService(db2, new TeamAuthorizationService(db2));
            return await svc2.ClaimFlashDealAsync(dealId, t2Id, cap2Id);
        });

        var results = await Task.WhenAll(task1, task2);
        Assert.Equal(1, results.Count(r => r.Success));
        Assert.Equal(1, results.Count(r => !r.Success));

        var winner = results.Single(r => r.Success);
        Assert.NotNull(winner.Match);

        using var verifyDb = CreateDb(dbName);
        var updatedDeal = await verifyDb.PitchFlashDeals.FindAsync(dealId);
        Assert.Equal(PitchFlashDealStatus.Claimed, updatedDeal!.Status);
        Assert.Equal(winner.Match!.Id, updatedDeal.ClaimedMatchId);

        // Pre-filled MatchExpense in Split-Bill uses discounted DealPriceVnd = 420,000 VND
        var expense = await verifyDb.MatchExpenses.Include(e => e.Shares).FirstOrDefaultAsync(e => e.MatchId == winner.Match.Id);
        Assert.NotNull(expense);
        Assert.Equal(420_000m, expense!.PitchFee);
        Assert.Equal(420_000m, expense.MatchTotalPayable);
        Assert.Equal(210_000m, expense.MatchPerPersonShare); // Split across 2 active players
    }

    [Fact]
    public async Task MatchConcierge_PrioritizesActiveFlashDeal_InSameRegionAndWindow()
    {
        using var db = CreateDb();
        var capA = new ApplicationUser { Id = "capA_fg", UserName = "ca@fc.vn", Email = "ca@fc.vn", FullName = "Cap A" };
        var owner = new ApplicationUser { Id = "owner_fg", UserName = "ow@san.vn", Email = "ow@san.vn", FullName = "Owner" };
        db.Users.AddRange(capA, owner);

        var teamA = new Team
        {
            Id = Guid.NewGuid(),
            Name = "FC Pro Flash",
            Slug = "fc-pro-flash-" + Guid.NewGuid().ToString("N")[..5],
            ActivityArea = "Cầu Giấy, Hà Nội",
            PlanTier = PlanTier.Plus,
            PlanExpiresAt = DateTime.UtcNow.AddDays(14),
            AverageRating = 4.8,
            IsPublic = true
        };
        var oppTeam = new Team
        {
            Id = Guid.NewGuid(),
            Name = "FC Đối Thủ",
            Slug = "fc-doi-thu-" + Guid.NewGuid().ToString("N")[..5],
            ActivityArea = "Cầu Giấy, Hà Nội",
            AverageRating = 4.7,
            IsPublic = true
        };
        var venue = MakeVenue(owner.Id, "Cầu Giấy", 700_000m);
        var targetKickoff = CompetitionRules.LocalNow.AddHours(8);

        var flashDeal = new PitchFlashDeal
        {
            Id = Guid.NewGuid(),
            PitchId = venue.Id,
            FieldCode = "Sân 7 - Flash A1",
            Region = "Cầu Giấy, Hà Nội",
            KickoffAt = targetKickoff,
            DurationMinutes = 90,
            OriginalPriceVnd = 700_000m,
            DealPriceVnd = 380_000m,
            DiscountPercent = 46,
            CreatedByUserId = owner.Id,
            Status = PitchFlashDealStatus.Active
        };

        db.Teams.AddRange(teamA, oppTeam);
        db.TeamMembers.Add(new TeamMember { TeamId = teamA.Id, UserId = capA.Id, Role = TeamRole.Owner });
        db.PitchVenues.Add(venue);
        db.PitchFlashDeals.Add(flashDeal);
        await db.SaveChangesAsync();

        var concierge = new MatchConciergeService(db, new TeamAuthorizationService(db));
        var res = await concierge.CreateAndFulfillConciergeOrderAsync(new ConciergeOrderInput
        {
            TeamId = teamA.Id,
            PlayDate = targetKickoff.Date,
            KickoffTime = targetKickoff.TimeOfDay,
            Region = "Cầu Giấy",
            MinStars = 0,
            MaxStars = 10,
            MinFriendlyRating = 4.0,
            IsProWithReferee = false
        }, capA.Id);

        Assert.True(res.Success);
        Assert.Equal(380_000m, res.Order!.EstimatedTotalFee);
        Assert.Equal(380_000m, res.Summary!.PitchFeeVnd);

        var updatedDeal = await db.PitchFlashDeals.FindAsync(flashDeal.Id);
        Assert.Equal(PitchFlashDealStatus.Claimed, updatedDeal!.Status);
        Assert.Equal(teamA.Id, updatedDeal.ClaimedByTeamId);

        var expense = await db.MatchExpenses.FirstOrDefaultAsync(e => e.MatchId == res.Order.ResultMatchId);
        Assert.NotNull(expense);
        Assert.Equal(380_000m, expense!.PitchFee);
    }

    [Fact]
    public async Task PartnerPortal_Security_EnforcesOwnerAndRefereeChecks_AndValidateAntiForgeryToken()
    {
        using var db = CreateDb();
        var owner = new ApplicationUser { Id = "own_sec", UserName = "o@s.vn", Email = "o@s.vn", FullName = "Owner" };
        var refUser = new ApplicationUser { Id = "ref_sec", UserName = "r@s.vn", Email = "r@s.vn", FullName = "Ref" };
        var outsider = new ApplicationUser { Id = "out_sec", UserName = "x@s.vn", Email = "x@s.vn", FullName = "Outsider" };
        db.Users.AddRange(owner, refUser, outsider);

        var venue = MakeVenue(owner.Id);
        var referee = new Referee { Id = Guid.NewGuid(), Name = "TT Tuấn", UserId = refUser.Id, IsActive = true, IsAvailable = true };
        var team = new Team { Id = Guid.NewGuid(), Name = "FC Chủ", Slug = "fc-chu-" + Guid.NewGuid().ToString("N")[..5], IsPublic = true };
        var deal = new PitchFlashDeal
        {
            Id = Guid.NewGuid(), PitchId = venue.Id, Region = "Cầu Giấy",
            KickoffAt = CompetitionRules.LocalNow.AddHours(4), OriginalPriceVnd = 600_000m, DealPriceVnd = 400_000m,
            DiscountPercent = 33, CreatedByUserId = owner.Id, Status = PitchFlashDealStatus.Active
        };

        db.PitchVenues.Add(venue);
        db.Referees.Add(referee);
        db.Teams.Add(team);
        db.PitchFlashDeals.Add(deal);
        await db.SaveChangesAsync();

        var svc = new PartnerPortalService(db, new TeamAuthorizationService(db));

        // Outsider cannot publish Flash Deal on another owner's venue, cancel it, toggle referee, or claim for unowned team
        Assert.False((await svc.CreateFlashDealAsync(new CreateFlashDealInput { PitchId = venue.Id, KickoffAt = CompetitionRules.LocalNow.AddHours(3), OriginalPriceVnd = 600_000m, DealPriceVnd = 400_000m }, outsider.Id)).Success);
        Assert.False((await svc.CancelFlashDealAsync(deal.Id, outsider.Id)).Success);
        Assert.False((await svc.ToggleRefereeAvailabilityAsync(referee.Id, outsider.Id)).Success);
        Assert.False((await svc.ClaimFlashDealAsync(deal.Id, team.Id, outsider.Id)).Success);

        // Referee owner toggles own availability cleanly
        Assert.True((await svc.ToggleRefereeAvailabilityAsync(referee.Id, refUser.Id)).Success);
        Assert.False((await db.Referees.FindAsync(referee.Id))!.IsAvailable);

        // All POST actions on PartnerPortalController enforce ValidateAntiForgeryToken
        var postMethods = typeof(PartnerPortalController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<HttpPostAttribute>() != null)
            .ToList();

        Assert.NotEmpty(postMethods);
        foreach (var pm in postMethods)
            Assert.NotNull(pm.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }
}
