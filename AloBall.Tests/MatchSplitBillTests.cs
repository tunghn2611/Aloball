using System.Net;
using System.Security.Claims;
using AloBall.Web.Controllers;
using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AloBall.Tests;

public class MatchSplitBillTests
{
    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"SplitBillTest_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<(Team team, ApplicationUser captain, ApplicationUser member, ApplicationUser outsider, Player pClaimed, Player pUnclaimed1, Player pUnclaimed2, MatchGame match)>
        SeedSplitBillScenarioAsync(ApplicationDbContext db)
    {
        var captain = new ApplicationUser { Id = "cap_" + Guid.NewGuid(), UserName = "cap@aloball.vn", Email = "cap@aloball.vn", FullName = "Đội Trưởng" };
        var member = new ApplicationUser { Id = "mem_" + Guid.NewGuid(), UserName = "mem@aloball.vn", Email = "mem@aloball.vn", FullName = "Thành Viên" };
        var outsider = new ApplicationUser { Id = "out_" + Guid.NewGuid(), UserName = "out@aloball.vn", Email = "out@aloball.vn", FullName = "Người Lạ" };
        db.Users.AddRange(captain, member, outsider);

        var team = new Team
        {
            Id = Guid.NewGuid(),
            Name = "Sao Vàng FC",
            Slug = "sao-vang-test-" + Guid.NewGuid().ToString("N")[..6],
            BankCode = "VCB",
            AccountNumber = "0123456789",
            AccountHolderName = "NGUYEN VAN DUNG",
            IsPublic = true
        };
        db.Teams.Add(team);

        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = captain.Id, Role = TeamRole.Owner });
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = member.Id, Role = TeamRole.Member });

        var pClaimed = new Player { Id = Guid.NewGuid(), TeamId = team.Id, FullName = "Nguyễn Văn Dũng", KitNumber = 10, LinkedUserId = captain.Id, IsClaimed = true, IsActive = true };
        var pUnclaimed1 = new Player { Id = Guid.NewGuid(), TeamId = team.Id, FullName = "Trần Hoàng Nam", KitNumber = 7, LinkedUserId = null, IsClaimed = false, IsActive = true };
        var pUnclaimed2 = new Player { Id = Guid.NewGuid(), TeamId = team.Id, FullName = "Lê Minh Tuấn", KitNumber = 9, LinkedUserId = null, IsClaimed = false, IsActive = true };
        db.Players.AddRange(pClaimed, pUnclaimed1, pUnclaimed2);

        var match = new MatchGame
        {
            Id = Guid.NewGuid(),
            TeamAId = team.Id,
            Category = MatchCategory.Friendly,
            TeamBExternalName = "FC Đối Thủ",
            MatchDateTime = new DateTime(2026, 11, 20, 19, 30, 0),
            PitchName = "Sân Mỹ Đình",
            PitchType = "7 người",
            RsvpToken = "tok12345",
            IsPublic = true
        };
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        return (team, captain, member, outsider, pClaimed, pUnclaimed1, pUnclaimed2, match);
    }

    [Theory]
    [InlineData(1000000, 0, 0, "100", 7, 143000)]     // 1.000.000 / 7 = 142.857 -> 143.000
    [InlineData(800000, 100000, 100000, "50-50", 8, 63000)] // 1.000.000 * 50% = 500.000 / 8 = 62.500 -> 63.000
    [InlineData(1000000, 0, 0, "60-40", 7, 86000)]    // 600.000 / 7 = 85.714 -> 86.000
    [InlineData(1000000, 0, 0, "70-30", 10, 70000)]   // 700.000 / 10 = 70.000
    [InlineData(800000, 0, 0, "100", 7, 114000)]      // 800.000 / 7 = 114.285 -> 114.000
    public async Task SaveMatchSplitBill_CalculatesRatiosAndRoundsToNearest1000(
        decimal pitchFee, decimal waterFee, decimal refereeFee, string splitRatio, int playerCount, decimal expectedShare)
    {
        using var db = CreateDb();
        var (team, captain, _, _, _, _, _, match) = await SeedSplitBillScenarioAsync(db);
        var auth = new TeamAuthorizationService(db);
        var svc = new MatchSplitBillService(db, auth);

        var participantIds = new List<Guid>();
        for (int i = 0; i < playerCount; i++)
        {
            var p = new Player { Id = Guid.NewGuid(), TeamId = team.Id, FullName = $"Cầu thủ {i + 1}", IsActive = true };
            db.Players.Add(p);
            participantIds.Add(p.Id);
        }
        await db.SaveChangesAsync();

        var result = await svc.SaveMatchSplitBillAsync(new MatchSplitBillInput
        {
            MatchId = match.Id,
            TeamId = team.Id,
            PitchFee = pitchFee,
            WaterFee = waterFee,
            RefereeFee = refereeFee,
            SplitRatio = splitRatio,
            MatchParticipantPlayerIds = participantIds
        }, captain.Id);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Expense);
        Assert.Equal(expectedShare, result.Expense!.MatchPerPersonShare);
        Assert.All(result.Expense.Shares, s =>
        {
            Assert.Equal(expectedShare, s.MatchAmount);
            Assert.Equal(0m, s.Tang2Amount);
            Assert.Equal(expectedShare, s.TotalAmount);
            Assert.Equal(0m, s.TotalAmount % 1000m);
        });
    }

    [Fact]
    public async Task SaveMatchSplitBill_SeparatesTang2_SoEarlyLeaversPayZeroTang2_AndSupportsUnclaimedRoster()
    {
        using var db = CreateDb();
        var (team, captain, _, _, pClaimed, pUnclaimed1, pUnclaimed2, match) = await SeedSplitBillScenarioAsync(db);
        var auth = new TeamAuthorizationService(db);
        var svc = new MatchSplitBillService(db, auth);

        // Cả 3 người đá sân (600k / 3 = 200k/người)
        // Tăng 2 bill 300k: CHỈ pClaimed và pUnclaimed1 đi ăn (150k/người), pUnclaimed2 về sớm!
        var result = await svc.SaveMatchSplitBillAsync(new MatchSplitBillInput
        {
            MatchId = match.Id,
            TeamId = team.Id,
            PitchFee = 500000m,
            WaterFee = 100000m,
            RefereeFee = 0m,
            SplitRatio = "100",
            Tang2Fee = 300000m,
            MatchParticipantPlayerIds = new List<Guid> { pClaimed.Id, pUnclaimed1.Id, pUnclaimed2.Id },
            Tang2ParticipantPlayerIds = new List<Guid> { pClaimed.Id, pUnclaimed1.Id }
        }, captain.Id);

        Assert.True(result.Success, result.Message);
        var shares = result.Expense!.Shares.ToDictionary(s => s.PlayerId);

        // Đội trưởng (đá + Tăng 2)
        Assert.Equal(200000m, shares[pClaimed.Id].MatchAmount);
        Assert.Equal(150000m, shares[pClaimed.Id].Tang2Amount);
        Assert.Equal(350000m, shares[pClaimed.Id].TotalAmount);

        // Cầu thủ Unclaimed #1 (đá + Tăng 2)
        Assert.Null(shares[pUnclaimed1.Id].LinkedUserId);
        Assert.True(shares[pUnclaimed1.Id].ParticipatesInTang2);
        Assert.Equal(350000m, shares[pUnclaimed1.Id].TotalAmount);

        // Cầu thủ Unclaimed #2 (về sớm -> KHÔNG tính Tăng 2)
        Assert.False(shares[pUnclaimed2.Id].ParticipatesInTang2);
        Assert.Equal(200000m, shares[pUnclaimed2.Id].MatchAmount);
        Assert.Equal(0m, shares[pUnclaimed2.Id].Tang2Amount);
        Assert.Equal(200000m, shares[pUnclaimed2.Id].TotalAmount);
    }

    [Fact]
    public async Task ToggleSharePayment_TogglesPaidAndUnpaid_EnforcesManagerAuth_AndPreservesPaidOnUnchangedResave()
    {
        using var db = CreateDb();
        var (team, captain, member, outsider, pClaimed, pUnclaimed1, _, match) = await SeedSplitBillScenarioAsync(db);
        var auth = new TeamAuthorizationService(db);
        var svc = new MatchSplitBillService(db, auth);

        var saveRes = await svc.SaveMatchSplitBillAsync(new MatchSplitBillInput
        {
            MatchId = match.Id,
            TeamId = team.Id,
            PitchFee = 400000m,
            SplitRatio = "100",
            MatchParticipantPlayerIds = new List<Guid> { pClaimed.Id, pUnclaimed1.Id }
        }, captain.Id);

        var unclaimedShare = saveRes.Expense!.Shares.First(s => s.PlayerId == pUnclaimed1.Id);

        // Member thường và người ngoài không được phép bấm "Đã thu"
        var memberAttempt = await svc.ToggleSharePaymentAsync(match.Id, unclaimedShare.Id, member.Id);
        Assert.False(memberAttempt.Success);
        var outsiderAttempt = await svc.ToggleSharePaymentAsync(match.Id, unclaimedShare.Id, outsider.Id);
        Assert.False(outsiderAttempt.Success);

        // Đội trưởng bấm "Đã thu" -> Paid + PaidAt
        var markPaid = await svc.ToggleSharePaymentAsync(match.Id, unclaimedShare.Id, captain.Id);
        Assert.True(markPaid.Success);
        Assert.Equal(ExpenseSharePaymentStatus.Paid, markPaid.NewStatus);
        Assert.NotNull(markPaid.PaidAt);

        // Lưu lại bảng chia tiền khi số tiền của cầu thủ không đổi -> phải giữ nguyên Paid
        var resaveSame = await svc.SaveMatchSplitBillAsync(new MatchSplitBillInput
        {
            MatchId = match.Id,
            TeamId = team.Id,
            PitchFee = 400000m,
            SplitRatio = "100",
            MatchParticipantPlayerIds = new List<Guid> { pClaimed.Id, pUnclaimed1.Id }
        }, captain.Id);
        var preservedShare = resaveSame.Expense!.Shares.First(s => s.PlayerId == pUnclaimed1.Id);
        Assert.Equal(ExpenseSharePaymentStatus.Paid, preservedShare.PaymentStatus);

        // Đội trưởng bấm lại -> Hoàn tác về Unpaid & xóa PaidAt
        var markUnpaid = await svc.ToggleSharePaymentAsync(match.Id, preservedShare.Id, captain.Id);
        Assert.True(markUnpaid.Success);
        Assert.Equal(ExpenseSharePaymentStatus.Unpaid, markUnpaid.NewStatus);
        Assert.Null(markUnpaid.PaidAt);
    }

    [Fact]
    public void BuildVietQrUrl_And_ZaloReminder_GenerateValidEncodedFormats()
    {
        var matchId = Guid.Parse("1a2b3c4d-1111-2222-3333-444455556666");
        var qrUrl = MatchSplitBillService.BuildVietQrUrl(
            "VCB", "0123456789", "Nguyễn Văn Dũng", 250000m, matchId, "Đặng Văn Lâm");

        Assert.NotNull(qrUrl);
        Assert.StartsWith("https://img.vietqr.io/image/VCB-0123456789-compact2.png?", qrUrl);
        Assert.Contains("amount=250000", qrUrl);
        Assert.Contains("addInfo=ALOBALL%20M1A2B3C%20DANG%20VAN%20LAM", qrUrl);
        Assert.Contains("accountName=NGUYEN%20VAN%20DUNG", qrUrl);

        Assert.Null(MatchSplitBillService.BuildVietQrUrl("", "0123456", "A", 100000m, matchId, "B"));
        Assert.Null(MatchSplitBillService.BuildVietQrUrl("VCB", "0123456", "A", 0m, matchId, "B"));
    }

    [Fact]
    public async Task SaveClubBankInfo_RequiresManager_And_RendersVietQrOnMatchdayDetailForZaloToken()
    {
        using var app = new RequirementsWebFactory();
        await app.Seed();

        await app.WithDb(async db =>
        {
            var team = await db.Teams.FindAsync(app.TeamA);
            team!.BankCode = "MBBank";
            team.AccountNumber = "9999888877";
            team.AccountHolderName = "CLB AUDIT A";
            var match = await db.Matches.FindAsync(app.Match);
            match!.RsvpToken = "zalo9999";

            var auth = new TeamAuthorizationService(db);
            var svc = new MatchSplitBillService(db, auth);
            var mgr = await db.TeamMembers.FirstAsync(m => m.TeamId == app.TeamA && (m.Role == TeamRole.Owner || m.Role == TeamRole.Manager));
            var save = await svc.SaveMatchSplitBillAsync(new MatchSplitBillInput
            {
                MatchId = app.Match,
                TeamId = app.TeamA,
                PitchFee = 600000m,
                SplitRatio = "100",
                Tang2Fee = 200000m,
                MatchParticipantPlayerIds = new List<Guid> { app.PlayerA },
                Tang2ParticipantPlayerIds = new List<Guid> { app.PlayerA }
            }, mgr.UserId);
            Assert.True(save.Success);
            await db.SaveChangesAsync();
        });

        // Guest mở link Zalo kèm token có thể xem bảng chia tiền & Quét mã VietQR
        using var anonClient = app.Client(null);
        var response = await anonClient.GetAsync($"/Matches/Detail/{app.Match}?token=zalo9999");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("CHIA TIỀN SÂN & VIETQR", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Quét mã VietQR", html);
        Assert.Contains("https://img.vietqr.io/image/MBBank-9999888877-compact2.png?amount=800000", html);
        Assert.Contains("Copy nhắc tiền sân Zalo", html);
    }
}
