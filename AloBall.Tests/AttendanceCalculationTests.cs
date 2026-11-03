using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AloBall.Tests;

public class AttendanceCalculationTests
{
    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public void Attending_And_Absent_Should_Fine_50000()
    {
        using var db = CreateInMemoryDbContext();
        var engine = new AttendanceCalculationEngine(db);

        var reg = new TeamRegulation
        {
            FineNoResponse = 30000m,
            FineAttendingButAbsent = 50000m,
            IsExcusedExemptFromFine = true
        };

        var record = new AttendanceRecord
        {
            Response = ResponseStatus.Attending,
            Actual = ActualStatus.Absent,
            IsExempt = false
        };

        var result = engine.CalculatePenalty(record, reg, isBtc: false);

        Assert.Equal(50000m, result.FineAmount);
        Assert.False(result.NeedsConfirmation);
        Assert.Contains("Xác nhận tham gia nhưng vắng mặt", result.PenaltyReason);
    }

    [Fact]
    public void NoResponse_Should_Fine_30000()
    {
        using var db = CreateInMemoryDbContext();
        var engine = new AttendanceCalculationEngine(db);

        var reg = new TeamRegulation
        {
            FineNoResponse = 30000m,
            FineAttendingButAbsent = 50000m
        };

        var record = new AttendanceRecord
        {
            Response = ResponseStatus.NoResponse,
            Actual = ActualStatus.Absent,
            IsExempt = false
        };

        var result = engine.CalculatePenalty(record, reg, isBtc: false);

        Assert.Equal(30000m, result.FineAmount);
        Assert.False(result.NeedsConfirmation);
    }

    [Fact]
    public void Excused_With_ExemptPolicy_Should_Have_Zero_Fine()
    {
        using var db = CreateInMemoryDbContext();
        var engine = new AttendanceCalculationEngine(db);

        var reg = new TeamRegulation
        {
            FineNoResponse = 30000m,
            IsExcusedExemptFromFine = true
        };

        var record = new AttendanceRecord
        {
            Response = ResponseStatus.Excused,
            Actual = ActualStatus.Absent,
            IsExempt = false
        };

        var result = engine.CalculatePenalty(record, reg, isBtc: false);

        Assert.Equal(0m, result.FineAmount);
        Assert.False(result.NeedsConfirmation);
        Assert.Contains("Báo vắng đúng hạn", result.PenaltyReason);
    }

    [Fact]
    public void BTC_Member_Must_Be_Exempt_From_Fines()
    {
        using var db = CreateInMemoryDbContext();
        var engine = new AttendanceCalculationEngine(db);

        var reg = new TeamRegulation
        {
            FineNoResponse = 30000m,
            FineAttendingButAbsent = 50000m
        };

        var record = new AttendanceRecord
        {
            Response = ResponseStatus.Attending,
            Actual = ActualStatus.Absent // Đáng ra phạt 50k
        };

        var result = engine.CalculatePenalty(record, reg, isBtc: true);

        Assert.Equal(0m, result.FineAmount);
        Assert.False(result.NeedsConfirmation);
        Assert.Contains("BTC", result.PenaltyReason);
    }

    [Fact]
    public void Unrecorded_Or_Unchecked_Must_Flag_NeedsConfirmation()
    {
        using var db = CreateInMemoryDbContext();
        var engine = new AttendanceCalculationEngine(db);

        var reg = new TeamRegulation();

        var record = new AttendanceRecord
        {
            Response = ResponseStatus.NotRecorded,
            Actual = ActualStatus.NotChecked
        };

        var result = engine.CalculatePenalty(record, reg, isBtc: false);

        Assert.Equal(0m, result.FineAmount);
        Assert.True(result.NeedsConfirmation);
        Assert.Contains("Cần xác nhận", result.PenaltyReason);
    }
}
