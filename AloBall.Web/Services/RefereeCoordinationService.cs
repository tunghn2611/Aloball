using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Services;

public interface IRefereeCoordinationService
{
    Task<RefereeRequest> CreateRequestAsync(RefereeRequest request, string userId);
    Task<bool> CancelRequestAsync(Guid requestId, string userId);
    Task<RefereeProposal> CreateProposalAsync(Guid requestId, string adminUserId, decimal totalPriceVnd, string? serviceNote, List<Guid> refereeIds);
    Task<bool> AcceptProposalAsync(Guid proposalId, string userId);
    Task<bool> DeclineProposalAsync(Guid proposalId, string userId);
    Task<bool> SetRequestUnderReviewAsync(Guid requestId, string adminUserId);
    Task<bool> MarkUnableToFulfillAsync(Guid requestId, string adminUserId, string? reason);
    Task<bool> CompleteRequestAsync(Guid requestId, string adminUserId);

    Task<RefereeRequest?> GetRequestDetailsAsync(Guid requestId, string userId, bool isPlatformAdmin);
    Task<List<RefereeRequest>> GetTeamRequestsAsync(Guid teamId);
    Task<List<RefereeRequest>> GetUserManageableRequestsAsync(string userId);
    Task<List<RefereeRequest>> GetAdminInboxRequestsAsync(RefereeRequestStatus? statusFilter = null, string? search = null);

    Task<List<Referee>> GetActiveRefereesAsync();
    Task<List<Referee>> GetAllRefereesAsync();
    Task<Referee?> GetRefereeByIdAsync(Guid id);
    Task<Referee> CreateRefereeAsync(Referee referee, string adminUserId);
    Task<Referee> UpdateRefereeAsync(Referee referee, string adminUserId);
    Task<bool> ToggleRefereeStatusAsync(Guid refereeId, string adminUserId);
}

public class RefereeCoordinationService : IRefereeCoordinationService
{
    private readonly ApplicationDbContext _db;
    private readonly ITeamAuthorizationService _auth;

    public RefereeCoordinationService(ApplicationDbContext db, ITeamAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<RefereeRequest> CreateRequestAsync(RefereeRequest request, string userId)
    {
        // 1. Authorization: Only Owner/Manager can request
        var hasAccess = await _auth.HasManagerAccessAsync(request.TeamId, userId);
        if (!hasAccess)
        {
            throw new UnauthorizedAccessException("Chỉ Ban cán sự hoặc Đội trưởng mới có quyền tạo yêu cầu trọng tài.");
        }

        // 2. Server-side validations
        if (request.ServiceDate.Date < DateTime.Today)
        {
            throw new ArgumentException("Ngày thi đấu không được ở trong quá khứ.");
        }

        if (request.EndTime <= request.StartTime)
        {
            throw new ArgumentException("Giờ kết thúc phải muộn hơn giờ bắt đầu.");
        }

        if (request.RefereeCount <= 0)
        {
            throw new ArgumentException("Số lượng trọng tài yêu cầu phải lớn hơn 0.");
        }

        if (string.IsNullOrWhiteSpace(request.VenueName))
        {
            throw new ArgumentException("Tên sân / địa điểm là bắt buộc.");
        }

        if (string.IsNullOrWhiteSpace(request.VenueAddress))
        {
            throw new ArgumentException("Địa chỉ sân thi đấu là bắt buộc.");
        }

        if (string.IsNullOrWhiteSpace(request.MatchFormat))
        {
            throw new ArgumentException("Loại sân / thể thức thi đấu là bắt buộc.");
        }

        if (request.BudgetVND.HasValue && request.BudgetVND.Value < 0)
        {
            throw new ArgumentException("Ngân sách dự kiến không được âm.");
        }

        // 3. Duplicate check for active request on same match
        if (request.MatchId.HasValue)
        {
            var existingActive = await _db.RefereeRequests
                .AnyAsync(r => r.MatchId == request.MatchId.Value
                               && r.TeamId == request.TeamId
                               && r.Status != RefereeRequestStatus.Cancelled
                               && r.Status != RefereeRequestStatus.UnableToFulfill);
            if (existingActive)
            {
                throw new InvalidOperationException("Đội bóng đã có một yêu cầu trọng tài đang hoạt động cho trận đấu này.");
            }
        }

        request.Id = Guid.NewGuid();
        request.Status = RefereeRequestStatus.Pending;
        request.CreatedAt = DateTime.UtcNow;
        request.UpdatedAt = null;

        _db.RefereeRequests.Add(request);
        await _db.SaveChangesAsync();

        return request;
    }

    public async Task<bool> CancelRequestAsync(Guid requestId, string userId)
    {
        var req = await _db.RefereeRequests.FirstOrDefaultAsync(r => r.Id == requestId);
        if (req == null) return false;

        var isPlatformAdmin = await _auth.IsPlatformAdminAsync(userId);
        var isManager = await _auth.HasManagerAccessAsync(req.TeamId, userId);
        if (!isPlatformAdmin && !isManager)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền hủy yêu cầu này.");
        }

        // Requester can cancel while Pending, UnderReview, AwaitingTeamDecision
        if (req.Status == RefereeRequestStatus.Confirmed)
        {
            throw new InvalidOperationException("Yêu cầu đã được chốt (Confirmed). Vui lòng liên hệ trực tiếp Quản trị viên để hủy dịch vụ.");
        }

        if (req.Status == RefereeRequestStatus.Completed || req.Status == RefereeRequestStatus.Cancelled)
        {
            throw new InvalidOperationException("Không thể hủy yêu cầu ở trạng thái hiện tại.");
        }

        req.Status = RefereeRequestStatus.Cancelled;
        req.UpdatedAt = DateTime.UtcNow;

        // Also withdraw pending proposals
        var pendingProposals = await _db.RefereeProposals
            .Where(p => p.RequestId == requestId && p.Status == RefereeProposalStatus.Pending)
            .ToListAsync();
        foreach (var p in pendingProposals)
        {
            p.Status = RefereeProposalStatus.Withdrawn;
        }

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<RefereeProposal> CreateProposalAsync(
        Guid requestId,
        string adminUserId,
        decimal totalPriceVnd,
        string? serviceNote,
        List<Guid> refereeIds)
    {
        if (!await _auth.IsPlatformAdminAsync(adminUserId))
        {
            throw new UnauthorizedAccessException("Chỉ Quản trị viên hệ thống mới có quyền gửi báo giá / đề xuất trọng tài.");
        }

        var req = await _db.RefereeRequests.FirstOrDefaultAsync(r => r.Id == requestId);
        if (req == null)
        {
            throw new KeyNotFoundException("Không tìm thấy yêu cầu trọng tài.");
        }

        if (req.Status == RefereeRequestStatus.Cancelled || req.Status == RefereeRequestStatus.Completed || req.Status == RefereeRequestStatus.Confirmed)
        {
            throw new InvalidOperationException($"Không thể tạo báo giá khi yêu cầu đang ở trạng thái {req.Status}.");
        }

        if (refereeIds == null || refereeIds.Count != req.RefereeCount)
        {
            throw new ArgumentException($"Số lượng trọng tài được chọn ({refereeIds?.Count ?? 0}) phải đúng bằng số lượng yêu cầu ({req.RefereeCount}).");
        }

        if (totalPriceVnd < 0)
        {
            throw new ArgumentException("Tổng giá dịch vụ không được âm.");
        }

        var selectedReferees = await _db.Referees
            .Where(r => refereeIds.Contains(r.Id))
            .ToListAsync();

        if (selectedReferees.Count != refereeIds.Count)
        {
            throw new ArgumentException("Một hoặc nhiều trọng tài được chọn không tồn tại.");
        }

        var inactive = selectedReferees.FirstOrDefault(r => !r.IsActive);
        if (inactive != null)
        {
            throw new InvalidOperationException($"Trọng tài {inactive.Name} hiện đang tạm ngừng hoạt động.");
        }

        // Check overlapping confirmed assignments
        var overlappingAssignments = await _db.RefereeAssignments
            .Include(a => a.Proposal).ThenInclude(p => p.Request)
            .Include(a => a.Referee)
            .Where(a => refereeIds.Contains(a.RefereeId)
                        && a.Proposal.Status == RefereeProposalStatus.Accepted
                        && a.Proposal.Request.Status == RefereeRequestStatus.Confirmed
                        && a.Proposal.Request.ServiceDate.Date == req.ServiceDate.Date
                        && a.Proposal.Request.EndTime > req.StartTime
                        && a.Proposal.Request.StartTime < req.EndTime)
            .ToListAsync();

        if (overlappingAssignments.Any())
        {
            var conflict = overlappingAssignments.First();
            throw new InvalidOperationException($"Trọng tài {conflict.Referee.Name} đã có lịch thi đấu được chốt trùng giờ ({conflict.Proposal.Request.StartTime:hh\\:mm} - {conflict.Proposal.Request.EndTime:hh\\:mm}) vào ngày này.");
        }

        var proposal = new RefereeProposal
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            AdminId = adminUserId,
            TotalPriceVND = totalPriceVnd,
            ServiceNote = serviceNote,
            Status = RefereeProposalStatus.Pending,
            SubmittedAt = DateTime.UtcNow
        };

        foreach (var refId in refereeIds)
        {
            proposal.Assignments.Add(new RefereeAssignment
            {
                Id = Guid.NewGuid(),
                ProposalId = proposal.Id,
                RefereeId = refId
            });
        }

        _db.RefereeProposals.Add(proposal);

        req.Status = RefereeRequestStatus.AwaitingTeamDecision;
        req.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return proposal;
    }

    public async Task<bool> AcceptProposalAsync(Guid proposalId, string userId)
    {
        // Enforce transaction
        using var tx = await _db.Database.BeginTransactionAsync();

        var proposal = await _db.RefereeProposals
            .Include(p => p.Request)
            .FirstOrDefaultAsync(p => p.Id == proposalId);

        if (proposal == null) return false;

        var req = proposal.Request;
        var hasAccess = await _auth.HasManagerAccessAsync(req.TeamId, userId);
        if (!hasAccess)
        {
            throw new UnauthorizedAccessException("Chỉ Ban cán sự hoặc Đội trưởng mới có quyền chốt báo giá.");
        }

        if (req.Status == RefereeRequestStatus.Cancelled || req.Status == RefereeRequestStatus.Completed)
        {
            throw new InvalidOperationException("Yêu cầu đã bị hủy hoặc hoàn thành, không thể chấp nhận báo giá.");
        }

        if (req.Status == RefereeRequestStatus.Confirmed)
        {
            throw new InvalidOperationException("Yêu cầu này đã được chốt báo giá trước đó.");
        }

        if (proposal.Status != RefereeProposalStatus.Pending)
        {
            throw new InvalidOperationException($"Chỉ có thể chấp nhận báo giá đang chờ duyệt (Pending), trạng thái hiện tại là {proposal.Status}.");
        }

        proposal.Status = RefereeProposalStatus.Accepted;
        req.Status = RefereeRequestStatus.Confirmed;
        req.UpdatedAt = DateTime.UtcNow;

        // Mark other proposals as NotSelected
        var otherProposals = await _db.RefereeProposals
            .Where(p => p.RequestId == req.Id && p.Id != proposalId && p.Status == RefereeProposalStatus.Pending)
            .ToListAsync();
        foreach (var other in otherProposals)
        {
            other.Status = RefereeProposalStatus.NotSelected;
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return true;
    }

    public async Task<bool> DeclineProposalAsync(Guid proposalId, string userId)
    {
        var proposal = await _db.RefereeProposals
            .Include(p => p.Request)
            .FirstOrDefaultAsync(p => p.Id == proposalId);

        if (proposal == null) return false;

        var req = proposal.Request;
        var hasAccess = await _auth.HasManagerAccessAsync(req.TeamId, userId);
        if (!hasAccess)
        {
            throw new UnauthorizedAccessException("Chỉ Ban cán sự hoặc Đội trưởng mới có quyền từ chối báo giá.");
        }

        if (proposal.Status != RefereeProposalStatus.Pending)
        {
            throw new InvalidOperationException("Chỉ có thể từ chối báo giá đang chờ duyệt.");
        }

        proposal.Status = RefereeProposalStatus.Declined;
        req.UpdatedAt = DateTime.UtcNow;

        // If no more pending proposals, revert status to UnderReview so Admin knows to re-propose
        var hasOtherPending = await _db.RefereeProposals
            .AnyAsync(p => p.RequestId == req.Id && p.Id != proposalId && p.Status == RefereeProposalStatus.Pending);

        if (!hasOtherPending && req.Status == RefereeRequestStatus.AwaitingTeamDecision)
        {
            req.Status = RefereeRequestStatus.UnderReview;
        }

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetRequestUnderReviewAsync(Guid requestId, string adminUserId)
    {
        if (!await _auth.IsPlatformAdminAsync(adminUserId))
            throw new UnauthorizedAccessException("Chỉ Quản trị viên mới có quyền cập nhật trạng thái.");

        var req = await _db.RefereeRequests.FirstOrDefaultAsync(r => r.Id == requestId);
        if (req == null) return false;

        if (req.Status == RefereeRequestStatus.Pending)
        {
            req.Status = RefereeRequestStatus.UnderReview;
            req.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return true;
    }

    public async Task<bool> MarkUnableToFulfillAsync(Guid requestId, string adminUserId, string? reason)
    {
        if (!await _auth.IsPlatformAdminAsync(adminUserId))
            throw new UnauthorizedAccessException("Chỉ Quản trị viên mới có quyền cập nhật trạng thái.");

        var req = await _db.RefereeRequests.FirstOrDefaultAsync(r => r.Id == requestId);
        if (req == null) return false;

        req.Status = RefereeRequestStatus.UnableToFulfill;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            req.Notes = string.IsNullOrWhiteSpace(req.Notes) ? $"[Admin]: {reason}" : $"{req.Notes}\n[Admin]: {reason}";
        }
        req.UpdatedAt = DateTime.UtcNow;

        // Withdraw pending proposals
        var pendingProposals = await _db.RefereeProposals
            .Where(p => p.RequestId == requestId && p.Status == RefereeProposalStatus.Pending)
            .ToListAsync();
        foreach (var p in pendingProposals)
        {
            p.Status = RefereeProposalStatus.Withdrawn;
        }

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CompleteRequestAsync(Guid requestId, string adminUserId)
    {
        if (!await _auth.IsPlatformAdminAsync(adminUserId))
            throw new UnauthorizedAccessException("Chỉ Quản trị viên mới có quyền hoàn thành dịch vụ.");

        var req = await _db.RefereeRequests.FirstOrDefaultAsync(r => r.Id == requestId);
        if (req == null) return false;

        if (req.Status != RefereeRequestStatus.Confirmed)
        {
            throw new InvalidOperationException("Chỉ có thể hoàn thành dịch vụ đã được xác nhận (Confirmed).");
        }

        req.Status = RefereeRequestStatus.Completed;
        req.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<RefereeRequest?> GetRequestDetailsAsync(Guid requestId, string userId, bool isPlatformAdmin)
    {
        var query = _db.RefereeRequests
            .AsNoTracking()
            .Include(r => r.Team)
            .Include(r => r.Match)
            .Include(r => r.Proposals)
                .ThenInclude(p => p.Assignments)
                    .ThenInclude(a => a.Referee);

        var req = await query.FirstOrDefaultAsync(r => r.Id == requestId);
        if (req == null) return null;

        var isTeamMember = await _auth.HasMemberAccessAsync(req.TeamId, userId);
        if (!isPlatformAdmin && !isTeamMember)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền truy cập yêu cầu của đội bóng khác.");
        }

        // Privacy mask: hide referee contact info unless the proposal is Accepted AND user is team member or admin
        foreach (var proposal in req.Proposals)
        {
            var isRevealed = proposal.Status == RefereeProposalStatus.Accepted || isPlatformAdmin;
            if (!isRevealed)
            {
                foreach (var assignment in proposal.Assignments)
                {
                    if (assignment.Referee != null)
                    {
                        assignment.Referee.ContactInfo = "[Bảo mật - hiển thị sau khi chốt báo giá]";
                    }
                }
            }
        }

        return req;
    }

    public async Task<List<RefereeRequest>> GetTeamRequestsAsync(Guid teamId)
    {
        return await _db.RefereeRequests
            .Include(r => r.Team)
            .Include(r => r.Match)
            .Include(r => r.Proposals)
            .Where(r => r.TeamId == teamId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<RefereeRequest>> GetUserManageableRequestsAsync(string userId)
    {
        // Get all teams where user is manager or owner or member
        var userTeamIds = await _db.TeamMembers
            .Where(m => m.UserId == userId && (m.Role == TeamRole.Manager || m.Role == TeamRole.Owner))
            .Select(m => m.TeamId)
            .ToListAsync();

        return await _db.RefereeRequests
            .Include(r => r.Team)
            .Include(r => r.Match)
            .Include(r => r.Proposals)
            .Where(r => userTeamIds.Contains(r.TeamId))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<RefereeRequest>> GetAdminInboxRequestsAsync(RefereeRequestStatus? statusFilter = null, string? search = null)
    {
        var query = _db.RefereeRequests
            .Include(r => r.Team)
            .Include(r => r.Match)
            .Include(r => r.Proposals).ThenInclude(p => p.Assignments)
            .AsQueryable();

        if (statusFilter.HasValue)
        {
            query = query.Where(r => r.Status == statusFilter.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(r => r.Team.Name.ToLower().Contains(s)
                                     || r.VenueName.ToLower().Contains(s)
                                     || r.VenueAddress.ToLower().Contains(s));
        }

        return await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
    }

    public async Task<List<Referee>> GetActiveRefereesAsync()
    {
        return await _db.Referees
            .Where(r => r.IsActive)
            .OrderBy(r => r.Name)
            .ToListAsync();
    }

    public async Task<List<Referee>> GetAllRefereesAsync()
    {
        return await _db.Referees
            .OrderByDescending(r => r.IsActive)
            .ThenBy(r => r.Name)
            .ToListAsync();
    }

    public async Task<Referee?> GetRefereeByIdAsync(Guid id)
    {
        return await _db.Referees.FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<Referee> CreateRefereeAsync(Referee referee, string adminUserId)
    {
        if (!await _auth.IsPlatformAdminAsync(adminUserId))
            throw new UnauthorizedAccessException("Chỉ Quản trị viên mới có quyền thêm trọng tài.");

        if (string.IsNullOrWhiteSpace(referee.Name))
            throw new ArgumentException("Tên trọng tài là bắt buộc.");
        if (string.IsNullOrWhiteSpace(referee.ContactInfo))
            throw new ArgumentException("Thông tin liên hệ trọng tài là bắt buộc.");
        if (string.IsNullOrWhiteSpace(referee.ExperienceSummary))
            throw new ArgumentException("Kinh nghiệm / chuyên môn trọng tài là bắt buộc.");

        referee.Id = Guid.NewGuid();
        _db.Referees.Add(referee);
        await _db.SaveChangesAsync();
        return referee;
    }

    public async Task<Referee> UpdateRefereeAsync(Referee referee, string adminUserId)
    {
        if (!await _auth.IsPlatformAdminAsync(adminUserId))
            throw new UnauthorizedAccessException("Chỉ Quản trị viên mới có quyền sửa thông tin trọng tài.");

        var existing = await _db.Referees.FirstOrDefaultAsync(r => r.Id == referee.Id);
        if (existing == null) throw new KeyNotFoundException("Không tìm thấy trọng tài.");

        existing.Name = referee.Name;
        existing.ContactInfo = referee.ContactInfo;
        existing.ExperienceSummary = referee.ExperienceSummary;
        existing.SupportedFormats = referee.SupportedFormats;
        existing.ServiceNotes = referee.ServiceNotes;
        existing.DefaultPriceVND = referee.DefaultPriceVND;
        existing.IsActive = referee.IsActive;

        await _db.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> ToggleRefereeStatusAsync(Guid refereeId, string adminUserId)
    {
        if (!await _auth.IsPlatformAdminAsync(adminUserId))
            throw new UnauthorizedAccessException("Chỉ Quản trị viên mới có quyền thay đổi trạng thái.");

        var existing = await _db.Referees.FirstOrDefaultAsync(r => r.Id == refereeId);
        if (existing == null) return false;

        existing.IsActive = !existing.IsActive;
        await _db.SaveChangesAsync();
        return true;
    }
}
