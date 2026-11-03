using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AloBall.Tests;

public class FireKeepingEngineTests
{
    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task StandardFormula_Attendance_2Goals_1Assist_Win_Should_Equal_10AB()
    {
        using var db = CreateInMemoryDbContext();
        var fireService = new FireKeepingService(db);

        var team = new Team { Name = "KLB FC", Slug = "klb-fc" };
        var player = new Player { TeamId = team.Id, FullName = "Nguyễn Văn A" };
        db.Teams.Add(team);
        db.Players.Add(player);
        await db.SaveChangesAsync();

        // 1. Buổi điểm danh: Finalized, Player có mặt -> +2 AB
        var session = new AttendanceSession
        {
            TeamId = team.Id,
            Title = "Buổi tập tuần",
            SessionDateTime = new DateTime(2026, 9, 10, 19, 0, 0, DateTimeKind.Utc),
            StatisticalPeriod = "2026-09",
            Status = SessionStatus.Finalized
        };
        db.AttendanceSessions.Add(session);
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            SessionId = session.Id,
            PlayerId = player.Id,
            Actual = ActualStatus.Present
        });
        await db.SaveChangesAsync();
        await fireService.SyncAttendanceSessionPointsAsync(session.Id);

        // 2. Trận đấu: Thắng 3 - 1, player tham gia thực tế, ghi 2 bàn, 1 kiến tạo
        var match = new MatchGame
        {
            TeamAId = team.Id,
            TeamBExternalName = "Đối thủ",
            MatchDateTime = new DateTime(2026, 9, 10, 20, 0, 0, DateTimeKind.Utc),
            Status = MatchStatus.Completed,
            ScoreA = 3,
            ScoreB = 1
        };
        db.Matches.Add(match);
        db.MatchPlayerLineups.Add(new MatchPlayerLineup
        {
            MatchId = match.Id,
            TeamId = team.Id,
            PlayerId = player.Id
        });

        // 2 bàn thắng của player
        db.MatchGoalEvents.Add(new MatchGoalEvent
        {
            MatchId = match.Id,
            TeamId = team.Id,
            ScorerPlayerId = player.Id,
            Minute = 15
        });
        db.MatchGoalEvents.Add(new MatchGoalEvent
        {
            MatchId = match.Id,
            TeamId = team.Id,
            ScorerPlayerId = player.Id,
            Minute = 30
        });
        // 1 kiến tạo của player
        var teammate = new Player { TeamId = team.Id, FullName = "Đồng đội B" };
        db.Players.Add(teammate);
        db.MatchGoalEvents.Add(new MatchGoalEvent
        {
            MatchId = match.Id,
            TeamId = team.Id,
            ScorerPlayerId = teammate.Id,
            AssistPlayerId = player.Id,
            Minute = 45
        });
        await db.SaveChangesAsync();
        await fireService.SyncMatchPointsAsync(match.Id);

        // Kiểm tra tổng AB của player trong kỳ 2026-09
        var summary = await fireService.GetPlayerAbSummaryAsync(team.Id, player.Id, "2026-09");
        // Có mặt: +2, 2 Bàn: +6, 1 Kiến tạo: +1, Thắng: +1 => Tổng = 10 AB
        Assert.Equal(10, summary.CurrentMonthAb);
        Assert.Equal(10, summary.TotalCumulativeAb);
    }

    [Fact]
    public async Task ReplayingSameEvent_DoesNotDuplicatePoints()
    {
        using var db = CreateInMemoryDbContext();
        var fireService = new FireKeepingService(db);

        var team = new Team { Name = "KLB FC", Slug = "klb-fc" };
        var player = new Player { TeamId = team.Id, FullName = "Nguyễn Văn A" };
        db.Teams.Add(team);
        db.Players.Add(player);

        var session = new AttendanceSession
        {
            TeamId = team.Id,
            SessionDateTime = new DateTime(2026, 9, 10, 19, 0, 0, DateTimeKind.Utc),
            StatisticalPeriod = "2026-09",
            Status = SessionStatus.Finalized
        };
        db.AttendanceSessions.Add(session);
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            SessionId = session.Id,
            PlayerId = player.Id,
            Actual = ActualStatus.Present
        });
        await db.SaveChangesAsync();

        // Đồng bộ lần 1
        await fireService.SyncAttendanceSessionPointsAsync(session.Id);
        // Đồng bộ lần 2 (gọi lặp)
        await fireService.SyncAttendanceSessionPointsAsync(session.Id);

        var summary = await fireService.GetPlayerAbSummaryAsync(team.Id, player.Id, "2026-09");
        Assert.Equal(2, summary.CurrentMonthAb);
    }

    [Fact]
    public async Task SameSession_MultipleMatches_Attendance_Counts_Only_Once()
    {
        using var db = CreateInMemoryDbContext();
        var fireService = new FireKeepingService(db);

        var team = new Team { Name = "KLB FC", Slug = "klb-fc" };
        var player = new Player { TeamId = team.Id, FullName = "Nguyễn Văn A" };
        db.Teams.Add(team);
        db.Players.Add(player);

        // 1 Buổi sinh hoạt có 2 trận đấu
        var session = new AttendanceSession
        {
            TeamId = team.Id,
            SessionDateTime = new DateTime(2026, 9, 10, 19, 0, 0, DateTimeKind.Utc),
            StatisticalPeriod = "2026-09",
            Status = SessionStatus.Finalized
        };
        db.AttendanceSessions.Add(session);
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            SessionId = session.Id,
            PlayerId = player.Id,
            Actual = ActualStatus.Present
        });

        var match1 = new MatchGame
        {
            TeamAId = team.Id,
            MatchDateTime = new DateTime(2026, 9, 10, 19, 30, 0, DateTimeKind.Utc),
            Status = MatchStatus.Completed,
            ScoreA = 2,
            ScoreB = 1
        };
        var match2 = new MatchGame
        {
            TeamAId = team.Id,
            MatchDateTime = new DateTime(2026, 9, 10, 20, 30, 0, DateTimeKind.Utc),
            Status = MatchStatus.Completed,
            ScoreA = 1,
            ScoreB = 0
        };
        db.Matches.AddRange(match1, match2);
        db.MatchPlayerLineups.Add(new MatchPlayerLineup { MatchId = match1.Id, TeamId = team.Id, PlayerId = player.Id });
        db.MatchPlayerLineups.Add(new MatchPlayerLineup { MatchId = match2.Id, TeamId = team.Id, PlayerId = player.Id });
        await db.SaveChangesAsync();

        await fireService.SyncAttendanceSessionPointsAsync(session.Id);
        await fireService.SyncMatchPointsAsync(match1.Id);
        await fireService.SyncMatchPointsAsync(match2.Id);

        var summary = await fireService.GetPlayerAbSummaryAsync(team.Id, player.Id, "2026-09");
        // Điểm có mặt chỉ tính 1 lần (+2 AB), Thắng trận 1 (+1 AB), Thắng trận 2 (+1 AB) => Tổng 4 AB
        Assert.Equal(4, summary.CurrentMonthAb);
    }

    [Fact]
    public async Task AbsentPlayer_DoesNotReceiveWinReward()
    {
        using var db = CreateInMemoryDbContext();
        var fireService = new FireKeepingService(db);

        var team = new Team { Name = "KLB FC", Slug = "klb-fc" };
        var playerPlaying = new Player { TeamId = team.Id, FullName = "Người đá" };
        var playerAbsent = new Player { TeamId = team.Id, FullName = "Người vắng" };
        db.Teams.Add(team);
        db.Players.AddRange(playerPlaying, playerAbsent);

        var match = new MatchGame
        {
            TeamAId = team.Id,
            MatchDateTime = new DateTime(2026, 9, 10, 20, 0, 0, DateTimeKind.Utc),
            Status = MatchStatus.Completed,
            ScoreA = 2,
            ScoreB = 0
        };
        db.Matches.Add(match);
        // Chỉ playerPlaying có trong Lineup
        db.MatchPlayerLineups.Add(new MatchPlayerLineup { MatchId = match.Id, TeamId = team.Id, PlayerId = playerPlaying.Id });
        await db.SaveChangesAsync();

        await fireService.SyncMatchPointsAsync(match.Id);

        var summaryPlaying = await fireService.GetPlayerAbSummaryAsync(team.Id, playerPlaying.Id, "2026-09");
        var summaryAbsent = await fireService.GetPlayerAbSummaryAsync(team.Id, playerAbsent.Id, "2026-09");

        Assert.Equal(1, summaryPlaying.CurrentMonthAb);
        Assert.Equal(0, summaryAbsent.CurrentMonthAb);
    }

    [Fact]
    public async Task ModifyGoal_Or_DeleteGoal_UpdatesPointsCorrectly()
    {
        using var db = CreateInMemoryDbContext();
        var fireService = new FireKeepingService(db);

        var team = new Team { Name = "KLB FC", Slug = "klb-fc" };
        var playerA = new Player { TeamId = team.Id, FullName = "Cầu thủ A" };
        var playerB = new Player { TeamId = team.Id, FullName = "Cầu thủ B" };
        db.Teams.Add(team);
        db.Players.AddRange(playerA, playerB);

        var match = new MatchGame
        {
            TeamAId = team.Id,
            MatchDateTime = new DateTime(2026, 9, 10, 20, 0, 0, DateTimeKind.Utc),
            Status = MatchStatus.Completed,
            ScoreA = 1,
            ScoreB = 1
        };
        db.Matches.Add(match);

        // Ban đầu gán bàn thắng cho A
        var goal = new MatchGoalEvent
        {
            MatchId = match.Id,
            TeamId = team.Id,
            ScorerPlayerId = playerA.Id,
            Revision = 1
        };
        db.MatchGoalEvents.Add(goal);
        await db.SaveChangesAsync();

        await fireService.SyncMatchPointsAsync(match.Id);
        var summaryA1 = await fireService.GetPlayerAbSummaryAsync(team.Id, playerA.Id, "2026-09");
        Assert.Equal(3, summaryA1.CurrentMonthAb);

        // Đổi người ghi bàn từ A sang B (Revision 2)
        goal.ScorerPlayerId = playerB.Id;
        goal.Revision = 2;
        await db.SaveChangesAsync();
        await fireService.SyncMatchPointsAsync(match.Id);

        var summaryA2 = await fireService.GetPlayerAbSummaryAsync(team.Id, playerA.Id, "2026-09");
        var summaryB2 = await fireService.GetPlayerAbSummaryAsync(team.Id, playerB.Id, "2026-09");
        Assert.Equal(0, summaryA2.CurrentMonthAb); // A đã bị đảo điểm
        Assert.Equal(3, summaryB2.CurrentMonthAb); // B được cộng điểm

        // Hủy bàn thắng
        db.MatchGoalEvents.Remove(goal);
        await db.SaveChangesAsync();
        await fireService.SyncMatchPointsAsync(match.Id);

        var summaryB3 = await fireService.GetPlayerAbSummaryAsync(team.Id, playerB.Id, "2026-09");
        Assert.Equal(0, summaryB3.CurrentMonthAb); // B đã bị đảo điểm
    }

    [Fact]
    public async Task ChangingWinToDraw_RevokesWinPoints()
    {
        using var db = CreateInMemoryDbContext();
        var fireService = new FireKeepingService(db);

        var team = new Team { Name = "KLB FC", Slug = "klb-fc" };
        var player = new Player { TeamId = team.Id, FullName = "Cầu thủ" };
        db.Teams.Add(team);
        db.Players.Add(player);

        var match = new MatchGame
        {
            TeamAId = team.Id,
            MatchDateTime = new DateTime(2026, 9, 10, 20, 0, 0, DateTimeKind.Utc),
            Status = MatchStatus.Completed,
            ScoreA = 2,
            ScoreB = 1
        };
        db.Matches.Add(match);
        db.MatchPlayerLineups.Add(new MatchPlayerLineup { MatchId = match.Id, TeamId = team.Id, PlayerId = player.Id });
        await db.SaveChangesAsync();

        await fireService.SyncMatchPointsAsync(match.Id);
        var summary1 = await fireService.GetPlayerAbSummaryAsync(team.Id, player.Id, "2026-09");
        Assert.Equal(1, summary1.CurrentMonthAb); // Đang thắng -> +1 AB

        // Sửa tỉ số thành hòa 2 - 2
        match.ScoreB = 2;
        await db.SaveChangesAsync();
        await fireService.SyncMatchPointsAsync(match.Id);

        var summary2 = await fireService.GetPlayerAbSummaryAsync(team.Id, player.Id, "2026-09");
        Assert.Equal(0, summary2.CurrentMonthAb); // Đã bị thu hồi điểm thưởng thắng
    }

    [Fact]
    public async Task TwoTeams_AreCompletelyIsolated()
    {
        using var db = CreateInMemoryDbContext();
        var fireService = new FireKeepingService(db);

        var teamA = new Team { Name = "Đội A", Slug = "doi-a" };
        var teamB = new Team { Name = "Đội B", Slug = "doi-b" };
        var playerA = new Player { TeamId = teamA.Id, FullName = "Cầu thủ A" };
        var playerB = new Player { TeamId = teamB.Id, FullName = "Cầu thủ B" };
        db.Teams.AddRange(teamA, teamB);
        db.Players.AddRange(playerA, playerB);
        await db.SaveChangesAsync();

        var sessionA = new AttendanceSession
        {
            TeamId = teamA.Id,
            SessionDateTime = new DateTime(2026, 9, 10, 19, 0, 0, DateTimeKind.Utc),
            StatisticalPeriod = "2026-09",
            Status = SessionStatus.Finalized
        };
        db.AttendanceSessions.Add(sessionA);
        db.AttendanceRecords.Add(new AttendanceRecord { SessionId = sessionA.Id, PlayerId = playerA.Id, Actual = ActualStatus.Present });
        await db.SaveChangesAsync();
        await fireService.SyncAttendanceSessionPointsAsync(sessionA.Id);

        var lbA = await fireService.GetMonthlyLeaderboardAsync(teamA.Id, "2026-09");
        var lbB = await fireService.GetMonthlyLeaderboardAsync(teamB.Id, "2026-09");

        Assert.Single(lbA.Entries);
        Assert.Equal(playerA.Id, lbA.Entries.First().PlayerId);
        Assert.Equal(2, lbA.Entries.First().TotalAb);

        Assert.Single(lbB.Entries);
        Assert.Equal(playerB.Id, lbB.Entries.First().PlayerId);
        Assert.Equal(0, lbB.Entries.First().TotalAb);
    }

    [Fact]
    public async Task TiedScore_DoesNotPickArbitraryWinner_RequiresAdminChoice()
    {
        using var db = CreateInMemoryDbContext();
        var fireService = new FireKeepingService(db);

        var team = new Team { Name = "KLB FC", Slug = "klb-fc" };
        var player1 = new Player { TeamId = team.Id, FullName = "Cầu thủ 1" };
        var player2 = new Player { TeamId = team.Id, FullName = "Cầu thủ 2" };
        db.Teams.Add(team);
        db.Players.AddRange(player1, player2);

        var session = new AttendanceSession
        {
            TeamId = team.Id,
            SessionDateTime = new DateTime(2026, 9, 10, 19, 0, 0, DateTimeKind.Utc),
            StatisticalPeriod = "2026-09",
            Status = SessionStatus.Finalized
        };
        db.AttendanceSessions.Add(session);
        // Cả 2 cầu thủ đều có mặt -> Bằng điểm nhau (2 AB)
        db.AttendanceRecords.Add(new AttendanceRecord { SessionId = session.Id, PlayerId = player1.Id, Actual = ActualStatus.Present });
        db.AttendanceRecords.Add(new AttendanceRecord { SessionId = session.Id, PlayerId = player2.Id, Actual = ActualStatus.Present });
        await db.SaveChangesAsync();
        await fireService.SyncAttendanceSessionPointsAsync(session.Id);

        var lb = await fireService.GetMonthlyLeaderboardAsync(team.Id, "2026-09");
        var rank1Entries = lb.Entries.Where(e => e.Rank == 1).ToList();

        // Khẳng định: Đồng hạng 1, không tự ý chọn 1 người
        Assert.Equal(2, rank1Entries.Count);

        // Hỗ trợ trao cúp đồng giải cho cả 2 người
        var awardResult = await fireService.FinalizePeriodAndAwardAsync(
            team.Id, "2026-09", new List<Guid> { player1.Id, player2.Id }, "admin_user", "Đồng giải xuất sắc nhất tháng");

        Assert.True(awardResult.Success);
        var awards = await db.MonthlyFireAwards.Where(a => a.TeamId == team.Id).ToListAsync();
        Assert.Equal(2, awards.Count);
    }

    [Fact]
    public async Task FinalizedPeriod_CannotBeFinalizedTwice_OrModifiedWithoutReopen()
    {
        using var db = CreateInMemoryDbContext();
        var fireService = new FireKeepingService(db);

        var team = new Team { Name = "KLB FC", Slug = "klb-fc" };
        var player = new Player { TeamId = team.Id, FullName = "Cầu thủ" };
        db.Teams.Add(team);
        db.Players.Add(player);

        var session = new AttendanceSession
        {
            TeamId = team.Id,
            SessionDateTime = new DateTime(2026, 9, 10, 19, 0, 0, DateTimeKind.Utc),
            StatisticalPeriod = "2026-09",
            Status = SessionStatus.Finalized
        };
        db.AttendanceSessions.Add(session);
        db.AttendanceRecords.Add(new AttendanceRecord { SessionId = session.Id, PlayerId = player.Id, Actual = ActualStatus.Present });
        await db.SaveChangesAsync();
        await fireService.SyncAttendanceSessionPointsAsync(session.Id);

        // Chốt kỳ lần 1
        var res1 = await fireService.FinalizePeriodAndAwardAsync(team.Id, "2026-09", new List<Guid> { player.Id }, "admin", "Chốt lần 1");
        Assert.True(res1.Success);

        // Chốt lặp kỳ đã chốt -> Phải từ chối
        var res2 = await fireService.FinalizePeriodAndAwardAsync(team.Id, "2026-09", new List<Guid> { player.Id }, "admin", "Chốt lại");
        Assert.False(res2.Success);

        // Cố tình sửa nguồn khi kỳ đã chốt -> Bị chặn
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await fireService.SyncAttendanceSessionPointsAsync(session.Id);
        });

        // Mở lại kỳ có lý do
        var reopenRes = await fireService.ReopenPeriodAsync(team.Id, "2026-09", "Phát hiện nhập sót bàn thắng", "admin");
        Assert.True(reopenRes.Success);

        // Sau khi mở lại, có thể cập nhật nguồn bình thường
        var ex = await Record.ExceptionAsync(async () =>
        {
            await fireService.SyncAttendanceSessionPointsAsync(session.Id);
        });
        Assert.Null(ex);
    }

    [Fact]
    public async Task GoalScorer_Cycle_A_to_B_to_A_Computes_Correct_Ledger()
    {
        using var db = CreateInMemoryDbContext();
        var fireService = new FireKeepingService(db);

        var team = new Team { Name = "KLB FC", Slug = "klb-fc" };
        var playerA = new Player { TeamId = team.Id, FullName = "Cầu thủ A" };
        var playerB = new Player { TeamId = team.Id, FullName = "Cầu thủ B" };
        db.Teams.Add(team);
        db.Players.AddRange(playerA, playerB);

        var match = new MatchGame
        {
            TeamAId = team.Id,
            MatchDateTime = new DateTime(2026, 9, 10, 20, 0, 0, DateTimeKind.Utc),
            Status = MatchStatus.Completed,
            ScoreA = 1,
            ScoreB = 0
        };
        db.Matches.Add(match);

        // Bước 1: Ghi nhận bàn thắng cho A (Rev 1)
        var goal = new MatchGoalEvent
        {
            MatchId = match.Id,
            TeamId = team.Id,
            ScorerPlayerId = playerA.Id,
            Revision = 1
        };
        db.MatchGoalEvents.Add(goal);
        await db.SaveChangesAsync();
        await fireService.SyncMatchPointsAsync(match.Id);

        // Bước 2: Đổi sang B (Rev 2)
        goal.ScorerPlayerId = playerB.Id;
        goal.Revision = 2;
        await db.SaveChangesAsync();
        await fireService.SyncMatchPointsAsync(match.Id);

        // Bước 3: Đổi ngược lại về A (Rev 3)
        goal.ScorerPlayerId = playerA.Id;
        goal.Revision = 3;
        await db.SaveChangesAsync();
        await fireService.SyncMatchPointsAsync(match.Id);

        var summaryA = await fireService.GetPlayerAbSummaryAsync(team.Id, playerA.Id, "2026-09");
        var summaryB = await fireService.GetPlayerAbSummaryAsync(team.Id, playerB.Id, "2026-09");

        // Chu kỳ A -> B -> A:
        // A nhận: +3 (b1), -3 (đảo ở b2), +3 (b3) = 3 AB
        // B nhận: +3 (b2), -3 (đảo ở b3) = 0 AB
        Assert.Equal(3, summaryA.CurrentMonthAb);
        Assert.Equal(0, summaryB.CurrentMonthAb);
    }

    [Fact]
    public async Task NewRegulationVersion_DoesNotRetroactivelyAlterPastPoints()
    {
        using var db = CreateInMemoryDbContext();
        var fireService = new FireKeepingService(db);

        var team = new Team { Name = "KLB FC", Slug = "klb-fc" };
        var player = new Player { TeamId = team.Id, FullName = "Cầu thủ" };
        db.Teams.Add(team);
        db.Players.Add(player);

        // Quy chế v1 áp dụng từ tháng 1: Điểm có mặt = 2 AB
        var regV1 = new TeamFireRegulation
        {
            TeamId = team.Id,
            Version = 1,
            EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PointsPerAttendance = 2
        };
        db.TeamFireRegulations.Add(regV1);
        await db.SaveChangesAsync();

        var sessionPast = new AttendanceSession
        {
            TeamId = team.Id,
            SessionDateTime = new DateTime(2026, 8, 10, 19, 0, 0, DateTimeKind.Utc),
            StatisticalPeriod = "2026-08",
            Status = SessionStatus.Finalized
        };
        db.AttendanceSessions.Add(sessionPast);
        db.AttendanceRecords.Add(new AttendanceRecord { SessionId = sessionPast.Id, PlayerId = player.Id, Actual = ActualStatus.Present });
        await db.SaveChangesAsync();
        await fireService.SyncAttendanceSessionPointsAsync(sessionPast.Id);

        var summaryBefore = await fireService.GetPlayerAbSummaryAsync(team.Id, player.Id, "2026-08");
        Assert.Equal(2, summaryBefore.TotalCumulativeAb);

        // Đội ban hành Quy chế mới v2 áp dụng từ tháng 9: Điểm có mặt = 5 AB
        var regV2 = new TeamFireRegulation
        {
            TeamId = team.Id,
            Version = 2,
            EffectiveFrom = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            PointsPerAttendance = 5
        };
        db.TeamFireRegulations.Add(regV2);
        await db.SaveChangesAsync();

        // Tạo buổi mới vào tháng 9
        var sessionNew = new AttendanceSession
        {
            TeamId = team.Id,
            SessionDateTime = new DateTime(2026, 9, 10, 19, 0, 0, DateTimeKind.Utc),
            StatisticalPeriod = "2026-09",
            Status = SessionStatus.Finalized
        };
        db.AttendanceSessions.Add(sessionNew);
        db.AttendanceRecords.Add(new AttendanceRecord { SessionId = sessionNew.Id, PlayerId = player.Id, Actual = ActualStatus.Present });
        await db.SaveChangesAsync();
        await fireService.SyncAttendanceSessionPointsAsync(sessionNew.Id);

        // Kiểm tra:
        // Tháng 8 (quá khứ) vẫn giữ nguyên theo v1: 2 AB
        // Tháng 9 (hiện tại) tính theo v2: 5 AB
        // Tổng tích lũy = 2 + 5 = 7 AB
        var summaryPast = (await fireService.GetMonthlyLeaderboardAsync(team.Id, "2026-08")).Entries.First();
        var summaryCurrent = (await fireService.GetMonthlyLeaderboardAsync(team.Id, "2026-09")).Entries.First();
        var summaryTotal = await fireService.GetPlayerAbSummaryAsync(team.Id, player.Id, "2026-09");

        Assert.Equal(2, summaryPast.TotalAb);
        Assert.Equal(5, summaryCurrent.TotalAb);
        Assert.Equal(7, summaryTotal.TotalCumulativeAb);
    }

    [Fact]
    public async Task InternalMatch_TeamSide_Winners_ReceivePoints_And_Losers_DoNot()
    {
        using var db = CreateInMemoryDbContext();
        var fireService = new FireKeepingService(db);

        var team = new Team { Name = "CLB Nội Bộ", Slug = "clb-noi-bo" };
        var playerA = new Player { TeamId = team.Id, FullName = "Cầu Thủ Bên A" };
        var playerB = new Player { TeamId = team.Id, FullName = "Cầu Thủ Bên B" };
        db.Teams.Add(team);
        db.Players.AddRange(playerA, playerB);
        await db.SaveChangesAsync();

        // Trận đấu nội bộ: Tỉ số Bên A 2 - 1 Bên B
        var match = new MatchGame
        {
            Category = MatchCategory.Internal,
            TeamAId = team.Id,
            MatchDateTime = new DateTime(2026, 9, 15, 19, 0, 0, DateTimeKind.Utc),
            Status = MatchStatus.Completed,
            ScoreA = 2,
            ScoreB = 1
        };
        db.Matches.Add(match);

        // Player A thuộc bên A, Player B thuộc bên B
        db.MatchPlayerLineups.Add(new MatchPlayerLineup
        {
            MatchId = match.Id,
            TeamId = team.Id,
            PlayerId = playerA.Id,
            TeamSide = "A"
        });
        db.MatchPlayerLineups.Add(new MatchPlayerLineup
        {
            MatchId = match.Id,
            TeamId = team.Id,
            PlayerId = playerB.Id,
            TeamSide = "B"
        });
        await db.SaveChangesAsync();

        await fireService.SyncMatchPointsAsync(match.Id);

        var summaryA = await fireService.GetPlayerAbSummaryAsync(team.Id, playerA.Id, "2026-09");
        var summaryB = await fireService.GetPlayerAbSummaryAsync(team.Id, playerB.Id, "2026-09");

        // Bên A thắng: Player A được +1 AB thắng trận
        Assert.Equal(1, summaryA.TotalCumulativeAb);
        // Bên B thua: Player B được 0 AB
        Assert.Equal(0, summaryB.TotalCumulativeAb);
    }
}
