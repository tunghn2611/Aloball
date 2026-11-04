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
using System.Text;
using Xunit;

namespace AloBall.Tests;

public class SmartMatchFinanceTests
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

    private async Task<(Team team, ApplicationUser manager, ApplicationUser memberUser, Player p1, Player p2, Player p3, ApplicationUser outsider, MatchGame match)>
        SeedFinanceTestDataAsync(ApplicationDbContext db)
    {
        var manager = new ApplicationUser { Id = "mgr_" + Guid.NewGuid(), UserName = "captain@aloball.vn", Email = "captain@aloball.vn", FullName = "Đội Trưởng A" };
        var memberUser = new ApplicationUser { Id = "mem_" + Guid.NewGuid(), UserName = "member@aloball.vn", Email = "member@aloball.vn", FullName = "Cầu Thủ B" };
        var outsider = new ApplicationUser { Id = "out_" + Guid.NewGuid(), UserName = "outsider@aloball.vn", Email = "outsider@aloball.vn", FullName = "Người Ngoài" };

        db.Users.AddRange(manager, memberUser, outsider);

        var team = new Team { Id = Guid.NewGuid(), Name = "Phoenix United FC", Slug = "phoenix-united-fc", IsPublic = true };
        db.Teams.Add(team);

        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = manager.Id, Role = TeamRole.Manager });
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = memberUser.Id, Role = TeamRole.Member });

        var p1 = new Player { Id = Guid.NewGuid(), TeamId = team.Id, FullName = "Nguyễn Văn Một", KitNumber = 7, LinkedUserId = manager.Id, IsActive = true };
        var p2 = new Player { Id = Guid.NewGuid(), TeamId = team.Id, FullName = "Trần Văn Hai", KitNumber = 9, LinkedUserId = memberUser.Id, IsActive = true };
        var p3 = new Player { Id = Guid.NewGuid(), TeamId = team.Id, FullName = "Lê Văn Ba", KitNumber = 10, IsActive = true };

        db.Players.AddRange(p1, p2, p3);

        var match = new MatchGame
        {
            Id = Guid.NewGuid(),
            TeamAId = team.Id,
            MatchDateTime = DateTime.UtcNow.AddDays(1),
            PitchName = "Sân bóng Chuyên Việt",
            PitchType = "7 người",
            Status = MatchStatus.Scheduled,
            IsPublic = true
        };
        db.Matches.Add(match);

        await db.SaveChangesAsync();

        return (team, manager, memberUser, p1, p2, p3, outsider, match);
    }

    [Fact]
    public async Task CostForecast_RetrievesConfirmedOffers_IgnoresUnconfirmedOffers()
    {
        using var db = CreateInMemoryDbContext();
        var (team, manager, memberUser, p1, p2, p3, outsider, match) = await SeedFinanceTestDataAsync(db);

        // Đơn đặt sân 1: Confirmed với giá 600.000 VNĐ
        var pitchReq = new PitchBookingRequest { Id = Guid.NewGuid(), MatchId = match.Id, TeamId = team.Id, Status = PitchBookingStatus.Confirmed };
        db.PitchBookingRequests.Add(pitchReq);
        db.PitchBookingOffers.Add(new PitchBookingOffer
        {
            RequestId = pitchReq.Id,
            AdminId = "admin",
            ConfirmedVenueName = "Sân Thành Phát",
            ConfirmedAddress = "Cầu Giấy",
            TotalPriceVND = 600000m,
            Status = PitchBookingOfferStatus.Accepted
        });

        // Đơn đặt sân 2: Pending (chưa confirm) với giá 999.000 VNĐ -> Phải bị bỏ qua
        var unconfirmedReq = new PitchBookingRequest { Id = Guid.NewGuid(), MatchId = match.Id, TeamId = team.Id, Status = PitchBookingStatus.Pending };
        db.PitchBookingRequests.Add(unconfirmedReq);
        db.PitchBookingOffers.Add(new PitchBookingOffer
        {
            RequestId = unconfirmedReq.Id,
            AdminId = "admin",
            ConfirmedVenueName = "Sân khác",
            ConfirmedAddress = "Đống Đa",
            TotalPriceVND = 999000m,
            Status = PitchBookingOfferStatus.Pending
        });

        // Yêu cầu trọng tài: Confirmed với giá 300.000 VNĐ
        var refReq = new RefereeRequest { Id = Guid.NewGuid(), MatchId = match.Id, TeamId = team.Id, Status = RefereeRequestStatus.Confirmed };
        db.RefereeRequests.Add(refReq);
        db.RefereeProposals.Add(new RefereeProposal
        {
            RequestId = refReq.Id,
            AdminId = "admin",
            TotalPriceVND = 300000m,
            Status = RefereeProposalStatus.Accepted
        });

        // Khoản chi bổ sung dự toán: 150.000 VNĐ tiền nước
        db.MatchExpenseEstimates.Add(new MatchExpenseEstimate
        {
            MatchId = match.Id,
            TeamId = team.Id,
            Description = "Nước suối Aquafina",
            EstimatedAmount = 150000m,
            CreatedByUserId = manager.Id
        });

        // Buổi điểm danh với 3 người báo tham gia (positive RSVP)
        var session = new AttendanceSession { Id = Guid.NewGuid(), MatchId = match.Id, TeamId = team.Id };
        db.AttendanceSessions.Add(session);
        db.AttendanceRecords.AddRange(
            new AttendanceRecord { SessionId = session.Id, PlayerId = p1.Id, Response = ResponseStatus.Attending },
            new AttendanceRecord { SessionId = session.Id, PlayerId = p2.Id, Response = ResponseStatus.Attending },
            new AttendanceRecord { SessionId = session.Id, PlayerId = p3.Id, Response = ResponseStatus.Attending }
        );

        await db.SaveChangesAsync();

        var auth = new TeamAuthorizationService(db);
        var financeService = new FinanceService(db, auth);

        var forecast = await financeService.GetMatchCostForecastAsync(match.Id, team.Id, manager.Id);

        Assert.Equal(600000m, forecast.ConfirmedPitchCost);
        Assert.True(forecast.HasConfirmedPitch);
        Assert.Equal(300000m, forecast.ConfirmedRefereeCost);
        Assert.True(forecast.HasConfirmedReferee);
        Assert.Single(forecast.CustomEstimates);
        Assert.Equal(150000m, forecast.TotalCustomEstimates);

        // Tổng chi phí dự toán = 600.000 + 300.000 + 150.000 = 1.050.000 VNĐ
        Assert.Equal(1050000m, forecast.TotalEstimatedCost);
        Assert.Equal(3, forecast.ExpectedParticipants);
        // Mỗi người: 1.050.000 / 3 = 350.000 VNĐ
        Assert.Equal(350000m, forecast.EstimatedCostPerPerson);
    }

    [Fact]
    public async Task SettlementPreparation_UsesAuthoritativeAttendanceAndLineup_NotRSVP()
    {
        using var db = CreateInMemoryDbContext();
        var (team, manager, memberUser, p1, p2, p3, outsider, match) = await SeedFinanceTestDataAsync(db);

        // p1: Báo RSVP vắng nhưng thực tế LÃNH ĐỘI ĐIỂM DANH CÓ MẶT (Present)
        // p2: Báo RSVP có mặt (Attending) nhưng thực tế ĐIỂM DANH VẮNG (Absent)
        // p3: Không điểm danh nhưng CÓ TÊN VÀO SÂN THI ĐẤU (Lineup HasActuallyPlayed = true)
        var session = new AttendanceSession { Id = Guid.NewGuid(), MatchId = match.Id, TeamId = team.Id };
        db.AttendanceSessions.Add(session);
        db.AttendanceRecords.AddRange(
            new AttendanceRecord { SessionId = session.Id, PlayerId = p1.Id, Response = ResponseStatus.Excused, Actual = ActualStatus.Present },
            new AttendanceRecord { SessionId = session.Id, PlayerId = p2.Id, Response = ResponseStatus.Attending, Actual = ActualStatus.Absent }
        );

        db.MatchPlayerLineups.Add(new MatchPlayerLineup
        {
            MatchId = match.Id,
            TeamId = team.Id,
            PlayerId = p3.Id,
            HasActuallyPlayed = true
        });

        await db.SaveChangesAsync();

        var auth = new TeamAuthorizationService(db);
        var financeService = new FinanceService(db, auth);

        var (success, message, prep) = await financeService.GetSettlementPreparationDataAsync(match.Id, team.Id, manager.Id);

        Assert.True(success);
        Assert.NotNull(prep);

        var cand1 = prep.Candidates.First(c => c.PlayerId == p1.Id);
        var cand2 = prep.Candidates.First(c => c.PlayerId == p2.Id);
        var cand3 = prep.Candidates.First(c => c.PlayerId == p3.Id);

        // p1 có mặt thực tế -> Được chọn mặc định
        Assert.True(cand1.HasAuthoritativeAttendance);
        Assert.True(cand1.IsSelected);

        // p2 thực tế vắng (mặc dù RSVP tham gia) -> KHÔNG được tự động chọn
        Assert.False(cand2.HasAuthoritativeAttendance);
        Assert.False(cand2.IsSelected);

        // p3 có thi đấu theo biên bản -> Được chọn mặc định
        Assert.True(cand3.HasLineupAppearance);
        Assert.True(cand3.IsSelected);
    }

    [Fact]
    public async Task CreateSettlement_EnforcesVndRounding_TotalAllocationsExactlyEqualsTotalExpenses()
    {
        using var db = CreateInMemoryDbContext();
        var (team, manager, memberUser, p1, p2, p3, outsider, match) = await SeedFinanceTestDataAsync(db);

        // Tổng chi phí 850.000 VNĐ chia cho 3 người
        // 850.000 / 3 = 283.333,33 VNĐ
        // Bội số 1.000 đ: 283.000 * 3 = 849.000 đ (dư 1.000 đ). Người 1 nhận 284.000 đ, người 2 nhận 283.000 đ, người 3 nhận 283.000 đ
        // Tổng: 284.000 + 283.000 + 283.000 = đúng 850.000 đ
        decimal totalExpense = 850000m;

        var input = new CreateSettlementInput
        {
            MatchId = match.Id,
            TeamId = team.Id,
            Expenses = new List<SettlementExpenseInputItem>
            {
                new SettlementExpenseInputItem { Category = SettlementExpenseCategory.Pitch, Description = "Tiền sân", Amount = 850000m }
            },
            SelectedPlayerIds = new List<Guid> { p1.Id, p2.Id, p3.Id },
            Allocations = new List<MemberAllocationPreviewItem>(), // Để server tự tính toán làm tròn
            Notes = "Quyết toán trận giao lưu thử nghiệm"
        };

        var auth = new TeamAuthorizationService(db);
        var financeService = new FinanceService(db, auth);

        var (success, message, settlementId) = await financeService.CreateSettlementAsync(input, manager.Id);

        Assert.True(success, message);
        Assert.NotNull(settlementId);

        var savedSettlement = await db.MatchSettlements
            .Include(s => s.Allocations)
            .FirstOrDefaultAsync(s => s.Id == settlementId.Value);

        Assert.NotNull(savedSettlement);
        Assert.Equal(850000m, savedSettlement.TotalAmount);
        Assert.Equal(3, savedSettlement.ParticipantCount);

        decimal sumAllocated = savedSettlement.Allocations.Sum(a => a.AdjustedAmount);
        Assert.Equal(totalExpense, sumAllocated); // Khớp từng đồng, không chênh lệch

        // Kiểm tra phân bổ chi tiết
        var allocs = savedSettlement.Allocations.OrderByDescending(a => a.AdjustedAmount).ToList();
        Assert.Equal(284000m, allocs[0].AdjustedAmount);
        Assert.Equal(283000m, allocs[1].AdjustedAmount);
        Assert.Equal(283000m, allocs[2].AdjustedAmount);
    }

    [Fact]
    public async Task CreateSettlement_PreventsDuplicateSettlementForTheSameMatch()
    {
        using var db = CreateInMemoryDbContext();
        var (team, manager, memberUser, p1, p2, p3, outsider, match) = await SeedFinanceTestDataAsync(db);

        var input = new CreateSettlementInput
        {
            MatchId = match.Id,
            TeamId = team.Id,
            Expenses = new List<SettlementExpenseInputItem>
            {
                new SettlementExpenseInputItem { Category = SettlementExpenseCategory.Pitch, Description = "Tiền sân", Amount = 600000m }
            },
            SelectedPlayerIds = new List<Guid> { p1.Id, p2.Id },
            Notes = "Bản quyết toán đầu tiên"
        };

        var auth = new TeamAuthorizationService(db);
        var financeService = new FinanceService(db, auth);

        // Lần 1: Thành công
        var (success1, _, _) = await financeService.CreateSettlementAsync(input, manager.Id);
        Assert.True(success1);

        // Lần 2: Cố tình tạo lại quyết toán cho cùng trận đấu -> Bị chặn
        var (success2, message2, _) = await financeService.CreateSettlementAsync(input, manager.Id);
        Assert.False(success2);
        Assert.Contains("trùng lặp", message2.ToLowerInvariant());
    }

    [Fact]
    public async Task PaymentTracking_RecordsPayments_UpdatesStatusAndRemainingBalance()
    {
        using var db = CreateInMemoryDbContext();
        var (team, manager, memberUser, p1, p2, p3, outsider, match) = await SeedFinanceTestDataAsync(db);

        var auth = new TeamAuthorizationService(db);
        var financeService = new FinanceService(db, auth);

        // Tạo quyết toán 600.000 VNĐ cho 2 người (Mỗi người 300.000 VNĐ)
        var input = new CreateSettlementInput
        {
            MatchId = match.Id,
            TeamId = team.Id,
            Expenses = new List<SettlementExpenseInputItem>
            {
                new SettlementExpenseInputItem { Category = SettlementExpenseCategory.Pitch, Description = "Tiền sân", Amount = 600000m }
            },
            SelectedPlayerIds = new List<Guid> { p1.Id, p2.Id }
        };

        var (_, _, settlementId) = await financeService.CreateSettlementAsync(input, manager.Id);

        var allocP1 = await db.MatchSettlementMemberAllocations.FirstAsync(a => a.SettlementId == settlementId && a.PlayerId == p1.Id);
        Assert.Equal(PaymentStatus.Unpaid, allocP1.PaymentStatus);
        Assert.Equal(300000m, allocP1.RemainingBalance);

        // 1. Nộp một phần: 100.000 VNĐ
        var payInput1 = new RecordPaymentInput
        {
            AllocationId = allocP1.Id,
            SettlementId = settlementId!.Value,
            Amount = 100000m,
            PaymentMethod = PaymentMethod.BankTransfer,
            ReferenceCode = "FT123456",
            Note = "Chuyển khoản techcombank"
        };
        var (pSuccess1, _) = await financeService.RecordPaymentAsync(payInput1, manager.Id);
        Assert.True(pSuccess1);

        allocP1 = await db.MatchSettlementMemberAllocations.FirstAsync(a => a.Id == allocP1.Id);
        Assert.Equal(PaymentStatus.PartiallyPaid, allocP1.PaymentStatus);
        Assert.Equal(100000m, allocP1.AmountPaid);
        Assert.Equal(200000m, allocP1.RemainingBalance);

        // 2. Cố tình nộp vượt số tiền còn thiếu (250.000 > 200.000) -> Bị chặn
        var payInputInvalid = new RecordPaymentInput
        {
            AllocationId = allocP1.Id,
            SettlementId = settlementId.Value,
            Amount = 250000m
        };
        var (invalidSuccess, invalidMsg) = await financeService.RecordPaymentAsync(payInputInvalid, manager.Id);
        Assert.False(invalidSuccess);
        Assert.Contains("vượt quá", invalidMsg.ToLowerInvariant());

        // 3. Nộp nốt phần còn lại: 200.000 VNĐ -> Trở thành Paid
        var payInput2 = new RecordPaymentInput
        {
            AllocationId = allocP1.Id,
            SettlementId = settlementId.Value,
            Amount = 200000m,
            PaymentMethod = PaymentMethod.Cash
        };
        var (pSuccess2, _) = await financeService.RecordPaymentAsync(payInput2, manager.Id);
        Assert.True(pSuccess2);

        allocP1 = await db.MatchSettlementMemberAllocations.FirstAsync(a => a.Id == allocP1.Id);
        Assert.Equal(PaymentStatus.Paid, allocP1.PaymentStatus);
        Assert.Equal(300000m, allocP1.AmountPaid);
        Assert.Equal(0m, allocP1.RemainingBalance);
    }

    [Fact]
    public async Task PaymentReversal_MaintainsAuditTrail_RevertsStatusCorrectly()
    {
        using var db = CreateInMemoryDbContext();
        var (team, manager, memberUser, p1, p2, p3, outsider, match) = await SeedFinanceTestDataAsync(db);

        var auth = new TeamAuthorizationService(db);
        var financeService = new FinanceService(db, auth);

        var input = new CreateSettlementInput
        {
            MatchId = match.Id,
            TeamId = team.Id,
            Expenses = new List<SettlementExpenseInputItem> { new SettlementExpenseInputItem { Category = SettlementExpenseCategory.Pitch, Description = "Tiền sân", Amount = 400000m } },
            SelectedPlayerIds = new List<Guid> { p1.Id }
        };
        var (_, _, settlementId) = await financeService.CreateSettlementAsync(input, manager.Id);
        var alloc = await db.MatchSettlementMemberAllocations.FirstAsync(a => a.SettlementId == settlementId);

        // Nộp 400.000 VNĐ -> Paid
        await financeService.RecordPaymentAsync(new RecordPaymentInput
        {
            AllocationId = alloc.Id,
            SettlementId = settlementId!.Value,
            Amount = 400000m,
            PaymentMethod = PaymentMethod.BankTransfer
        }, manager.Id);

        var payment = await db.MatchSettlementPayments.FirstAsync(p => p.AllocationId == alloc.Id);

        // Đảo khoản thu với lý do bắt buộc
        var (revSuccess, revMsg) = await financeService.ReversePaymentAsync(payment.Id, team.Id, "Chuyển nhầm hóa đơn trận khác", manager.Id);
        Assert.True(revSuccess, revMsg);

        // Bản ghi payment không bị xóa mà có cờ IsReversed và lưu vết
        var reversedPayment = await db.MatchSettlementPayments.FindAsync(payment.Id);
        Assert.True(reversedPayment!.IsReversed);
        Assert.Equal("Chuyển nhầm hóa đơn trận khác", reversedPayment.ReversalReason);
        Assert.Equal(manager.Id, reversedPayment.ReversedByUserId);

        // Allocation hoàn về trạng thái Unpaid
        var updatedAlloc = await db.MatchSettlementMemberAllocations.FindAsync(alloc.Id);
        Assert.Equal(0m, updatedAlloc!.AmountPaid);
        Assert.Equal(PaymentStatus.Unpaid, updatedAlloc.PaymentStatus);
        Assert.Equal(400000m, updatedAlloc.RemainingBalance);
    }

    [Fact]
    public async Task ManualAdjustment_RequiresMandatoryReason_AndLogsAuditHistory()
    {
        using var db = CreateInMemoryDbContext();
        var (team, manager, memberUser, p1, p2, p3, outsider, match) = await SeedFinanceTestDataAsync(db);

        var auth = new TeamAuthorizationService(db);
        var financeService = new FinanceService(db, auth);

        var input = new CreateSettlementInput
        {
            MatchId = match.Id,
            TeamId = team.Id,
            Expenses = new List<SettlementExpenseInputItem> { new SettlementExpenseInputItem { Category = SettlementExpenseCategory.Pitch, Description = "Tiền sân", Amount = 200000m } },
            SelectedPlayerIds = new List<Guid> { p1.Id }
        };
        var (_, _, settlementId) = await financeService.CreateSettlementAsync(input, manager.Id);
        var alloc = await db.MatchSettlementMemberAllocations.FirstAsync(a => a.SettlementId == settlementId);

        // Cố tình điều chỉnh không có lý do -> Bị từ chối
        var noReasonInput = new ManualAdjustmentInput
        {
            AllocationId = alloc.Id,
            SettlementId = settlementId!.Value,
            NewAmount = 150000m,
            MandatoryReason = ""
        };
        var (failResult, failMsg) = await financeService.AdjustAllocationAsync(noReasonInput, manager.Id);
        Assert.False(failResult);

        // Điều chỉnh có lý do hợp lệ: Giảm 50.000 đ do chấn thương ra sớm
        var validAdjustment = new ManualAdjustmentInput
        {
            AllocationId = alloc.Id,
            SettlementId = settlementId.Value,
            NewAmount = 150000m,
            MandatoryReason = "Cầu thủ chấn thương phút thứ 10"
        };
        var (adjustSuccess, _) = await financeService.AdjustAllocationAsync(validAdjustment, manager.Id);
        Assert.True(adjustSuccess);

        var updatedAlloc = await db.MatchSettlementMemberAllocations
            .Include(a => a.AdjustmentAudits)
            .FirstAsync(a => a.Id == alloc.Id);

        Assert.Equal(150000m, updatedAlloc.AdjustedAmount);
        Assert.Equal("Cầu thủ chấn thương phút thứ 10", updatedAlloc.AdjustmentReason);
        Assert.Single(updatedAlloc.AdjustmentAudits);
        Assert.Equal(200000m, updatedAlloc.AdjustmentAudits.First().PreviousAmount);
        Assert.Equal(150000m, updatedAlloc.AdjustmentAudits.First().NewAmount);
    }

    [Fact]
    public async Task Security_EnforcesRoleAccess_AndProtectsMemberPrivacy()
    {
        using var db = CreateInMemoryDbContext();
        var (team, manager, memberUser, p1, p2, p3, outsider, match) = await SeedFinanceTestDataAsync(db);

        var auth = new TeamAuthorizationService(db);
        var financeService = new FinanceService(db, auth);
        var userManager = CreateMockUserManager(db);
        var controller = new FinanceController(db, financeService, auth, userManager);

        // 1. Người ngoài (outsider) cố tạo quyết toán -> 403 Forbid
        SetUserContext(controller, outsider);
        var forbidCreate = await controller.CreateSettlement(match.Id, team.Id);
        Assert.IsType<ForbidResult>(forbidCreate);

        // Lãnh đội tạo quyết toán cho p1 (manager) và p2 (memberUser)
        var input = new CreateSettlementInput
        {
            MatchId = match.Id,
            TeamId = team.Id,
            Expenses = new List<SettlementExpenseInputItem> { new SettlementExpenseInputItem { Category = SettlementExpenseCategory.Pitch, Description = "Tiền sân", Amount = 600000m } },
            SelectedPlayerIds = new List<Guid> { p1.Id, p2.Id }
        };
        var (_, _, settlementId) = await financeService.CreateSettlementAsync(input, manager.Id);

        // 2. Thành viên thông thường xem chi tiết quyết toán -> Chỉ thấy bản phân bổ của chính mình
        var memberDetail = await financeService.GetSettlementDetailAsync(match.Id, team.Id, memberUser.Id);
        Assert.NotNull(memberDetail);
        Assert.False(memberDetail.IsManager);
        Assert.Single(memberDetail.Allocations); // Chỉ xem được allocation của chính mình (p2)
        Assert.Equal(p2.Id, memberDetail.Allocations.First().PlayerId);

        // 3. Lãnh đội xem -> Thấy toàn bộ danh sách phân bổ của cả đội
        var managerDetail = await financeService.GetSettlementDetailAsync(match.Id, team.Id, manager.Id);
        Assert.NotNull(managerDetail);
        Assert.True(managerDetail.IsManager);
        Assert.Equal(2, managerDetail.Allocations.Count);
    }

    [Fact]
    public async Task TeamFinanceReport_ComputesTotalsAndExportsValidCsv()
    {
        using var db = CreateInMemoryDbContext();
        var (team, manager, memberUser, p1, p2, p3, outsider, match) = await SeedFinanceTestDataAsync(db);

        var auth = new TeamAuthorizationService(db);
        var financeService = new FinanceService(db, auth);

        var input = new CreateSettlementInput
        {
            MatchId = match.Id,
            TeamId = team.Id,
            Expenses = new List<SettlementExpenseInputItem> { new SettlementExpenseInputItem { Category = SettlementExpenseCategory.Pitch, Description = "Tiền sân", Amount = 600000m } },
            SelectedPlayerIds = new List<Guid> { p1.Id, p2.Id }
        };
        var (_, _, settlementId) = await financeService.CreateSettlementAsync(input, manager.Id);

        var alloc = await db.MatchSettlementMemberAllocations.FirstAsync(a => a.SettlementId == settlementId && a.PlayerId == p1.Id);
        await financeService.RecordPaymentAsync(new RecordPaymentInput
        {
            AllocationId = alloc.Id,
            SettlementId = settlementId!.Value,
            Amount = 300000m
        }, manager.Id);

        // Báo cáo tài chính
        var report = await financeService.GetTeamFinanceReportAsync(team.Id, null, manager.Id);
        Assert.Equal(600000m, report.TotalMatchExpenses);
        Assert.Equal(600000m, report.TotalMemberContributionsDue);
        Assert.Equal(300000m, report.TotalCollected);
        Assert.Equal(300000m, report.TotalOutstanding);
        Assert.Equal(50.0, report.CollectionRatePercentage);
        Assert.Equal(1, report.TotalSettledMatches);

        // Xuất file CSV
        var csvBytes = await financeService.ExportTeamFinanceReportCsvAsync(team.Id, null, manager.Id);
        Assert.NotNull(csvBytes);
        Assert.True(csvBytes.Length > 0);

        string csvContent = Encoding.UTF8.GetString(csvBytes);
        Assert.Contains("BÁO CÁO TÀI CHÍNH", csvContent);
        Assert.Contains("600000", csvContent);
        Assert.Contains(p1.FullName, csvContent);
    }
}
