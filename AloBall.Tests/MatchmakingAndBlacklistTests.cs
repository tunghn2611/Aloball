using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AloBall.Tests;

public class MatchmakingAndBlacklistTests
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
    public async Task Valid_1Star_Review_Must_Trigger_Blacklist_And_Suspend_Open_Posts()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var mmService = new MatchmakingService(db, authService);

        // 1. Tạo 2 đội
        var teamA = new Team { Name = "Đội Thách Đấu", Slug = "doi-a", IsRestrictedFromMatchmaking = false };
        var teamB = new Team { Name = "Đội Bị Đánh Giá", Slug = "doi-b", IsRestrictedFromMatchmaking = false };
        db.Teams.AddRange(teamA, teamB);

        // Tạo bài đăng Open của Đội B
        var openPostB = new MatchmakingPost
        {
            TeamId = teamB.Id,
            Area = "Cầu Giấy",
            Status = MatchmakingStatus.Open
        };
        db.MatchmakingPosts.Add(openPostB);

        // Tạo trận đấu giao hữu đã hoàn thành
        var match = new MatchGame
        {
            Category = MatchCategory.Friendly,
            Status = MatchStatus.Completed,
            TeamAId = teamA.Id,
            TeamBId = teamB.Id
        };
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        // Thêm đánh giá 1 sao hợp lệ từ Đội A cho Đội B (Bỏ bóng đá người, bạo lực)
        var review = new TeamMatchReview
        {
            MatchId = match.Id,
            ReviewerTeamId = teamA.Id,
            TargetTeamId = teamB.Id,
            RatingStars = 1,
            Comment = "Hành vi bạo lực, xúc phạm trọng tài và đối thủ.",
            IsValid = true
        };
        db.TeamMatchReviews.Add(review);
        await db.SaveChangesAsync();

        // 2. Chạy cập nhật rating và blacklist
        await mmService.UpdateTeamRatingAndBlacklistStatusAsync(teamB.Id);

        // 3. Kiểm tra kết quả
        var updatedTeamB = await db.Teams.FindAsync(teamB.Id);
        Assert.NotNull(updatedTeamB);
        Assert.True(updatedTeamB.IsRestrictedFromMatchmaking, "Đội nhận 1 sao hợp lệ phải bị đưa vào blacklist hạn chế bắt đối ngay lập tức!");
        Assert.Contains("Nhận đánh giá 1 sao", updatedTeamB.MatchmakingRestrictionReason);

        var updatedPostB = await db.MatchmakingPosts.FindAsync(openPostB.Id);
        Assert.NotNull(updatedPostB);
        Assert.Equal(MatchmakingStatus.Suspended, updatedPostB.Status);
    }

    [Fact]
    public async Task Restricted_Team_Cannot_Accept_Or_Be_Accepted_In_Matchmaking()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var mmService = new MatchmakingService(db, authService);

        var managerUser = new ApplicationUser { Id = "mgr-1", UserName = "mgr@test.vn" };
        db.Users.Add(managerUser);

        var teamA = new Team { Name = "Đội Bị Khóa", Slug = "doi-khoa", IsRestrictedFromMatchmaking = true, MatchmakingRestrictionReason = "Bị blacklist 1 sao" };
        var teamB = new Team { Name = "Đội Bình Thường", Slug = "doi-bt", IsRestrictedFromMatchmaking = false };
        db.Teams.AddRange(teamA, teamB);

        db.TeamMembers.Add(new TeamMember { TeamId = teamA.Id, UserId = managerUser.Id, Role = TeamRole.Manager });

        var post = new MatchmakingPost
        {
            TeamId = teamA.Id,
            Status = MatchmakingStatus.Open,
            MatchDate = DateTime.Today.AddDays(2),
            StartTime = new TimeSpan(19, 0, 0),
            EndTime = new TimeSpan(20, 30, 0),
            Area = "Mỹ Đình"
        };
        db.MatchmakingPosts.Add(post);

        var proposal = new MatchmakingProposal
        {
            PostId = post.Id,
            ChallengingTeamId = teamB.Id,
            Status = ProposalStatus.Pending
        };
        db.MatchmakingProposals.Add(proposal);
        await db.SaveChangesAsync();

        // Thử chốt đối khi Đội A đang bị hạn chế
        var result = await mmService.AcceptProposalAndLockMatchAsync(post.Id, proposal.Id, managerUser.Id);

        Assert.False(result.Success);
        Assert.Contains("bị hạn chế bắt đối", result.Message);
        Assert.Null(result.MatchId);
    }

    [Fact]
    public async Task Invalidating_False_Review_Removes_Blacklist_If_No_Other_OneStar()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var mmService = new MatchmakingService(db, authService);

        var team = new Team { Name = "Đội Bị Oan", Slug = "oan-fc", IsRestrictedFromMatchmaking = true, MatchmakingRestrictionReason = "1 sao cũ" };
        var opponent = new Team { Name = "Đội Vu Khống", Slug = "vu-khong" };
        db.Teams.AddRange(team, opponent);

        var match = new MatchGame { TeamAId = opponent.Id, TeamBId = team.Id, Status = MatchStatus.Completed };
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var review = new TeamMatchReview
        {
            MatchId = match.Id,
            ReviewerTeamId = opponent.Id,
            TargetTeamId = team.Id,
            RatingStars = 1,
            Comment = "Đánh giá ác ý không đúng sự thật",
            IsValid = true
        };
        db.TeamMatchReviews.Add(review);

        var appeal = new ReviewDisputeAppeal
        {
            ReviewId = review.Id,
            TeamId = team.Id,
            AppealReason = "Có video quay lại trận đấu chứng minh hoàn toàn fair-play",
            IsResolved = false
        };
        db.ReviewDisputeAppeals.Add(appeal);
        await db.SaveChangesAsync();

        // Admin xử lý giải trình: Vô hiệu hóa đánh giá sai
        var result = await mmService.ResolveAppealAsync(appeal.Id, invalidateReview: true, adminNote: "Xác minh qua video: Đánh giá vu khống", adminUserId: "admin-1");

        Assert.True(result.Success);

        var updatedTeam = await db.Teams.FindAsync(team.Id);
        Assert.NotNull(updatedTeam);
        Assert.False(updatedTeam.IsRestrictedFromMatchmaking, "Sau khi vô hiệu hóa 1 sao duy nhất, đội phải được gỡ hạn chế bắt đối.");
        Assert.Null(updatedTeam.MatchmakingRestrictionReason);
    }
}
