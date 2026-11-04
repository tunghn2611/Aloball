using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Services;

public interface IPitchBookingService
{
    // Public pitch discovery
    Task<List<PitchVenue>> GetActiveVenuesAsync(
        string? search = null,
        string? city = null,
        string? district = null,
        string? format = null,
        string? surface = null,
        decimal? maxPrice = null);

    Task<PitchVenue?> GetVenueDetailsAsync(Guid id);
    Task<List<string>> GetAvailableCitiesAsync();
    Task<List<string>> GetAvailableDistrictsAsync(string? city = null);

    // Super Admin venue CRUD
    Task<List<PitchVenue>> GetAllVenuesAsync();
    Task<PitchVenue> CreateVenueAsync(PitchVenue venue, string adminUserId);
    Task<PitchVenue> UpdateVenueAsync(PitchVenue venue, string adminUserId);
    Task<bool> ToggleVenueStatusAsync(Guid venueId, string adminUserId);

    // Booking Requests (Team side)
    Task<PitchBookingRequest> CreateBookingRequestAsync(PitchBookingRequest request, string userId);
    Task<bool> CancelBookingRequestAsync(Guid requestId, string userId);
    Task<bool> AcceptBookingOfferAsync(Guid offerId, string userId);
    Task<bool> DeclineBookingOfferAsync(Guid offerId, string userId);

    // Coordination (Admin side)
    Task<PitchBookingOffer> CreateBookingOfferAsync(Guid requestId, string adminUserId, PitchBookingOffer offer);
    Task<bool> SetRequestUnderReviewAsync(Guid requestId, string adminUserId);
    Task<bool> MarkUnableToFulfillAsync(Guid requestId, string adminUserId, string? reason);
    Task<bool> CompleteBookingAsync(Guid requestId, string adminUserId);

    // Queries
    Task<PitchBookingRequest?> GetRequestDetailsAsync(Guid requestId, string userId, bool isPlatformAdmin);
    Task<List<PitchBookingRequest>> GetUserManageableRequestsAsync(string userId);
    Task<List<PitchBookingRequest>> GetAdminInboxRequestsAsync(PitchBookingStatus? statusFilter = null, string? search = null);
}

public class PitchBookingService : IPitchBookingService
{
    private readonly ApplicationDbContext _db;
    private readonly ITeamAuthorizationService _auth;

    public PitchBookingService(ApplicationDbContext db, ITeamAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    // ==========================================
    // 1. PUBLIC PITCH DISCOVERY
    // ==========================================

    public async Task<List<PitchVenue>> GetActiveVenuesAsync(
        string? search = null,
        string? city = null,
        string? district = null,
        string? format = null,
        string? surface = null,
        decimal? maxPrice = null)
    {
        var query = _db.PitchVenues
            .AsNoTracking()
            .Where(v => v.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(v => v.Name.ToLower().Contains(s)
                                     || v.FullAddress.ToLower().Contains(s)
                                     || v.District.ToLower().Contains(s)
                                     || (v.Description != null && v.Description.ToLower().Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(city))
        {
            query = query.Where(v => v.City == city);
        }

        if (!string.IsNullOrWhiteSpace(district))
        {
            query = query.Where(v => v.District == district);
        }

        if (!string.IsNullOrWhiteSpace(format))
        {
            query = query.Where(v => v.SupportedFormats.Contains(format));
        }

        if (!string.IsNullOrWhiteSpace(surface))
        {
            query = query.Where(v => v.SurfaceType.Contains(surface));
        }

        if (maxPrice.HasValue && maxPrice.Value > 0)
        {
            query = query.Where(v => !v.IndicativePricePerHour.HasValue || v.IndicativePricePerHour <= maxPrice.Value);
        }

        return await query.OrderBy(v => v.City).ThenBy(v => v.District).ThenBy(v => v.Name).ToListAsync();
    }

    public async Task<PitchVenue?> GetVenueDetailsAsync(Guid id)
    {
        return await _db.PitchVenues
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id);
    }

    public async Task<List<string>> GetAvailableCitiesAsync()
    {
        return await _db.PitchVenues
            .Where(v => v.IsActive)
            .Select(v => v.City)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
    }

    public async Task<List<string>> GetAvailableDistrictsAsync(string? city = null)
    {
        var query = _db.PitchVenues.Where(v => v.IsActive);
        if (!string.IsNullOrWhiteSpace(city))
        {
            query = query.Where(v => v.City == city);
        }
        return await query.Select(v => v.District).Distinct().OrderBy(d => d).ToListAsync();
    }

    // ==========================================
    // 2. ADMIN VENUE MANAGEMENT (CRUD)
    // ==========================================

    public async Task<List<PitchVenue>> GetAllVenuesAsync()
    {
        return await _db.PitchVenues
            .AsNoTracking()
            .OrderByDescending(v => v.IsActive)
            .ThenBy(v => v.City)
            .ThenBy(v => v.Name)
            .ToListAsync();
    }

    public async Task<PitchVenue> CreateVenueAsync(PitchVenue venue, string adminUserId)
    {
        if (!await _auth.IsPlatformAdminAsync(adminUserId))
            throw new UnauthorizedAccessException("Chỉ Quản trị viên hệ thống mới có quyền thêm sân bóng.");

        if (string.IsNullOrWhiteSpace(venue.Name))
            throw new ArgumentException("Tên sân bóng là bắt buộc.");
        if (string.IsNullOrWhiteSpace(venue.FullAddress))
            throw new ArgumentException("Địa chỉ sân bóng là bắt buộc.");
        if (string.IsNullOrWhiteSpace(venue.ContactPhone))
            throw new ArgumentException("Số điện thoại liên hệ là bắt buộc.");
        if (string.IsNullOrWhiteSpace(venue.SupportedFormats))
            throw new ArgumentException("Thể thức sân hỗ trợ là bắt buộc.");

        venue.Id = Guid.NewGuid();
        venue.CreatedAt = DateTime.UtcNow;
        _db.PitchVenues.Add(venue);
        await _db.SaveChangesAsync();

        return venue;
    }

    public async Task<PitchVenue> UpdateVenueAsync(PitchVenue venue, string adminUserId)
    {
        if (!await _auth.IsPlatformAdminAsync(adminUserId))
            throw new UnauthorizedAccessException("Chỉ Quản trị viên hệ thống mới có quyền sửa thông tin sân bóng.");

        var existing = await _db.PitchVenues.FirstOrDefaultAsync(v => v.Id == venue.Id);
        if (existing == null) throw new KeyNotFoundException("Không tìm thấy sân bóng.");

        existing.Name = venue.Name.Trim();
        existing.Description = venue.Description?.Trim();
        existing.City = venue.City.Trim();
        existing.District = venue.District.Trim();
        existing.FullAddress = venue.FullAddress.Trim();
        existing.ContactPhone = venue.ContactPhone.Trim();
        existing.SupportedFormats = venue.SupportedFormats.Trim();
        existing.SurfaceType = venue.SurfaceType.Trim();
        existing.Facilities = venue.Facilities?.Trim();
        existing.OpeningHours = venue.OpeningHours?.Trim();
        existing.IndicativePricePerHour = venue.IndicativePricePerHour;
        existing.PriceNotes = venue.PriceNotes?.Trim();
        existing.ImageUrl = venue.ImageUrl?.Trim();
        existing.MapLink = venue.MapLink?.Trim();
        existing.TotalPitches = venue.TotalPitches > 0 ? venue.TotalPitches : 1;
        existing.IsActive = venue.IsActive;

        await _db.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> ToggleVenueStatusAsync(Guid venueId, string adminUserId)
    {
        if (!await _auth.IsPlatformAdminAsync(adminUserId))
            throw new UnauthorizedAccessException("Chỉ Quản trị viên mới có quyền đổi trạng thái sân bóng.");

        var existing = await _db.PitchVenues.FirstOrDefaultAsync(v => v.Id == venueId);
        if (existing == null) return false;

        // Soft deactivation: preserves venue record and booking history
        existing.IsActive = !existing.IsActive;
        await _db.SaveChangesAsync();
        return true;
    }

    // ==========================================
    // 3. BOOKING REQUESTS (TEAM SIDE)
    // ==========================================

    public async Task<PitchBookingRequest> CreateBookingRequestAsync(PitchBookingRequest request, string userId)
    {
        // 1. Authorization: Only Owner/Manager can submit
        var hasAccess = await _auth.HasManagerAccessAsync(request.TeamId, userId);
        if (!hasAccess)
        {
            throw new UnauthorizedAccessException("Chỉ Ban cán sự hoặc Đội trưởng mới có quyền tạo yêu cầu đặt sân.");
        }

        // 2. Server-side validations
        if (request.ServiceDate.Date < DateTime.Today)
        {
            throw new ArgumentException("Ngày đặt sân không được ở trong quá khứ.");
        }

        if (request.EndTime <= request.StartTime)
        {
            throw new ArgumentException("Giờ kết thúc phải muộn hơn giờ bắt đầu.");
        }

        if (request.PitchCount <= 0)
        {
            throw new ArgumentException("Số lượng sân đặt phải từ 1 sân trở lên.");
        }

        if (string.IsNullOrWhiteSpace(request.PitchFormat))
        {
            throw new ArgumentException("Thể thức sân (5/7/11 người) là bắt buộc.");
        }

        if (request.BudgetVND.HasValue && request.BudgetVND.Value < 0)
        {
            throw new ArgumentException("Ngân sách dự kiến không được âm.");
        }

        var venue = await _db.PitchVenues.FirstOrDefaultAsync(v => v.Id == request.PitchVenueId);
        if (venue == null || !venue.IsActive)
        {
            throw new InvalidOperationException("Sân bóng được chọn hiện không khả dụng hoặc đã tạm ngừng nhận đặt sân.");
        }

        request.Id = Guid.NewGuid();
        request.Status = PitchBookingStatus.Pending;
        request.CreatedAt = DateTime.UtcNow;
        request.UpdatedAt = null;

        _db.PitchBookingRequests.Add(request);
        await _db.SaveChangesAsync();

        // 3. Send Notification to Team
        await SendNotificationAsync(
            request.TeamId,
            userId,
            title: "Yêu cầu đặt sân mới đã được gửi",
            content: $"Yêu cầu đặt {request.PitchCount} sân {request.PitchFormat} tại {venue.Name} vào ngày {request.ServiceDate:dd/MM/yyyy} ({request.StartTime:hh\\:mm} - {request.EndTime:hh\\:mm}) đã được gửi tới Quản trị viên AloBall.");

        return request;
    }

    public async Task<bool> CancelBookingRequestAsync(Guid requestId, string userId)
    {
        var req = await _db.PitchBookingRequests
            .Include(r => r.PitchVenue)
            .FirstOrDefaultAsync(r => r.Id == requestId);
        if (req == null) return false;

        var isPlatformAdmin = await _auth.IsPlatformAdminAsync(userId);
        var isManager = await _auth.HasManagerAccessAsync(req.TeamId, userId);
        if (!isPlatformAdmin && !isManager)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền hủy yêu cầu đặt sân này.");
        }

        if (req.Status == PitchBookingStatus.Confirmed)
        {
            throw new InvalidOperationException("Yêu cầu đặt sân đã được xác nhận (Confirmed). Vui lòng liên hệ trực tiếp Super Admin để hủy sân.");
        }

        if (req.Status == PitchBookingStatus.Completed || req.Status == PitchBookingStatus.Cancelled)
        {
            throw new InvalidOperationException("Không thể hủy yêu cầu ở trạng thái hiện tại.");
        }

        req.Status = PitchBookingStatus.Cancelled;
        req.UpdatedAt = DateTime.UtcNow;

        // Withdraw pending offers
        var pendingOffers = await _db.PitchBookingOffers
            .Where(o => o.RequestId == requestId && o.Status == PitchBookingOfferStatus.Pending)
            .ToListAsync();
        foreach (var offer in pendingOffers)
        {
            offer.Status = PitchBookingOfferStatus.Withdrawn;
        }

        await _db.SaveChangesAsync();

        // Notification
        await SendNotificationAsync(
            req.TeamId,
            userId,
            title: "Yêu cầu đặt sân đã được hủy",
            content: $"Yêu cầu đặt sân tại {req.PitchVenue.Name} ngày {req.ServiceDate:dd/MM/yyyy} đã được hủy.");

        return true;
    }

    public async Task<bool> AcceptBookingOfferAsync(Guid offerId, string userId)
    {
        using var tx = await _db.Database.BeginTransactionAsync();

        var offer = await _db.PitchBookingOffers
            .Include(o => o.Request).ThenInclude(r => r.PitchVenue)
            .FirstOrDefaultAsync(o => o.Id == offerId);

        if (offer == null) return false;

        var req = offer.Request;
        var hasAccess = await _auth.HasManagerAccessAsync(req.TeamId, userId);
        if (!hasAccess)
        {
            throw new UnauthorizedAccessException("Chỉ Ban cán sự hoặc Đội trưởng mới có quyền chốt đặt sân.");
        }

        if (req.Status == PitchBookingStatus.Cancelled || req.Status == PitchBookingStatus.Completed)
        {
            throw new InvalidOperationException("Yêu cầu đã bị hủy hoặc hoàn thành, không thể chấp nhận báo giá.");
        }

        if (req.Status == PitchBookingStatus.Confirmed)
        {
            throw new InvalidOperationException("Yêu cầu đặt sân này đã được xác nhận trước đó.");
        }

        if (offer.Status != PitchBookingOfferStatus.Pending)
        {
            throw new InvalidOperationException($"Chỉ có thể chấp nhận báo giá đang chờ duyệt (Pending), trạng thái hiện tại là {offer.Status}.");
        }

        // Conflict check: Ensure no overlapping confirmed booking exists for the same venue/specific field
        await CheckConflictAsync(req.PitchVenueId, offer.ConfirmedDate, offer.ConfirmedStartTime, offer.ConfirmedEndTime, offer.ConfirmedPitchCount, offer.SpecificFieldCode, req.Id);

        offer.Status = PitchBookingOfferStatus.Accepted;
        req.Status = PitchBookingStatus.Confirmed;
        req.ConfirmedFieldCode = offer.SpecificFieldCode;
        req.UpdatedAt = DateTime.UtcNow;

        // Mark other competing offers as NotSelected
        var otherOffers = await _db.PitchBookingOffers
            .Where(o => o.RequestId == req.Id && o.Id != offerId && o.Status == PitchBookingOfferStatus.Pending)
            .ToListAsync();
        foreach (var other in otherOffers)
        {
            other.Status = PitchBookingOfferStatus.NotSelected;
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        // Notification
        await SendNotificationAsync(
            req.TeamId,
            userId,
            title: "Xác nhận đặt sân thành công (Confirmed)",
            content: $"Đội bóng đã chấp nhận báo giá đặt sân tại {offer.ConfirmedVenueName} (Giá: {offer.TotalPriceVND:N0} VNĐ, ngày {offer.ConfirmedDate:dd/MM/yyyy} lúc {offer.ConfirmedStartTime:hh\\:mm} - {offer.ConfirmedEndTime:hh\\:mm}).");

        return true;
    }

    public async Task<bool> DeclineBookingOfferAsync(Guid offerId, string userId)
    {
        var offer = await _db.PitchBookingOffers
            .Include(o => o.Request).ThenInclude(r => r.PitchVenue)
            .FirstOrDefaultAsync(o => o.Id == offerId);

        if (offer == null) return false;

        var req = offer.Request;
        var hasAccess = await _auth.HasManagerAccessAsync(req.TeamId, userId);
        if (!hasAccess)
        {
            throw new UnauthorizedAccessException("Chỉ Ban cán sự hoặc Đội trưởng mới có quyền từ chối báo giá đặt sân.");
        }

        if (offer.Status != PitchBookingOfferStatus.Pending)
        {
            throw new InvalidOperationException("Chỉ có thể từ chối báo giá đang chờ duyệt.");
        }

        offer.Status = PitchBookingOfferStatus.Declined;
        req.UpdatedAt = DateTime.UtcNow;

        var hasOtherPending = await _db.PitchBookingOffers
            .AnyAsync(o => o.RequestId == req.Id && o.Id != offerId && o.Status == PitchBookingOfferStatus.Pending);

        if (!hasOtherPending && req.Status == PitchBookingStatus.AwaitingTeamDecision)
        {
            req.Status = PitchBookingStatus.UnderReview;
        }

        await _db.SaveChangesAsync();

        // Notification
        await SendNotificationAsync(
            req.TeamId,
            userId,
            title: "Đã từ chối phương án đặt sân",
            content: $"Đội bóng đã từ chối báo giá đặt sân tại {offer.ConfirmedVenueName}. Quản trị viên sẽ kiểm tra lại để gửi phương án mới.");

        return true;
    }

    // ==========================================
    // 4. COORDINATION (ADMIN SIDE)
    // ==========================================

    public async Task<PitchBookingOffer> CreateBookingOfferAsync(Guid requestId, string adminUserId, PitchBookingOffer offer)
    {
        if (!await _auth.IsPlatformAdminAsync(adminUserId))
            throw new UnauthorizedAccessException("Chỉ Quản trị viên hệ thống mới có quyền gửi báo giá đặt sân.");

        var req = await _db.PitchBookingRequests
            .Include(r => r.PitchVenue)
            .FirstOrDefaultAsync(r => r.Id == requestId);
        if (req == null)
            throw new KeyNotFoundException("Không tìm thấy yêu cầu đặt sân.");

        if (req.Status == PitchBookingStatus.Cancelled || req.Status == PitchBookingStatus.Completed || req.Status == PitchBookingStatus.Confirmed)
        {
            throw new InvalidOperationException($"Không thể gửi báo giá khi yêu cầu đang ở trạng thái {req.Status}.");
        }

        if (offer.TotalPriceVND < 0)
        {
            throw new ArgumentException("Tổng giá đặt sân không được âm.");
        }

        if (offer.ConfirmedPitchCount <= 0)
        {
            offer.ConfirmedPitchCount = req.PitchCount > 0 ? req.PitchCount : 1;
        }

        // Conflict check
        await CheckConflictAsync(req.PitchVenueId, offer.ConfirmedDate, offer.ConfirmedStartTime, offer.ConfirmedEndTime, offer.ConfirmedPitchCount, offer.SpecificFieldCode, req.Id);

        offer.Id = Guid.NewGuid();
        offer.RequestId = requestId;
        offer.AdminId = adminUserId;
        offer.Status = PitchBookingOfferStatus.Pending;
        offer.SubmittedAt = DateTime.UtcNow;

        _db.PitchBookingOffers.Add(offer);

        req.Status = PitchBookingStatus.AwaitingTeamDecision;
        req.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        // Notification to team
        await SendNotificationAsync(
            req.TeamId,
            adminUserId,
            title: "Báo giá đặt sân mới từ Super Admin",
            content: $"AloBall đã gửi phương án đặt sân tại {offer.ConfirmedVenueName} (Giá: {offer.TotalPriceVND:N0} VNĐ) cho trận ngày {offer.ConfirmedDate:dd/MM/yyyy}. Vui lòng kiểm tra và xác nhận.");

        return offer;
    }

    public async Task<bool> SetRequestUnderReviewAsync(Guid requestId, string adminUserId)
    {
        if (!await _auth.IsPlatformAdminAsync(adminUserId))
            throw new UnauthorizedAccessException("Chỉ Quản trị viên mới có quyền cập nhật trạng thái.");

        var req = await _db.PitchBookingRequests.FirstOrDefaultAsync(r => r.Id == requestId);
        if (req == null) return false;

        if (req.Status == PitchBookingStatus.Pending)
        {
            req.Status = PitchBookingStatus.UnderReview;
            req.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return true;
    }

    public async Task<bool> MarkUnableToFulfillAsync(Guid requestId, string adminUserId, string? reason)
    {
        if (!await _auth.IsPlatformAdminAsync(adminUserId))
            throw new UnauthorizedAccessException("Chỉ Quản trị viên mới có quyền cập nhật trạng thái.");

        var req = await _db.PitchBookingRequests
            .Include(r => r.PitchVenue)
            .FirstOrDefaultAsync(r => r.Id == requestId);
        if (req == null) return false;

        req.Status = PitchBookingStatus.UnableToFulfill;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            req.Notes = string.IsNullOrWhiteSpace(req.Notes) ? $"[Admin]: {reason}" : $"{req.Notes}\n[Admin]: {reason}";
        }
        req.UpdatedAt = DateTime.UtcNow;

        // Withdraw pending offers
        var pendingOffers = await _db.PitchBookingOffers
            .Where(o => o.RequestId == requestId && o.Status == PitchBookingOfferStatus.Pending)
            .ToListAsync();
        foreach (var offer in pendingOffers)
        {
            offer.Status = PitchBookingOfferStatus.Withdrawn;
        }

        await _db.SaveChangesAsync();

        // Notification
        await SendNotificationAsync(
            req.TeamId,
            adminUserId,
            title: "Không thể đáp ứng yêu cầu đặt sân",
            content: $"Rất tiếc yêu cầu đặt sân tại {req.PitchVenue.Name} ngày {req.ServiceDate:dd/MM/yyyy} không thể đáp ứng. Lý do: {reason ?? "Đã kín lịch tất cả các khung giờ."}");

        return true;
    }

    public async Task<bool> CompleteBookingAsync(Guid requestId, string adminUserId)
    {
        if (!await _auth.IsPlatformAdminAsync(adminUserId))
            throw new UnauthorizedAccessException("Chỉ Quản trị viên mới có quyền hoàn thành dịch vụ.");

        var req = await _db.PitchBookingRequests
            .Include(r => r.PitchVenue)
            .FirstOrDefaultAsync(r => r.Id == requestId);
        if (req == null) return false;

        if (req.Status != PitchBookingStatus.Confirmed)
        {
            throw new InvalidOperationException("Chỉ có thể hoàn thành yêu cầu đã được xác nhận (Confirmed).");
        }

        req.Status = PitchBookingStatus.Completed;
        req.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Notification
        await SendNotificationAsync(
            req.TeamId,
            adminUserId,
            title: "Trận đấu và dịch vụ đặt sân đã hoàn tất",
            content: $"Dịch vụ đặt sân tại {req.PitchVenue.Name} ngày {req.ServiceDate:dd/MM/yyyy} đã hoàn tất. Cảm ơn đội bóng đã sử dụng AloBall!");

        return true;
    }

    // ==========================================
    // 5. QUERIES & CONFLICT LOGIC
    // ==========================================

    public async Task<PitchBookingRequest?> GetRequestDetailsAsync(Guid requestId, string userId, bool isPlatformAdmin)
    {
        var req = await _db.PitchBookingRequests
            .AsNoTracking()
            .Include(r => r.Team)
            .Include(r => r.PitchVenue)
            .Include(r => r.Match)
            .Include(r => r.Offers)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (req == null) return null;

        var isTeamMember = await _auth.HasMemberAccessAsync(req.TeamId, userId);
        if (!isPlatformAdmin && !isTeamMember)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền truy cập yêu cầu đặt sân của đội bóng khác.");
        }

        return req;
    }

    public async Task<List<PitchBookingRequest>> GetUserManageableRequestsAsync(string userId)
    {
        var userTeamIds = await _db.TeamMembers
            .Where(m => m.UserId == userId && (m.Role == TeamRole.Manager || m.Role == TeamRole.Owner))
            .Select(m => m.TeamId)
            .ToListAsync();

        return await _db.PitchBookingRequests
            .AsNoTracking()
            .Include(r => r.Team)
            .Include(r => r.PitchVenue)
            .Include(r => r.Match)
            .Include(r => r.Offers)
            .Where(r => userTeamIds.Contains(r.TeamId))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<PitchBookingRequest>> GetAdminInboxRequestsAsync(PitchBookingStatus? statusFilter = null, string? search = null)
    {
        var query = _db.PitchBookingRequests
            .AsNoTracking()
            .Include(r => r.Team)
            .Include(r => r.PitchVenue)
            .Include(r => r.Match)
            .Include(r => r.Offers)
            .AsQueryable();

        if (statusFilter.HasValue)
        {
            query = query.Where(r => r.Status == statusFilter.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(r => r.Team.Name.ToLower().Contains(s)
                                     || r.PitchVenue.Name.ToLower().Contains(s)
                                     || r.PitchVenue.District.ToLower().Contains(s));
        }

        return await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
    }

    private async Task CheckConflictAsync(
        Guid venueId,
        DateTime date,
        TimeSpan startTime,
        TimeSpan endTime,
        int requestedPitchCount,
        string? specificFieldCode,
        Guid currentRequestId)
    {
        var venue = await _db.PitchVenues.FirstOrDefaultAsync(v => v.Id == venueId);
        int totalVenueCapacity = venue?.TotalPitches > 0 ? venue.TotalPitches : 4;

        // Query all confirmed requests on same venue, same date, overlapping time range
        var confirmedOverlaps = await _db.PitchBookingRequests
            .Include(r => r.Offers)
            .Where(r => r.PitchVenueId == venueId
                        && r.Id != currentRequestId
                        && r.Status == PitchBookingStatus.Confirmed
                        && r.ServiceDate.Date == date.Date
                        && r.EndTime > startTime
                        && r.StartTime < endTime)
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(specificFieldCode))
        {
            var fieldConflict = confirmedOverlaps.FirstOrDefault(r =>
                string.Equals(r.ConfirmedFieldCode, specificFieldCode, StringComparison.OrdinalIgnoreCase)
                || r.Offers.Any(o => o.Status == PitchBookingOfferStatus.Accepted && string.Equals(o.SpecificFieldCode, specificFieldCode, StringComparison.OrdinalIgnoreCase)));

            if (fieldConflict != null)
            {
                throw new InvalidOperationException($"Sân con '{specificFieldCode}' tại {venue?.Name} đã có lịch đặt được xác nhận ({fieldConflict.StartTime:hh\\:mm} - {fieldConflict.EndTime:hh\\:mm}) vào ngày này.");
            }
        }

        // Capacity conflict check
        int currentlyConfirmedCount = confirmedOverlaps.Sum(r => r.PitchCount);
        if (currentlyConfirmedCount + requestedPitchCount > totalVenueCapacity)
        {
            throw new InvalidOperationException($"Khung giờ {startTime:hh\\:mm} - {endTime:hh\\:mm} tại {venue?.Name} đã kín sân (Đã xác nhận: {currentlyConfirmedCount}/{totalVenueCapacity} sân). Không thể xếp thêm {requestedPitchCount} sân.");
        }
    }

    private async Task SendNotificationAsync(Guid teamId, string senderUserId, string title, string content)
    {
        try
        {
            var msg = new TeamMessage
            {
                Id = Guid.NewGuid(),
                TeamId = teamId,
                SenderUserId = senderUserId,
                RecipientUserId = null, // Team announcement / leadership broadcast
                ConversationId = Guid.NewGuid(),
                MessageType = TeamMessageType.Announcement,
                Title = title,
                Content = content,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            _db.TeamMessages.Add(msg);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PitchBookingService] Notification error: {ex.Message}");
        }
    }
}
