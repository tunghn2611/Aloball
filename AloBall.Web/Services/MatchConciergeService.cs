using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Services;

public interface IMatchConciergeService
{
    Task<(bool Success, string Message)> ActivatePlusTrialAsync(Guid teamId, string managerUserId, PlanTier tier = PlanTier.Plus);
    Task<int> GetTeamAloballStarsAsync(Guid teamId);
    Task<(bool Success, string Message, TeamRecurringSchedule? Schedule)> SaveRecurringScheduleAsync(RecurringScheduleInput input, string managerUserId);
    Task<(bool Success, string Message, List<MatchConciergeOrder> Proposals)> RunAutoPairingForTeamAsync(Guid teamId, string managerUserId);
    Task<(bool Success, string Message, MatchConciergeOrder? Order)> ConfirmAutoPairingOrderAsync(Guid orderId, string managerUserId);
    Task<(bool Success, string Message, MatchConciergeOrder? Order, ConciergeOrderCardDto? Summary)> CreateAndFulfillConciergeOrderAsync(ConciergeOrderInput input, string managerUserId);
    Task<(bool Success, string Message)> CancelOrderAsync(Guid orderId, string managerUserId);
    Task<ConciergeWorkspaceViewModel> BuildWorkspaceAsync(string? userId, Guid? teamId, string? activeTab, string baseAppUrl);
}

public class MatchConciergeService : IMatchConciergeService
{
    private static readonly TimeSpan DefaultMatchDuration = TimeSpan.FromMinutes(90);

    private readonly ApplicationDbContext _db;
    private readonly ITeamAuthorizationService _auth;
    private readonly IAloballStarsService _stars;

    public MatchConciergeService(ApplicationDbContext db, ITeamAuthorizationService auth, IAloballStarsService? stars = null)
    {
        _db = db;
        _auth = auth;
        _stars = stars ?? new AloballStarsService(db);
    }

    public static int ComputeMaskFromDays(IEnumerable<int>? days)
    {
        if (days == null) return 0;
        int mask = 0;
        foreach (var d in days)
        {
            if (d is >= 0 and <= 6)
                mask |= 1 << d;
        }
        return mask;
    }

    public static bool RegionsMatch(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        var x = a.Trim();
        var y = b.Trim();
        return x.Contains(y, StringComparison.OrdinalIgnoreCase) || y.Contains(x, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<(bool Success, string Message)> ActivatePlusTrialAsync(Guid teamId, string managerUserId, PlanTier tier = PlanTier.Plus)
    {
        if (!await _auth.HasManagerAccessAsync(teamId, managerUserId))
            return (false, "Chỉ Đội trưởng / Quản lý mới được kích hoạt gói Plus/Pro cho đội.");

        var team = await _db.Teams.FindAsync(teamId);
        if (team == null)
            return (false, "Đội bóng không tồn tại.");

        var targetTier = tier == PlanTier.Free ? PlanTier.Plus : tier;
        team.PlanTier = targetTier;
        team.PlanExpiresAt = DateTime.UtcNow.AddDays(14);
        await _db.SaveChangesAsync();

        var tierName = targetTier == PlanTier.Pro ? "AloBall Pro" : "AloBall Plus";
        return (true, $"Đã kích hoạt dùng thử 14 ngày gói {tierName} cho {team.Name}!");
    }

    public async Task<int> GetTeamAloballStarsAsync(Guid teamId)
    {
        var map = await _stars.ComputeTeamStarsMapAsync([teamId]);
        return map.GetValueOrDefault(teamId, 0);
    }

    public async Task<(bool Success, string Message, TeamRecurringSchedule? Schedule)> SaveRecurringScheduleAsync(
        RecurringScheduleInput input, string managerUserId)
    {
        if (!await _auth.HasManagerAccessAsync(input.TeamId, managerUserId))
            return (false, "Chỉ Đội trưởng / Quản lý mới được cấu hình lịch cố định.", null);

        var team = await _db.Teams.FindAsync(input.TeamId);
        if (team == null)
            return (false, "Đội bóng không tồn tại.", null);

        if (!team.IsPaidPlanActive)
            return (false, "Xếp cặp tự động lịch cố định yêu cầu gói Plus/Pro. Vui lòng kích hoạt dùng thử Plus 14 ngày.", null);

        if (input.MinAloballStars < 0 || input.MinAloballStars > input.MaxAloballStars)
            return (false, "Khoảng Sao Aloball không hợp lệ (Min phải nhỏ hơn hoặc bằng Max).", null);

        if (input.MinFriendlyRating is < 1 or > 5)
            return (false, "Độ thân thiện tối thiểu phải từ 1.0 đến 5.0 sao.", null);

        int mask = input.SelectedDays is { Length: > 0 }
            ? ComputeMaskFromDays(input.SelectedDays)
            : input.DaysOfWeekMask & 0x7F;

        if (input.IsAutoPairingEnabled && mask == 0)
            return (false, "Vui lòng chọn ít nhất 1 thứ trong tuần để bật xếp cặp tự động.", null);

        var region = string.IsNullOrWhiteSpace(input.Region) ? team.ActivityArea?.Trim() : input.Region.Trim();
        if (string.IsNullOrWhiteSpace(region))
            return (false, "Vui lòng nhập Khu vực / Quận thi đấu.", null);

        var sched = await _db.TeamRecurringSchedules.FirstOrDefaultAsync(s => s.TeamId == team.Id);
        if (sched == null)
        {
            sched = new TeamRecurringSchedule { TeamId = team.Id };
            _db.TeamRecurringSchedules.Add(sched);
        }

        sched.IsAutoPairingEnabled = input.IsAutoPairingEnabled;
        sched.DaysOfWeekMask = mask;
        sched.PreferredKickoffTime = input.PreferredKickoffTime;
        sched.Region = region;
        sched.MinAloballStars = input.MinAloballStars;
        sched.MaxAloballStars = input.MaxAloballStars;
        sched.MinFriendlyRating = input.MinFriendlyRating;
        sched.AvoidHardTackle = input.AvoidHardTackle;
        sched.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return (true, $"Đã lưu lịch cố định & tiêu chuẩn đối thủ cho {team.Name}.", sched);
    }

    public async Task<(bool Success, string Message, List<MatchConciergeOrder> Proposals)> RunAutoPairingForTeamAsync(
        Guid teamId, string managerUserId)
    {
        if (!await _auth.HasManagerAccessAsync(teamId, managerUserId))
            return (false, "Chỉ Đội trưởng / Quản lý mới được quét ghép cặp tự động.", []);

        var myTeam = await _db.Teams.FindAsync(teamId);
        if (myTeam == null)
            return (false, "Đội bóng không tồn tại.", []);

        if (!myTeam.IsPaidPlanActive)
            return (false, "Tính năng Xếp cặp tự động yêu cầu gói Club Plus hoặc Pro.", []);

        if (myTeam.IsRestrictedFromMatchmaking)
            return (false, $"Đội bóng đang bị hạn chế bắt đối: {myTeam.MatchmakingRestrictionReason}", []);

        var mySchedule = await _db.TeamRecurringSchedules.FirstOrDefaultAsync(s => s.TeamId == teamId);
        if (mySchedule == null || !mySchedule.IsAutoPairingEnabled || mySchedule.DaysOfWeekMask == 0)
            return (false, "Vui lòng bật Xếp cặp tự động và chọn ngày đá cố định trước khi quét.", []);

        var peerSchedules = await _db.TeamRecurringSchedules
            .Include(s => s.Team)
            .Where(s => s.TeamId != teamId
                        && s.IsAutoPairingEnabled
                        && s.DaysOfWeekMask != 0
                        && !s.Team.IsDraft
                        && !s.Team.IsRestrictedFromMatchmaking
                        && s.Team.PlanTier != PlanTier.Free
                        && (!s.Team.PlanExpiresAt.HasValue || s.Team.PlanExpiresAt.Value > DateTime.UtcNow))
            .ToListAsync();

        var involvedIds = peerSchedules.Select(s => s.TeamId).Append(teamId).Distinct().ToList();
        var starsMap = await _stars.ComputeTeamStarsMapAsync(involvedIds);
        var vibesMap = await MatchmakingService.LoadTeamVibesMapAsync(_db, involvedIds);

        int myStars = starsMap.GetValueOrDefault(teamId, 0);
        bool myHardTackle = vibesMap.TryGetValue(teamId, out var myVibe) && myVibe.IsHighHardTackle;

        var existingOrders = await _db.MatchConciergeOrders
            .Where(o => o.TeamId == teamId && (o.Status == ConciergeOrderStatus.Proposed || o.Status == ConciergeOrderStatus.Fulfilled))
            .ToListAsync();

        var localNow = CompetitionRules.LocalNow;
        var createdProposals = new List<MatchConciergeOrder>();

        foreach (var oppSched in peerSchedules)
        {
            var oppTeam = oppSched.Team;
            int sharedMask = mySchedule.DaysOfWeekMask & oppSched.DaysOfWeekMask;
            if (sharedMask == 0) continue;

            if (!RegionsMatch(mySchedule.Region, oppSched.Region) && !RegionsMatch(mySchedule.Region, oppTeam.ActivityArea))
                continue;

            if (Math.Abs((mySchedule.PreferredKickoffTime - oppSched.PreferredKickoffTime).TotalMinutes) > 120)
                continue;

            int oppStars = starsMap.GetValueOrDefault(oppTeam.Id, 0);
            if (oppStars < mySchedule.MinAloballStars || oppStars > mySchedule.MaxAloballStars)
                continue;
            if (myStars < oppSched.MinAloballStars || myStars > oppSched.MaxAloballStars)
                continue;

            if (oppTeam.AverageRating < mySchedule.MinFriendlyRating || myTeam.AverageRating < oppSched.MinFriendlyRating)
                continue;

            bool oppHardTackle = vibesMap.TryGetValue(oppTeam.Id, out var oppVibe) && oppVibe.IsHighHardTackle;
            if (mySchedule.AvoidHardTackle && oppHardTackle) continue;
            if (oppSched.AvoidHardTackle && myHardTackle) continue;

            var kickoffAt = ResolveNextSharedKickoffLocal(sharedMask, mySchedule.PreferredKickoffTime, localNow);
            if (!kickoffAt.HasValue) continue;

            bool duplicateExists = existingOrders.Any(o =>
                o.MatchedOpponentTeamId == oppTeam.Id && o.KickoffAt.Date == kickoffAt.Value.Date);
            if (duplicateExists) continue;

            var proposal = new MatchConciergeOrder
            {
                Source = ConciergeOrderSource.AutoPairing,
                TeamId = teamId,
                Region = mySchedule.Region,
                KickoffAt = kickoffAt.Value,
                MinStars = mySchedule.MinAloballStars,
                MaxStars = mySchedule.MaxAloballStars,
                MinFriendlyRating = mySchedule.MinFriendlyRating,
                MatchedOpponentTeamId = oppTeam.Id,
                Status = ConciergeOrderStatus.Proposed,
                CreatedByUserId = managerUserId,
                CreatedAt = DateTime.UtcNow
            };

            _db.MatchConciergeOrders.Add(proposal);
            createdProposals.Add(proposal);
            existingOrders.Add(proposal);
        }

        if (createdProposals.Count > 0)
            await _db.SaveChangesAsync();

        var msg = createdProposals.Count > 0
            ? $"Đã quét và ghép được {createdProposals.Count} kèo tự động phù hợp chờ xác nhận!"
            : "Chưa tìm thấy CLB Plus/Pro mới trùng lịch, khu vực & tiêu chuẩn sao/uy tín.";

        return (true, msg, createdProposals);
    }

    public async Task<(bool Success, string Message, MatchConciergeOrder? Order)> ConfirmAutoPairingOrderAsync(
        Guid orderId, string managerUserId)
    {
        var order = await _db.MatchConciergeOrders
            .Include(o => o.Team)
            .Include(o => o.MatchedOpponentTeam)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            return (false, "Không tìm thấy đề xuất ghép cặp.", null);

        if (!await _auth.HasManagerAccessAsync(order.TeamId, managerUserId))
            return (false, "Chỉ Đội trưởng / Quản lý mới được xác nhận kèo ghép cặp.", null);

        if (!order.Team.IsPaidPlanActive)
            return (false, "Gói Plus/Pro của đội đã hết hạn. Vui lòng gia hạn hoặc kích hoạt dùng thử.", null);

        if (order.Status != ConciergeOrderStatus.Proposed || !order.MatchedOpponentTeamId.HasValue)
            return (false, "Đề xuất này đã được xử lý hoặc không còn hợp lệ.", null);

        if (order.KickoffAt <= CompetitionRules.LocalNow)
            return (false, "Thời gian dự kiến của kèo đã qua. Hãy quét ghép cặp lại cho tuần tới.", null);

        var venue = await FindAvailableVenueAsync(order.Region, order.KickoffAt);
        string pitchName = venue?.Venue.Name ?? order.Team.HomePitch ?? order.Region;

        var match = new MatchGame
        {
            Category = MatchCategory.Friendly,
            Status = MatchStatus.Scheduled,
            MatchDateTime = order.KickoffAt,
            PitchName = pitchName,
            PitchType = "7 người",
            TeamAId = order.TeamId,
            TeamBId = order.MatchedOpponentTeamId,
            RsvpToken = Guid.NewGuid().ToString("N")[..8],
            IsPublic = true,
            Notes = $"Kèo xếp cặp tự động ({order.Region})"
        };

        _db.Matches.Add(match);
        order.ResultMatchId = match.Id;
        order.Status = ConciergeOrderStatus.Fulfilled;

        await _db.SaveChangesAsync();
        return (true, $"Đã xác nhận kèo và tạo phòng Matchday với {order.MatchedOpponentTeam?.Name}!", order);
    }

    public async Task<(bool Success, string Message, MatchConciergeOrder? Order, ConciergeOrderCardDto? Summary)>
        CreateAndFulfillConciergeOrderAsync(ConciergeOrderInput input, string managerUserId)
    {
        if (!await _auth.HasManagerAccessAsync(input.TeamId, managerUserId))
            return (false, "Chỉ Đội trưởng / Quản lý mới được đặt Thi đấu trọn gói.", null, null);

        var team = await _db.Teams.FindAsync(input.TeamId);
        if (team == null)
            return (false, "Đội bóng không tồn tại.", null, null);

        if (!team.IsPaidPlanActive)
            return (false, "Tính năng Thi đấu trọn gói A–Z chỉ dành cho CLB Plus/Pro. Hãy kích hoạt dùng thử Plus 14 ngày.", null, null);

        if (team.IsRestrictedFromMatchmaking)
            return (false, $"Đội bóng đang bị hạn chế bắt đối: {team.MatchmakingRestrictionReason}", null, null);

        if (string.IsNullOrWhiteSpace(input.Region))
            return (false, "Vui lòng nhập Khu vực / Quận thi đấu.", null, null);

        if (input.MinStars < 0 || input.MinStars > input.MaxStars)
            return (false, "Khoảng Sao Aloball không hợp lệ (MinStars phải nhỏ hơn hoặc bằng MaxStars).", null, null);

        if (input.MinFriendlyRating is < 1 or > 5)
            return (false, "Độ thân thiện tối thiểu phải từ 1.0 đến 5.0 sao.", null, null);

        var kickoffAt = input.PlayDate.Date.Add(input.KickoffTime);
        if (kickoffAt <= CompetitionRules.LocalNow)
            return (false, "Ngày và giờ bóng lăn phải ở tương lai (theo giờ Việt Nam Asia/Ho_Chi_Minh).", null, null);

        var region = input.Region.Trim();

        // 1. Find best matching opponent club meeting star & friendliness criteria
        var candidates = await _db.Teams
            .Where(t => t.Id != team.Id
                        && t.IsPublic
                        && !t.IsDraft
                        && !t.IsRestrictedFromMatchmaking
                        && t.AverageRating >= input.MinFriendlyRating)
            .ToListAsync();

        var candidateIds = candidates.Select(t => t.Id).ToList();
        var starsMap = await _stars.ComputeTeamStarsMapAsync(candidateIds);
        var vibesMap = await MatchmakingService.LoadTeamVibesMapAsync(_db, candidateIds);

        var busyWindowStart = kickoffAt.AddMinutes(-90);
        var busyWindowEnd = kickoffAt.AddMinutes(90);
        var busyTeamIds = await _db.Matches
            .Where(m => m.Status == MatchStatus.Scheduled && m.MatchDateTime > busyWindowStart && m.MatchDateTime < busyWindowEnd)
            .Select(m => new { m.TeamAId, m.TeamBId })
            .ToListAsync();

        var busySet = new HashSet<Guid>();
        foreach (var bm in busyTeamIds)
        {
            busySet.Add(bm.TeamAId);
            if (bm.TeamBId.HasValue) busySet.Add(bm.TeamBId.Value);
        }

        double targetStars = (input.MinStars + input.MaxStars) / 2.0;
        var bestOpponent = candidates
            .Where(t => !busySet.Contains(t.Id))
            .Where(t =>
            {
                int stars = starsMap.GetValueOrDefault(t.Id, 0);
                if (stars < input.MinStars || stars > input.MaxStars) return false;
                if (input.AvoidHardTackle && vibesMap.TryGetValue(t.Id, out var v) && v.IsHighHardTackle) return false;
                return true;
            })
            .OrderByDescending(t => RegionsMatch(t.ActivityArea, region) ? 1 : 0)
            .ThenBy(t => Math.Abs(starsMap.GetValueOrDefault(t.Id, 0) - targetStars))
            .ThenByDescending(t => t.AverageRating)
            .FirstOrDefault();

        if (bestOpponent == null)
            return (false, "Không tìm thấy đối thủ phù hợp tiêu chuẩn Sao Aloball, độ thân thiện và lịch trống.", null, null);

        // 2. Prioritize an active Flash Deal slot in the same Region & time window (+/- 90 mins) before standard venues
        Guid preMatchId = Guid.NewGuid();
        PitchVenue? venue = null;
        string fieldCode = "Sân 1";
        decimal pitchFee = 600_000m;
        PitchFlashDeal? claimedFlashDeal = null;

        var localNow = CompetitionRules.LocalNow;
        var windowMin = kickoffAt.AddMinutes(-90);
        var windowMax = kickoffAt.AddMinutes(90);
        var candidateFlashDeals = await _db.PitchFlashDeals
            .Include(d => d.Pitch)
            .Where(d => d.Status == PitchFlashDealStatus.Active
                        && d.KickoffAt > localNow
                        && d.KickoffAt >= windowMin
                        && d.KickoffAt <= windowMax)
            .OrderByDescending(d => d.DiscountPercent)
            .ThenBy(d => d.KickoffAt)
            .ToListAsync();

        var matchingFlash = candidateFlashDeals.FirstOrDefault(d =>
            RegionsMatch(d.Region, region)
            || RegionsMatch(d.Pitch?.District, region)
            || RegionsMatch(d.Pitch?.City, region)
            || RegionsMatch(d.Pitch?.FullAddress, region));

        if (matchingFlash != null)
        {
            var (locked, _, lockedDeal) = await PartnerPortalService.TryLockFlashDealAsync(_db, matchingFlash.Id, team.Id, preMatchId);
            if (locked && lockedDeal != null && lockedDeal.Pitch != null)
            {
                claimedFlashDeal = lockedDeal;
                venue = lockedDeal.Pitch;
                fieldCode = string.IsNullOrWhiteSpace(lockedDeal.FieldCode) ? "Suất Flash Deal" : lockedDeal.FieldCode;
                pitchFee = lockedDeal.DealPriceVnd;
                kickoffAt = lockedDeal.KickoffAt;
            }
        }

        if (venue == null)
        {
            var availablePitch = await FindAvailableVenueAsync(region, kickoffAt);
            if (availablePitch == null)
                return (false, $"Khu vực '{region}' hiện chưa có sân trống vào khung giờ {kickoffAt:HH:mm dd/MM/yyyy}.", null, null);

            (venue, fieldCode) = availablePitch.Value;
            pitchFee = venue.IndicativePricePerHour.HasValue && venue.IndicativePricePerHour.Value > 0
                ? MatchSplitBillService.RoundToNearest1000(venue.IndicativePricePerHour.Value)
                : 600_000m;
        }

        // 3. Referee assignment (only if IsProWithReferee == true)
        Referee? assignedReferee = null;
        decimal refereeFee = 0m;
        var startTime = kickoffAt.TimeOfDay;
        var endTime = startTime.Add(DefaultMatchDuration);

        if (input.IsProWithReferee)
        {
            var activeRefs = await _db.Referees.Where(r => r.IsActive && r.IsAvailable).OrderBy(r => r.Name).ToListAsync();
            var busyRefIds = await _db.RefereeAssignments
                .Where(a => a.Proposal.Status == RefereeProposalStatus.Accepted
                            && a.Proposal.Request.Status == RefereeRequestStatus.Confirmed
                            && a.Proposal.Request.ServiceDate.Date == kickoffAt.Date
                            && a.Proposal.Request.EndTime > startTime
                            && a.Proposal.Request.StartTime < endTime)
                .Select(a => a.RefereeId)
                .Distinct()
                .ToListAsync();

            assignedReferee = activeRefs.FirstOrDefault(r => !busyRefIds.Contains(r.Id));
            if (assignedReferee == null)
                return (false, "Hiện không có trọng tài trống lịch vào khung giờ này cho gói Chuyên nghiệp.", null, null);

            refereeFee = assignedReferee.DefaultPriceVND.HasValue && assignedReferee.DefaultPriceVND.Value > 0
                ? assignedReferee.DefaultPriceVND.Value
                : 300_000m;
        }

        // 4. Create Matchday room, confirmed Pitch booking, Referee assignment (if Pro) & Split-Bill pre-fill
        string dealTag = claimedFlashDeal != null ? $" [FLASH DEAL -{claimedFlashDeal.DiscountPercent}%]" : string.Empty;
        var match = new MatchGame
        {
            Id = preMatchId,
            Category = MatchCategory.Friendly,
            Status = MatchStatus.Scheduled,
            MatchDateTime = kickoffAt,
            PitchName = $"{venue.Name} ({fieldCode})",
            PitchType = "7 người",
            TeamAId = team.Id,
            TeamBId = bestOpponent.Id,
            RsvpToken = Guid.NewGuid().ToString("N")[..8],
            IsPublic = true,
            Notes = input.IsProWithReferee
                ? $"Thi đấu trọn gói Chuyên nghiệp{dealTag} (Sân: {venue.Name} · Trọng tài: {assignedReferee!.Name})"
                : $"Thi đấu trọn gói Nghiệp dư{dealTag} (Sân: {venue.Name} · Không thuê trọng tài)"
        };
        _db.Matches.Add(match);

        var pitchReq = new PitchBookingRequest
        {
            TeamId = team.Id,
            PitchVenueId = venue.Id,
            MatchId = match.Id,
            ServiceDate = kickoffAt.Date,
            StartTime = startTime,
            EndTime = endTime,
            PitchFormat = "7 người",
            PitchCount = 1,
            BudgetVND = pitchFee,
            Status = PitchBookingStatus.Confirmed,
            ConfirmedFieldCode = fieldCode,
            Notes = claimedFlashDeal != null
                ? $"⚡ Flash Deal -{claimedFlashDeal.DiscountPercent}% qua AloBall Concierge A–Z"
                : "Giữ sân tự động qua AloBall Concierge A–Z"
        };
        var pitchOffer = new PitchBookingOffer
        {
            RequestId = pitchReq.Id,
            AdminId = managerUserId,
            ConfirmedVenueName = venue.Name,
            ConfirmedAddress = venue.FullAddress,
            ConfirmedDate = kickoffAt.Date,
            ConfirmedStartTime = startTime,
            ConfirmedEndTime = endTime,
            ConfirmedPitchFormat = "7 người",
            ConfirmedPitchCount = 1,
            SpecificFieldCode = fieldCode,
            TotalPriceVND = pitchFee,
            Status = PitchBookingOfferStatus.Accepted
        };
        _db.PitchBookingRequests.Add(pitchReq);
        _db.PitchBookingOffers.Add(pitchOffer);

        if (assignedReferee != null)
        {
            var refReq = new RefereeRequest
            {
                TeamId = team.Id,
                MatchId = match.Id,
                ServiceDate = kickoffAt.Date,
                StartTime = startTime,
                EndTime = endTime,
                VenueName = venue.Name,
                VenueAddress = venue.FullAddress,
                MatchFormat = "7 người",
                RefereeCount = 1,
                BudgetVND = refereeFee,
                Status = RefereeRequestStatus.Confirmed,
                Notes = "Điều phối trọng tài tự động qua AloBall Concierge Pro"
            };
            var refProposal = new RefereeProposal
            {
                RequestId = refReq.Id,
                AdminId = managerUserId,
                TotalPriceVND = refereeFee,
                ServiceNote = $"Phân công trọng tài {assignedReferee.Name}",
                Status = RefereeProposalStatus.Accepted
            };
            var refAssign = new RefereeAssignment
            {
                ProposalId = refProposal.Id,
                RefereeId = assignedReferee.Id
            };
            _db.RefereeRequests.Add(refReq);
            _db.RefereeProposals.Add(refProposal);
            _db.RefereeAssignments.Add(refAssign);
        }

        // Pre-fill MatchExpense (Split-Bill VietQR, 50-50 default)
        var activePlayers = await _db.Players.Where(p => p.TeamId == team.Id && p.IsActive).ToListAsync();
        var expense = MatchSplitBillService.BuildPreFilledExpense(
            match.Id,
            team.Id,
            pitchFee,
            refereeFee,
            "50-50",
            claimedFlashDeal != null
                ? $"Dự toán từ Thi đấu trọn gói A–Z (Ưu đãi Flash Deal -{claimedFlashDeal.DiscountPercent}%)"
                : "Dự toán tự động từ Thi đấu trọn gói A–Z",
            managerUserId,
            activePlayers);
        _db.MatchExpenses.Add(expense);

        var order = new MatchConciergeOrder
        {
            Source = ConciergeOrderSource.Concierge,
            TeamId = team.Id,
            Region = region,
            KickoffAt = kickoffAt,
            MinStars = input.MinStars,
            MaxStars = input.MaxStars,
            MinFriendlyRating = input.MinFriendlyRating,
            IsProWithReferee = input.IsProWithReferee,
            MatchedOpponentTeamId = bestOpponent.Id,
            BookedPitchSlotId = pitchReq.Id,
            AssignedRefereeId = assignedReferee?.Id,
            ResultMatchId = match.Id,
            EstimatedTotalFee = pitchFee + refereeFee,
            Status = ConciergeOrderStatus.Fulfilled,
            CreatedByUserId = managerUserId,
            CreatedAt = DateTime.UtcNow
        };
        _db.MatchConciergeOrders.Add(order);

        await _db.SaveChangesAsync();

        var summary = new ConciergeOrderCardDto
        {
            Id = order.Id,
            Source = order.Source,
            TeamId = team.Id,
            TeamName = team.Name,
            Region = region,
            KickoffAt = kickoffAt,
            MinStars = input.MinStars,
            MaxStars = input.MaxStars,
            MinFriendlyRating = input.MinFriendlyRating,
            IsProWithReferee = input.IsProWithReferee,
            Status = order.Status,
            EstimatedTotalFee = order.EstimatedTotalFee,
            CreatedAt = order.CreatedAt,
            MatchedOpponentTeamId = bestOpponent.Id,
            MatchedOpponentName = bestOpponent.Name,
            MatchedOpponentSlug = bestOpponent.Slug,
            MatchedOpponentStars = starsMap.GetValueOrDefault(bestOpponent.Id, 0),
            MatchedOpponentRating = bestOpponent.AverageRating,
            MatchedOpponentHardTacklePct = vibesMap.TryGetValue(bestOpponent.Id, out var ov) ? ov.HardTacklePercentage : 0,
            BookedPitchSlotId = pitchReq.Id,
            BookedPitchName = venue.Name,
            BookedPitchAddress = venue.FullAddress,
            BookedFieldCode = fieldCode,
            PitchFeeVnd = pitchFee,
            AssignedRefereeId = assignedReferee?.Id,
            AssignedRefereeName = assignedReferee?.Name,
            RefereeFeeVnd = refereeFee,
            ResultMatchId = match.Id,
            ResultMatchRsvpToken = match.RsvpToken
        };
        summary.ZaloSummaryMessage = BuildZaloConciergeSummary(summary, $"/Matches/Detail/{match.Id}?token={match.RsvpToken}");

        return (true, $"Đã hoàn tất kèo trọn gói vs {bestOpponent.Name} tại {venue.Name}!", order, summary);
    }

    public async Task<(bool Success, string Message)> CancelOrderAsync(Guid orderId, string managerUserId)
    {
        var order = await _db.MatchConciergeOrders.FindAsync(orderId);
        if (order == null) return (false, "Không tìm thấy yêu cầu.");

        if (!await _auth.HasManagerAccessAsync(order.TeamId, managerUserId))
            return (false, "Chỉ Đội trưởng / Quản lý mới được hủy.");

        if (order.Status == ConciergeOrderStatus.Fulfilled)
            return (false, "Kèo đã tạo phòng Matchday thành công, vui lòng quản lý tại Matchday Center.");

        order.Status = ConciergeOrderStatus.Cancelled;
        await _db.SaveChangesAsync();
        return (true, "Đã hủy đề xuất kèo.");
    }

    public async Task<ConciergeWorkspaceViewModel> BuildWorkspaceAsync(
        string? userId, Guid? teamId, string? activeTab, string baseAppUrl)
    {
        var vm = new ConciergeWorkspaceViewModel
        {
            ActiveTab = string.Equals(activeTab, "autopair", StringComparison.OrdinalIgnoreCase) ? "autopair" : "concierge",
            IsAuthenticated = !string.IsNullOrWhiteSpace(userId),
            SuggestedRegions = await _db.PitchVenues
                .Where(v => v.IsActive)
                .Select(v => v.District)
                .Distinct()
                .OrderBy(d => d)
                .ToListAsync()
        };

        if (string.IsNullOrWhiteSpace(userId))
        {
            vm.OrderInput = new ConciergeOrderInput
            {
                PlayDate = CompetitionRules.LocalNow.Date.AddDays(2),
                Region = vm.SuggestedRegions.FirstOrDefault() ?? "Cầu Giấy"
            };
            return vm;
        }

        var managedTeamIds = await _db.TeamMembers
            .Where(m => m.UserId == userId && (m.Role == TeamRole.Owner || m.Role == TeamRole.Manager))
            .Select(m => m.TeamId)
            .ToListAsync();

        vm.ManagedTeams = await _db.Teams
            .Where(t => managedTeamIds.Contains(t.Id))
            .OrderBy(t => t.Name)
            .ToListAsync();

        var selectedTeam = (teamId.HasValue ? vm.ManagedTeams.FirstOrDefault(t => t.Id == teamId.Value) : null)
                           ?? vm.ManagedTeams.FirstOrDefault();

        if (selectedTeam == null)
        {
            vm.OrderInput = new ConciergeOrderInput
            {
                PlayDate = CompetitionRules.LocalNow.Date.AddDays(2),
                Region = vm.SuggestedRegions.FirstOrDefault() ?? "Cầu Giấy"
            };
            return vm;
        }

        vm.SelectedTeam = selectedTeam;
        vm.SelectedTeamId = selectedTeam.Id;
        vm.SelectedTeamStars = await GetTeamAloballStarsAsync(selectedTeam.Id);

        var myVibes = await MatchmakingService.LoadTeamVibesMapAsync(_db, [selectedTeam.Id]);
        vm.SelectedTeamHardTacklePct = myVibes.TryGetValue(selectedTeam.Id, out var mv) ? mv.HardTacklePercentage : 0;

        var sched = await _db.TeamRecurringSchedules.FirstOrDefaultAsync(s => s.TeamId == selectedTeam.Id);
        vm.RecurringSchedule = sched;

        vm.OrderInput = new ConciergeOrderInput
        {
            TeamId = selectedTeam.Id,
            PlayDate = CompetitionRules.LocalNow.Date.AddDays(2),
            KickoffTime = sched?.PreferredKickoffTime ?? new TimeSpan(19, 0, 0),
            Region = sched?.Region ?? selectedTeam.ActivityArea ?? vm.SuggestedRegions.FirstOrDefault() ?? "Cầu Giấy",
            MinStars = sched?.MinAloballStars ?? 0,
            MaxStars = sched?.MaxAloballStars ?? 30,
            MinFriendlyRating = sched?.MinFriendlyRating ?? 4.0,
            IsProWithReferee = selectedTeam.PlanTier == PlanTier.Pro,
            AvoidHardTackle = sched?.AvoidHardTackle ?? true
        };

        vm.ScheduleInput = new RecurringScheduleInput
        {
            TeamId = selectedTeam.Id,
            IsAutoPairingEnabled = sched?.IsAutoPairingEnabled ?? true,
            DaysOfWeekMask = sched?.DaysOfWeekMask ?? ((1 << (int)DayOfWeek.Tuesday) | (1 << (int)DayOfWeek.Thursday) | (1 << (int)DayOfWeek.Saturday)),
            PreferredKickoffTime = sched?.PreferredKickoffTime ?? new TimeSpan(19, 0, 0),
            Region = sched?.Region ?? selectedTeam.ActivityArea ?? vm.SuggestedRegions.FirstOrDefault() ?? "Cầu Giấy",
            MinAloballStars = sched?.MinAloballStars ?? 0,
            MaxAloballStars = sched?.MaxAloballStars ?? 30,
            MinFriendlyRating = sched?.MinFriendlyRating ?? 4.0,
            AvoidHardTackle = sched?.AvoidHardTackle ?? true
        };

        var orders = await _db.MatchConciergeOrders
            .Include(o => o.MatchedOpponentTeam)
            .Where(o => o.TeamId == selectedTeam.Id && o.Status != ConciergeOrderStatus.Cancelled)
            .OrderByDescending(o => o.CreatedAt)
            .Take(20)
            .ToListAsync();

        var oppIds = orders.Where(o => o.MatchedOpponentTeamId.HasValue).Select(o => o.MatchedOpponentTeamId!.Value).Distinct().ToList();
        var oppStarsMap = await _stars.ComputeTeamStarsMapAsync(oppIds);
        var oppVibesMap = await MatchmakingService.LoadTeamVibesMapAsync(_db, oppIds);

        var pitchSlotIds = orders.Where(o => o.BookedPitchSlotId.HasValue).Select(o => o.BookedPitchSlotId!.Value).ToList();
        var pitchMap = pitchSlotIds.Count > 0
            ? await _db.PitchBookingRequests
                .Include(r => r.PitchVenue)
                .Include(r => r.Offers)
                .Where(r => pitchSlotIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id)
            : new Dictionary<Guid, PitchBookingRequest>();

        var refIds = orders.Where(o => o.AssignedRefereeId.HasValue).Select(o => o.AssignedRefereeId!.Value).Distinct().ToList();
        var refMap = refIds.Count > 0
            ? await _db.Referees.Where(r => refIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id)
            : new Dictionary<Guid, Referee>();

        var matchIds = orders.Where(o => o.ResultMatchId.HasValue).Select(o => o.ResultMatchId!.Value).Distinct().ToList();
        var matchMap = matchIds.Count > 0
            ? await _db.Matches.Where(m => matchIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id)
            : new Dictionary<Guid, MatchGame>();

        foreach (var o in orders)
        {
            pitchMap.TryGetValue(o.BookedPitchSlotId ?? Guid.Empty, out var pitchReq);
            var pitchOffer = pitchReq?.Offers.FirstOrDefault(x => x.Status == PitchBookingOfferStatus.Accepted);
            refMap.TryGetValue(o.AssignedRefereeId ?? Guid.Empty, out var refEntity);
            matchMap.TryGetValue(o.ResultMatchId ?? Guid.Empty, out var matchEntity);

            decimal pitchFee = pitchOffer?.TotalPriceVND ?? 0m;
            decimal refFee = refEntity != null ? Math.Max(0m, o.EstimatedTotalFee - pitchFee) : 0m;

            var card = new ConciergeOrderCardDto
            {
                Id = o.Id,
                Source = o.Source,
                TeamId = o.TeamId,
                TeamName = selectedTeam.Name,
                Region = o.Region,
                KickoffAt = o.KickoffAt,
                MinStars = o.MinStars,
                MaxStars = o.MaxStars,
                MinFriendlyRating = o.MinFriendlyRating,
                IsProWithReferee = o.IsProWithReferee,
                Status = o.Status,
                EstimatedTotalFee = o.EstimatedTotalFee,
                FailureReason = o.FailureReason,
                CreatedAt = o.CreatedAt,
                MatchedOpponentTeamId = o.MatchedOpponentTeamId,
                MatchedOpponentName = o.MatchedOpponentTeam?.Name,
                MatchedOpponentSlug = o.MatchedOpponentTeam?.Slug,
                MatchedOpponentStars = o.MatchedOpponentTeamId.HasValue ? oppStarsMap.GetValueOrDefault(o.MatchedOpponentTeamId.Value, 0) : 0,
                MatchedOpponentRating = o.MatchedOpponentTeam?.AverageRating ?? 5.0,
                MatchedOpponentHardTacklePct = o.MatchedOpponentTeamId.HasValue && oppVibesMap.TryGetValue(o.MatchedOpponentTeamId.Value, out var ov) ? ov.HardTacklePercentage : 0,
                BookedPitchSlotId = o.BookedPitchSlotId,
                BookedPitchName = pitchOffer?.ConfirmedVenueName ?? pitchReq?.PitchVenue?.Name ?? matchEntity?.PitchName,
                BookedPitchAddress = pitchOffer?.ConfirmedAddress ?? pitchReq?.PitchVenue?.FullAddress,
                BookedFieldCode = pitchOffer?.SpecificFieldCode ?? pitchReq?.ConfirmedFieldCode,
                PitchFeeVnd = pitchFee,
                AssignedRefereeId = o.AssignedRefereeId,
                AssignedRefereeName = refEntity?.Name,
                RefereeFeeVnd = refFee,
                ResultMatchId = o.ResultMatchId,
                ResultMatchRsvpToken = matchEntity?.RsvpToken
            };

            string link = matchEntity != null
                ? $"{baseAppUrl}/Matches/Detail/{matchEntity.Id}?token={matchEntity.RsvpToken}"
                : $"{baseAppUrl}/Concierge?teamId={selectedTeam.Id}";
            card.ZaloSummaryMessage = BuildZaloConciergeSummary(card, link);

            if (o.Source == ConciergeOrderSource.Concierge)
                vm.ConciergeOrders.Add(card);
            else
                vm.AutoPairingOrders.Add(card);
        }

        return vm;
    }

    public static string BuildZaloConciergeSummary(ConciergeOrderCardDto card, string matchUrl)
    {
        var header = card.Source == ConciergeOrderSource.AutoPairing
            ? "🤖 [ALOBALL] KÈO GHÉP CẶP TỰ ĐỘNG"
            : "🏆 [ALOBALL] KÈO THI ĐẤU TRỌN GÓI A–Z";

        var modeLine = card.IsProWithReferee
            ? $"⚖️ Chế độ: Chuyên nghiệp · Trọng tài: {card.AssignedRefereeName ?? "Đã phân công"}"
            : "🤝 Chế độ: Nghiệp dư (Không trọng tài)";

        var pitchLine = !string.IsNullOrWhiteSpace(card.BookedPitchName)
            ? $"📍 Sân: {card.BookedPitchName}{(!string.IsNullOrEmpty(card.BookedFieldCode) ? $" ({card.BookedFieldCode})" : "")} — {card.BookedPitchAddress ?? card.Region}"
            : $"📍 Khu vực: {card.Region}";

        var oppLine = !string.IsNullOrWhiteSpace(card.MatchedOpponentName)
            ? $"⚔️ Trận đấu: {card.TeamName} vs {card.MatchedOpponentName} ({card.MatchedOpponentStars}⭐ · {card.MatchedOpponentRating:0.0}★)"
            : $"⚔️ Đội nhà: {card.TeamName}";

        var feeLine = card.EstimatedTotalFee > 0
            ? $"\n💰 Tổng chi phí dự kiến: {card.EstimatedTotalFee:N0}đ (Chia 50-50 trên Matchday)"
            : string.Empty;

        return $"{header}\n{oppLine}\n🗓 Bóng lăn: {card.KickoffAt:HH:mm} | {card.KickoffAt:dd/MM/yyyy} (Giờ VN)\n{pitchLine}\n{modeLine}{feeLine}\n👉 Điểm danh & Chia tiền VietQR: {matchUrl}";
    }

    private async Task<(PitchVenue Venue, string FieldCode)?> FindAvailableVenueAsync(string region, DateTime kickoffAt)
    {
        var activeVenues = await _db.PitchVenues
            .Where(v => v.IsActive)
            .OrderBy(v => v.Name)
            .ToListAsync();

        var regionVenues = activeVenues
            .Where(v => RegionsMatch(v.District, region) || RegionsMatch(v.City, region) || RegionsMatch(v.FullAddress, region) || RegionsMatch(v.Name, region))
            .ToList();

        if (regionVenues.Count == 0) return null;

        var startTime = kickoffAt.TimeOfDay;
        var endTime = startTime.Add(DefaultMatchDuration);
        var venueIds = regionVenues.Select(v => v.Id).ToList();

        var overlappingBookings = await _db.PitchBookingRequests
            .Where(r => venueIds.Contains(r.PitchVenueId)
                        && r.Status == PitchBookingStatus.Confirmed
                        && r.ServiceDate.Date == kickoffAt.Date
                        && r.EndTime > startTime
                        && r.StartTime < endTime)
            .ToListAsync();

        foreach (var v in regionVenues)
        {
            int capacity = v.TotalPitches > 0 ? v.TotalPitches : 4;
            int used = overlappingBookings.Where(r => r.PitchVenueId == v.Id).Sum(r => r.PitchCount);
            if (used + 1 <= capacity)
            {
                return (v, $"Sân {used + 1}");
            }
        }

        return null;
    }

    private static DateTime? ResolveNextSharedKickoffLocal(int sharedMask, TimeSpan preferredTime, DateTime localNow)
    {
        for (int offset = 0; offset <= 7; offset++)
        {
            var candidateDate = localNow.Date.AddDays(offset);
            int bit = 1 << (int)candidateDate.DayOfWeek;
            if ((sharedMask & bit) == 0) continue;

            var dt = candidateDate.Add(preferredTime);
            if (dt > localNow) return dt;
        }
        return null;
    }
}
