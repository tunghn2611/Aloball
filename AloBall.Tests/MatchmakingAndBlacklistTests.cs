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

    // 1. A single 1-star review does not automatically blacklist a team
    [Fact]
    public async Task Single_1Star_Review_Does_Not_Automatically_Blacklist_Team()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var mmService = new MatchmakingService(db, authService);

        var teamA = new Team { Name = "Đội Thách Đấu", Slug = "doi-a", IsRestrictedFromMatchmaking = false };
        var teamB = new Team { Name = "Đội Bị Đánh Giá", Slug = "doi-b", IsRestrictedFromMatchmaking = false };
        db.Teams.AddRange(teamA, teamB);

        var openPostB = new MatchmakingPost
        {
            TeamId = teamB.Id,
            Area = "Cầu Giấy",
            Status = MatchmakingStatus.Open
        };
        db.MatchmakingPosts.Add(openPostB);

        var match = new MatchGame
        {
            Category = MatchCategory.Friendly,
            Status = MatchStatus.Completed,
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            ConfirmedByTeamA = true,
            ConfirmedByTeamB = true,
            MatchDateTime = CompetitionRules.LocalNow.AddHours(-2)
        };
        db.Matches.Add(match);

        var review = new TeamMatchReview
        {
            MatchId = match.Id,
            ReviewerTeamId = teamA.Id,
            TargetTeamId = teamB.Id,
            RatingStars = 1,
            Comment = "Hành vi bạo lực, xúc phạm trọng tài.",
            IsValid = true
        };
        db.TeamMatchReviews.Add(review);
        await db.SaveChangesAsync();

        await mmService.UpdateTeamRatingAndBlacklistStatusAsync(teamB.Id);

        var updatedTeamB = await db.Teams.FindAsync(teamB.Id);
        Assert.NotNull(updatedTeamB);
        // Blueprint 2.4 / D-05: Đánh giá 1 sao KHÔNG được tự ý đưa đội vào blacklist tự động
        Assert.False(updatedTeamB.IsRestrictedFromMatchmaking, "Một đánh giá 1 sao không được tự động đưa đội vào blacklist.");
        Assert.Null(updatedTeamB.MatchmakingRestrictionReason);

        var updatedPostB = await db.MatchmakingPosts.FindAsync(openPostB.Id);
        Assert.NotNull(updatedPostB);
        Assert.Equal(MatchmakingStatus.Open, updatedPostB.Status);
    }

    // 2. Review submission still succeeds and is persisted correctly
    [Fact]
    public async Task Review_Submission_Succeeds_And_Is_Persisted_Correctly()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var mmService = new MatchmakingService(db, authService);

        var managerUser = new ApplicationUser { Id = "mgr-a", UserName = "manager@teama.vn" };
        db.Users.Add(managerUser);

        var teamA = new Team { Name = "Đội A", Slug = "doi-a" };
        var teamB = new Team { Name = "Đội B", Slug = "doi-b" };
        db.Teams.AddRange(teamA, teamB);
        db.TeamMembers.Add(new TeamMember { TeamId = teamA.Id, UserId = managerUser.Id, Role = TeamRole.Manager });

        var match = new MatchGame
        {
            Category = MatchCategory.Friendly,
            Status = MatchStatus.Completed,
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            ConfirmedByTeamA = true,
            ConfirmedByTeamB = true,
            MatchDateTime = CompetitionRules.LocalNow.AddHours(-2)
        };
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var result = await mmService.SubmitReviewAsync(match.Id, teamA.Id, managerUser.Id, 1, "Thi đấu phi thể thao");

        Assert.True(result.Success, result.Message);

        var savedReview = await db.TeamMatchReviews.FirstOrDefaultAsync(r => r.MatchId == match.Id && r.ReviewerTeamId == teamA.Id);
        Assert.NotNull(savedReview);
        Assert.Equal(1, savedReview.RatingStars);
        Assert.Equal("Thi đấu phi thể thao", savedReview.Comment);
        Assert.True(savedReview.IsValid);

        var updatedTeamB = await db.Teams.FindAsync(teamB.Id);
        Assert.NotNull(updatedTeamB);
        Assert.Equal(1, updatedTeamB.ValidReviewCount);
        Assert.Equal(1.0, updatedTeamB.AverageRating);
        Assert.False(updatedTeamB.IsRestrictedFromMatchmaking);
    }

    // 3. Reporting/flagging does not automatically equal administrative blacklisting
    [Fact]
    public async Task Reporting_Or_1Star_Review_Does_Not_Equal_Administrative_Blacklisting()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var mmService = new MatchmakingService(db, authService);

        var managerB = new ApplicationUser { Id = "mgr-b", UserName = "manager@teamb.vn" };
        db.Users.Add(managerB);

        var teamA = new Team { Name = "Đội A", Slug = "doi-a" };
        var teamB = new Team { Name = "Đội Bị Báo Cáo", Slug = "doi-b", IsRestrictedFromMatchmaking = false };
        db.Teams.AddRange(teamA, teamB);
        db.TeamMembers.Add(new TeamMember { TeamId = teamB.Id, UserId = managerB.Id, Role = TeamRole.Manager });

        var match = new MatchGame
        {
            Category = MatchCategory.Friendly,
            Status = MatchStatus.Completed,
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            ConfirmedByTeamA = true,
            ConfirmedByTeamB = true,
            MatchDateTime = CompetitionRules.LocalNow.AddHours(-2)
        };
        db.Matches.Add(match);

        // Đội A báo cáo Đội B với 1 sao
        var review = new TeamMatchReview
        {
            MatchId = match.Id,
            ReviewerTeamId = teamA.Id,
            TargetTeamId = teamB.Id,
            RatingStars = 1,
            Comment = "Báo cáo đội đến muộn và thiếu người.",
            IsValid = true
        };
        db.TeamMatchReviews.Add(review);
        await db.SaveChangesAsync();

        await mmService.UpdateTeamRatingAndBlacklistStatusAsync(teamB.Id);

        // Đội B dù có 1 sao nhưng chưa bị Platform Admin khóa, vẫn có thể bắt đối bình thường
        var post = new MatchmakingPost
        {
            TeamId = teamB.Id,
            Status = MatchmakingStatus.Open,
            MatchDate = DateTime.Today.AddDays(2),
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(19, 30, 0),
            Area = "Thanh Xuân"
        };
        db.MatchmakingPosts.Add(post);

        var proposal = new MatchmakingProposal
        {
            PostId = post.Id,
            ChallengingTeamId = teamA.Id,
            Status = ProposalStatus.Pending
        };
        db.MatchmakingProposals.Add(proposal);
        await db.SaveChangesAsync();

        var acceptResult = await mmService.AcceptProposalAndLockMatchAsync(post.Id, proposal.Id, managerB.Id);
        Assert.True(acceptResult.Success, acceptResult.Message);
        Assert.NotNull(acceptResult.MatchId);
    }

    // 4. Unauthorized users cannot blacklist or unblacklist teams
    [Fact]
    public async Task Unauthorized_Users_Cannot_Blacklist_Or_Unblacklist_Teams()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var mmService = new MatchmakingService(db, authService);

        var normalUser = new ApplicationUser { Id = "user-normal", UserName = "normal@user.vn" };
        db.Users.Add(normalUser);

        var team = new Team { Name = "Đội Test", Slug = "doi-test", IsRestrictedFromMatchmaking = false };
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        // Thử blacklist bởi người dùng thường
        var restrictResult = await mmService.SetTeamBlacklistStatusAsync(team.Id, true, "Cố ý khóa đội", normalUser.Id);
        Assert.False(restrictResult.Success);
        Assert.Contains("Platform Admin", restrictResult.Message);

        var reloadedTeam = await db.Teams.FindAsync(team.Id);
        Assert.NotNull(reloadedTeam);
        Assert.False(reloadedTeam.IsRestrictedFromMatchmaking);

        // Gán thủ công để thử unblacklist bởi người dùng thường
        reloadedTeam.IsRestrictedFromMatchmaking = true;
        await db.SaveChangesAsync();

        var unrestrictResult = await mmService.SetTeamBlacklistStatusAsync(team.Id, false, null, normalUser.Id);
        Assert.False(unrestrictResult.Success);
        Assert.Contains("Platform Admin", unrestrictResult.Message);

        var reloadedTeam2 = await db.Teams.FindAsync(team.Id);
        Assert.NotNull(reloadedTeam2);
        Assert.True(reloadedTeam2.IsRestrictedFromMatchmaking);
    }

    // 5. Authorized administrators can apply and remove blacklist decisions
    [Fact]
    public async Task Authorized_Administrators_Can_Apply_And_Remove_Blacklist_Decisions()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var mmService = new MatchmakingService(db, authService);

        var adminUser = new ApplicationUser { Id = "admin-1", UserName = "admin@aloball.vn" };
        db.Users.Add(adminUser);

        var team = new Team { Name = "Đội Vi Phạm", Slug = "doi-vi-pham", IsRestrictedFromMatchmaking = false };
        db.Teams.Add(team);

        var openPost = new MatchmakingPost { TeamId = team.Id, Status = MatchmakingStatus.Open, Area = "Hà Đông" };
        db.MatchmakingPosts.Add(openPost);
        await db.SaveChangesAsync();

        // 5a. Admin áp dụng Blacklist
        var restrictResult = await mmService.SetTeamBlacklistStatusAsync(team.Id, true, "Bạo lực sân cỏ có video bằng chứng", adminUser.Id);
        Assert.True(restrictResult.Success, restrictResult.Message);

        var restrictedTeam = await db.Teams.FindAsync(team.Id);
        Assert.NotNull(restrictedTeam);
        Assert.True(restrictedTeam.IsRestrictedFromMatchmaking);
        Assert.Equal("Bạo lực sân cỏ có video bằng chứng", restrictedTeam.MatchmakingRestrictionReason);

        var suspendedPost = await db.MatchmakingPosts.FindAsync(openPost.Id);
        Assert.NotNull(suspendedPost);
        Assert.Equal(MatchmakingStatus.Suspended, suspendedPost.Status);

        // 5b. Admin gỡ bỏ Blacklist
        var unrestrictResult = await mmService.SetTeamBlacklistStatusAsync(team.Id, false, null, adminUser.Id);
        Assert.True(unrestrictResult.Success, unrestrictResult.Message);

        var unrestrictedTeam = await db.Teams.FindAsync(team.Id);
        Assert.NotNull(unrestrictedTeam);
        Assert.False(unrestrictedTeam.IsRestrictedFromMatchmaking);
        Assert.Null(unrestrictedTeam.MatchmakingRestrictionReason);
    }

    // 6. Matchmaking correctly respects administrative blacklist status
    [Fact]
    public async Task Restricted_Team_Cannot_Accept_Or_Be_Accepted_In_Matchmaking()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var mmService = new MatchmakingService(db, authService);

        var managerUser = new ApplicationUser { Id = "mgr-1", UserName = "mgr@test.vn" };
        db.Users.Add(managerUser);

        var teamA = new Team { Name = "Đội Bị Khóa", Slug = "doi-khoa", IsRestrictedFromMatchmaking = true, MatchmakingRestrictionReason = "Bị blacklist bởi admin" };
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

    // 7. Invalidating a review does not remove an independent administrative blacklist
    [Fact]
    public async Task Invalidating_Review_Does_Not_Remove_Independent_Administrative_Blacklist()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var mmService = new MatchmakingService(db, authService);

        var adminUser = new ApplicationUser { Id = "admin-1", UserName = "admin@aloball.vn", Email = "admin@aloball.vn" };
        db.Users.Add(adminUser);

        // Đội có lệnh phạt hành chính độc lập từ Platform Admin (ví dụ do gian lận/bạo lực)
        var team = new Team
        {
            Name = "Đội Bị Phạt",
            Slug = "phat-fc",
            IsRestrictedFromMatchmaking = true,
            MatchmakingRestrictionReason = "Hạn chế do vi phạm kỷ luật giải đấu bởi Platform Admin"
        };
        var opponent = new Team { Name = "Đội Đối Thủ", Slug = "doi-thu" };
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
            Comment = "Đánh giá 1 sao không chính xác",
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

        var updatedReview = await db.TeamMatchReviews.FindAsync(review.Id);
        Assert.NotNull(updatedReview);
        Assert.False(updatedReview!.IsValid, "Đánh giá sai phải được vô hiệu hóa.");

        var updatedTeam = await db.Teams.FindAsync(team.Id);
        Assert.NotNull(updatedTeam);
        // Lệnh phạt hành chính độc lập không được tự động gỡ bỏ bởi việc duyệt khiếu nại review
        Assert.True(updatedTeam!.IsRestrictedFromMatchmaking, "Hạn chế hành chính độc lập không được tự động gỡ khi duyệt khiếu nại review.");
        Assert.Equal("Hạn chế do vi phạm kỷ luật giải đấu bởi Platform Admin", updatedTeam.MatchmakingRestrictionReason);

        // 8. Platform Admin gỡ phạt thủ công khi hết hạn kỷ luật
        var unrestrictResult = await mmService.SetTeamBlacklistStatusAsync(team.Id, false, null, "admin-1");
        Assert.True(unrestrictResult.Success);

        var finallyUnrestrictedTeam = await db.Teams.FindAsync(team.Id);
        Assert.False(finallyUnrestrictedTeam!.IsRestrictedFromMatchmaking, "Platform Admin gỡ phạt thủ công phải thành công.");
        Assert.Null(finallyUnrestrictedTeam.MatchmakingRestrictionReason);
    }
}
