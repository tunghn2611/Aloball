using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using AloBall.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AloBall.Tests;

public class LoanMarketPhase3ATests
{
    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"LoanMarketTest_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<(Team recruitingTeam, Team ringerHomeTeam, ApplicationUser captain, ApplicationUser ringerUser, Player ringerPlayer, MatchGame match)>
        SeedLoanScenarioAsync(ApplicationDbContext db, int initialAbCoin = 15)
    {
        var captain = new ApplicationUser
        {
            Id = "cap_" + Guid.NewGuid(),
            UserName = "cap@aloball.vn",
            Email = "cap@aloball.vn",
            FullName = "Đội Trưởng FC Thái Sơn"
        };
        var ringerUser = new ApplicationUser
        {
            Id = "ringer_" + Guid.NewGuid(),
            UserName = "ringer@aloball.vn",
            Email = "ringer@aloball.vn",
            FullName = "Nguyễn Quang Hải Phủi",
            PreferredFoot = "Trái",
            Experience = "Phủi cứng (> 5 năm)",
            Region = "Cầu Giấy",
            PlayStyles = "FINESSE+,TIKI_TAKA,INCISIVE"
        };
        db.Users.AddRange(captain, ringerUser);

        var recruitingTeam = new Team
        {
            Id = Guid.NewGuid(),
            Name = "FC Thái Sơn",
            Slug = "fc-thai-son-" + Guid.NewGuid().ToString("N")[..6],
            ActivityArea = "Cầu Giấy",
            HomePitch = "Sân bóng Thành Phát",
            AverageRating = 4.9,
            IsPublic = true
        };
        var ringerHomeTeam = new Team
        {
            Id = Guid.NewGuid(),
            Name = "FC Đông Anh",
            Slug = "fc-dong-anh-" + Guid.NewGuid().ToString("N")[..6],
            ActivityArea = "Đông Anh",
            AverageRating = 4.8,
            IsPublic = true
        };
        db.Teams.AddRange(recruitingTeam, ringerHomeTeam);

        db.TeamMembers.Add(new TeamMember { TeamId = recruitingTeam.Id, UserId = captain.Id, Role = TeamRole.Owner });
        db.TeamMembers.Add(new TeamMember { TeamId = ringerHomeTeam.Id, UserId = ringerUser.Id, Role = TeamRole.Member });

        var capPlayer = new Player
        {
            Id = Guid.NewGuid(),
            TeamId = recruitingTeam.Id,
            LinkedUserId = captain.Id,
            FullName = captain.FullName,
            Position = "CM",
            IsClaimed = true,
            IsActive = true
        };
        var ringerPlayer = new Player
        {
            Id = Guid.NewGuid(),
            TeamId = ringerHomeTeam.Id,
            LinkedUserId = ringerUser.Id,
            FullName = ringerUser.FullName,
            Position = "FWD",
            StandardPrimaryPosition = "ST",
            PreferredFoot = "Trái",
            Experience = "Phủi cứng (> 5 năm)",
            PlayStyles = "FINESSE+,TIKI_TAKA,INCISIVE",
            IsClaimed = true,
            IsActive = true
        };
        db.Players.AddRange(capPlayer, ringerPlayer);

        if (initialAbCoin > 0)
        {
            db.PlayerAbJournalEntries.Add(new PlayerAbJournalEntry
            {
                Id = Guid.NewGuid(),
                TeamId = ringerHomeTeam.Id,
                PlayerId = ringerPlayer.Id,
                StatisticalPeriod = "2026-11",
                SourceDomain = AbSourceDomain.AttendanceSession,
                SourceId = Guid.NewGuid(),
                SourceRole = "Attendance",
                SourceRevision = 1,
                OperationKey = $"{ringerHomeTeam.Id}_Init_{ringerPlayer.Id}",
                Points = initialAbCoin,
                RegulationVersion = 1,
                EventDate = DateTime.UtcNow
            });
        }

        var match = new MatchGame
        {
            Id = Guid.NewGuid(),
            TeamAId = recruitingTeam.Id,
            Category = MatchCategory.Friendly,
            TeamBExternalName = "FC Đối Thủ",
            MatchDateTime = CompetitionRules.LocalNow.Date.AddDays(1).AddHours(19),
            PitchName = "Sân bóng Thành Phát",
            PitchType = "7 người",
            IsPublic = true
        };
        db.Matches.Add(match);

        // Pre-seed a MatchExpense split-bill for the match to verify automatic split-bill inclusion
        var expense = new MatchExpense
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            TeamId = recruitingTeam.Id,
            PitchFee = 700_000m,
            MatchTotalPayable = 700_000m,
            MatchPerPersonShare = 700_000m
        };
        expense.Shares.Add(new MatchExpenseShare
        {
            Id = Guid.NewGuid(),
            MatchExpenseId = expense.Id,
            PlayerId = capPlayer.Id,
            LinkedUserId = captain.Id,
            ParticipatesInMatch = true,
            MatchAmount = 700_000m,
            TotalAmount = 700_000m
        });
        db.MatchExpenses.Add(expense);

        await db.SaveChangesAsync();
        return (recruitingTeam, ringerHomeTeam, captain, ringerUser, ringerPlayer, match);
    }

    [Theory]
    [InlineData(LoanFeeType.SplitPitch, 0, true)]
    [InlineData(LoanFeeType.SplitPitch, 50_000, false)]
    [InlineData(LoanFeeType.PaidMercenary, 0, false)]
    [InlineData(LoanFeeType.PaidMercenary, 150_000, true)]
    [InlineData(LoanFeeType.PaidMercenary, 1_000_000, true)]
    [InlineData(LoanFeeType.PaidMercenary, 1_000_001, false)]
    [InlineData(LoanFeeType.PaidMercenary, -10_000, false)]
    public void ValidateLoanFee_EnforcesZeroToOneMillionVndRules(
        LoanFeeType feeType, decimal amount, bool expectedValid)
    {
        var (isValid, _, _) = LoanMarketService.ValidateLoanFee(feeType, amount);
        Assert.Equal(expectedValid, isValid);
    }

    [Fact]
    public async Task TeamRecruitingFlow_LocksAbCoinEscrow_AddsGuestToMatchRosterAndSplitBill_AndReleasesEscrowOnCompletion()
    {
        using var db = CreateDb();
        var (recruitingTeam, _, captain, ringerUser, _, match) = await SeedLoanScenarioAsync(db, initialAbCoin: 12);
        var auth = new TeamAuthorizationService(db);
        var service = new LoanMarketService(db, auth);

        // 1. Captain creates TeamRecruiting listing requiring 5 AB Coin escrow
        var createRes = await service.CreateListingAsync(new CreateLoanListingInput
        {
            ListingType = LoanListingType.TeamRecruiting,
            TeamId = recruitingTeam.Id,
            MatchId = match.Id,
            Region = "Cầu Giấy",
            PitchName = "Sân bóng Thành Phát",
            PlayDate = match.MatchDateTime.Date,
            TimeSlot = "19:00 - 20:30",
            Positions = ["FWD", "MID"],
            FeeType = LoanFeeType.SplitPitch,
            LoanFeeAmount = 0m,
            EscrowAbCoin = 5
        }, captain.Id);

        Assert.True(createRes.Success);
        Assert.NotNull(createRes.Listing);

        // 2. Player applies to the listing
        var applyRes = await service.ApplyToListingAsync(new ApplyLoanInput
        {
            ListingId = createRes.Listing!.Id,
            Position = "FWD",
            Message = "Em đá tiền đạo cánh trái, cam kết đúng giờ!"
        }, ringerUser.Id);

        Assert.True(applyRes.Success);
        Assert.NotNull(applyRes.Application);

        // 3. Captain approves the application -> locks deal, holds 5 AB Coin, adds guest to match & split-bill
        var acceptRes = await service.AcceptApplicationAsync(createRes.Listing.Id, applyRes.Application!.Id, captain.Id);
        Assert.True(acceptRes.Success);
        Assert.Equal(LoanListingStatus.Matched, acceptRes.Listing!.Status);
        Assert.NotNull(acceptRes.Listing.LoanedGuestPlayerId);

        // Verify AB Coin escrow held: 12 - 5 = 7 AB Coin available
        int abDuringHold = await service.GetUserAvailableAbCoinAsync(ringerUser.Id);
        Assert.Equal(7, abDuringHold);

        // Verify guest player added to MatchPlayerLineups, AttendanceRecords, and MatchExpenseShares
        bool inLineup = await db.MatchPlayerLineups.AnyAsync(l => l.MatchId == match.Id && l.PlayerId == acceptRes.Listing.LoanedGuestPlayerId);
        bool inSplitBill = await db.MatchExpenseShares.AnyAsync(s => s.PlayerId == acceptRes.Listing.LoanedGuestPlayerId && s.ParticipatesInMatch);
        Assert.True(inLineup);
        Assert.True(inSplitBill);

        // 4. Complete Matchday -> automatically releases 5 AB Coin back to ringer (7 + 5 = 12)
        await service.CompleteLoansForMatchAsync(match.Id);

        int abAfterRelease = await service.GetUserAvailableAbCoinAsync(ringerUser.Id);
        Assert.Equal(12, abAfterRelease);

        var updatedListing = await db.PlayerLoanListings.FindAsync(createRes.Listing.Id);
        Assert.Equal(LoanListingStatus.Completed, updatedListing!.Status);
        Assert.True(updatedListing.IsEscrowReleased);
    }

    [Fact]
    public async Task PlayerAvailableFlow_CaptainDirectRecruit_ScoutMetricsAndZaloMessageWorkCleanly()
    {
        using var db = CreateDb();
        var (recruitingTeam, _, captain, ringerUser, _, match) = await SeedLoanScenarioAsync(db, initialAbCoin: 20);
        var auth = new TeamAuthorizationService(db);
        var service = new LoanMarketService(db, auth);

        // 1. Player posts PlayerAvailable listing with PaidMercenary 200.000đ and 4 AB Coin escrow
        var createRes = await service.CreateListingAsync(new CreateLoanListingInput
        {
            ListingType = LoanListingType.PlayerAvailable,
            Region = "Cầu Giấy",
            PlayDate = match.MatchDateTime.Date,
            TimeSlot = "19:00 - 20:30",
            Positions = ["FWD"],
            FeeType = LoanFeeType.PaidMercenary,
            LoanFeeAmount = 200_000m,
            EscrowAbCoin = 4,
            Notes = "Sẵn sàng đá tiền đạo cắm hoặc cánh phải"
        }, ringerUser.Id);

        Assert.True(createRes.Success);

        // 2. Check Marketplace UI ViewModel & Scout Profile (OVR, PlayStyles, PreferredFoot, Reputation)
        var vmBefore = await service.BuildMarketplaceAsync("players", "Cầu Giấy", null, "FWD", "paid", captain.Id, "https://aloball.vn/LoanMarket");
        Assert.Single(vmBefore.PlayerAvailableListings);
        var card = vmBefore.PlayerAvailableListings[0];
        Assert.NotNull(card.PlayerScout);
        Assert.InRange(card.PlayerScout!.Ovr, 55, 99);
        Assert.Equal("Trái", card.PlayerScout.PreferredFoot);
        Assert.Contains(card.PlayerScout.PlayStyles, ps => ps.Definition.Code == "FINESSE" && ps.IsPlus);

        // 3. Captain 1-Tap Direct Recruits the player into the match
        var recruitRes = await service.DirectRecruitAvailablePlayerAsync(
            card.Id, recruitingTeam.Id, match.Id, "FWD", captain.Id);

        Assert.True(recruitRes.Success);
        Assert.Equal(LoanListingStatus.Matched, recruitRes.Listing!.Status);
        Assert.Equal(16, await service.GetUserAvailableAbCoinAsync(ringerUser.Id));

        // 4. Verify Zalo confirmation message & manual Complete release
        var vmAfter = await service.BuildMarketplaceAsync("players", null, null, null, "all", captain.Id, "https://aloball.vn/LoanMarket");
        Assert.Contains("CHỐT KÈO MƯỢN CẦU THỦ", vmAfter.PlayerAvailableListings[0].ZaloShareMessage);

        var completeRes = await service.CompleteLoanListingAsync(card.Id, captain.Id);
        Assert.True(completeRes.Success);
        Assert.Equal(20, await service.GetUserAvailableAbCoinAsync(ringerUser.Id));
    }

    [Fact]
    public async Task EscrowValidation_RejectsWhenPlayerLacksSufficientAbCoin()
    {
        using var db = CreateDb();
        var (_, _, _, ringerUser, _, match) = await SeedLoanScenarioAsync(db, initialAbCoin: 2);
        var auth = new TeamAuthorizationService(db);
        var service = new LoanMarketService(db, auth);

        var res = await service.CreateListingAsync(new CreateLoanListingInput
        {
            ListingType = LoanListingType.PlayerAvailable,
            Region = "Đống Đa",
            PlayDate = match.MatchDateTime.Date,
            TimeSlot = "19:00 - 20:30",
            Positions = ["MID"],
            FeeType = LoanFeeType.SplitPitch,
            LoanFeeAmount = 0m,
            EscrowAbCoin = 10
        }, ringerUser.Id);

        Assert.False(res.Success);
        Assert.Contains("AB Coin", res.Message);
    }
}
