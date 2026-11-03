using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Services;

public class AttendanceCalculationResult
{
    public decimal FineAmount { get; set; }
    public bool NeedsConfirmation { get; set; }
    public string PenaltyReason { get; set; } = string.Empty;
}

public interface IAttendanceCalculationEngine
{
    AttendanceCalculationResult CalculatePenalty(AttendanceRecord record, TeamRegulation regulation, bool isBtc);
    Task RecalculateSessionPenaltiesAsync(Guid sessionId);
    Task<MonthlyAttendanceClosing> CalculateMonthlyClosingAsync(Guid teamId, string period);
}

public class AttendanceCalculationEngine : IAttendanceCalculationEngine
{
    private readonly ApplicationDbContext _db;

    public AttendanceCalculationEngine(ApplicationDbContext db)
    {
        _db = db;
    }

    public AttendanceCalculationResult CalculatePenalty(AttendanceRecord record, TeamRegulation regulation, bool isBtc)
    {
        var result = new AttendanceCalculationResult();

        // 1. Nếu là BTC hoặc đã được duyệt miễn nghĩa vụ
        if (isBtc || record.IsExempt)
        {
            result.FineAmount = 0;
            result.NeedsConfirmation = false;
            result.PenaltyReason = isBtc ? "Miễn trừ theo quy chế Ban Tổ Chức (BTC)" : (record.ExemptionReason ?? "Được miễn trừ");
            return result;
        }

        // 2. Kiểm tra dữ kiện chưa đủ:
        // - Chưa ghi nhận phản hồi HOẶC Chưa điểm danh thực tế
        if (record.Response == ResponseStatus.NotRecorded || record.Actual == ActualStatus.NotChecked)
        {
            result.FineAmount = 0;
            result.NeedsConfirmation = true;
            result.PenaltyReason = "Cần xác nhận (chưa đủ dữ kiện phản hồi hoặc điểm danh)";
            return result;
        }

        // 3. Quy tắc: Báo vắng
        if (record.Response == ResponseStatus.Excused)
        {
            if (record.Actual == ActualStatus.Absent)
            {
                if (regulation.IsExcusedExemptFromFine)
                {
                    result.FineAmount = 0;
                    result.NeedsConfirmation = false;
                    result.PenaltyReason = "Báo vắng đúng hạn (hợp lệ)";
                }
                else
                {
                    result.FineAmount = regulation.FineNoResponse;
                    result.NeedsConfirmation = false;
                    result.PenaltyReason = "Báo vắng tính phí duy trì";
                }
                return result;
            }
            else if (record.Actual == ActualStatus.Present)
            {
                // Báo vắng nhưng vẫn đi đá
                result.FineAmount = 0;
                result.NeedsConfirmation = false;
                result.PenaltyReason = "Báo vắng nhưng có mặt thi đấu";
                return result;
            }
        }

        // 4. Quy tắc: Không phản hồi (NoResponse)
        if (record.Response == ResponseStatus.NoResponse)
        {
            // Vi phạm không xác nhận
            result.FineAmount = regulation.FineNoResponse;
            result.NeedsConfirmation = false;
            result.PenaltyReason = record.Actual == ActualStatus.Present
                ? "Có mặt nhưng không phản hồi đúng hạn"
                : "Không phản hồi và vắng mặt";
            return result;
        }

        // 5. Quy tắc: Xác nhận tham gia (Attending) nhưng VẮNG (Absent)
        if (record.Response == ResponseStatus.Attending && record.Actual == ActualStatus.Absent)
        {
            result.FineAmount = regulation.FineAttendingButAbsent;
            result.NeedsConfirmation = false;
            result.PenaltyReason = "Xác nhận tham gia nhưng vắng mặt";
            return result;
        }

        // 6. Xác nhận tham gia và Có mặt
        if (record.Response == ResponseStatus.Attending && record.Actual == ActualStatus.Present)
        {
            result.FineAmount = 0;
            result.NeedsConfirmation = false;
            result.PenaltyReason = "Có mặt đúng cam kết";
            return result;
        }

        return result;
    }

    public async Task RecalculateSessionPenaltiesAsync(Guid sessionId)
    {
        var session = await _db.AttendanceSessions
            .Include(s => s.Records)
            .ThenInclude(r => r.Player)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null || session.Status == SessionStatus.Cancelled) return;

        var regulation = await _db.TeamRegulations
            .Where(r => r.TeamId == session.TeamId && r.IsActive)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync() ?? new TeamRegulation { TeamId = session.TeamId };

        // Lấy danh sách BTC
        var btcUserIds = await _db.TeamMembers
            .Where(m => m.TeamId == session.TeamId && m.IsBtc)
            .Select(m => m.UserId)
            .ToListAsync();

        foreach (var record in session.Records)
        {
            bool isBtc = !string.IsNullOrEmpty(record.Player.LinkedUserId) && btcUserIds.Contains(record.Player.LinkedUserId);
            var calc = CalculatePenalty(record, regulation, isBtc);
            record.FineAmount = calc.FineAmount;
            record.NeedsConfirmation = calc.NeedsConfirmation;
            record.PenaltyReason = calc.PenaltyReason;
        }

        await _db.SaveChangesAsync();
    }

    public async Task<MonthlyAttendanceClosing> CalculateMonthlyClosingAsync(Guid teamId, string period)
    {
        var regulation = await _db.TeamRegulations
            .Where(r => r.TeamId == teamId && r.IsActive)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync() ?? new TeamRegulation { TeamId = teamId };

        var closing = await _db.MonthlyAttendanceClosings
            .Include(c => c.Summaries)
            .FirstOrDefaultAsync(c => c.TeamId == teamId && c.StatisticalPeriod == period);

        if (closing == null)
        {
            closing = new MonthlyAttendanceClosing
            {
                TeamId = teamId,
                StatisticalPeriod = period,
                RegulationVersion = regulation.Version,
                RegulationMinMatches = regulation.MinMatchesPerMonth,
                RegulationUnderAttendanceFine = regulation.FineUnderMinAttendance
            };
            _db.MonthlyAttendanceClosings.Add(closing);
        }

        // Lấy danh sách các buổi trong kỳ thống kê (loại trừ các buổi bị hủy)
        var sessions = await _db.AttendanceSessions
            .Include(s => s.Records)
            .ThenInclude(r => r.Player)
            .Where(s => s.TeamId == teamId && s.StatisticalPeriod == period && s.Status != SessionStatus.Cancelled)
            .ToListAsync();

        var players = await _db.Players
            .Where(p => p.TeamId == teamId && p.IsActive)
            .ToListAsync();

        var btcUserIds = await _db.TeamMembers
            .Where(m => m.TeamId == teamId && m.IsBtc)
            .Select(m => m.UserId)
            .ToListAsync();

        closing.Summaries.Clear();

        foreach (var player in players)
        {
            bool isBtc = !string.IsNullOrEmpty(player.LinkedUserId) && btcUserIds.Contains(player.LinkedUserId);

            var playerRecords = sessions
                .SelectMany(s => s.Records)
                .Where(r => r.PlayerId == player.Id)
                .ToList();

            int attendedCount = playerRecords.Count(r => r.Actual == ActualStatus.Present);
            decimal sessionFines = playerRecords.Sum(r => r.FineAmount);
            decimal underAttendanceFine = 0m;

            // Kiểm tra chuyên cần nếu không phải BTC
            if (!isBtc && attendedCount < regulation.MinMatchesPerMonth)
            {
                underAttendanceFine = regulation.FineUnderMinAttendance;
            }

            decimal totalDue = regulation.CombineMonthlyAndSessionFines
                ? (sessionFines + underAttendanceFine)
                : Math.Max(sessionFines, underAttendanceFine);

            var summary = new MonthlyPlayerSummary
            {
                MonthlyClosingId = closing.Id,
                PlayerId = player.Id,
                TotalSessionsInMonth = sessions.Count,
                AttendedSessionsCount = attendedCount,
                IsBtc = isBtc,
                TotalSessionFines = sessionFines,
                UnderAttendanceFine = underAttendanceFine,
                TotalDue = totalDue,
                TotalPaid = 0m,
                Notes = isBtc ? "Miễn phạt chuyên cần do là thành viên BTC" : null
            };

            closing.Summaries.Add(summary);
        }

        await _db.SaveChangesAsync();
        return closing;
    }
}
