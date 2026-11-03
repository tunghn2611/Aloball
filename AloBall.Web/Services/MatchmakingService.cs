using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Services;

public interface IMatchmakingService
{
    Task<(bool Success, string Message, Guid? MatchId)> AcceptProposalAndLockMatchAsync(Guid postId, Guid proposalId, string managerUserId);
    Task<(bool Success, string Message)> SubmitReviewAsync(Guid matchId, Guid reviewerTeamId, string userId, int stars, string comment);
    Task UpdateTeamRatingAndBlacklistStatusAsync(Guid teamId);
    Task<(bool Success, string Message)> ResolveAppealAsync(Guid appealId, bool invalidateReview, string adminNote, string adminUserId);
}

public class MatchmakingService : IMatchmakingService
{
    private readonly ApplicationDbContext _db;
    private readonly ITeamAuthorizationService _auth;

    public MatchmakingService(ApplicationDbContext db, ITeamAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<(bool Success, string Message, Guid? MatchId)> AcceptProposalAndLockMatchAsync(Guid postId, Guid proposalId, string managerUserId)
    {
        // Sử dụng database transaction chống race condition
        using var tx = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync() : null;
        try
        {
            var post = await _db.MatchmakingPosts
                .Include(p => p.Team)
                .Include(p => p.Proposals)
                .FirstOrDefaultAsync(p => p.Id == postId);

            if (post == null)
                return (false, "Tin tìm đối không tồn tại.", null);

            // Kiểm tra quyền quản lý:
            // Với kèo Public: Người chốt là manager của post.TeamId (chủ bài đăng).
            // Với kèo DirectChallenge: Người chốt là manager của post.TargetTeamId (đội nhận lời mời thách đấu).
            bool isAuthorized = false;
            if (post.PostType == MatchmakingPostType.DirectChallenge && post.TargetTeamId.HasValue)
            {
                isAuthorized = await _auth.HasManagerAccessAsync(post.TargetTeamId.Value, managerUserId);
            }
            else
            {
                isAuthorized = await _auth.HasManagerAccessAsync(post.TeamId, managerUserId);
            }

            if (!isAuthorized)
                return (false, "Bạn không có quyền quản lý đội để chốt đối/nhận lời mời này.", null);

            // Kiểm tra trạng thái đội đăng có đang bị hạn chế (Blacklist) không
            if (post.Team.IsRestrictedFromMatchmaking)
                return (false, $"Đội mời đang bị hạn chế bắt đối: {post.Team.MatchmakingRestrictionReason}", null);

            if (post.Status != MatchmakingStatus.Open)
                return (false, "Tin tìm đối này không còn mở (đã chốt hoặc đã hủy).", null);

            var proposal = post.Proposals.FirstOrDefault(p => p.Id == proposalId);
            if (proposal == null || proposal.Status != ProposalStatus.Pending)
                return (false, "Đề nghị giao lưu không tồn tại hoặc không còn hợp lệ.", null);

            var challenger = await _db.Teams.FirstOrDefaultAsync(t => t.Id == proposal.ChallengingTeamId);
            if (challenger == null)
                return (false, "Đội thách đấu không tồn tại.", null);

            // Kiểm tra đội thách đấu có bị hạn chế (Blacklist) không
            if (challenger.IsRestrictedFromMatchmaking)
                return (false, $"Đội đối thủ đang bị hạn chế bắt đối: {challenger.MatchmakingRestrictionReason}", null);

            // Chốt đối
            post.Status = MatchmakingStatus.Locked;
            post.AcceptedProposalId = proposalId;
            proposal.Status = ProposalStatus.Accepted;
            proposal.RespondedAt = DateTime.UtcNow;

            // Từ chối các lời mời khác trên cùng post này
            foreach (var other in post.Proposals.Where(p => p.Id != proposalId && p.Status == ProposalStatus.Pending))
            {
                other.Status = ProposalStatus.Rejected;
                other.RespondedAt = DateTime.UtcNow;
            }

            // Tự động tạo trận chung giữa 2 đội
            var combinedDateTime = post.MatchDate.Date.Add(post.StartTime);
            var match = new MatchGame
            {
                Category = MatchCategory.Friendly,
                Status = MatchStatus.Scheduled,
                MatchDateTime = combinedDateTime,
                PitchName = post.PitchName ?? post.Area,
                PitchType = post.PitchType,
                TeamAId = post.TeamId,
                TeamBId = challenger.Id,
                IsPublic = true,
                Notes = $"Trận đấu tạo tự động từ tin tìm đối: {post.Notes ?? "Kèo giao lưu hữu nghị"}"
            };

            _db.Matches.Add(match);
            await _db.SaveChangesAsync();

            post.CreatedMatchId = match.Id;
            await _db.SaveChangesAsync();

            if (tx != null) await tx.CommitAsync();
            return (true, "Đã chốt đối thành công và tự động tạo trận đấu chung!", match.Id);
        }
        catch (Exception ex)
        {
            if (tx != null) await tx.RollbackAsync();
            return (false, $"Lỗi hệ thống khi chốt đối: {ex.Message}", null);
        }
    }

    public async Task<(bool Success, string Message)> SubmitReviewAsync(Guid matchId, Guid reviewerTeamId, string userId, int stars, string comment)
    {
        if (stars < 1 || stars > 5) return (false, "Đánh giá phải từ 1 đến 5 sao.");
        comment = comment?.Trim() ?? "";
        var match = await _db.Matches
            .Include(m => m.TeamA)
            .Include(m => m.TeamB)
            .FirstOrDefaultAsync(m => m.Id == matchId);

        if (match == null) return (false, "Trận đấu không tồn tại.");

        // Không đánh giá trận nội bộ
        if (match.Category == MatchCategory.Internal)
            return (false, "Không thể đánh giá đối thủ cho trận đấu nội bộ.");

        // Trận phải có 2 đội trên hệ thống
        if (!match.TeamBId.HasValue)
            return (false, "Chỉ có thể đánh giá trận đấu giữa 2 đội bóng có tài khoản trên hệ thống.");

        // Trận phải được hoàn thành và xác nhận
        if (match.Status != MatchStatus.Completed)
            return (false, "Trận đấu phải ở trạng thái Đã hoàn thành mới có thể đánh giá.");

        if (match.MatchDateTime > CompetitionRules.LocalNow)
            return (false, "Chỉ được đánh giá sau giờ thi đấu.");
        if (!match.ConfirmedByTeamA || !match.ConfirmedByTeamB)
            return (false, "Hai đội cần xác nhận kết quả trước khi đánh giá đối thủ.");

        // Xác minh reviewer thuộc đội tham gia trận
        if (reviewerTeamId != match.TeamAId && reviewerTeamId != match.TeamBId)
            return (false, "Đội của bạn không tham gia trận đấu này.");

        // Xác minh quyền Manager của reviewer
        if (!await _auth.HasManagerAccessAsync(reviewerTeamId, userId))
            return (false, "Chỉ lãnh đạo (Owner / Manager) của đội mới có quyền gửi đánh giá.");

        // Xác định đội bị đánh giá
        Guid targetTeamId = (reviewerTeamId == match.TeamAId) ? match.TeamBId.Value : match.TeamAId;

        // Kiểm tra đã đánh giá trận này chưa
        var existing = await _db.TeamMatchReviews
            .FirstOrDefaultAsync(r => r.MatchId == matchId && r.ReviewerTeamId == reviewerTeamId);
        if (existing != null)
            return (false, "Đội của bạn đã gửi đánh giá cho trận đấu này rồi.");

        // Quy tắc bắt buộc: Đánh giá 1 sao phải có lý do chi tiết
        if (stars == 1 && string.IsNullOrWhiteSpace(comment))
            return (false, "Đánh giá 1 sao bắt buộc phải ghi rõ lý do vi phạm / hành vi không đúng mực.");

        var review = new TeamMatchReview
        {
            MatchId = matchId,
            ReviewerTeamId = reviewerTeamId,
            TargetTeamId = targetTeamId,
            SubmittedByUserId = userId,
            RatingStars = stars,
            Comment = comment.Trim(),
            IsValid = true
        };

        _db.TeamMatchReviews.Add(review);
        await _db.SaveChangesAsync();

        // Cập nhật rating và Blacklist
        await UpdateTeamRatingAndBlacklistStatusAsync(targetTeamId);

        return (true, "Đánh giá của bạn đã được ghi nhận thành công.");
    }

    public async Task UpdateTeamRatingAndBlacklistStatusAsync(Guid teamId)
    {
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == teamId);
        if (team == null) return;

        var validReviews = await _db.TeamMatchReviews
            .Where(r => r.TargetTeamId == teamId && r.IsValid)
            .ToListAsync();

        if (validReviews.Any())
        {
            team.ValidReviewCount = validReviews.Count;
            team.AverageRating = Math.Round(validReviews.Average(r => r.RatingStars), 1);
        }
        else
        {
            team.ValidReviewCount = 0;
            team.AverageRating = 5.0;
        }

        // QUY TẮC BLACKLIST BẮT BUỘC:
        // "Một đánh giá 1 sao hợp lệ khiến đội được đánh giá tự động bị hạn chế bắt đối.
        // Không tự đổi sang nhiều đánh giá hoặc điểm trung bình dưới ngưỡng."
        var hasValidOneStar = validReviews.Any(r => r.RatingStars == 1);
        if (hasValidOneStar)
        {
            team.IsRestrictedFromMatchmaking = true;
            team.MatchmakingRestrictionReason = "Nhận đánh giá 1 sao từ đối thủ thi đấu (hạn chế bắt đối tự động).";

            // Tự động tạm ngưng các bài đăng tìm đối đang Open của đội này
            var openPosts = await _db.MatchmakingPosts
                .Where(p => p.TeamId == teamId && p.Status == MatchmakingStatus.Open)
                .ToListAsync();

            foreach (var p in openPosts)
            {
                p.Status = MatchmakingStatus.Suspended;
            }
        }
        else
        {
            // Nếu không còn đánh giá 1 sao hợp lệ nào (ví dụ sau khi admin hủy đánh giá sai do khiếu nại)
            team.IsRestrictedFromMatchmaking = false;
            team.MatchmakingRestrictionReason = null;
        }

        await _db.SaveChangesAsync();
    }

    public async Task<(bool Success, string Message)> ResolveAppealAsync(Guid appealId, bool invalidateReview, string adminNote, string adminUserId)
    {
        var appeal = await _db.ReviewDisputeAppeals
            .Include(a => a.Review)
            .FirstOrDefaultAsync(a => a.Id == appealId);

        if (appeal == null) return (false, "Khiếu nại không tồn tại.");

        appeal.IsResolved = true;
        appeal.AdminResolutionNote = adminNote;
        appeal.ResolvedByAdminId = adminUserId;
        appeal.ResolvedAt = DateTime.UtcNow;

        if (invalidateReview)
        {
            appeal.Review.IsValid = false;
            appeal.Review.InvalidationReason = $"Vô hiệu hóa bởi PlatformAdmin: {adminNote}";
            appeal.Review.ModeratedByAdminId = adminUserId;
            appeal.Review.ModeratedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        // Tính toán lại rating và blacklist cho đội được đánh giá
        await UpdateTeamRatingAndBlacklistStatusAsync(appeal.Review.TargetTeamId);

        return (true, "Đã xử lý khiếu nại thành công.");
    }
}
