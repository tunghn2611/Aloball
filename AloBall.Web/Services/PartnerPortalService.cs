using System.Collections.Concurrent;
using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Services;

public interface IPartnerPortalService
{
    Task<PartnerPortalViewModel> BuildPortalAsync(string? userId, string? activeTab, Guid? selectedPitchId, string baseAppUrl);
    Task<FlashDealBannerViewModel> GetActiveFlashDealsForRegionAsync(string? region, string? userId, string baseAppUrl);
    Task<(bool Success, string Message, PitchFlashDeal? Deal)> CreateFlashDealAsync(CreateFlashDealInput input, string actorUserId);
    Task<(bool Success, string Message, MatchGame? Match)> ClaimFlashDealAsync(Guid dealId, Guid teamId, string captainUserId);
    Task<(bool Success, string Message)> CancelFlashDealAsync(Guid dealId, string actorUserId);
    Task<(bool Success, string Message)> UpdateBookingStatusByOwnerAsync(Guid requestId, bool approve, string? fieldCode, decimal? offerPriceVnd, string actorUserId);
    Task<(bool Success, string Message)> ToggleRefereeAvailabilityAsync(Guid refereeId, string actorUserId);
    Task<(bool Success, string Message, PitchVenue? Venue)> RegisterPartnerVenueAsync(RegisterPartnerVenueInput input, string userId);
    Task<(bool Success, string Message, Referee? Referee)> RegisterRefereePartnerAsync(RegisterRefereePartnerInput input, string userId);
}

public class PartnerPortalService : IPartnerPortalService
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> DealClaimLocks = new();

    private readonly ApplicationDbContext _db;
    private readonly ITeamAuthorizationService _auth;

    public PartnerPortalService(ApplicationDbContext db, ITeamAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public static int CalculateDiscountPercent(decimal originalPriceVnd, decimal dealPriceVnd)
    {
        if (originalPriceVnd <= 0m || dealPriceVnd >= originalPriceVnd) return 0;
        var raw = (originalPriceVnd - dealPriceVnd) / originalPriceVnd * 100m;
        return Math.Clamp((int)Math.Round(raw, MidpointRounding.AwayFromZero), 1, 99);
    }

    public static async Task<(bool Success, string? Error, PitchFlashDeal? LockedDeal)> TryLockFlashDealAsync(
        ApplicationDbContext db, Guid dealId, Guid teamId, Guid matchId)
    {
        var sem = DealClaimLocks.GetOrAdd(dealId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync();
        try
        {
            var deal = await db.PitchFlashDeals
                .Include(d => d.Pitch)
                .FirstOrDefaultAsync(d => d.Id == dealId);

            if (deal == null)
                return (false, "Không tìm thấy suất sân Flash Deal này.", null);

            if (db.Entry(deal).State != EntityState.Detached)
                await db.Entry(deal).ReloadAsync();

            var localNow = CompetitionRules.LocalNow;
            if (deal.Status == PitchFlashDealStatus.Active && deal.KickoffAt <= localNow)
            {
                deal.Status = PitchFlashDealStatus.Expired;
                await db.SaveChangesAsync();
                return (false, "Suất sân giờ chót này đã quá giờ bóng lăn.", null);
            }

            if (deal.Status != PitchFlashDealStatus.Active)
                return (false, "Kèo giờ chót này vừa được đội khác chốt mất rồi!", null);

            var nowUtc = DateTime.UtcNow;
            if (db.Database.IsRelational())
            {
                int affected = await db.PitchFlashDeals
                    .Where(d => d.Id == dealId && d.Status == PitchFlashDealStatus.Active && d.ClaimVersion == deal.ClaimVersion)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(d => d.Status, PitchFlashDealStatus.Claimed)
                        .SetProperty(d => d.ClaimedByTeamId, teamId)
                        .SetProperty(d => d.ClaimedMatchId, matchId)
                        .SetProperty(d => d.ClaimedAt, nowUtc)
                        .SetProperty(d => d.ClaimVersion, deal.ClaimVersion + 1));

                if (affected == 0)
                    return (false, "Kèo giờ chót này vừa được đội khác chốt mất rồi!", null);

                await db.Entry(deal).ReloadAsync();
                return (true, null, deal);
            }

            deal.Status = PitchFlashDealStatus.Claimed;
            deal.ClaimedByTeamId = teamId;
            deal.ClaimedMatchId = matchId;
            deal.ClaimedAt = nowUtc;
            deal.ClaimVersion += 1;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return (false, "Kèo giờ chót này vừa được đội khác chốt mất rồi!", null);
            }

            return (true, null, deal);
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task<FlashDealBannerViewModel> GetActiveFlashDealsForRegionAsync(
        string? region, string? userId, string baseAppUrl)
    {
        await ExpireStaleDealsAsync();

        var localNow = CompetitionRules.LocalNow;
        var activeDeals = await _db.PitchFlashDeals
            .AsNoTracking()
            .Include(d => d.Pitch)
            .Where(d => d.Status == PitchFlashDealStatus.Active && d.KickoffAt > localNow)
            .OrderByDescending(d => d.DiscountPercent)
            .ThenBy(d => d.KickoffAt)
            .Take(12)
            .ToListAsync();

        string? preferredRegion = string.IsNullOrWhiteSpace(region) ? null : region.Trim();
        var managedTeams = new List<Team>();

        if (!string.IsNullOrWhiteSpace(userId))
        {
            managedTeams = await _db.TeamMembers
                .AsNoTracking()
                .Where(m => m.UserId == userId && (m.Role == TeamRole.Owner || m.Role == TeamRole.Manager))
                .Select(m => m.Team)
                .OrderBy(t => t.Name)
                .ToListAsync();

            if (string.IsNullOrEmpty(preferredRegion))
            {
                preferredRegion = managedTeams.Select(t => t.ActivityArea).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a))
                    ?? await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Region).FirstOrDefaultAsync();
            }
        }

        var mapped = activeDeals
            .Select(d => MapDealCard(d, preferredRegion, baseAppUrl))
            .OrderByDescending(c => c.IsUserRegionMatch ? 1 : 0)
            .ThenByDescending(c => c.DiscountPercent)
            .ThenBy(c => c.KickoffAt)
            .ToList();

        return new FlashDealBannerViewModel
        {
            Deals = mapped,
            ManagedTeams = managedTeams,
            PreferredRegion = preferredRegion,
            IsAuthenticated = !string.IsNullOrWhiteSpace(userId)
        };
    }

    public async Task<PartnerPortalViewModel> BuildPortalAsync(
        string? userId, string? activeTab, Guid? selectedPitchId, string baseAppUrl)
    {
        await ExpireStaleDealsAsync();

        var tab = string.Equals(activeTab, "referee", StringComparison.OrdinalIgnoreCase) ? "referee" : "pitch";
        var localNow = CompetitionRules.LocalNow;
        var activeMarketDealsRaw = await _db.PitchFlashDeals
            .AsNoTracking()
            .Include(d => d.Pitch)
            .Where(d => d.Status == PitchFlashDealStatus.Active && d.KickoffAt > localNow)
            .OrderByDescending(d => d.DiscountPercent)
            .ThenBy(d => d.KickoffAt)
            .ToListAsync();

        var vm = new PartnerPortalViewModel
        {
            ActiveTab = tab,
            IsAuthenticated = !string.IsNullOrWhiteSpace(userId),
            MarketplaceFlashDeals = activeMarketDealsRaw.Select(d => MapDealCard(d, null, baseAppUrl)).ToList(),
            CreateDealInput = new CreateFlashDealInput
            {
                KickoffAt = new DateTime(localNow.Year, localNow.Month, localNow.Day, Math.Min(localNow.Hour + 2, 22), 0, 0)
            }
        };

        if (string.IsNullOrWhiteSpace(userId))
            return vm;

        bool isAdmin = await _auth.IsPlatformAdminAsync(userId);
        vm.IsPlatformAdmin = isAdmin;

        vm.ManagedTeams = await _db.TeamMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId && (m.Role == TeamRole.Owner || m.Role == TeamRole.Manager))
            .Select(m => m.Team)
            .OrderBy(t => t.Name)
            .ToListAsync();

        // 1. Pitch Owner Portal data
        var ownedVenues = await _db.PitchVenues
            .AsNoTracking()
            .Where(v => isAdmin || v.OwnerUserId == userId)
            .OrderBy(v => v.Name)
            .ToListAsync();

        vm.OwnedVenues = ownedVenues;
        if (ownedVenues.Count > 0)
        {
            var venueIds = ownedVenues.Select(v => v.Id).ToList();
            var pickedVenue = (selectedPitchId.HasValue ? ownedVenues.FirstOrDefault(v => v.Id == selectedPitchId.Value) : null)
                              ?? ownedVenues.First();
            vm.SelectedPitchId = pickedVenue.Id;
            vm.CreateDealInput.PitchId = pickedVenue.Id;
            vm.CreateDealInput.OriginalPriceVnd = pickedVenue.IndicativePricePerHour ?? 650_000m;
            vm.CreateDealInput.DealPriceVnd = Math.Round(vm.CreateDealInput.OriginalPriceVnd * 0.65m / 10000m, 0) * 10000m;

            var bookings = await _db.PitchBookingRequests
                .AsNoTracking()
                .Include(r => r.PitchVenue)
                .Include(r => r.Team)
                .Include(r => r.Offers)
                .Where(r => venueIds.Contains(r.PitchVenueId))
                .OrderByDescending(r => r.ServiceDate)
                .ThenBy(r => r.StartTime)
                .Take(40)
                .ToListAsync();

            var weekAgo = localNow.Date.AddDays(-7);
            var weekAhead = localNow.Date.AddDays(7);
            vm.WeeklyBookingCount = bookings.Count(b =>
                (b.Status == PitchBookingStatus.Confirmed || b.Status == PitchBookingStatus.Completed)
                && b.ServiceDate.Date >= weekAgo && b.ServiceDate.Date <= weekAhead);

            decimal offerRevenue = bookings
                .Where(b => b.Status == PitchBookingStatus.Confirmed || b.Status == PitchBookingStatus.Completed)
                .Select(b => b.Offers.FirstOrDefault(o => o.Status == PitchBookingOfferStatus.Accepted)?.TotalPriceVND ?? b.BudgetVND ?? 0m)
                .Sum();

            var ownerDeals = await _db.PitchFlashDeals
                .AsNoTracking()
                .Include(d => d.Pitch)
                .Include(d => d.ClaimedByTeam)
                .Where(d => venueIds.Contains(d.PitchId))
                .OrderByDescending(d => d.CreatedAt)
                .Take(30)
                .ToListAsync();

            decimal flashRevenue = ownerDeals
                .Where(d => d.Status == PitchFlashDealStatus.Claimed)
                .Sum(d => d.DealPriceVnd);

            vm.GrossRevenueVnd = offerRevenue + flashRevenue;
            vm.ActiveDealsCount = ownerDeals.Count(d => d.Status == PitchFlashDealStatus.Active && d.KickoffAt > localNow);
            vm.OwnerFlashDeals = ownerDeals.Select(d => MapDealCard(d, null, baseAppUrl)).ToList();

            vm.Bookings = bookings.Select(b => new PartnerBookingRowDto
            {
                RequestId = b.Id,
                PitchName = b.PitchVenue.Name,
                TeamName = b.Team.Name,
                ServiceDate = b.ServiceDate,
                StartTime = b.StartTime,
                EndTime = b.EndTime,
                PitchFormat = b.PitchFormat,
                Status = b.Status,
                ConfirmedFieldCode = b.ConfirmedFieldCode,
                AcceptedPriceVnd = b.Offers.FirstOrDefault(o => o.Status == PitchBookingOfferStatus.Accepted)?.TotalPriceVND ?? b.BudgetVND
            }).ToList();
        }

        // 2. Referee Partner Portal data
        var refProfiles = await _db.Referees
            .AsNoTracking()
            .Where(r => r.IsActive && (isAdmin || r.UserId == userId))
            .OrderBy(r => r.Name)
            .ToListAsync();

        vm.RefereeProfiles = refProfiles;
        vm.ActiveRefereeProfile = refProfiles.FirstOrDefault();

        if (refProfiles.Count > 0)
        {
            var refIds = refProfiles.Select(r => r.Id).ToList();
            var assignments = await _db.RefereeAssignments
                .AsNoTracking()
                .Include(a => a.Proposal).ThenInclude(p => p.Request).ThenInclude(req => req.Team)
                .Include(a => a.Proposal).ThenInclude(p => p.Request).ThenInclude(req => req.Match).ThenInclude(m => m!.TeamA)
                .Include(a => a.Proposal).ThenInclude(p => p.Request).ThenInclude(req => req.Match).ThenInclude(m => m!.TeamB)
                .Include(a => a.Proposal).ThenInclude(p => p.Request).ThenInclude(req => req.Match).ThenInclude(m => m!.Tournament)
                .Where(a => refIds.Contains(a.RefereeId) && a.Proposal.Status == RefereeProposalStatus.Accepted)
                .OrderByDescending(a => a.Proposal.Request.ServiceDate)
                .ToListAsync();

            foreach (var a in assignments)
            {
                var req = a.Proposal.Request;
                var match = req.Match;
                DateTime kickoffLocal = match != null
                    ? CompetitionRules.ToLocal(match.MatchDateTime)
                    : req.ServiceDate.Date.Add(req.StartTime);

                bool isCompleted = req.Status == RefereeRequestStatus.Completed || match?.Status == MatchStatus.Completed;
                string sourceTag = match?.TournamentId != null
                    ? "Giải đấu"
                    : (req.Notes?.Contains("Concierge", StringComparison.OrdinalIgnoreCase) == true ? "Concierge Pro" : "Điều phối");

                string matchTitle = match != null
                    ? $"{match.TeamA.Name} vs {(match.TeamB?.Name ?? match.TeamBExternalName ?? "Đội khách")}"
                    : $"{req.Team.Name} ({req.MatchFormat})";

                var item = new RefereeAssignmentItemDto
                {
                    AssignmentId = a.Id,
                    RequestId = req.Id,
                    MatchId = req.MatchId,
                    SourceTag = sourceTag,
                    MatchTitle = matchTitle,
                    VenueName = req.VenueName,
                    KickoffAt = kickoffLocal,
                    FeeVnd = a.Proposal.TotalPriceVND,
                    IsCompleted = isCompleted,
                    StatusLabel = isCompleted ? "Hoàn thành" : "Sắp bắt chính"
                };

                if (isCompleted)
                {
                    vm.PastAssignments.Add(item);
                    vm.CompletedRefereeMatches++;
                    vm.RefereeTotalEarningsVnd += item.FeeVnd;
                }
                else
                {
                    vm.UpcomingAssignments.Add(item);
                }
            }
        }

        return vm;
    }

    public async Task<(bool Success, string Message, PitchFlashDeal? Deal)> CreateFlashDealAsync(
        CreateFlashDealInput input, string actorUserId)
    {
        var venue = await _db.PitchVenues.FindAsync(input.PitchId);
        if (venue == null || !venue.IsActive)
            return (false, "Cụm sân không tồn tại hoặc đang ngừng hoạt động.", null);

        bool isOwner = !string.IsNullOrEmpty(venue.OwnerUserId) && venue.OwnerUserId == actorUserId;
        if (!isOwner && !await _auth.IsPlatformAdminAsync(actorUserId))
            return (false, "Chỉ Chủ sân sở hữu hoặc Quản trị viên mới được phát hành Flash Deal cho sân này.", null);

        var localNow = CompetitionRules.LocalNow;
        if (input.KickoffAt <= localNow)
            return (false, "Giờ bóng lăn Flash Deal phải ở tương lai (theo giờ Việt Nam Asia/Ho_Chi_Minh).", null);

        if (input.KickoffAt > localNow.AddHours(24))
            return (false, "Flash Deal giờ chót chỉ áp dụng cho khung giờ trong vòng 24 giờ tới.", null);

        if (input.DurationMinutes is < 30 or > 240)
            return (false, "Thời lượng trận đấu phải từ 30 đến 240 phút.", null);

        if (input.OriginalPriceVnd <= 0m || input.DealPriceVnd <= 0m || input.DealPriceVnd >= input.OriginalPriceVnd)
            return (false, "Giá Flash Deal phải lớn hơn 0đ và thấp hơn hẳn giá gốc.", null);

        int discountPct = CalculateDiscountPercent(input.OriginalPriceVnd, input.DealPriceVnd);
        var region = !string.IsNullOrWhiteSpace(venue.District) && !string.IsNullOrWhiteSpace(venue.City)
            ? $"{venue.District}, {venue.City}"
            : venue.District;

        var deal = new PitchFlashDeal
        {
            PitchId = venue.Id,
            FieldCode = string.IsNullOrWhiteSpace(input.FieldCode) ? "Sân 7 - Suất giờ chót" : input.FieldCode.Trim(),
            Region = region,
            KickoffAt = input.KickoffAt,
            DurationMinutes = input.DurationMinutes,
            OriginalPriceVnd = input.OriginalPriceVnd,
            DealPriceVnd = input.DealPriceVnd,
            DiscountPercent = discountPct,
            Note = string.IsNullOrWhiteSpace(input.Note) ? $"Giảm {discountPct}% sân trống giờ chót" : input.Note.Trim(),
            Status = PitchFlashDealStatus.Active,
            CreatedByUserId = actorUserId,
            CreatedAt = DateTime.UtcNow
        };

        _db.PitchFlashDeals.Add(deal);
        await _db.SaveChangesAsync();

        return (true, $"Đã phát hành Flash Deal -{discountPct}% tại {venue.Name} ({deal.KickoffAt:HH:mm dd/MM})!", deal);
    }

    public async Task<(bool Success, string Message, MatchGame? Match)> ClaimFlashDealAsync(
        Guid dealId, Guid teamId, string captainUserId)
    {
        if (!await _auth.HasManagerAccessAsync(teamId, captainUserId))
            return (false, "Chỉ Đội trưởng / Quản lý đội mới có quyền chốt Flash Deal cho CLB.", null);

        var team = await _db.Teams.FindAsync(teamId);
        if (team == null || team.IsDraft)
            return (false, "Đội bóng không tồn tại hoặc chưa hoàn tất.", null);

        Guid matchId = Guid.NewGuid();
        var (locked, lockErr, deal) = await TryLockFlashDealAsync(_db, dealId, teamId, matchId);
        if (!locked || deal == null)
            return (false, lockErr ?? "Không thể chốt suất sân giờ chót.", null);

        var venue = deal.Pitch ?? await _db.PitchVenues.FindAsync(deal.PitchId);
        string pitchName = venue != null
            ? $"{venue.Name}{(!string.IsNullOrWhiteSpace(deal.FieldCode) ? $" ({deal.FieldCode})" : "")}"
            : deal.Region;

        var match = new MatchGame
        {
            Id = matchId,
            Category = MatchCategory.Friendly,
            Status = MatchStatus.Scheduled,
            MatchDateTime = deal.KickoffAt,
            PitchName = pitchName,
            PitchType = "7 người",
            TeamAId = team.Id,
            RsvpToken = Guid.NewGuid().ToString("N")[..8],
            IsPublic = true,
            Notes = $"⚡ Flash Deal -{deal.DiscountPercent}% ({deal.DealPriceVnd:N0}đ): {deal.Note}"
        };
        _db.Matches.Add(match);

        if (venue != null)
        {
            var startTime = deal.KickoffAt.TimeOfDay;
            var endTime = startTime.Add(TimeSpan.FromMinutes(deal.DurationMinutes));
            var bookingReq = new PitchBookingRequest
            {
                TeamId = team.Id,
                PitchVenueId = venue.Id,
                MatchId = match.Id,
                ServiceDate = deal.KickoffAt.Date,
                StartTime = startTime,
                EndTime = endTime,
                PitchFormat = "7 người",
                PitchCount = 1,
                BudgetVND = deal.DealPriceVnd,
                Status = PitchBookingStatus.Confirmed,
                ConfirmedFieldCode = deal.FieldCode,
                Notes = $"⚡ Chốt sân Flash Deal -{deal.DiscountPercent}% ({deal.DealPriceVnd:N0}đ)"
            };
            var offer = new PitchBookingOffer
            {
                RequestId = bookingReq.Id,
                AdminId = deal.CreatedByUserId,
                ConfirmedVenueName = venue.Name,
                ConfirmedAddress = venue.FullAddress,
                ConfirmedDate = deal.KickoffAt.Date,
                ConfirmedStartTime = startTime,
                ConfirmedEndTime = endTime,
                ConfirmedPitchFormat = "7 người",
                ConfirmedPitchCount = 1,
                SpecificFieldCode = deal.FieldCode,
                TotalPriceVND = deal.DealPriceVnd,
                Status = PitchBookingOfferStatus.Accepted
            };
            _db.PitchBookingRequests.Add(bookingReq);
            _db.PitchBookingOffers.Add(offer);
        }

        // Pre-fill discounted pitch fee directly into MatchSplitBillService expense ledger
        var activePlayers = await _db.Players.Where(p => p.TeamId == team.Id && p.IsActive).ToListAsync();
        var expense = MatchSplitBillService.BuildPreFilledExpense(
            match.Id,
            team.Id,
            deal.DealPriceVnd,
            0m,
            "100",
            $"⚡ Tiền sân ưu đãi Flash Deal -{deal.DiscountPercent}% (Giá gốc {deal.OriginalPriceVnd:N0}đ)",
            captainUserId,
            activePlayers);
        _db.MatchExpenses.Add(expense);

        await _db.SaveChangesAsync();

        return (true, $"Chốt thành công sân {pitchName} giá Flash {deal.DealPriceVnd:N0}đ! Phòng Matchday & VietQR đã được tạo sẵn.", match);
    }

    public async Task<(bool Success, string Message)> CancelFlashDealAsync(Guid dealId, string actorUserId)
    {
        var deal = await _db.PitchFlashDeals.Include(d => d.Pitch).FirstOrDefaultAsync(d => d.Id == dealId);
        if (deal == null) return (false, "Không tìm thấy Flash Deal.");

        bool isOwner = !string.IsNullOrEmpty(deal.Pitch.OwnerUserId) && deal.Pitch.OwnerUserId == actorUserId;
        if (!isOwner && deal.CreatedByUserId != actorUserId && !await _auth.IsPlatformAdminAsync(actorUserId))
            return (false, "Bạn không có quyền hủy Flash Deal này.");

        if (deal.Status != PitchFlashDealStatus.Active)
            return (false, "Chỉ có thể hủy Flash Deal đang ở trạng thái mở.");

        deal.Status = PitchFlashDealStatus.Expired;
        await _db.SaveChangesAsync();
        return (true, "Đã đóng Flash Deal.");
    }

    public async Task<(bool Success, string Message)> UpdateBookingStatusByOwnerAsync(
        Guid requestId, bool approve, string? fieldCode, decimal? offerPriceVnd, string actorUserId)
    {
        var req = await _db.PitchBookingRequests
            .Include(r => r.PitchVenue)
            .Include(r => r.Offers)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (req == null) return (false, "Không tìm thấy yêu cầu đặt sân.");

        bool isOwner = !string.IsNullOrEmpty(req.PitchVenue.OwnerUserId) && req.PitchVenue.OwnerUserId == actorUserId;
        if (!isOwner && !await _auth.IsPlatformAdminAsync(actorUserId))
            return (false, "Chỉ Chủ sân sở hữu mới có quyền xử lý lịch đặt sân này.");

        if (!approve)
        {
            req.Status = PitchBookingStatus.UnableToFulfill;
            req.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return (true, "Đã cập nhật trạng thái sân hết chỗ.");
        }

        decimal price = offerPriceVnd ?? req.BudgetVND ?? req.PitchVenue.IndicativePricePerHour ?? 600_000m;
        req.Status = PitchBookingStatus.Confirmed;
        req.ConfirmedFieldCode = string.IsNullOrWhiteSpace(fieldCode) ? "Sân 1" : fieldCode.Trim();
        req.UpdatedAt = DateTime.UtcNow;

        if (!req.Offers.Any(o => o.Status == PitchBookingOfferStatus.Accepted))
        {
            _db.PitchBookingOffers.Add(new PitchBookingOffer
            {
                RequestId = req.Id,
                AdminId = actorUserId,
                ConfirmedVenueName = req.PitchVenue.Name,
                ConfirmedAddress = req.PitchVenue.FullAddress,
                ConfirmedDate = req.ServiceDate.Date,
                ConfirmedStartTime = req.StartTime,
                ConfirmedEndTime = req.EndTime,
                ConfirmedPitchFormat = req.PitchFormat,
                ConfirmedPitchCount = req.PitchCount,
                SpecificFieldCode = req.ConfirmedFieldCode,
                TotalPriceVND = price,
                Status = PitchBookingOfferStatus.Accepted
            });
        }

        await _db.SaveChangesAsync();
        return (true, $"Đã xác nhận giữ sân {req.ConfirmedFieldCode} ({price:N0}đ).");
    }

    public async Task<(bool Success, string Message)> ToggleRefereeAvailabilityAsync(Guid refereeId, string actorUserId)
    {
        var referee = await _db.Referees.FindAsync(refereeId);
        if (referee == null || !referee.IsActive)
            return (false, "Hồ sơ trọng tài không tồn tại.");

        bool isOwner = !string.IsNullOrEmpty(referee.UserId) && referee.UserId == actorUserId;
        if (!isOwner && !await _auth.IsPlatformAdminAsync(actorUserId))
            return (false, "Chỉ trọng tài sở hữu hồ sơ mới được đổi trạng thái sẵn sàng.");

        referee.IsAvailable = !referee.IsAvailable;
        await _db.SaveChangesAsync();
        return (true, referee.IsAvailable ? "Trạng thái: Đang sẵn sàng nhận kèo bắt chính!" : "Trạng thái: Tạm bận, tạm ẩn nhận kèo tự động.");
    }

    public async Task<(bool Success, string Message, PitchVenue? Venue)> RegisterPartnerVenueAsync(
        RegisterPartnerVenueInput input, string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return (false, "Vui lòng đăng nhập.", null);

        var venue = new PitchVenue
        {
            Name = input.Name.Trim(),
            City = input.City.Trim(),
            District = input.District.Trim(),
            FullAddress = input.FullAddress.Trim(),
            ContactPhone = input.ContactPhone.Trim(),
            SupportedFormats = "5 người, 7 người",
            SurfaceType = "Cỏ nhân tạo",
            IndicativePricePerHour = input.IndicativePricePerHour,
            TotalPitches = input.TotalPitches,
            OwnerUserId = userId,
            IsActive = true
        };
        _db.PitchVenues.Add(venue);
        await _db.SaveChangesAsync();

        return (true, $"Đã đăng ký cụm sân đối tác '{venue.Name}' thành công!", venue);
    }

    public async Task<(bool Success, string Message, Referee? Referee)> RegisterRefereePartnerAsync(
        RegisterRefereePartnerInput input, string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return (false, "Vui lòng đăng nhập.", null);

        var existing = await _db.Referees.FirstOrDefaultAsync(r => r.UserId == userId);
        if (existing != null)
            return (false, "Tài khoản của bạn đã liên kết với hồ sơ trọng tài.", existing);

        var r = new Referee
        {
            Name = input.Name.Trim(),
            ContactInfo = input.ContactInfo.Trim(),
            ExperienceSummary = input.ExperienceSummary.Trim(),
            SupportedFormats = "5 người, 7 người, 11 người",
            DefaultPriceVND = input.DefaultPriceVND,
            UserId = userId,
            IsAvailable = true,
            IsActive = true
        };
        _db.Referees.Add(r);
        await _db.SaveChangesAsync();

        return (true, $"Đã kích hoạt hồ sơ Trọng tài đối tác '{r.Name}' thành công!", r);
    }

    private async Task ExpireStaleDealsAsync()
    {
        var localNow = CompetitionRules.LocalNow;
        var stale = await _db.PitchFlashDeals
            .Where(d => d.Status == PitchFlashDealStatus.Active && d.KickoffAt <= localNow)
            .ToListAsync();

        if (stale.Count > 0)
        {
            foreach (var d in stale)
                d.Status = PitchFlashDealStatus.Expired;
            await _db.SaveChangesAsync();
        }
    }

    private static PitchFlashDealCardDto MapDealCard(PitchFlashDeal d, string? preferredRegion, string baseAppUrl)
    {
        bool regionMatch = !string.IsNullOrWhiteSpace(preferredRegion)
            && (MatchConciergeService.RegionsMatch(d.Region, preferredRegion)
                || MatchConciergeService.RegionsMatch(d.Pitch?.District, preferredRegion)
                || MatchConciergeService.RegionsMatch(d.Pitch?.City, preferredRegion));

        var link = $"{baseAppUrl.TrimEnd('/')}/PartnerPortal?tab=pitch";
        var zaloText = $"⚡ [ALOBALL FLASH DEAL -{d.DiscountPercent}%] SÂN HỦY KÈO GIỜ CHÓT!\n" +
                       $"🏟 {d.Pitch?.Name ?? "Sân bóng"}{(!string.IsNullOrEmpty(d.FieldCode) ? $" ({d.FieldCode})" : "")} — {d.Region}\n" +
                       $"⏰ Bóng lăn: {d.KickoffAt:HH:mm} hôm nay {d.KickoffAt:dd/MM} ({d.DurationMinutes} phút)\n" +
                       $"💸 Giá gốc {d.OriginalPriceVnd:N0}đ ➝ CÒN {d.DealPriceVnd:N0}đ\n" +
                       (!string.IsNullOrWhiteSpace(d.Note) ? $"🎁 Ưu đãi: {d.Note}\n" : "") +
                       $"👉 Chốt ngay 1 chạm (đã chia sẵn VietQR): {link}";

        return new PitchFlashDealCardDto
        {
            Id = d.Id,
            PitchId = d.PitchId,
            PitchName = d.Pitch?.Name ?? "Sân đối tác AloBall",
            PitchAddress = d.Pitch?.FullAddress ?? d.Region,
            FieldCode = d.FieldCode,
            Region = d.Region,
            KickoffAt = d.KickoffAt,
            DurationMinutes = d.DurationMinutes,
            OriginalPriceVnd = d.OriginalPriceVnd,
            DealPriceVnd = d.DealPriceVnd,
            DiscountPercent = d.DiscountPercent,
            Note = d.Note,
            Status = d.Status,
            ClaimedByTeamId = d.ClaimedByTeamId,
            ClaimedByTeamName = d.ClaimedByTeam?.Name,
            ClaimedMatchId = d.ClaimedMatchId,
            IsUserRegionMatch = regionMatch,
            ZaloShareText = zaloText
        };
    }
}
