using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Services;

public interface ILoanMarketService
{
    Task<LoanMarketIndexViewModel> BuildMarketplaceAsync(
        string? activeTab,
        string? region,
        DateTime? playDate,
        string? position,
        string? feeFilter,
        string? currentUserId,
        string baseMarketUrl);

    Task<(bool Success, string Message, PlayerLoanListing? Listing)> CreateListingAsync(
        CreateLoanListingInput input,
        string actorUserId);

    Task<(bool Success, string Message, PlayerLoanApplication? Application)> ApplyToListingAsync(
        ApplyLoanInput input,
        string actorUserId);

    Task<(bool Success, string Message, PlayerLoanListing? Listing)> AcceptApplicationAsync(
        Guid listingId,
        Guid applicationId,
        string actorUserId);

    Task<(bool Success, string Message, PlayerLoanListing? Listing)> DirectRecruitAvailablePlayerAsync(
        Guid listingId,
        Guid recruitingTeamId,
        Guid? matchId,
        string? position,
        string captainUserId);

    Task<(bool Success, string Message)> CompleteLoanListingAsync(
        Guid listingId,
        string actorUserId);

    Task CompleteLoansForMatchAsync(Guid matchId);

    Task<(bool Success, string Message)> CancelListingAsync(
        Guid listingId,
        string actorUserId);

    Task<int> GetUserAvailableAbCoinAsync(string userId);
}

public class LoanMarketService : ILoanMarketService
{
    public const decimal MaxLoanFeeVnd = 1_000_000m;
    public static readonly string[] ValidPositions = ["GK", "DEF", "MID", "FWD"];

    private readonly ApplicationDbContext _db;
    private readonly ITeamAuthorizationService _auth;

    public LoanMarketService(ApplicationDbContext db, ITeamAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public static (bool IsValid, string? Error, decimal NormalizedFee) ValidateLoanFee(LoanFeeType feeType, decimal loanFeeAmount)
    {
        if (loanFeeAmount < 0m || loanFeeAmount > MaxLoanFeeVnd)
        {
            return (false, "Phí mượn cầu thủ phải từ 0đ đến tối đa 1.000.000đ.", 0m);
        }

        if (feeType == LoanFeeType.SplitPitch)
        {
            if (loanFeeAmount > 0m)
            {
                return (false, "Hình thức Share tiền sân (0đ) không được thu thêm phí mượn. Hãy chọn 'Có phí tăng cường' nếu muốn đặt phí.", 0m);
            }
            return (true, null, 0m);
        }

        if (loanFeeAmount <= 0m)
        {
            return (false, "Vui lòng nhập mức phí tăng cường hợp lệ (từ 1.000đ đến 1.000.000đ) hoặc chọn Share tiền sân (0đ).", 0m);
        }

        return (true, null, Math.Round(loanFeeAmount, 0, MidpointRounding.AwayFromZero));
    }

    public static string NormalizePositions(IEnumerable<string>? positions, string? rawPositions)
    {
        var tokens = new List<string>();
        if (positions != null)
        {
            tokens.AddRange(positions);
        }
        if (!string.IsNullOrWhiteSpace(rawPositions))
        {
            tokens.AddRange(rawPositions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        var normalized = tokens
            .Select(MapToStandardPositionCode)
            .Where(p => ValidPositions.Contains(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalized.Count > 0 ? string.Join(",", normalized) : "MID";
    }

    public static string MapToStandardPositionCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "MID";
        var u = raw.Trim().ToUpperInvariant();
        if (u.Contains("GK") || u.Contains("THỦ MÔN")) return "GK";
        if (u.Contains("DEF") || u.Contains("CB") || u.Contains("LB") || u.Contains("RB") || u.Contains("HẬU VỆ") || u.Contains("DF")) return "DEF";
        if (u.Contains("FWD") || u.Contains("ST") || u.Contains("CF") || u.Contains("LW") || u.Contains("RW") || u.Contains("TIỀN ĐẠO") || u.Contains("FW")) return "FWD";
        return "MID";
    }

    public async Task<int> GetUserAvailableAbCoinAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return 0;

        var playerIds = await _db.Players
            .Where(p => p.LinkedUserId == userId)
            .Select(p => p.Id)
            .ToListAsync();

        if (playerIds.Count == 0) return 0;

        var balance = await _db.PlayerAbJournalEntries
            .Where(e => playerIds.Contains(e.PlayerId))
            .SumAsync(e => (int?)e.Points) ?? 0;

        return Math.Max(0, balance);
    }

    public async Task<(bool Success, string Message, PlayerLoanListing? Listing)> CreateListingAsync(
        CreateLoanListingInput input,
        string actorUserId)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
            return (false, "Vui lòng đăng nhập để đăng tin.", null);

        var feeCheck = ValidateLoanFee(input.FeeType, input.LoanFeeAmount);
        if (!feeCheck.IsValid)
            return (false, feeCheck.Error!, null);

        if (input.EscrowAbCoin < 0 || input.EscrowAbCoin > 500)
            return (false, "Mức cọc AB Coin phải từ 0 đến 500 AB Coin.", null);

        var region = input.Region?.Trim();
        if (string.IsNullOrWhiteSpace(region))
            return (false, "Vui lòng nhập Khu vực / Quận thi đấu.", null);

        var timeSlot = string.IsNullOrWhiteSpace(input.TimeSlot) ? "19:00 - 20:30" : input.TimeSlot.Trim();
        var positions = NormalizePositions(input.Positions, input.PositionsNeeded);
        var playDate = input.PlayDate == default ? CompetitionRules.LocalNow.Date.AddDays(1) : input.PlayDate.Date;

        Guid? teamId = input.TeamId;
        Guid? matchId = input.MatchId;
        Guid? playerId = null;
        string? pitchName = string.IsNullOrWhiteSpace(input.PitchName) ? null : input.PitchName.Trim();

        if (input.ListingType == LoanListingType.TeamRecruiting)
        {
            if (!teamId.HasValue)
                return (false, "Vui lòng chọn đội bóng cần tuyển người.", null);

            if (!await _auth.HasManagerAccessAsync(teamId.Value, actorUserId))
                return (false, "Chỉ Đội trưởng / Quản lý mới được đăng tin tuyển cầu thủ cho đội.", null);

            var team = await _db.Teams.FindAsync(teamId.Value);
            if (team == null)
                return (false, "Đội bóng không tồn tại.", null);

            if (matchId.HasValue)
            {
                var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == matchId.Value);
                if (match == null || (match.TeamAId != teamId.Value && match.TeamBId != teamId.Value))
                    return (false, "Trận đấu liên kết không thuộc đội bóng này.", null);

                playDate = match.MatchDateTime.Date;
                if (string.IsNullOrWhiteSpace(pitchName))
                    pitchName = match.PitchName;
            }
        }
        else
        {
            // PlayerAvailable: Cầu thủ đăng tin rảnh lịch
            var primaryPlayer = await _db.Players
                .Where(p => p.LinkedUserId == actorUserId && p.IsActive)
                .OrderByDescending(p => p.JoinedDate)
                .FirstOrDefaultAsync();

            playerId = primaryPlayer?.Id;
            if (!teamId.HasValue && primaryPlayer != null)
            {
                teamId = primaryPlayer.TeamId;
            }

            if (input.EscrowAbCoin > 0)
            {
                int availableAb = await GetUserAvailableAbCoinAsync(actorUserId);
                if (availableAb < input.EscrowAbCoin)
                {
                    return (false, $"Số dư AB Coin của bạn ({availableAb} AB) không đủ để đặt cọc {input.EscrowAbCoin} AB Coin.", null);
                }
            }
        }

        var listing = new PlayerLoanListing
        {
            ListingType = input.ListingType,
            UserId = actorUserId,
            PlayerId = playerId,
            TeamId = teamId,
            MatchId = matchId,
            Region = region,
            PitchName = pitchName,
            PlayDate = playDate,
            TimeSlot = timeSlot,
            PositionsNeeded = positions,
            FeeType = input.FeeType,
            LoanFeeAmount = feeCheck.NormalizedFee,
            EscrowAbCoin = input.EscrowAbCoin,
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
            Status = LoanListingStatus.Open,
            CreatedAt = DateTime.UtcNow
        };

        _db.PlayerLoanListings.Add(listing);
        await _db.SaveChangesAsync();

        var label = input.ListingType == LoanListingType.TeamRecruiting
            ? "Đã đăng tin tìm cầu thủ tăng cường thành công!"
            : "Đã đăng lịch rảnh sẵn sàng đá thuê/đá ké thành công!";

        return (true, label, listing);
    }

    public async Task<(bool Success, string Message, PlayerLoanApplication? Application)> ApplyToListingAsync(
        ApplyLoanInput input,
        string actorUserId)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
            return (false, "Vui lòng đăng nhập để ứng tuyển.", null);

        var listing = await _db.PlayerLoanListings
            .Include(l => l.Applications)
            .FirstOrDefaultAsync(l => l.Id == input.ListingId);

        if (listing == null)
            return (false, "Tin mượn cầu thủ không tồn tại.", null);

        if (listing.Status != LoanListingStatus.Open)
            return (false, "Tin này đã chốt kèo hoặc đã đóng.", null);

        if (listing.Applications.Any(a => a.ApplicantUserId == actorUserId && a.Status == LoanApplicationStatus.Pending))
            return (false, "Bạn đã gửi yêu cầu cho kèo này rồi.", null);

        var pos = MapToStandardPositionCode(input.Position);
        Guid? applicantPlayerId = null;
        Guid? applicantTeamId = input.ApplicantTeamId;
        Guid? targetMatchId = input.TargetMatchId;

        if (listing.ListingType == LoanListingType.TeamRecruiting)
        {
            if (listing.EscrowAbCoin > 0)
            {
                int availableAb = await GetUserAvailableAbCoinAsync(actorUserId);
                if (availableAb < listing.EscrowAbCoin)
                {
                    return (false, $"Kèo này yêu cầu đặt cọc {listing.EscrowAbCoin} AB Coin chống bùng. Số dư hiện tại của bạn là {availableAb} AB Coin.", null);
                }
            }

            var linkedPlayer = await _db.Players
                .Where(p => p.LinkedUserId == actorUserId && p.IsActive)
                .OrderByDescending(p => p.JoinedDate)
                .FirstOrDefaultAsync();

            applicantPlayerId = linkedPlayer?.Id;
            applicantTeamId ??= linkedPlayer?.TeamId;
        }
        else
        {
            // Đội trưởng gửi lời mời cho cầu thủ đang rảnh (PlayerAvailable)
            if (listing.UserId == actorUserId)
                return (false, "Bạn không thể tự mời chính mình.", null);

            if (!applicantTeamId.HasValue)
                return (false, "Vui lòng chọn đội bóng của bạn để mời cầu thủ.", null);

            if (!await _auth.HasManagerAccessAsync(applicantTeamId.Value, actorUserId))
                return (false, "Chỉ Đội trưởng / Quản lý mới được mời cầu thủ về đội.", null);
        }

        var app = new PlayerLoanApplication
        {
            ListingId = listing.Id,
            ApplicantUserId = actorUserId,
            ApplicantPlayerId = applicantPlayerId,
            ApplicantTeamId = applicantTeamId,
            TargetMatchId = targetMatchId,
            Position = pos,
            Message = string.IsNullOrWhiteSpace(input.Message) ? null : input.Message.Trim(),
            Status = LoanApplicationStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _db.PlayerLoanApplications.Add(app);
        await _db.SaveChangesAsync();

        return (true, "Đã gửi đăng ký tham gia kèo thành công!", app);
    }

    public async Task<(bool Success, string Message, PlayerLoanListing? Listing)> AcceptApplicationAsync(
        Guid listingId,
        Guid applicationId,
        string actorUserId)
    {
        var listing = await _db.PlayerLoanListings
            .Include(l => l.Applications)
            .FirstOrDefaultAsync(l => l.Id == listingId);

        if (listing == null)
            return (false, "Tin mượn cầu thủ không tồn tại.", null);

        if (listing.Status != LoanListingStatus.Open)
            return (false, "Kèo này đã được chốt hoặc đã hủy trước đó.", null);

        var app = listing.Applications.FirstOrDefault(a => a.Id == applicationId);
        if (app == null || app.Status != LoanApplicationStatus.Pending)
            return (false, "Đơn đăng ký không hợp lệ hoặc đã xử lý.", null);

        string loanedUserId;
        Guid recruitingTeamId;
        Guid? targetMatchId;

        if (listing.ListingType == LoanListingType.TeamRecruiting)
        {
            if (!listing.TeamId.HasValue || !await _auth.HasManagerAccessAsync(listing.TeamId.Value, actorUserId))
                return (false, "Chỉ Đội trưởng / Quản lý của đội tuyển quân mới được duyệt cầu thủ.", null);

            loanedUserId = app.ApplicantUserId;
            recruitingTeamId = listing.TeamId.Value;
            targetMatchId = listing.MatchId ?? app.TargetMatchId;
        }
        else
        {
            // PlayerAvailable: Cầu thủ duyệt lời mời từ Đội trưởng, hoặc Đội trưởng chốt
            if (!app.ApplicantTeamId.HasValue)
                return (false, "Lời mời chưa gắn với đội bóng cần mượn quân.", null);

            bool isListingOwner = listing.UserId == actorUserId;
            bool isRecruitingManager = await _auth.HasManagerAccessAsync(app.ApplicantTeamId.Value, actorUserId);
            if (!isListingOwner && !isRecruitingManager)
                return (false, "Bạn không có quyền chốt kèo này.", null);

            if (string.IsNullOrWhiteSpace(listing.UserId))
                return (false, "Không xác định được hồ sơ cầu thủ đăng tin.", null);

            loanedUserId = listing.UserId;
            recruitingTeamId = app.ApplicantTeamId.Value;
            targetMatchId = app.TargetMatchId ?? listing.MatchId;
        }

        return await LockDealInternalAsync(listing, app, loanedUserId, recruitingTeamId, targetMatchId, app.Position);
    }

    public async Task<(bool Success, string Message, PlayerLoanListing? Listing)> DirectRecruitAvailablePlayerAsync(
        Guid listingId,
        Guid recruitingTeamId,
        Guid? matchId,
        string? position,
        string captainUserId)
    {
        var listing = await _db.PlayerLoanListings
            .Include(l => l.Applications)
            .FirstOrDefaultAsync(l => l.Id == listingId);

        if (listing == null)
            return (false, "Tin cầu thủ không tồn tại.", null);

        if (listing.ListingType != LoanListingType.PlayerAvailable)
            return (false, "Thao tác chốt nhanh này chỉ áp dụng cho tin Cầu thủ sẵn sàng đá.", null);

        if (listing.Status != LoanListingStatus.Open)
            return (false, "Cầu thủ này đã chốt kèo khác hoặc đã đóng tin.", null);

        if (!await _auth.HasManagerAccessAsync(recruitingTeamId, captainUserId))
            return (false, "Chỉ Đội trưởng / Quản lý mới được chốt mượn cầu thủ về đội.", null);

        if (string.IsNullOrWhiteSpace(listing.UserId))
            return (false, "Không tìm thấy tài khoản cầu thủ.", null);

        if (listing.UserId == captainUserId)
            return (false, "Bạn không thể tự mượn chính mình.", null);

        var chosenPos = string.IsNullOrWhiteSpace(position)
            ? listing.PositionsNeeded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "MID"
            : MapToStandardPositionCode(position);

        var app = new PlayerLoanApplication
        {
            ListingId = listing.Id,
            ApplicantUserId = captainUserId,
            ApplicantTeamId = recruitingTeamId,
            TargetMatchId = matchId,
            Position = chosenPos,
            Message = "Đội trưởng chốt mượn nhanh 1 chạm",
            Status = LoanApplicationStatus.Accepted,
            CreatedAt = DateTime.UtcNow,
            RespondedAt = DateTime.UtcNow
        };

        _db.PlayerLoanApplications.Add(app);
        listing.Applications.Add(app);

        return await LockDealInternalAsync(listing, app, listing.UserId, recruitingTeamId, matchId ?? listing.MatchId, chosenPos);
    }

    private async Task<(bool Success, string Message, PlayerLoanListing? Listing)> LockDealInternalAsync(
        PlayerLoanListing listing,
        PlayerLoanApplication acceptedApp,
        string loanedUserId,
        Guid recruitingTeamId,
        Guid? matchId,
        string chosenPosition)
    {
        var loanedUser = await _db.Users.FindAsync(loanedUserId);
        if (loanedUser == null)
            return (false, "Không tìm thấy thông tin người dùng của cầu thủ.", null);

        var recruitingTeam = await _db.Teams.FindAsync(recruitingTeamId);
        if (recruitingTeam == null)
            return (false, "Không tìm thấy đội bóng mượn quân.", null);

        var sourcePlayer = await _db.Players
            .Where(p => p.LinkedUserId == loanedUserId && p.IsActive)
            .OrderByDescending(p => p.JoinedDate)
            .FirstOrDefaultAsync();

        // 1. Hold Escrow AB Coin if configured
        if (listing.EscrowAbCoin > 0 && !listing.EscrowJournalEntryId.HasValue)
        {
            var playerProfiles = await _db.Players
                .Where(p => p.LinkedUserId == loanedUserId)
                .ToListAsync();

            var playerIds = playerProfiles.Select(p => p.Id).ToList();
            int currentAb = playerIds.Count > 0
                ? await _db.PlayerAbJournalEntries.Where(e => playerIds.Contains(e.PlayerId)).SumAsync(e => (int?)e.Points) ?? 0
                : 0;

            if (currentAb < listing.EscrowAbCoin)
            {
                return (false, $"Cầu thủ không đủ số dư AB Coin ({currentAb} AB) để khóa cọc {listing.EscrowAbCoin} AB Coin.", null);
            }

            // Pick a player record to attach the journal entry (or create guest player first)
            var escrowPlayer = sourcePlayer ?? playerProfiles.First();
            var holdEntry = new PlayerAbJournalEntry
            {
                TeamId = escrowPlayer.TeamId,
                PlayerId = escrowPlayer.Id,
                StatisticalPeriod = listing.PlayDate.ToString("yyyy-MM"),
                SourceDomain = AbSourceDomain.LoanEscrow,
                SourceId = listing.Id,
                SourceRole = "LoanEscrowHold",
                SourceRevision = 1,
                OperationKey = $"{escrowPlayer.TeamId}_LoanEscrowHold_{listing.Id}_{escrowPlayer.Id}",
                Points = -listing.EscrowAbCoin,
                RegulationVersion = 1,
                AdjustmentReason = $"Tạm khóa cọc {listing.EscrowAbCoin} AB Coin cho kèo mượn cầu thủ ({recruitingTeam.Name})",
                EventDate = listing.PlayDate
            };
            _db.PlayerAbJournalEntries.Add(holdEntry);
            listing.EscrowJournalEntryId = holdEntry.Id;
            listing.IsEscrowReleased = false;
        }

        // 2. Ensure Guest Player exists in recruiting team
        var guestPlayer = await _db.Players
            .FirstOrDefaultAsync(p => p.TeamId == recruitingTeamId && p.LinkedUserId == loanedUserId);

        if (guestPlayer == null)
        {
            guestPlayer = new Player
            {
                TeamId = recruitingTeamId,
                LinkedUserId = loanedUserId,
                FullName = !string.IsNullOrWhiteSpace(loanedUser.FullName) ? loanedUser.FullName : (sourcePlayer?.FullName ?? "Cầu thủ tăng cường"),
                Position = chosenPosition,
                StandardPrimaryPosition = chosenPosition,
                PreferredFoot = sourcePlayer?.PreferredFoot ?? loanedUser.PreferredFoot ?? "Phải",
                Experience = sourcePlayer?.Experience ?? loanedUser.Experience ?? "Phong trào",
                Region = sourcePlayer?.Region ?? loanedUser.Region ?? listing.Region,
                PlayStyles = sourcePlayer?.PlayStyles ?? loanedUser.PlayStyles,
                PhotoUrl = sourcePlayer?.PhotoUrl ?? loanedUser.AvatarUrl,
                IsActive = true,
                IsClaimed = true,
                InternalNotes = $"Cầu thủ mượn qua Chợ Cầu Thủ ({listing.PlayDate:dd/MM/yyyy})"
            };
            _db.Players.Add(guestPlayer);
        }
        else if (!guestPlayer.IsActive)
        {
            guestPlayer.IsActive = true;
        }

        listing.LoanedGuestPlayerId = guestPlayer.Id;

        // 3. If linked to a Matchday, auto-add guest player to Attendance, Lineup, and Split-Bill
        if (matchId.HasValue)
        {
            var match = await _db.Matches
                .Include(m => m.Lineups)
                .FirstOrDefaultAsync(m => m.Id == matchId.Value);

            if (match != null && (match.TeamAId == recruitingTeamId || match.TeamBId == recruitingTeamId))
            {
                listing.MatchId = match.Id;

                // 3a. AttendanceSession & AttendanceRecord
                var session = await _db.AttendanceSessions
                    .Include(s => s.Records)
                    .FirstOrDefaultAsync(s => s.MatchId == match.Id && s.TeamId == recruitingTeamId);

                if (session == null)
                {
                    session = new AttendanceSession
                    {
                        TeamId = recruitingTeamId,
                        MatchId = match.Id,
                        Title = $"Trận đấu - {match.PitchName}",
                        SessionDateTime = match.MatchDateTime,
                        ResponseDeadline = match.MatchDateTime.Date.AddHours(12),
                        StatisticalPeriod = match.MatchDateTime.ToString("yyyy-MM"),
                        PitchName = match.PitchName ?? string.Empty,
                        PitchType = match.PitchType ?? "7 người",
                        Status = SessionStatus.Draft
                    };
                    _db.AttendanceSessions.Add(session);
                }

                var attRecord = session.Records.FirstOrDefault(r => r.PlayerId == guestPlayer.Id);
                if (attRecord == null)
                {
                    attRecord = new AttendanceRecord
                    {
                        SessionId = session.Id,
                        PlayerId = guestPlayer.Id,
                        Response = ResponseStatus.Attending,
                        ResponseTime = DateTime.UtcNow,
                        Actual = ActualStatus.Present
                    };
                    session.Records.Add(attRecord);
                    _db.AttendanceRecords.Add(attRecord);
                }
                else
                {
                    attRecord.Response = ResponseStatus.Attending;
                    attRecord.ResponseTime = DateTime.UtcNow;
                }

                // 3b. MatchPlayerLineup
                if (!match.Lineups.Any(l => l.PlayerId == guestPlayer.Id))
                {
                    string side = match.TeamBId == recruitingTeamId ? "B" : "A";
                    _db.MatchPlayerLineups.Add(new MatchPlayerLineup
                    {
                        MatchId = match.Id,
                        TeamId = recruitingTeamId,
                        PlayerId = guestPlayer.Id,
                        TeamSide = side,
                        IsStarting = false,
                        AssignedPosition = chosenPosition,
                        HasActuallyPlayed = true
                    });
                }

                // 3c. MatchExpense & MatchExpenseShare (auto-update split-bill if already created)
                var expense = await _db.MatchExpenses
                    .Include(e => e.Shares)
                    .FirstOrDefaultAsync(e => e.MatchId == match.Id && e.TeamId == recruitingTeamId);

                if (expense != null)
                {
                    var existingShare = expense.Shares.FirstOrDefault(s => s.PlayerId == guestPlayer.Id);
                    if (existingShare == null)
                    {
                        existingShare = new MatchExpenseShare
                        {
                            MatchExpenseId = expense.Id,
                            PlayerId = guestPlayer.Id,
                            LinkedUserId = loanedUserId,
                            ParticipatesInMatch = true,
                            ParticipatesInTang2 = false,
                            PaymentStatus = ExpenseSharePaymentStatus.Unpaid
                        };
                        expense.Shares.Add(existingShare);
                        _db.MatchExpenseShares.Add(existingShare);
                    }
                    else
                    {
                        existingShare.ParticipatesInMatch = true;
                    }

                    // Recalculate per-person match share cleanly rounded to 1.000đ
                    var matchParticipants = expense.Shares.Where(s => s.ParticipatesInMatch).ToList();
                    if (matchParticipants.Count > 0 && expense.MatchTotalPayable > 0m)
                    {
                        var newPerPerson = MatchSplitBillService.RoundToNearest1000(expense.MatchTotalPayable / matchParticipants.Count);
                        expense.MatchPerPersonShare = newPerPerson;
                        foreach (var s in matchParticipants)
                        {
                            s.MatchAmount = newPerPerson;
                            s.TotalAmount = s.MatchAmount + s.Tang2Amount;
                        }
                    }
                }
            }
        }

        // 4. Update listing & applications status
        acceptedApp.Status = LoanApplicationStatus.Accepted;
        acceptedApp.RespondedAt = DateTime.UtcNow;

        foreach (var other in listing.Applications.Where(a => a.Id != acceptedApp.Id && a.Status == LoanApplicationStatus.Pending))
        {
            other.Status = LoanApplicationStatus.Rejected;
            other.RespondedAt = DateTime.UtcNow;
        }

        listing.AcceptedApplicationId = acceptedApp.Id;
        listing.Status = LoanListingStatus.Matched;
        listing.MatchedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var escrowNote = listing.EscrowAbCoin > 0 ? $" (Đã khóa cọc {listing.EscrowAbCoin} AB Coin)" : string.Empty;
        return (true, $"Đã chốt kèo mượn cầu thủ {guestPlayer.FullName} cho {recruitingTeam.Name}{escrowNote}!", listing);
    }

    public async Task<(bool Success, string Message)> CompleteLoanListingAsync(Guid listingId, string actorUserId)
    {
        var listing = await _db.PlayerLoanListings
            .Include(l => l.Applications)
            .FirstOrDefaultAsync(l => l.Id == listingId);

        if (listing == null)
            return (false, "Tin mượn cầu thủ không tồn tại.");

        if (listing.Status == LoanListingStatus.Completed)
            return (true, "Kèo mượn cầu thủ này đã được hoàn tất trước đó.");

        if (listing.Status != LoanListingStatus.Matched)
            return (false, "Chỉ có thể hoàn tất kèo đang ở trạng thái Đã chốt (Matched).");

        var acceptedApp = listing.Applications.FirstOrDefault(a => a.Id == listing.AcceptedApplicationId);
        Guid? recruitingTeamId = listing.ListingType == LoanListingType.TeamRecruiting
            ? listing.TeamId
            : acceptedApp?.ApplicantTeamId;

        bool canComplete = listing.UserId == actorUserId ||
                           (recruitingTeamId.HasValue && await _auth.HasManagerAccessAsync(recruitingTeamId.Value, actorUserId));

        if (!canComplete)
            return (false, "Chỉ Đội trưởng đội mượn quân hoặc người tạo kèo mới được xác nhận hoàn tất.");

        await ReleaseEscrowAndMarkCompletedAsync(listing);
        await _db.SaveChangesAsync();

        return (true, listing.EscrowAbCoin > 0
            ? $"Đã hoàn tất kèo mượn cầu thủ và hoàn trả {listing.EscrowAbCoin} AB Coin tiền cọc!"
            : "Đã xác nhận hoàn tất kèo mượn cầu thủ!");
    }

    public async Task CompleteLoansForMatchAsync(Guid matchId)
    {
        var matchedListings = await _db.PlayerLoanListings
            .Include(l => l.Applications)
            .Where(l => l.Status == LoanListingStatus.Matched &&
                        (l.MatchId == matchId || l.Applications.Any(a => a.Id == l.AcceptedApplicationId && a.TargetMatchId == matchId)))
            .ToListAsync();

        if (matchedListings.Count == 0) return;

        foreach (var listing in matchedListings)
        {
            await ReleaseEscrowAndMarkCompletedAsync(listing);
        }

        await _db.SaveChangesAsync();
    }

    private async Task ReleaseEscrowAndMarkCompletedAsync(PlayerLoanListing listing)
    {
        if (listing.EscrowAbCoin > 0 && listing.EscrowJournalEntryId.HasValue && !listing.IsEscrowReleased)
        {
            var holdEntry = await _db.PlayerAbJournalEntries
                .FirstOrDefaultAsync(e => e.Id == listing.EscrowJournalEntryId.Value);

            if (holdEntry != null)
            {
                bool alreadyReversed = await _db.PlayerAbJournalEntries
                    .AnyAsync(e => e.ReversedEntryId == holdEntry.Id);

                if (!alreadyReversed)
                {
                    var releaseEntry = new PlayerAbJournalEntry
                    {
                        TeamId = holdEntry.TeamId,
                        PlayerId = holdEntry.PlayerId,
                        StatisticalPeriod = holdEntry.StatisticalPeriod,
                        SourceDomain = AbSourceDomain.LoanEscrow,
                        SourceId = listing.Id,
                        SourceRole = "LoanEscrowRelease",
                        SourceRevision = 1,
                        OperationKey = $"{holdEntry.TeamId}_LoanEscrowRelease_{listing.Id}_{holdEntry.PlayerId}_revof_{holdEntry.Id}",
                        Points = Math.Abs(holdEntry.Points),
                        RegulationVersion = holdEntry.RegulationVersion,
                        ReversedEntryId = holdEntry.Id,
                        AdjustmentReason = "Hoàn cọc AB Coin sau khi hoàn thành kèo mượn cầu thủ đúng hẹn",
                        EventDate = CompetitionRules.LocalNow
                    };
                    _db.PlayerAbJournalEntries.Add(releaseEntry);
                }
            }
            listing.IsEscrowReleased = true;
        }

        listing.Status = LoanListingStatus.Completed;
        listing.CompletedAt = DateTime.UtcNow;
    }

    public async Task<(bool Success, string Message)> CancelListingAsync(Guid listingId, string actorUserId)
    {
        var listing = await _db.PlayerLoanListings
            .FirstOrDefaultAsync(l => l.Id == listingId);

        if (listing == null)
            return (false, "Tin không tồn tại.");

        bool canCancel = listing.UserId == actorUserId ||
                         (listing.TeamId.HasValue && await _auth.HasManagerAccessAsync(listing.TeamId.Value, actorUserId));

        if (!canCancel)
            return (false, "Bạn không có quyền hủy tin này.");

        if (listing.Status == LoanListingStatus.Completed || listing.Status == LoanListingStatus.Cancelled)
            return (false, "Tin này đã hoàn tất hoặc đã hủy trước đó.");

        if (listing.EscrowJournalEntryId.HasValue && !listing.IsEscrowReleased)
        {
            var holdEntry = await _db.PlayerAbJournalEntries
                .FirstOrDefaultAsync(e => e.Id == listing.EscrowJournalEntryId.Value);

            if (holdEntry != null && !await _db.PlayerAbJournalEntries.AnyAsync(e => e.ReversedEntryId == holdEntry.Id))
            {
                _db.PlayerAbJournalEntries.Add(new PlayerAbJournalEntry
                {
                    TeamId = holdEntry.TeamId,
                    PlayerId = holdEntry.PlayerId,
                    StatisticalPeriod = holdEntry.StatisticalPeriod,
                    SourceDomain = AbSourceDomain.LoanEscrow,
                    SourceId = listing.Id,
                    SourceRole = "LoanEscrowRelease",
                    SourceRevision = 1,
                    OperationKey = $"{holdEntry.TeamId}_LoanEscrowCancel_{listing.Id}_{holdEntry.PlayerId}_revof_{holdEntry.Id}",
                    Points = Math.Abs(holdEntry.Points),
                    RegulationVersion = holdEntry.RegulationVersion,
                    ReversedEntryId = holdEntry.Id,
                    AdjustmentReason = "Hoàn cọc AB Coin do hủy tin mượn cầu thủ",
                    EventDate = CompetitionRules.LocalNow
                });
            }
            listing.IsEscrowReleased = true;
        }

        listing.Status = LoanListingStatus.Cancelled;
        await _db.SaveChangesAsync();

        return (true, "Đã hủy tin mượn cầu thủ thành công.");
    }

    public async Task<LoanMarketIndexViewModel> BuildMarketplaceAsync(
        string? activeTab,
        string? region,
        DateTime? playDate,
        string? position,
        string? feeFilter,
        string? currentUserId,
        string baseMarketUrl)
    {
        string normalizedTab = string.Equals(activeTab, "players", StringComparison.OrdinalIgnoreCase) ? "players" : "teams";
        string normalizedFee = (feeFilter ?? "all").Trim().ToLowerInvariant();
        string? cleanRegion = string.IsNullOrWhiteSpace(region) ? null : region.Trim();
        string? cleanPos = string.IsNullOrWhiteSpace(position) ? null : MapToStandardPositionCode(position);

        var query = _db.PlayerLoanListings
            .Include(l => l.User)
            .Include(l => l.Player).ThenInclude(p => p!.Team)
            .Include(l => l.Team)
            .Include(l => l.Match)
            .Include(l => l.Applications).ThenInclude(a => a.ApplicantUser)
            .Include(l => l.Applications).ThenInclude(a => a.ApplicantTeam)
            .Include(l => l.Applications).ThenInclude(a => a.TargetMatch)
            .Where(l => l.Status != LoanListingStatus.Cancelled)
            .AsQueryable();

        if (!string.IsNullOrEmpty(cleanRegion))
        {
            var rLower = cleanRegion.ToLower();
            query = query.Where(l => l.Region.ToLower().Contains(rLower) ||
                                     (l.PitchName != null && l.PitchName.ToLower().Contains(rLower)));
        }

        if (playDate.HasValue)
        {
            var d = playDate.Value.Date;
            query = query.Where(l => l.PlayDate.Date == d);
        }

        if (normalizedFee == "split")
        {
            query = query.Where(l => l.FeeType == LoanFeeType.SplitPitch);
        }
        else if (normalizedFee == "paid")
        {
            query = query.Where(l => l.FeeType == LoanFeeType.PaidMercenary);
        }

        var rawListings = await query
            .OrderBy(l => l.Status == LoanListingStatus.Open ? 0 : l.Status == LoanListingStatus.Matched ? 1 : 2)
            .ThenBy(l => l.PlayDate)
            .ThenByDescending(l => l.CreatedAt)
            .ToListAsync();

        if (!string.IsNullOrEmpty(cleanPos))
        {
            rawListings = rawListings
                .Where(l => l.PositionsNeeded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Any(p => string.Equals(p, cleanPos, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        // Gather user IDs to build scout profiles in batch
        var relevantUserIds = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(currentUserId)) relevantUserIds.Add(currentUserId);
        foreach (var l in rawListings)
        {
            if (!string.IsNullOrEmpty(l.UserId)) relevantUserIds.Add(l.UserId);
            foreach (var a in l.Applications)
            {
                if (!string.IsNullOrEmpty(a.ApplicantUserId)) relevantUserIds.Add(a.ApplicantUserId);
            }
        }

        var scoutMap = await BuildScoutProfilesMapAsync(relevantUserIds);

        var managedTeams = new List<Team>();
        var upcomingMatches = new List<MatchGame>();
        if (!string.IsNullOrEmpty(currentUserId))
        {
            var managerTeamIds = await _db.TeamMembers
                .Where(tm => tm.UserId == currentUserId && (tm.Role == TeamRole.Owner || tm.Role == TeamRole.Manager))
                .Select(tm => tm.TeamId)
                .ToListAsync();

            if (managerTeamIds.Count > 0)
            {
                managedTeams = await _db.Teams
                    .Where(t => managerTeamIds.Contains(t.Id))
                    .OrderBy(t => t.Name)
                    .ToListAsync();

                var today = CompetitionRules.LocalNow.Date.AddDays(-1);
                upcomingMatches = await _db.Matches
                    .Include(m => m.TeamA)
                    .Include(m => m.TeamB)
                    .Where(m => (managerTeamIds.Contains(m.TeamAId) || (m.TeamBId.HasValue && managerTeamIds.Contains(m.TeamBId.Value)))
                                && m.Status == MatchStatus.Scheduled
                                && m.MatchDateTime >= today)
                    .OrderBy(m => m.MatchDateTime)
                    .ToListAsync();
            }
        }

        var managedTeamIdSet = managedTeams.Select(t => t.Id).ToHashSet();

        var teamRecruitingDtos = new List<LoanListingCardDto>();
        var playerAvailableDtos = new List<LoanListingCardDto>();

        foreach (var l in rawListings)
        {
            var posBadges = l.PositionsNeeded
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var appDtos = l.Applications
                .OrderByDescending(a => a.Status == LoanApplicationStatus.Accepted)
                .ThenBy(a => a.CreatedAt)
                .Select(a => new LoanApplicationCardDto
                {
                    Id = a.Id,
                    ListingId = a.ListingId,
                    Position = a.Position,
                    Message = a.Message,
                    Status = a.Status,
                    CreatedAt = a.CreatedAt,
                    ApplicantTeamId = a.ApplicantTeamId,
                    ApplicantTeamName = a.ApplicantTeam?.Name,
                    TargetMatchId = a.TargetMatchId,
                    TargetMatchLabel = a.TargetMatch != null ? $"{a.TargetMatch.PitchName} ({a.TargetMatch.MatchDateTime:HH:mm dd/MM})" : null,
                    Scout = scoutMap.TryGetValue(a.ApplicantUserId, out var sc) ? sc : new PlayerScoutProfileDto
                    {
                        UserId = a.ApplicantUserId,
                        FullName = a.ApplicantUser?.FullName ?? "Cầu thủ AloBall",
                        PrimaryPosition = a.Position
                    }
                })
                .ToList();

            var acceptedApp = appDtos.FirstOrDefault(a => a.Id == l.AcceptedApplicationId)
                              ?? appDtos.FirstOrDefault(a => a.Status == LoanApplicationStatus.Accepted);

            PlayerScoutProfileDto? playerScout = null;
            if (!string.IsNullOrEmpty(l.UserId) && scoutMap.TryGetValue(l.UserId, out var ownerScout))
            {
                playerScout = ownerScout;
            }

            PlayerScoutProfileDto? matchedPlayerScout = l.ListingType == LoanListingType.TeamRecruiting
                ? acceptedApp?.Scout
                : playerScout;

            string? matchedTeamName = l.ListingType == LoanListingType.TeamRecruiting
                ? l.Team?.Name
                : (acceptedApp?.ApplicantTeamName ?? l.Team?.Name);

            Guid? effectiveMatchId = l.MatchId ?? acceptedApp?.TargetMatchId;

            bool canManage = (!string.IsNullOrEmpty(currentUserId) && l.UserId == currentUserId) ||
                             (l.TeamId.HasValue && managedTeamIdSet.Contains(l.TeamId.Value)) ||
                             (acceptedApp?.ApplicantTeamId != null && managedTeamIdSet.Contains(acceptedApp.ApplicantTeamId.Value));

            bool hasApplied = !string.IsNullOrEmpty(currentUserId) &&
                              l.Applications.Any(a => a.ApplicantUserId == currentUserId && a.Status != LoanApplicationStatus.Cancelled);

            var dto = new LoanListingCardDto
            {
                Id = l.Id,
                ListingType = l.ListingType,
                UserId = l.UserId,
                PlayerId = l.PlayerId ?? playerScout?.PlayerId,
                TeamId = l.TeamId,
                TeamName = l.Team?.Name ?? playerScout?.HomeTeamName ?? "Cầu thủ tự do",
                TeamSlug = l.Team?.Slug ?? playerScout?.HomeTeamSlug,
                TeamLogoUrl = l.Team?.LogoUrl,
                TeamRating = l.Team?.AverageRating ?? 5.0,
                MatchId = l.MatchId,
                MatchLabel = l.Match != null ? $"{l.Match.PitchName} ({l.Match.MatchDateTime:HH:mm dd/MM})" : null,
                Region = l.Region,
                PitchName = l.PitchName ?? l.Match?.PitchName,
                PlayDate = l.PlayDate,
                TimeSlot = l.TimeSlot,
                PositionsNeeded = l.PositionsNeeded,
                PositionBadges = posBadges,
                FeeType = l.FeeType,
                LoanFeeAmount = l.LoanFeeAmount,
                EscrowAbCoin = l.EscrowAbCoin,
                IsEscrowHeld = l.EscrowJournalEntryId.HasValue && !l.IsEscrowReleased,
                IsEscrowReleased = l.IsEscrowReleased,
                Notes = l.Notes,
                Status = l.Status,
                AcceptedApplicationId = l.AcceptedApplicationId,
                LoanedGuestPlayerId = l.LoanedGuestPlayerId,
                CreatedAt = l.CreatedAt,
                MatchedAt = l.MatchedAt,
                CompletedAt = l.CompletedAt,
                PlayerScout = playerScout,
                MatchedPlayerScout = matchedPlayerScout,
                MatchedTeamName = matchedTeamName,
                EffectiveMatchId = effectiveMatchId,
                Applications = appDtos,
                CanManage = canManage,
                HasApplied = hasApplied
            };

            dto.ZaloShareMessage = BuildZaloLoanMessage(dto, baseMarketUrl);

            if (l.ListingType == LoanListingType.TeamRecruiting)
                teamRecruitingDtos.Add(dto);
            else
                playerAvailableDtos.Add(dto);
        }

        PlayerScoutProfileDto? currentUserScout = (!string.IsNullOrEmpty(currentUserId) && scoutMap.TryGetValue(currentUserId, out var mySc))
            ? mySc
            : null;

        return new LoanMarketIndexViewModel
        {
            ActiveTab = normalizedTab,
            Region = cleanRegion,
            PlayDate = playDate,
            Position = cleanPos,
            FeeFilter = normalizedFee,
            TeamRecruitingListings = teamRecruitingDtos,
            PlayerAvailableListings = playerAvailableDtos,
            ManagedTeams = managedTeams,
            UpcomingManagedMatches = upcomingMatches,
            CurrentUserAbCoin = currentUserScout?.AvailableAbCoin ?? 0,
            CurrentUserScout = currentUserScout
        };
    }

    public static string BuildZaloLoanMessage(LoanListingCardDto card, string baseMarketUrl)
    {
        string feeText = card.FeeType == LoanFeeType.SplitPitch
            ? "Share tiền sân (0đ phí mượn)"
            : $"Có phí tăng cường: {card.LoanFeeAmount:N0}đ";

        string escrowText = card.EscrowAbCoin > 0
            ? $"\n🛡️ Cọc uy tín AB Coin: {card.EscrowAbCoin} AB Coin {(card.IsEscrowReleased ? "(Đã hoàn cọc)" : card.IsEscrowHeld ? "(Đã khóa cọc)" : "(Yêu cầu đặt cọc)")}"
            : string.Empty;

        string pitchText = !string.IsNullOrWhiteSpace(card.PitchName)
            ? $"{card.PitchName} ({card.Region})"
            : card.Region;

        if (card.Status == LoanListingStatus.Matched || card.Status == LoanListingStatus.Completed)
        {
            var ringer = card.MatchedPlayerScout ?? card.PlayerScout;
            string ringerInfo = ringer != null
                ? $"{ringer.FullName} ({ringer.Ovr} OVR · {ringer.PrimaryPosition} · Chân {ringer.PreferredFoot} · Uy tín {ringer.ReputationStars:0.0}★)"
                : "Cầu thủ tăng cường";

            return $"🤝 [ALOBALL] CHỐT KÈO MƯỢN CẦU THỦ\n" +
                   $"⚽ Đội bóng: {card.MatchedTeamName ?? card.TeamName}\n" +
                   $"🏃 Ngoại binh: {ringerInfo}\n" +
                   $"🗓 Khung giờ: {card.TimeSlot} | {card.PlayDate:dd/MM/yyyy} (Giờ VN)\n" +
                   $"📍 Sân bóng: {pitchText}\n" +
                   $"💰 Hình thức: {feeText}{escrowText}\n" +
                   $"🔗 Xem chi tiết trên AloBall: {baseMarketUrl}";
        }

        if (card.ListingType == LoanListingType.TeamRecruiting)
        {
            return $"📢 [ALOBALL] ĐỘI {card.TeamName?.ToUpperInvariant()} CẦN MƯỢN QUÂN\n" +
                   $"🗓 Thời gian: {card.TimeSlot} | {card.PlayDate:dd/MM/yyyy}\n" +
                   $"📍 Khu vực / Sân: {pitchText}\n" +
                   $"🎯 Vị trí cần tuyển: {string.Join(", ", card.PositionBadges)}\n" +
                   $"💰 Mức phí: {feeText}{escrowText}\n" +
                   $"👉 Ứng tuyển 1 chạm tại: {baseMarketUrl}";
        }

        var pScout = card.PlayerScout;
        string pSummary = pScout != null
            ? $"{pScout.FullName} ({pScout.Ovr} OVR · Chân {pScout.PreferredFoot} · Uy tín {pScout.ReputationStars:0.0}★)"
            : "Cầu thủ AloBall";

        return $"⚡ [ALOBALL] CẦU THỦ SẴN SÀNG ĐÁ BÙ / TĂNG CƯỜNG\n" +
               $"🏃 Cầu thủ: {pSummary}\n" +
               $"🎯 Vị trí sở trường: {string.Join(", ", card.PositionBadges)}\n" +
               $"🗓 Rảnh lịch: {card.TimeSlot} | {card.PlayDate:dd/MM/yyyy}\n" +
               $"📍 Khu vực: {pitchText}\n" +
               $"💰 Hình thức: {feeText}{escrowText}\n" +
               $"👉 Chốt mượn 1 chạm tại: {baseMarketUrl}";
    }

    private async Task<Dictionary<string, PlayerScoutProfileDto>> BuildScoutProfilesMapAsync(HashSet<string> userIds)
    {
        var result = new Dictionary<string, PlayerScoutProfileDto>(StringComparer.Ordinal);
        if (userIds.Count == 0) return result;

        var users = await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToListAsync();

        var players = await _db.Players
            .Include(p => p.Team)
            .Where(p => p.LinkedUserId != null && userIds.Contains(p.LinkedUserId))
            .OrderByDescending(p => p.IsActive)
            .ThenByDescending(p => p.JoinedDate)
            .ToListAsync();

        var playerIds = players.Select(p => p.Id).ToList();

        var abByPlayer = playerIds.Count > 0
            ? await _db.PlayerAbJournalEntries
                .Where(e => playerIds.Contains(e.PlayerId))
                .GroupBy(e => e.PlayerId)
                .Select(g => new { PlayerId = g.Key, TotalAb = g.Sum(x => x.Points) })
                .ToDictionaryAsync(x => x.PlayerId, x => x.TotalAb)
            : new Dictionary<Guid, int>();

        var goalCounts = playerIds.Count > 0
            ? await _db.MatchGoalEvents
                .Where(g => playerIds.Contains(g.ScorerPlayerId))
                .GroupBy(g => g.ScorerPlayerId)
                .Select(g => new { PlayerId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.PlayerId, x => x.Count)
            : new Dictionary<Guid, int>();

        var assistCounts = playerIds.Count > 0
            ? await _db.MatchGoalEvents
                .Where(g => g.AssistPlayerId.HasValue && playerIds.Contains(g.AssistPlayerId.Value))
                .GroupBy(g => g.AssistPlayerId!.Value)
                .Select(g => new { PlayerId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.PlayerId, x => x.Count)
            : new Dictionary<Guid, int>();

        var lineupCounts = playerIds.Count > 0
            ? await _db.MatchPlayerLineups
                .Where(l => playerIds.Contains(l.PlayerId))
                .GroupBy(l => l.PlayerId)
                .Select(g => new { PlayerId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.PlayerId, x => x.Count)
            : new Dictionary<Guid, int>();

        foreach (var u in users)
        {
            var userPlayers = players.Where(p => p.LinkedUserId == u.Id).ToList();
            var primary = userPlayers.FirstOrDefault();

            int totalAb = userPlayers.Sum(p => abByPlayer.GetValueOrDefault(p.Id, 0));
            int goals = userPlayers.Sum(p => goalCounts.GetValueOrDefault(p.Id, 0));
            int assists = userPlayers.Sum(p => assistCounts.GetValueOrDefault(p.Id, 0));
            int apps = userPlayers.Sum(p => lineupCounts.GetValueOrDefault(p.Id, 0));

            string rawPos = primary?.Position ?? primary?.StandardPrimaryPosition ?? u.PrimaryPosition ?? "MID";
            string stdPos = MapToStandardPositionCode(rawPos);
            string foot = primary?.PreferredFoot ?? u.PreferredFoot ?? "Phải";
            string exp = primary?.Experience ?? u.Experience ?? "Phong trào";
            string reg = primary?.Region ?? u.Region ?? primary?.Team?.ActivityArea ?? "Hà Nội";
            string? rawPlayStyles = primary?.PlayStyles ?? u.PlayStyles;

            double teamRating = primary?.Team?.AverageRating ?? 4.8;
            int repStat = Math.Clamp((int)Math.Round(55 + (teamRating / 5.0) * 44), 55, 99);
            int expBase = exp.Contains("5") || exp.Contains("Phủi cứng") ? 82 :
                          exp.Contains("3") ? 76 :
                          exp.Contains("1") ? 72 : 68;
            int expStat = Math.Clamp(expBase + Math.Min(15, apps * 2), 55, 99);
            int att = Math.Clamp(64 + (goals * 4) + (stdPos == "FWD" ? 10 : stdPos == "MID" ? 5 : 0), 55, 99);
            int cre = Math.Clamp(64 + (assists * 4) + (stdPos == "MID" ? 10 : stdPos == "FWD" ? 4 : 0), 55, 99);
            int sta = Math.Clamp(72 + Math.Min(18, apps * 2), 55, 99);
            int win = Math.Clamp(70 + Math.Min(20, totalAb / 2), 55, 99);

            int ovr = stdPos switch
            {
                "FWD" => (int)Math.Round(att * 0.35 + cre * 0.20 + win * 0.15 + sta * 0.10 + expStat * 0.10 + repStat * 0.10),
                "MID" => (int)Math.Round(cre * 0.35 + att * 0.20 + sta * 0.15 + win * 0.15 + expStat * 0.08 + repStat * 0.07),
                "DEF" => (int)Math.Round(sta * 0.25 + win * 0.25 + expStat * 0.20 + repStat * 0.15 + cre * 0.10 + att * 0.05),
                "GK" => (int)Math.Round(win * 0.30 + repStat * 0.25 + expStat * 0.20 + sta * 0.15 + cre * 0.10),
                _ => 74
            };
            ovr = Math.Clamp(ovr, 55, 99);
            bool isMotm = totalAb >= 30;
            string tier = isMotm ? "motm" : ovr >= 80 ? "gold" : ovr >= 70 ? "silver" : "bronze";

            result[u.Id] = new PlayerScoutProfileDto
            {
                PlayerId = primary?.Id,
                UserId = u.Id,
                FullName = !string.IsNullOrWhiteSpace(u.FullName) ? u.FullName : (primary?.FullName ?? u.UserName ?? "Cầu thủ AloBall"),
                AvatarUrl = primary?.PhotoUrl ?? u.AvatarUrl,
                PrimaryPosition = stdPos,
                PreferredFoot = foot,
                Experience = exp,
                Region = reg,
                Ovr = ovr,
                Tier = tier,
                RepStat = repStat,
                ReputationStars = Math.Round(Math.Clamp(teamRating, 3.5, 5.0), 1),
                AvailableAbCoin = Math.Max(0, totalAb),
                HomeTeamName = primary?.Team?.Name,
                HomeTeamSlug = primary?.Team?.Slug,
                PlayStyles = PlayStyleCatalog.Parse(rawPlayStyles)
            };
        }

        return result;
    }
}
