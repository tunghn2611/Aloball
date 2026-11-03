using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Services;

public class LeaderboardEntryDto
{
    public int Rank { get; set; }
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int? KitNumber { get; set; }
    public string? Position { get; set; }
    public string? PhotoUrl { get; set; }
    public bool IsPublicProfile { get; set; }

    public int AttendedSessions { get; set; }
    public int Goals { get; set; }
    public int Assists { get; set; }
    public int Wins { get; set; }
    public int TotalAb { get; set; }

    public string CalculationBreakdown { get; set; } = string.Empty;
}

public class MonthlyLeaderboardResult
{
    public Guid TeamId { get; set; }
    public string StatisticalPeriod { get; set; } = string.Empty;
    public PeriodStatus PeriodStatus { get; set; } = PeriodStatus.Open;
    public int PeriodRevision { get; set; } = 1;
    public bool HasIncompleteData { get; set; }
    public List<string> IncompleteDataWarnings { get; set; } = new();

    public List<LeaderboardEntryDto> Entries { get; set; } = new();
    public List<MonthlyFireAward> ExistingAwards { get; set; } = new();
}

public class PlayerAbSummaryDto
{
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int? KitNumber { get; set; }
    public string? Position { get; set; }
    public int CurrentMonthAb { get; set; }
    public int TotalCumulativeAb { get; set; }
    public List<PlayerAbJournalEntry> JournalEntries { get; set; } = new();
    public List<MonthlyFireAward> Awards { get; set; } = new();
}

public interface IFireKeepingService
{
    Task<TeamFireRegulation> GetEffectiveRegulationAsync(Guid teamId, DateTime eventDate);
    Task<FireCompetitionPeriod> GetOrCreatePeriodAsync(Guid teamId, string statisticalPeriod);

    // Đồng bộ nguồn vào sổ AB với Source Revision & Transaction
    Task SyncAttendanceSessionPointsAsync(Guid sessionId, string? triggeredByUserId = null);
    Task SyncMatchPointsAsync(Guid matchId, string? triggeredByUserId = null);
    Task RevokeMatchPointsAsync(Guid matchId, string reason, string? triggeredByUserId = null);

    // Bảng xếp hạng và Danh hiệu
    Task<MonthlyLeaderboardResult> GetMonthlyLeaderboardAsync(Guid teamId, string period);
    Task<PlayerAbSummaryDto> GetPlayerAbSummaryAsync(Guid teamId, Guid playerId, string? period = null);
    Task<(bool Success, string Message)> FinalizePeriodAndAwardAsync(Guid teamId, string period, List<Guid> chosenPlayerIds, string decidedByUserId, string? notes);
    Task<(bool Success, string Message)> ReopenPeriodAsync(Guid teamId, string period, string reason, string reopenedByUserId);
}

public class FireKeepingService : IFireKeepingService
{
    private readonly ApplicationDbContext _db;

    public FireKeepingService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<TeamFireRegulation> GetEffectiveRegulationAsync(Guid teamId, DateTime eventDate)
    {
        var reg = await _db.TeamFireRegulations
            .Where(r => r.TeamId == teamId && r.EffectiveFrom <= eventDate)
            .OrderByDescending(r => r.EffectiveFrom)
            .ThenByDescending(r => r.Version)
            .FirstOrDefaultAsync();

        if (reg != null) return reg;

        // Tạo quy chế mặc định v1 nếu chưa có
        var defaultReg = new TeamFireRegulation
        {
            TeamId = teamId,
            Version = 1,
            EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PointsPerAttendance = 2,
            PointsPerGoal = 3,
            PointsPerAssist = 1,
            PointsPerWin = 1,
            IsActive = true
        };
        _db.TeamFireRegulations.Add(defaultReg);
        await _db.SaveChangesAsync();
        return defaultReg;
    }

    public async Task<FireCompetitionPeriod> GetOrCreatePeriodAsync(Guid teamId, string statisticalPeriod)
    {
        var period = await _db.FireCompetitionPeriods
            .FirstOrDefaultAsync(p => p.TeamId == teamId && p.StatisticalPeriod == statisticalPeriod);

        if (period == null)
        {
            period = new FireCompetitionPeriod
            {
                TeamId = teamId,
                StatisticalPeriod = statisticalPeriod,
                Status = PeriodStatus.Open,
                Revision = 1
            };
            _db.FireCompetitionPeriods.Add(period);
            await _db.SaveChangesAsync();
        }

        return period;
    }

    public async Task SyncAttendanceSessionPointsAsync(Guid sessionId, string? triggeredByUserId = null)
    {
        using var tx = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync() : null;
        try
        {
            var session = await _db.AttendanceSessions
                .Include(s => s.Records)
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session == null) return;

            // Kiểm tra trạng thái kỳ thi đua
            var period = await GetOrCreatePeriodAsync(session.TeamId, session.StatisticalPeriod);
            if (period.Status == PeriodStatus.Closed)
            {
                throw new InvalidOperationException($"Kỳ thi đua {session.StatisticalPeriod} của đội đã được chốt sổ. Phải mở lại kỳ trước khi thay đổi nguồn.");
            }

            var reg = await GetEffectiveRegulationAsync(session.TeamId, session.SessionDateTime);

            // Tăng revision của session
            session.Revision += 1;
            int currentRev = session.Revision;

            // Tìm các phát sinh chuyên cần trước đó của session này chưa bị đảo
            var existingEntries = await _db.PlayerAbJournalEntries
                .Where(j => j.TeamId == session.TeamId &&
                            j.SourceDomain == AbSourceDomain.AttendanceSession &&
                            j.SourceId == session.Id &&
                            j.SourceRole == "Attendance")
                .ToListAsync();

            // Lập danh sách các entry active hiện tại (entry có điểm dương và chưa bị đảo bởi entry nào)
            var activeEntries = existingEntries
                .Where(e => e.Points > 0 && !existingEntries.Any(r => r.ReversedEntryId == e.Id))
                .ToList();

            // Nếu buổi bị hủy (Cancelled) hoặc chưa chốt điểm danh (Draft):
            // Tất cả active entries phải được đảo (thu hồi)
            if (session.Status != SessionStatus.Finalized)
            {
                foreach (var active in activeEntries)
                {
                    var reversal = new PlayerAbJournalEntry
                    {
                        TeamId = session.TeamId,
                        PlayerId = active.PlayerId,
                        StatisticalPeriod = session.StatisticalPeriod,
                        SourceDomain = AbSourceDomain.AttendanceSession,
                        SourceId = session.Id,
                        SourceRole = "Attendance",
                        SourceRevision = currentRev,
                        OperationKey = $"{session.TeamId}_Att_{session.Id}_{active.PlayerId}_rev_{currentRev}_revof_{active.Id}",
                        Points = -active.Points,
                        RegulationVersion = active.RegulationVersion,
                        ReversedEntryId = active.Id,
                        AdjustmentReason = session.Status == SessionStatus.Cancelled ? "Thu hồi do buổi bị hủy" : "Thu hồi do buổi chuyển về dự thảo",
                        EventDate = session.SessionDateTime
                    };
                    _db.PlayerAbJournalEntries.Add(reversal);
                }

                await _db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
                return;
            }

            // Trường hợp session.Status == SessionStatus.Finalized:
            // Cầu thủ có mặt thực tế: Actual == ActualStatus.Present
            var presentPlayerIds = session.Records
                .Where(r => r.Actual == ActualStatus.Present)
                .Select(r => r.PlayerId)
                .ToHashSet();

            // 1. Đảo điểm đối với những cầu thủ trước đây được cộng nhưng giờ không còn Present
            foreach (var active in activeEntries)
            {
                if (!presentPlayerIds.Contains(active.PlayerId))
                {
                    var reversal = new PlayerAbJournalEntry
                    {
                        TeamId = session.TeamId,
                        PlayerId = active.PlayerId,
                        StatisticalPeriod = session.StatisticalPeriod,
                        SourceDomain = AbSourceDomain.AttendanceSession,
                        SourceId = session.Id,
                        SourceRole = "Attendance",
                        SourceRevision = currentRev,
                        OperationKey = $"{session.TeamId}_Att_{session.Id}_{active.PlayerId}_rev_{currentRev}_revof_{active.Id}",
                        Points = -active.Points,
                        RegulationVersion = active.RegulationVersion,
                        ReversedEntryId = active.Id,
                        AdjustmentReason = "Thu hồi do cập nhật lại vắng mặt",
                        EventDate = session.SessionDateTime
                    };
                    _db.PlayerAbJournalEntries.Add(reversal);
                }
            }

            // 2. Cộng điểm cho những cầu thủ Present mà chưa có active entry
            var activePlayerIds = activeEntries
                .Where(e => !existingEntries.Any(r => r.ReversedEntryId == e.Id))
                .Select(e => e.PlayerId)
                .ToHashSet();

            foreach (var playerId in presentPlayerIds)
            {
                if (!activePlayerIds.Contains(playerId))
                {
                    var entry = new PlayerAbJournalEntry
                    {
                        TeamId = session.TeamId,
                        PlayerId = playerId,
                        StatisticalPeriod = session.StatisticalPeriod,
                        SourceDomain = AbSourceDomain.AttendanceSession,
                        SourceId = session.Id,
                        SourceRole = "Attendance",
                        SourceRevision = currentRev,
                        OperationKey = $"{session.TeamId}_Att_{session.Id}_{playerId}_rev_{currentRev}",
                        Points = reg.PointsPerAttendance,
                        RegulationVersion = reg.Version,
                        AdjustmentReason = "Điểm có mặt buổi sinh hoạt",
                        EventDate = session.SessionDateTime
                    };
                    _db.PlayerAbJournalEntries.Add(entry);
                }
            }

            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync();
            throw;
        }
    }

    public async Task SyncMatchPointsAsync(Guid matchId, string? triggeredByUserId = null)
    {
        using var tx = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync() : null;
        try
        {
            var match = await _db.Matches
                .Include(m => m.Lineups)
                .Include(m => m.GoalEvents)
                .FirstOrDefaultAsync(m => m.Id == matchId);

            if (match == null) return;

            string periodStr = match.MatchDateTime.ToString("yyyy-MM");

            // Kiểm tra trạng thái kỳ của cả TeamA và TeamB (nếu có)
            var periodA = await GetOrCreatePeriodAsync(match.TeamAId, periodStr);
            if (periodA.Status == PeriodStatus.Closed)
            {
                throw new InvalidOperationException($"Kỳ thi đua {periodStr} của đội chủ nhà đã chốt. Cần mở lại kỳ trước khi sửa kết quả trận.");
            }

            if (match.TeamBId.HasValue)
            {
                var periodB = await GetOrCreatePeriodAsync(match.TeamBId.Value, periodStr);
                if (periodB.Status == PeriodStatus.Closed)
                {
                    throw new InvalidOperationException($"Kỳ thi đua {periodStr} của đội khách đã chốt. Cần mở lại kỳ trước khi sửa kết quả trận.");
                }
            }

            match.Revision += 1;
            int matchRev = match.Revision;

            // Xử lý cho từng đội tham gia trận
            var teamsToProcess = new List<Guid> { match.TeamAId };
            if (match.TeamBId.HasValue) teamsToProcess.Add(match.TeamBId.Value);

            foreach (var teamId in teamsToProcess)
            {
                var reg = await GetEffectiveRegulationAsync(teamId, match.MatchDateTime);

                // --- A. XỬ LÝ THƯỞNG THẮNG TRẬN (MatchWin) ---
                var winEntries = await _db.PlayerAbJournalEntries
                    .Where(j => j.TeamId == teamId &&
                                j.SourceDomain == AbSourceDomain.MatchWin &&
                                j.SourceId == match.Id &&
                                j.SourceRole == "Winner")
                    .ToListAsync();

                var activeWinEntries = winEntries
                    .Where(e => e.Points > 0 && !winEntries.Any(r => r.ReversedEntryId == e.Id))
                    .ToList();

                // Xác định danh sách cầu thủ thuộc bên thắng:
                // 1. Trận nội bộ (Category == Internal):
                //    - Đội bóng chia làm 2 bên (TeamSide == "A" vs TeamSide == "B").
                //    - Nếu ScoreA > ScoreB: Cầu thủ bên A thắng.
                //    - Nếu ScoreB > ScoreA: Cầu thủ bên B thắng.
                // 2. Trận đối ngoại (Friendly/Tournament):
                //    - Nếu teamId == TeamAId && ScoreA > ScoreB: Team A thắng.
                //    - Nếu teamId == TeamBId && ScoreB > ScoreA: Team B thắng.
                var participatingPlayerIds = new HashSet<Guid>();

                if (match.Status == MatchStatus.Completed && match.ScoreA.HasValue && match.ScoreB.HasValue)
                {
                    bool isInternal = match.Category == MatchCategory.Internal;

                    if (isInternal)
                    {
                        if (match.ScoreA > match.ScoreB)
                        {
                            // Bên A thắng
                            participatingPlayerIds = match.Lineups
                                .Where(l => l.TeamId == teamId && (string.IsNullOrEmpty(l.TeamSide) || l.TeamSide == "A"))
                                .Select(l => l.PlayerId)
                                .ToHashSet();
                        }
                        else if (match.ScoreB > match.ScoreA)
                        {
                            // Bên B thắng
                            participatingPlayerIds = match.Lineups
                                .Where(l => l.TeamId == teamId && l.TeamSide == "B")
                                .Select(l => l.PlayerId)
                                .ToHashSet();
                        }
                    }
                    else
                    {
                        bool isWinner = false;
                        if (teamId == match.TeamAId && match.ScoreA > match.ScoreB) isWinner = true;
                        else if (match.TeamBId.HasValue && teamId == match.TeamBId.Value && match.ScoreB > match.ScoreA) isWinner = true;

                        if (isWinner)
                        {
                            participatingPlayerIds = match.Lineups
                                .Where(l => l.TeamId == teamId)
                                .Select(l => l.PlayerId)
                                .ToHashSet();
                        }
                    }
                }

                if (participatingPlayerIds.Any())
                {
                    // Đảo các active entries cho cầu thủ không còn trong danh sách thi đấu
                    foreach (var winEntry in activeWinEntries)
                    {
                        if (!participatingPlayerIds.Contains(winEntry.PlayerId))
                        {
                            _db.PlayerAbJournalEntries.Add(new PlayerAbJournalEntry
                            {
                                TeamId = teamId,
                                PlayerId = winEntry.PlayerId,
                                StatisticalPeriod = periodStr,
                                SourceDomain = AbSourceDomain.MatchWin,
                                SourceId = match.Id,
                                SourceRole = "Winner",
                                SourceRevision = matchRev,
                                OperationKey = $"{teamId}_Win_{match.Id}_{winEntry.PlayerId}_rev_{matchRev}_revof_{winEntry.Id}",
                                Points = -winEntry.Points,
                                RegulationVersion = winEntry.RegulationVersion,
                                ReversedEntryId = winEntry.Id,
                                AdjustmentReason = "Thu hồi thưởng thắng do loại khỏi danh sách thi đấu",
                                EventDate = match.MatchDateTime
                            });
                        }
                    }

                    // Cộng điểm thưởng thắng cho cầu thủ thi đấu chưa có active win entry
                    var activeWinPlayerIds = activeWinEntries.Select(e => e.PlayerId).ToHashSet();
                    foreach (var pId in participatingPlayerIds)
                    {
                        if (!activeWinPlayerIds.Contains(pId))
                        {
                            _db.PlayerAbJournalEntries.Add(new PlayerAbJournalEntry
                            {
                                TeamId = teamId,
                                PlayerId = pId,
                                StatisticalPeriod = periodStr,
                                SourceDomain = AbSourceDomain.MatchWin,
                                SourceId = match.Id,
                                SourceRole = "Winner",
                                SourceRevision = matchRev,
                                OperationKey = $"{teamId}_Win_{match.Id}_{pId}_rev_{matchRev}",
                                Points = reg.PointsPerWin,
                                RegulationVersion = reg.Version,
                                AdjustmentReason = "Thưởng thắng trận đấu",
                                EventDate = match.MatchDateTime
                            });
                        }
                    }
                }
                else
                {
                    // Hòa, Thua, Chưa xong, hoặc Hủy: Thu hồi toàn bộ active win entries
                    foreach (var winEntry in activeWinEntries)
                    {
                        _db.PlayerAbJournalEntries.Add(new PlayerAbJournalEntry
                        {
                            TeamId = teamId,
                            PlayerId = winEntry.PlayerId,
                            StatisticalPeriod = periodStr,
                            SourceDomain = AbSourceDomain.MatchWin,
                            SourceId = match.Id,
                            SourceRole = "Winner",
                            SourceRevision = matchRev,
                            OperationKey = $"{teamId}_Win_{match.Id}_{winEntry.PlayerId}_rev_{matchRev}_revof_{winEntry.Id}",
                            Points = -winEntry.Points,
                            RegulationVersion = winEntry.RegulationVersion,
                            ReversedEntryId = winEntry.Id,
                            AdjustmentReason = match.Status == MatchStatus.Cancelled
                                ? "Thu hồi thưởng thắng do trận bị hủy"
                                : "Thu hồi thưởng thắng do kết quả hòa/thua hoặc sửa tỉ số",
                            EventDate = match.MatchDateTime
                        });
                    }
                }

                // --- B. XỬ LÝ BÀN THẮNG & KIẾN TẠO (MatchGoalEvent) ---
                var goalEvents = match.GoalEvents.Where(g => g.TeamId == teamId).ToList();

                // Lấy tất cả entries về Goal và Assist của trận này
                var goalEntries = await _db.PlayerAbJournalEntries
                    .Where(j => j.TeamId == teamId &&
                                j.SourceDomain == AbSourceDomain.MatchGoalEvent &&
                                j.SourceId == match.Id)
                    .ToListAsync();

                // 1. Đồng bộ từng GoalEvent hiện có trong trận (nếu trận Completed)
                if (match.Status == MatchStatus.Completed)
                {
                    foreach (var ge in goalEvents)
                    {
                        // a. Người ghi bàn (Scorer)
                        var activeScorer = goalEntries.FirstOrDefault(e =>
                            e.SourceRole == "Scorer" &&
                            e.OperationKey.Contains($"_ge_{ge.Id}_Scorer") &&
                            e.Points > 0 &&
                            !goalEntries.Any(r => r.ReversedEntryId == e.Id));

                        if (activeScorer == null)
                        {
                            // Chưa có entry cho bàn này -> Tạo mới
                            _db.PlayerAbJournalEntries.Add(new PlayerAbJournalEntry
                            {
                                TeamId = teamId,
                                PlayerId = ge.ScorerPlayerId,
                                StatisticalPeriod = periodStr,
                                SourceDomain = AbSourceDomain.MatchGoalEvent,
                                SourceId = match.Id,
                                SourceRole = "Scorer",
                                SourceRevision = ge.Revision,
                                OperationKey = $"{teamId}_GE_{match.Id}_ge_{ge.Id}_Scorer_{ge.ScorerPlayerId}_rev_{ge.Revision}",
                                Points = reg.PointsPerGoal,
                                RegulationVersion = reg.Version,
                                AdjustmentReason = "Điểm ghi bàn thắng",
                                EventDate = match.MatchDateTime
                            });
                        }
                        else if (activeScorer.PlayerId != ge.ScorerPlayerId)
                        {
                            // Người ghi bàn đã bị đổi -> Đảo điểm người cũ, cộng điểm người mới
                            _db.PlayerAbJournalEntries.Add(new PlayerAbJournalEntry
                            {
                                TeamId = teamId,
                                PlayerId = activeScorer.PlayerId,
                                StatisticalPeriod = periodStr,
                                SourceDomain = AbSourceDomain.MatchGoalEvent,
                                SourceId = match.Id,
                                SourceRole = "Scorer",
                                SourceRevision = ge.Revision,
                                OperationKey = $"{teamId}_GE_{match.Id}_ge_{ge.Id}_Scorer_{activeScorer.PlayerId}_rev_{ge.Revision}_revof_{activeScorer.Id}",
                                Points = -activeScorer.Points,
                                RegulationVersion = activeScorer.RegulationVersion,
                                ReversedEntryId = activeScorer.Id,
                                AdjustmentReason = "Thu hồi do sửa người ghi bàn",
                                EventDate = match.MatchDateTime
                            });

                            _db.PlayerAbJournalEntries.Add(new PlayerAbJournalEntry
                            {
                                TeamId = teamId,
                                PlayerId = ge.ScorerPlayerId,
                                StatisticalPeriod = periodStr,
                                SourceDomain = AbSourceDomain.MatchGoalEvent,
                                SourceId = match.Id,
                                SourceRole = "Scorer",
                                SourceRevision = ge.Revision,
                                OperationKey = $"{teamId}_GE_{match.Id}_ge_{ge.Id}_Scorer_{ge.ScorerPlayerId}_rev_{ge.Revision}",
                                Points = reg.PointsPerGoal,
                                RegulationVersion = reg.Version,
                                AdjustmentReason = "Điểm ghi bàn thắng (cập nhật mới)",
                                EventDate = match.MatchDateTime
                            });
                        }

                        // b. Người kiến tạo (Assist)
                        var activeAssist = goalEntries.FirstOrDefault(e =>
                            e.SourceRole == "Assist" &&
                            e.OperationKey.Contains($"_ge_{ge.Id}_Assist") &&
                            e.Points > 0 &&
                            !goalEntries.Any(r => r.ReversedEntryId == e.Id));

                        if (ge.AssistPlayerId.HasValue)
                        {
                            if (activeAssist == null)
                            {
                                _db.PlayerAbJournalEntries.Add(new PlayerAbJournalEntry
                                {
                                    TeamId = teamId,
                                    PlayerId = ge.AssistPlayerId.Value,
                                    StatisticalPeriod = periodStr,
                                    SourceDomain = AbSourceDomain.MatchGoalEvent,
                                    SourceId = match.Id,
                                    SourceRole = "Assist",
                                    SourceRevision = ge.Revision,
                                    OperationKey = $"{teamId}_GE_{match.Id}_ge_{ge.Id}_Assist_{ge.AssistPlayerId.Value}_rev_{ge.Revision}",
                                    Points = reg.PointsPerAssist,
                                    RegulationVersion = reg.Version,
                                    AdjustmentReason = "Điểm kiến tạo bàn thắng",
                                    EventDate = match.MatchDateTime
                                });
                            }
                            else if (activeAssist.PlayerId != ge.AssistPlayerId.Value)
                            {
                                _db.PlayerAbJournalEntries.Add(new PlayerAbJournalEntry
                                {
                                    TeamId = teamId,
                                    PlayerId = activeAssist.PlayerId,
                                    StatisticalPeriod = periodStr,
                                    SourceDomain = AbSourceDomain.MatchGoalEvent,
                                    SourceId = match.Id,
                                    SourceRole = "Assist",
                                    SourceRevision = ge.Revision,
                                    OperationKey = $"{teamId}_GE_{match.Id}_ge_{ge.Id}_Assist_{activeAssist.PlayerId}_rev_{ge.Revision}_revof_{activeAssist.Id}",
                                    Points = -activeAssist.Points,
                                    RegulationVersion = activeAssist.RegulationVersion,
                                    ReversedEntryId = activeAssist.Id,
                                    AdjustmentReason = "Thu hồi do sửa người kiến tạo",
                                    EventDate = match.MatchDateTime
                                });

                                _db.PlayerAbJournalEntries.Add(new PlayerAbJournalEntry
                                {
                                    TeamId = teamId,
                                    PlayerId = ge.AssistPlayerId.Value,
                                    StatisticalPeriod = periodStr,
                                    SourceDomain = AbSourceDomain.MatchGoalEvent,
                                    SourceId = match.Id,
                                    SourceRole = "Assist",
                                    SourceRevision = ge.Revision,
                                    OperationKey = $"{teamId}_GE_{match.Id}_ge_{ge.Id}_Assist_{ge.AssistPlayerId.Value}_rev_{ge.Revision}",
                                    Points = reg.PointsPerAssist,
                                    RegulationVersion = reg.Version,
                                    AdjustmentReason = "Điểm kiến tạo bàn thắng (cập nhật mới)",
                                    EventDate = match.MatchDateTime
                                });
                            }
                        }
                        else if (activeAssist != null)
                        {
                            // Trước có kiến tạo, giờ xóa kiến tạo
                            _db.PlayerAbJournalEntries.Add(new PlayerAbJournalEntry
                            {
                                TeamId = teamId,
                                PlayerId = activeAssist.PlayerId,
                                StatisticalPeriod = periodStr,
                                SourceDomain = AbSourceDomain.MatchGoalEvent,
                                SourceId = match.Id,
                                SourceRole = "Assist",
                                SourceRevision = ge.Revision,
                                OperationKey = $"{teamId}_GE_{match.Id}_ge_{ge.Id}_Assist_{activeAssist.PlayerId}_rev_{ge.Revision}_revof_{activeAssist.Id}",
                                Points = -activeAssist.Points,
                                RegulationVersion = activeAssist.RegulationVersion,
                                ReversedEntryId = activeAssist.Id,
                                AdjustmentReason = "Thu hồi do hủy người kiến tạo",
                                EventDate = match.MatchDateTime
                            });
                        }
                    }

                    // Thu hồi các bàn thắng trước đây mà giờ đã bị xóa khỏi trận
                    var currentGoalEventIds = goalEvents.Select(g => g.Id).ToHashSet();
                    var activeGoalEntries = goalEntries
                        .Where(e => e.Points > 0 && !goalEntries.Any(r => r.ReversedEntryId == e.Id))
                        .ToList();

                    foreach (var activeEntry in activeGoalEntries)
                    {
                        // Kiểm tra xem entry này có thuộc goalEvent nào còn tồn tại không
                        bool stillExists = currentGoalEventIds.Any(geId => activeEntry.OperationKey.Contains($"_ge_{geId}_"));
                        if (!stillExists)
                        {
                            _db.PlayerAbJournalEntries.Add(new PlayerAbJournalEntry
                            {
                                TeamId = teamId,
                                PlayerId = activeEntry.PlayerId,
                                StatisticalPeriod = periodStr,
                                SourceDomain = AbSourceDomain.MatchGoalEvent,
                                SourceId = match.Id,
                                SourceRole = activeEntry.SourceRole,
                                SourceRevision = matchRev,
                                OperationKey = $"{teamId}_GE_{match.Id}_{activeEntry.PlayerId}_{activeEntry.SourceRole}_rev_{matchRev}_revof_{activeEntry.Id}",
                                Points = -activeEntry.Points,
                                RegulationVersion = activeEntry.RegulationVersion,
                                ReversedEntryId = activeEntry.Id,
                                AdjustmentReason = "Thu hồi do sự kiện bàn thắng bị xóa",
                                EventDate = match.MatchDateTime
                            });
                        }
                    }
                }
                else
                {
                    // Trận không Completed (bị hủy hoặc quay về Scheduled): Thu hồi toàn bộ active goal/assist
                    var activeGoalEntries = goalEntries
                        .Where(e => e.Points > 0 && !goalEntries.Any(r => r.ReversedEntryId == e.Id))
                        .ToList();

                    foreach (var activeEntry in activeGoalEntries)
                    {
                        _db.PlayerAbJournalEntries.Add(new PlayerAbJournalEntry
                        {
                            TeamId = teamId,
                            PlayerId = activeEntry.PlayerId,
                            StatisticalPeriod = periodStr,
                            SourceDomain = AbSourceDomain.MatchGoalEvent,
                            SourceId = match.Id,
                            SourceRole = activeEntry.SourceRole,
                            SourceRevision = matchRev,
                            OperationKey = $"{teamId}_GE_{match.Id}_{activeEntry.PlayerId}_{activeEntry.SourceRole}_rev_{matchRev}_revof_{activeEntry.Id}",
                            Points = -activeEntry.Points,
                            RegulationVersion = activeEntry.RegulationVersion,
                            ReversedEntryId = activeEntry.Id,
                            AdjustmentReason = "Thu hồi điểm bàn thắng/kiến tạo do trận bị hủy/chưa hoàn thành",
                            EventDate = match.MatchDateTime
                        });
                    }
                }
            }

            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync();
            throw;
        }
    }

    public async Task RevokeMatchPointsAsync(Guid matchId, string reason, string? triggeredByUserId = null)
    {
        var match = await _db.Matches.FindAsync(matchId);
        if (match == null) return;

        match.Status = MatchStatus.Cancelled;
        await SyncMatchPointsAsync(matchId, triggeredByUserId);
    }

    public async Task<MonthlyLeaderboardResult> GetMonthlyLeaderboardAsync(Guid teamId, string period)
    {
        var compPeriod = await GetOrCreatePeriodAsync(teamId, period);

        var result = new MonthlyLeaderboardResult
        {
            TeamId = teamId,
            StatisticalPeriod = period,
            PeriodStatus = compPeriod.Status,
            PeriodRevision = compPeriod.Revision
        };

        // Kiểm tra dữ liệu chưa hoàn chỉnh trong kỳ
        var draftSessions = await _db.AttendanceSessions
            .Where(s => s.TeamId == teamId && s.StatisticalPeriod == period && s.Status == SessionStatus.Draft)
            .ToListAsync();
        if (draftSessions.Any())
        {
            result.HasIncompleteData = true;
            result.IncompleteDataWarnings.Add($"Có {draftSessions.Count} buổi sinh hoạt/tập luyện chưa chốt điểm danh.");
        }

        // Parse period "yyyy-MM" thành khoảng ngày để EF Core dịch được sang SQL
        DateTime startOfMonth;
        DateTime endOfMonth;
        if (DateTime.TryParseExact(period, "yyyy-MM", null, System.Globalization.DateTimeStyles.None, out var parsedMonth))
        {
            startOfMonth = new DateTime(parsedMonth.Year, parsedMonth.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            endOfMonth = startOfMonth.AddMonths(1);
        }
        else
        {
            startOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            endOfMonth = startOfMonth.AddMonths(1);
        }

        var periodMatches = await _db.Matches
            .Where(m => (m.TeamAId == teamId || m.TeamBId == teamId) &&
                        m.MatchDateTime >= startOfMonth && m.MatchDateTime < endOfMonth &&
                        m.Status == MatchStatus.Scheduled)
            .ToListAsync();
        if (periodMatches.Any())
        {
            result.HasIncompleteData = true;
            result.IncompleteDataWarnings.Add($"Có {periodMatches.Count} trận đấu trong tháng chưa cập nhật kết quả.");
        }

        // Lấy danh sách cầu thủ của đội
        var players = await _db.Players
            .Where(p => p.TeamId == teamId && p.IsActive)
            .ToListAsync();

        // Lấy toàn bộ entries sổ AB của đội trong kỳ
        var entries = await _db.PlayerAbJournalEntries
            .Where(j => j.TeamId == teamId && j.StatisticalPeriod == period)
            .ToListAsync();

        // Lấy quy chế hiện hành
        var reg = await GetEffectiveRegulationAsync(teamId, DateTime.UtcNow);

        var list = new List<LeaderboardEntryDto>();

        foreach (var p in players)
        {
            var pEntries = entries.Where(e => e.PlayerId == p.Id).ToList();
            int totalAb = pEntries.Sum(e => e.Points);

            // Đếm các sự kiện ròng (Net count sau khi trừ các bút toán đảo)
            int attPoints = pEntries.Where(e => e.SourceDomain == AbSourceDomain.AttendanceSession).Sum(e => e.Points);
            int attCount = reg.PointsPerAttendance > 0 ? Math.Max(0, attPoints / reg.PointsPerAttendance) : 0;

            int goalPoints = pEntries.Where(e => e.SourceDomain == AbSourceDomain.MatchGoalEvent && e.SourceRole == "Scorer").Sum(e => e.Points);
            int goalCount = reg.PointsPerGoal > 0 ? Math.Max(0, goalPoints / reg.PointsPerGoal) : 0;

            int assistPoints = pEntries.Where(e => e.SourceDomain == AbSourceDomain.MatchGoalEvent && e.SourceRole == "Assist").Sum(e => e.Points);
            int assistCount = reg.PointsPerAssist > 0 ? Math.Max(0, assistPoints / reg.PointsPerAssist) : 0;

            int winPoints = pEntries.Where(e => e.SourceDomain == AbSourceDomain.MatchWin).Sum(e => e.Points);
            int winCount = reg.PointsPerWin > 0 ? Math.Max(0, winPoints / reg.PointsPerWin) : 0;

            list.Add(new LeaderboardEntryDto
            {
                PlayerId = p.Id,
                PlayerName = p.FullName,
                KitNumber = p.KitNumber,
                Position = p.Position,
                PhotoUrl = p.PhotoUrl,
                IsPublicProfile = p.IsPublicProfile,
                AttendedSessions = attCount,
                Goals = goalCount,
                Assists = assistCount,
                Wins = winCount,
                TotalAb = totalAb,
                CalculationBreakdown = $"{attCount} buổi (+{attPoints} AB) + {goalCount} bàn (+{goalPoints} AB) + {assistCount} kiến tạo (+{assistPoints} AB) + {winCount} thắng (+{winPoints} AB)"
            });
        }

        // Xếp hạng: Sắp xếp giảm dần theo TotalAb.
        // Xử lý đồng hạng chuẩn: các cầu thủ bằng điểm có Rank bằng nhau
        var sorted = list.OrderByDescending(x => x.TotalAb)
            .ThenByDescending(x => x.Goals)
            .ThenByDescending(x => x.AttendedSessions)
            .ToList();

        int currentRank = 1;
        for (int i = 0; i < sorted.Count; i++)
        {
            if (i > 0 && sorted[i].TotalAb < sorted[i - 1].TotalAb)
            {
                currentRank = i + 1;
            }
            sorted[i].Rank = currentRank;
        }

        result.Entries = sorted;

        // Lấy cúp đã trao (nếu có)
        result.ExistingAwards = await _db.MonthlyFireAwards
            .Include(a => a.Player)
            .Where(a => a.PeriodId == compPeriod.Id && a.PeriodRevision == compPeriod.Revision && !a.IsArchived)
            .ToListAsync();

        return result;
    }

    public async Task<PlayerAbSummaryDto> GetPlayerAbSummaryAsync(Guid teamId, Guid playerId, string? period = null)
    {
        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId && p.TeamId == teamId);
        if (player == null) throw new KeyNotFoundException("Không tìm thấy thông tin cầu thủ trong đội.");

        var allEntries = await _db.PlayerAbJournalEntries
            .Where(j => j.TeamId == teamId && j.PlayerId == playerId)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();

        string targetPeriod = period ?? CompetitionRules.LocalNow.ToString("yyyy-MM");

        int monthAb = allEntries.Where(e => e.StatisticalPeriod == targetPeriod).Sum(e => e.Points);
        int totalCumulativeAb = allEntries.Sum(e => e.Points);

        var awards = await _db.MonthlyFireAwards
            .Where(a => a.TeamId == teamId && a.PlayerId == playerId && !a.IsArchived)
            .OrderByDescending(a => a.StatisticalPeriod)
            .ToListAsync();

        return new PlayerAbSummaryDto
        {
            PlayerId = player.Id,
            PlayerName = player.FullName,
            KitNumber = player.KitNumber,
            Position = player.Position,
            CurrentMonthAb = monthAb,
            TotalCumulativeAb = totalCumulativeAb,
            JournalEntries = allEntries,
            Awards = awards
        };
    }

    public async Task<(bool Success, string Message)> FinalizePeriodAndAwardAsync(
        Guid teamId, string period, List<Guid> chosenPlayerIds, string decidedByUserId, string? notes)
    {
        using var tx = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync() : null;
        try
        {
            var compPeriod = await GetOrCreatePeriodAsync(teamId, period);

            if (compPeriod.Status == PeriodStatus.Closed)
            {
                return (false, "Kỳ thi đua này đã được chốt sổ trước đó.");
            }

            // Kiểm tra xem còn buổi hoặc trận nào chưa chốt không
            var draftSessions = await _db.AttendanceSessions
                .Where(s => s.TeamId == teamId && s.StatisticalPeriod == period && s.Status == SessionStatus.Draft)
                .AnyAsync();
            if (draftSessions)
            {
                return (false, "Không thể chốt kỳ khi vẫn còn buổi sinh hoạt ở trạng thái dự thảo.");
            }

            var leaderboard = await GetMonthlyLeaderboardAsync(teamId, period);

            if (leaderboard.HasIncompleteData)
                return (false, "Hãy hoàn tất các buổi và trận đấu trong tháng trước khi trao cúp.");
            chosenPlayerIds = chosenPlayerIds.Distinct().ToList();

            if (!chosenPlayerIds.Any())
            {
                return (false, "Vui lòng chọn cầu thủ nhận danh hiệu xuất sắc nhất tháng.");
            }

            // Kiểm tra: Không trao cúp cho người có 0 AB hoặc chưa có hoạt động hợp lệ
            foreach (var pId in chosenPlayerIds)
            {
                var entry = leaderboard.Entries.FirstOrDefault(e => e.PlayerId == pId);
                if (entry == null || entry.TotalAb <= 0 || entry.Rank != 1)
                {
                    return (false, "Cúp tháng chỉ dành cho cầu thủ có điểm AB cao nhất (hạng 1) và có điểm lớn hơn 0.");
                }
            }

            // Lưu danh hiệu cho các cầu thủ được chọn
            foreach (var pId in chosenPlayerIds)
            {
                var entry = leaderboard.Entries.First(e => e.PlayerId == pId);
                var award = new MonthlyFireAward
                {
                    PeriodId = compPeriod.Id,
                    TeamId = teamId,
                    PlayerId = pId,
                    StatisticalPeriod = period,
                    PeriodRevision = compPeriod.Revision,
                    TotalAbPoints = entry.TotalAb,
                    AttendedSessions = entry.AttendedSessions,
                    Goals = entry.Goals,
                    Assists = entry.Assists,
                    Wins = entry.Wins,
                    Rank = entry.Rank,
                    AwardTitle = "Cầu thủ xuất sắc nhất tháng",
                    DecidedByUserId = decidedByUserId,
                    DecidedAt = DateTime.UtcNow,
                    Notes = notes,
                    IsArchived = false
                };
                _db.MonthlyFireAwards.Add(award);
            }

            compPeriod.Status = PeriodStatus.Closed;
            compPeriod.ClosedAt = DateTime.UtcNow;
            compPeriod.ClosedByUserId = decidedByUserId;

            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();

            return (true, $"Đã chốt kỳ {period} và trao danh hiệu Cầu thủ xuất sắc nhất tháng thành công!");
        }
        catch (Exception ex)
        {
            if (tx != null) await tx.RollbackAsync();
            return (false, $"Lỗi khi chốt kỳ: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> ReopenPeriodAsync(Guid teamId, string period, string reason, string reopenedByUserId)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return (false, "Bắt buộc phải nhập lý do mở lại kỳ thi đua.");
        }

        using var tx = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync() : null;
        try
        {
            var compPeriod = await GetOrCreatePeriodAsync(teamId, period);

            if (compPeriod.Status != PeriodStatus.Closed)
            {
                return (false, "Kỳ thi đua hiện đang mở, không cần thao tác mở lại.");
            }

            // Đánh dấu các danh hiệu của revision cũ thành archived
            var oldAwards = await _db.MonthlyFireAwards
                .Where(a => a.PeriodId == compPeriod.Id && a.PeriodRevision == compPeriod.Revision)
                .ToListAsync();
            foreach (var a in oldAwards)
            {
                a.IsArchived = true;
            }

            compPeriod.Status = PeriodStatus.Open;
            compPeriod.Revision += 1;
            compPeriod.ReopenedAt = DateTime.UtcNow;
            compPeriod.ReopenedByUserId = reopenedByUserId;
            compPeriod.ReopenReason = reason;

            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();

            return (true, $"Đã mở lại kỳ thi đua {period} (Lần sửa #{compPeriod.Revision}). Bạn có thể điều chỉnh lại nguồn dữ liệu.");
        }
        catch (Exception ex)
        {
            if (tx != null) await tx.RollbackAsync();
            return (false, $"Lỗi khi mở lại kỳ: {ex.Message}");
        }
    }
}
