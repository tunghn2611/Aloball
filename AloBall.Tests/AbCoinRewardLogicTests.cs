using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AloBall.Tests;

public class AbCoinRewardLogicTests
{
    private ApplicationDbContext CreateInMemoryDbContext(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName ?? Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    // 1. Correct attendance reward: +2 AB Coin
    [Fact]
    public async Task AttendanceReward_Is_Plus2_Coins()
    {
        using var db = CreateInMemoryDbContext();
        var fireService = new FireKeepingService(db);

        var team = new Team { Name = "Đội Test", Slug = "doi-test" };
        var player = new Player { TeamId = team.Id, FullName = "Cầu Thủ A" };
        db.Teams.Add(team);
        db.Players.Add(player);

        var session = new AttendanceSession
        {
            TeamId = team.Id,
            SessionDateTime = new DateTime(2026, 10, 5, 19, 0, 0, DateTimeKind.Utc),
            StatisticalPeriod = "2026-10",
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

        var entry = await db.PlayerAbJournalEntries
            .FirstOrDefaultAsync(j => j.PlayerId == player.Id && j.SourceRole == "Attendance");

        Assert.NotNull(entry);
        Assert.Equal(2, entry.Points);
        Assert.Equal(AbSourceDomain.AttendanceSession, entry.SourceDomain);

        var summary = await fireService.GetPlayerAbSummaryAsync(team.Id, player.Id, "2026-10");
        Assert.Equal(2, summary.CurrentMonthAb);
    }

    // 2. Correct goal reward: +3 AB Coin
    [Fact]
    public async Task GoalReward_Is_Plus3_Coins()
    {
        using var db = CreateInMemoryDbContext();
        var fireService = new FireKeepingService(db);

        var team = new Team { Name = "Đội Test", Slug = "doi-test" };
        var player = new Player { TeamId = team.Id, FullName = "Tiền Đạo" };
        db.Teams.Add(team);
        db.Players.Add(player);

        var match = new MatchGame
        {
            TeamAId = team.Id,
            Category = MatchCategory.Internal,
            Status = MatchStatus.Completed,
            ScoreA = 1,
            ScoreB = 0,
            ConfirmedByTeamA = true,
            MatchDateTime = new DateTime(2026, 10, 10, 19, 0, 0, DateTimeKind.Utc)
        };
        db.Matches.Add(match);

        var goal = new MatchGoalEvent
        {
            MatchId = match.Id,
            TeamId = team.Id,
            ScorerPlayerId = player.Id,
            Minute = 25
        };
        db.MatchGoalEvents.Add(goal);
        await db.SaveChangesAsync();

        await fireService.SyncMatchPointsAsync(match.Id);

        var goalEntry = await db.PlayerAbJournalEntries
            .FirstOrDefaultAsync(j => j.PlayerId == player.Id && j.SourceRole == "Scorer" && j.Points > 0);

        Assert.NotNull(goalEntry);
        Assert.Equal(3, goalEntry.Points);
        Assert.Equal(AbSourceDomain.MatchGoalEvent, goalEntry.SourceDomain);
    }

    // 3. Correct assist reward: +1 AB Coin
    [Fact]
    public async Task AssistReward_Is_Plus1_Coin()
    {
        using var db = CreateInMemoryDbContext();
        var fireService = new FireKeepingService(db);

        var team = new Team { Name = "Đội Test", Slug = "doi-test" };
        var scorer = new Player { TeamId = team.Id, FullName = "Tiền Đạo" };
        var assister = new Player { TeamId = team.Id, FullName = "Tiền Vệ Kiến Tạo" };
        db.Teams.Add(team);
        db.Players.AddRange(scorer, assister);

        var match = new MatchGame
        {
            TeamAId = team.Id,
            Category = MatchCategory.Internal,
            Status = MatchStatus.Completed,
            ScoreA = 1,
            ScoreB = 0,
            ConfirmedByTeamA = true,
            MatchDateTime = new DateTime(2026, 10, 10, 19, 0, 0, DateTimeKind.Utc)
        };
        db.Matches.Add(match);

        var goal = new MatchGoalEvent
        {
            MatchId = match.Id,
            TeamId = team.Id,
            ScorerPlayerId = scorer.Id,
            AssistPlayerId = assister.Id,
            Minute = 40
        };
        db.MatchGoalEvents.Add(goal);
        await db.SaveChangesAsync();

        await fireService.SyncMatchPointsAsync(match.Id);

        var assistEntry = await db.PlayerAbJournalEntries
            .FirstOrDefaultAsync(j => j.PlayerId == assister.Id && j.SourceRole == "Assist" && j.Points > 0);

        Assert.NotNull(assistEntry);
        Assert.Equal(1, assistEntry.Points);
        Assert.Equal(AbSourceDomain.MatchGoalEvent, assistEntry.SourceDomain);
    }

    // 4. Correct winner reward: +1 AB Coin
    [Fact]
    public async Task WinnerReward_Is_Plus1_Coin()
    {
        using var db = CreateInMemoryDbContext();
        var fireService = new FireKeepingService(db);

        var teamA = new Team { Name = "Đội Thắng", Slug = "doi-thang" };
        var teamB = new Team { Name = "Đội Thua", Slug = "doi-thua" };
        var playerA = new Player { TeamId = teamA.Id, FullName = "Cầu Thủ Thắng" };
        var playerB = new Player { TeamId = teamB.Id, FullName = "Cầu Thủ Thua" };
        db.Teams.AddRange(teamA, teamB);
        db.Players.AddRange(playerA, playerB);

        var match = new MatchGame
        {
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            Category = MatchCategory.Friendly,
            Status = MatchStatus.Completed,
            ScoreA = 2,
            ScoreB = 0,
            ConfirmedByTeamA = true,
            ConfirmedByTeamB = true,
            ConfirmedAt = DateTime.UtcNow,
            MatchDateTime = new DateTime(2026, 10, 10, 19, 0, 0, DateTimeKind.Utc)
        };
        db.Matches.Add(match);
        db.MatchPlayerLineups.Add(new MatchPlayerLineup { MatchId = match.Id, TeamId = teamA.Id, PlayerId = playerA.Id });
        db.MatchPlayerLineups.Add(new MatchPlayerLineup { MatchId = match.Id, TeamId = teamB.Id, PlayerId = playerB.Id });
        await db.SaveChangesAsync();

        await fireService.SyncMatchPointsAsync(match.Id);

        var winEntry = await db.PlayerAbJournalEntries
            .FirstOrDefaultAsync(j => j.PlayerId == playerA.Id && j.SourceRole == "Winner" && j.Points > 0);

        Assert.NotNull(winEntry);
        Assert.Equal(1, winEntry.Points);
        Assert.Equal(AbSourceDomain.MatchWin, winEntry.SourceDomain);

        // Đội thua không nhận được điểm thắng
        var loseEntry = await db.PlayerAbJournalEntries
            .FirstOrDefaultAsync(j => j.PlayerId == playerB.Id && j.SourceRole == "Winner");
        Assert.Null(loseEntry);
    }

    // 5. No reward before confirmed match completion (bilateral confirmation required)
    [Fact]
    public async Task NoReward_Before_Confirmed_Match_Completion()
    {
        using var db = CreateInMemoryDbContext();
        var fireService = new FireKeepingService(db);

        var teamA = new Team { Name = "Đội A", Slug = "doi-a" };
        var teamB = new Team { Name = "Đội B", Slug = "doi-b" };
        var playerA = new Player { TeamId = teamA.Id, FullName = "Cầu Thủ A" };
        db.Teams.AddRange(teamA, teamB);
        db.Players.Add(playerA);

        // Đội A nhập tỉ số 2 - 1, nhưng Đội B chưa xác nhận đối soát (ConfirmedByTeamB = false)
        var match = new MatchGame
        {
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            Category = MatchCategory.Friendly,
            Status = MatchStatus.Completed,
            ScoreA = 2,
            ScoreB = 1,
            ConfirmedByTeamA = true,
            ConfirmedByTeamB = false, // Chưa xác nhận đối soát từ đội bạn
            ConfirmedAt = null,
            MatchDateTime = new DateTime(2026, 10, 10, 19, 0, 0, DateTimeKind.Utc)
        };
        db.Matches.Add(match);
        db.MatchPlayerLineups.Add(new MatchPlayerLineup { MatchId = match.Id, TeamId = teamA.Id, PlayerId = playerA.Id });
        db.MatchGoalEvents.Add(new MatchGoalEvent { MatchId = match.Id, TeamId = teamA.Id, ScorerPlayerId = playerA.Id, Minute = 10 });
        await db.SaveChangesAsync();

        // Đồng bộ điểm trận
        await fireService.SyncMatchPointsAsync(match.Id);

        // Vì chưa có xác nhận từ 2 bên nên chưa được cấp bất kỳ đồng AB Coin nào
        var entries = await db.PlayerAbJournalEntries
            .Where(j => j.SourceId == match.Id && j.Points > 0)
            .ToListAsync();
        Assert.Empty(entries);

        var summary = await fireService.GetPlayerAbSummaryAsync(teamA.Id, playerA.Id, "2026-10");
        Assert.Equal(0, summary.CurrentMonthAb);
    }

    // 6. Repeated completion does not duplicate rewards
    [Fact]
    public async Task RepeatedCompletion_DoesNotDuplicate_Rewards()
    {
        using var db = CreateInMemoryDbContext();
        var fireService = new FireKeepingService(db);

        var team = new Team { Name = "Đội Test", Slug = "doi-test" };
        var player = new Player { TeamId = team.Id, FullName = "Cầu Thủ A" };
        db.Teams.Add(team);
        db.Players.Add(player);

        var match = new MatchGame
        {
            TeamAId = team.Id,
            Category = MatchCategory.Internal,
            Status = MatchStatus.Completed,
            ScoreA = 1,
            ScoreB = 0,
            ConfirmedByTeamA = true,
            MatchDateTime = new DateTime(2026, 10, 10, 19, 0, 0, DateTimeKind.Utc)
        };
        db.Matches.Add(match);
        db.MatchPlayerLineups.Add(new MatchPlayerLineup { MatchId = match.Id, TeamId = team.Id, PlayerId = player.Id, TeamSide = "A" });
        db.MatchGoalEvents.Add(new MatchGoalEvent { MatchId = match.Id, TeamId = team.Id, ScorerPlayerId = player.Id, Minute = 15 });
        await db.SaveChangesAsync();

        // Gọi đồng bộ lần 1
        await fireService.SyncMatchPointsAsync(match.Id);
        // Gọi đồng bộ lần 2, lần 3 (lặp lại hoàn tất)
        await fireService.SyncMatchPointsAsync(match.Id);
        await fireService.SyncMatchPointsAsync(match.Id);

        var activeEntries = await db.PlayerAbJournalEntries
            .Where(j => j.PlayerId == player.Id && j.Points > 0)
            .ToListAsync();

        // 1 Goal (+3) + 1 Win (+1) = 2 entries, không bị trùng lặp
        Assert.Equal(2, activeEntries.Count);

        var summary = await fireService.GetPlayerAbSummaryAsync(team.Id, player.Id, "2026-10");
        Assert.Equal(4, summary.CurrentMonthAb);
    }

    // 7. Repeated result confirmation does not duplicate rewards
    [Fact]
    public async Task RepeatedResultConfirmation_DoesNotDuplicate_Rewards()
    {
        using var db = CreateInMemoryDbContext();
        var fireService = new FireKeepingService(db);

        var teamA = new Team { Name = "Đội A", Slug = "doi-a" };
        var teamB = new Team { Name = "Đội B", Slug = "doi-b" };
        var playerA = new Player { TeamId = teamA.Id, FullName = "Cầu Thủ A" };
        db.Teams.AddRange(teamA, teamB);
        db.Players.Add(playerA);

        var match = new MatchGame
        {
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            Category = MatchCategory.Friendly,
            Status = MatchStatus.Completed,
            ScoreA = 1,
            ScoreB = 0,
            ConfirmedByTeamA = true,
            ConfirmedByTeamB = true,
            ConfirmedAt = DateTime.UtcNow,
            MatchDateTime = new DateTime(2026, 10, 10, 19, 0, 0, DateTimeKind.Utc)
        };
        db.Matches.Add(match);
        db.MatchPlayerLineups.Add(new MatchPlayerLineup { MatchId = match.Id, TeamId = teamA.Id, PlayerId = playerA.Id });
        await db.SaveChangesAsync();

        // Xác nhận lần 1
        await fireService.SyncMatchPointsAsync(match.Id);

        // Xác nhận lại lần 2 (người dùng bấm xác nhận lại hoặc retry)
        match.ConfirmedByTeamB = true;
        match.ConfirmedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await fireService.SyncMatchPointsAsync(match.Id);

        var winEntries = await db.PlayerAbJournalEntries
            .Where(j => j.PlayerId == playerA.Id && j.SourceRole == "Winner" && j.Points > 0)
            .ToListAsync();

        Assert.Single(winEntries);
        Assert.Equal(1, winEntries.First().Points);
    }

    // 8. Concurrent completion requests do not duplicate ledger entries
    [Fact]
    public async Task ConcurrentCompletionRequests_DoNotDuplicate_LedgerEntries()
    {
        string sharedDb = Guid.NewGuid().ToString();
        using (var setupDb = CreateInMemoryDbContext(sharedDb))
        {
            var team = new Team { Name = "Đội Concurrent", Slug = "doi-concurrent" };
            var player = new Player { TeamId = team.Id, FullName = "Cầu Thủ" };
            setupDb.Teams.Add(team);
            setupDb.Players.Add(player);

            var match = new MatchGame
            {
                TeamAId = team.Id,
                Category = MatchCategory.Internal,
                Status = MatchStatus.Completed,
                ScoreA = 2,
                ScoreB = 0,
                ConfirmedByTeamA = true,
                MatchDateTime = new DateTime(2026, 10, 10, 19, 0, 0, DateTimeKind.Utc)
            };
            setupDb.Matches.Add(match);
            setupDb.MatchPlayerLineups.Add(new MatchPlayerLineup { MatchId = match.Id, TeamId = team.Id, PlayerId = player.Id, TeamSide = "A" });
            setupDb.MatchGoalEvents.Add(new MatchGoalEvent { MatchId = match.Id, TeamId = team.Id, ScorerPlayerId = player.Id, Minute = 20 });
            await setupDb.SaveChangesAsync();
        }

        // Chạy nhiều task đồng thời hoàn tất điểm
        var tasks = Enumerable.Range(0, 3).Select(async _ =>
        {
            try
            {
                using var db = CreateInMemoryDbContext(sharedDb);
                var service = new FireKeepingService(db);
                var match = await db.Matches.FirstAsync();
                await service.SyncMatchPointsAsync(match.Id);
            }
            catch
            {
                // Concurrency retry/conflict expected in high concurrency
            }
        });
        await Task.WhenAll(tasks);

        using (var verifyDb = CreateInMemoryDbContext(sharedDb))
        {
            var service = new FireKeepingService(verifyDb);
            var player = await verifyDb.Players.FirstAsync();
            var team = await verifyDb.Teams.FirstAsync();

            var summary = await service.GetPlayerAbSummaryAsync(team.Id, player.Id, "2026-10");
            // Ghi bàn: +3, Thắng: +1 => Tổng = 4 AB, không được vượt quá do race condition
            Assert.Equal(4, summary.CurrentMonthAb);
        }
    }

    // 9. Corrected results use established reversal mechanism without corrupting ledger history
    [Fact]
    public async Task CorrectedResults_UseReversalMechanism_WithoutCorruptingHistory()
    {
        using var db = CreateInMemoryDbContext();
        var fireService = new FireKeepingService(db);

        var teamA = new Team { Name = "Đội A", Slug = "doi-a" };
        var teamB = new Team { Name = "Đội B", Slug = "doi-b" };
        var playerA = new Player { TeamId = teamA.Id, FullName = "Cầu Thủ A" };
        db.Teams.AddRange(teamA, teamB);
        db.Players.Add(playerA);

        // Ban đầu: Thắng 1 - 0
        var match = new MatchGame
        {
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            Category = MatchCategory.Friendly,
            Status = MatchStatus.Completed,
            ScoreA = 1,
            ScoreB = 0,
            ConfirmedByTeamA = true,
            ConfirmedByTeamB = true,
            ConfirmedAt = DateTime.UtcNow,
            MatchDateTime = new DateTime(2026, 10, 10, 19, 0, 0, DateTimeKind.Utc)
        };
        db.Matches.Add(match);
        db.MatchPlayerLineups.Add(new MatchPlayerLineup { MatchId = match.Id, TeamId = teamA.Id, PlayerId = playerA.Id });
        await db.SaveChangesAsync();

        await fireService.SyncMatchPointsAsync(match.Id);

        var initialWinEntry = await db.PlayerAbJournalEntries
            .FirstAsync(j => j.PlayerId == playerA.Id && j.SourceRole == "Winner" && j.Points > 0);
        Assert.Equal(1, initialWinEntry.Points);

        // Sửa tỉ số thành Hòa 1 - 1 (và cả 2 bên cùng xác nhận lại)
        match.ScoreA = 1;
        match.ScoreB = 1;
        match.ConfirmedByTeamA = true;
        match.ConfirmedByTeamB = true;
        match.ConfirmedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await fireService.SyncMatchPointsAsync(match.Id);

        // Kiểm tra lịch sử sổ AB: Entry gốc không bị xóa, có thêm entry bút toán đảo
        var allWinEntries = await db.PlayerAbJournalEntries
            .Where(j => j.PlayerId == playerA.Id && j.SourceRole == "Winner")
            .ToListAsync();

        Assert.Equal(2, allWinEntries.Count);

        var reversal = allWinEntries.FirstOrDefault(j => j.Points == -1);
        Assert.NotNull(reversal);
        Assert.Equal(initialWinEntry.Id, reversal.ReversedEntryId);

        var summary = await fireService.GetPlayerAbSummaryAsync(teamA.Id, playerA.Id, "2026-10");
        Assert.Equal(0, summary.CurrentMonthAb);
    }

    // 10. Cancelled or unconfirmed matches do not receive completion rewards
    [Fact]
    public async Task CancelledOrUnconfirmedMatches_DoNotReceive_CompletionRewards()
    {
        using var db = CreateInMemoryDbContext();
        var fireService = new FireKeepingService(db);

        var team = new Team { Name = "Đội Test", Slug = "doi-test" };
        var player = new Player { TeamId = team.Id, FullName = "Cầu Thủ A" };
        db.Teams.Add(team);
        db.Players.Add(player);

        // 10a. Trận Scheduled không nhận điểm
        var scheduledMatch = new MatchGame
        {
            TeamAId = team.Id,
            Category = MatchCategory.Internal,
            Status = MatchStatus.Scheduled,
            ScoreA = 2,
            ScoreB = 0,
            MatchDateTime = new DateTime(2026, 10, 10, 19, 0, 0, DateTimeKind.Utc)
        };
        db.Matches.Add(scheduledMatch);
        db.MatchPlayerLineups.Add(new MatchPlayerLineup { MatchId = scheduledMatch.Id, TeamId = team.Id, PlayerId = player.Id, TeamSide = "A" });
        await db.SaveChangesAsync();

        await fireService.SyncMatchPointsAsync(scheduledMatch.Id);
        Assert.Empty(await db.PlayerAbJournalEntries.Where(j => j.SourceId == scheduledMatch.Id).ToListAsync());

        // 10b. Trận Completed trước đây -> chuyển sang Cancelled qua RevokeMatchPointsAsync -> đảo sạch điểm
        var completedMatch = new MatchGame
        {
            TeamAId = team.Id,
            Category = MatchCategory.Internal,
            Status = MatchStatus.Completed,
            ScoreA = 2,
            ScoreB = 0,
            ConfirmedByTeamA = true,
            MatchDateTime = new DateTime(2026, 10, 12, 19, 0, 0, DateTimeKind.Utc)
        };
        db.Matches.Add(completedMatch);
        db.MatchPlayerLineups.Add(new MatchPlayerLineup { MatchId = completedMatch.Id, TeamId = team.Id, PlayerId = player.Id, TeamSide = "A" });
        await db.SaveChangesAsync();

        await fireService.SyncMatchPointsAsync(completedMatch.Id);
        var entriesBeforeCancel = await db.PlayerAbJournalEntries.Where(j => j.SourceId == completedMatch.Id && j.Points > 0).ToListAsync();
        Assert.NotEmpty(entriesBeforeCancel);

        // Thu hồi do hủy trận
        await fireService.RevokeMatchPointsAsync(completedMatch.Id, "Trận đấu bị hủy sau giờ thi đấu");

        var summaryAfterCancel = await fireService.GetPlayerAbSummaryAsync(team.Id, player.Id, "2026-10");
        Assert.Equal(0, summaryAfterCancel.CurrentMonthAb);
    }
}
