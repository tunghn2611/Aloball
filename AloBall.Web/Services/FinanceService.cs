using System.Text;
using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Services;

public interface IFinanceService
{
    Task<MatchCostForecastViewModel> GetMatchCostForecastAsync(Guid matchId, Guid teamId, string? userId);
    Task<(bool Success, string Message)> AddEstimatedExpenseAsync(Guid matchId, Guid teamId, string description, decimal amount, string userId);
    Task<(bool Success, string Message)> DeleteEstimatedExpenseAsync(Guid estimateId, Guid teamId, string userId);

    Task<(bool Success, string Message, SettlementWizardViewModel? Model)> GetSettlementPreparationDataAsync(Guid matchId, Guid teamId, string userId);
    Task<(bool Success, string Message, Guid? SettlementId)> CreateSettlementAsync(CreateSettlementInput input, string userId);
    Task<SettlementDetailViewModel?> GetSettlementDetailAsync(Guid matchId, Guid teamId, string? userId);

    Task<(bool Success, string Message)> RecordPaymentAsync(RecordPaymentInput input, string userId);
    Task<(bool Success, string Message)> ReversePaymentAsync(Guid paymentId, Guid teamId, string reason, string userId);
    Task<(bool Success, string Message)> AdjustAllocationAsync(ManualAdjustmentInput input, string userId);
    Task<(bool Success, string Message)> SendPaymentReminderAsync(Guid allocationId, Guid teamId, string userId);

    Task<TeamFinanceReportViewModel> GetTeamFinanceReportAsync(Guid teamId, string? monthFilter, string userId);
    Task<byte[]> ExportTeamFinanceReportCsvAsync(Guid teamId, string? monthFilter, string userId);
}

public class FinanceService : IFinanceService
{
    private readonly ApplicationDbContext _db;
    private readonly ITeamAuthorizationService _auth;
    private readonly ITeamMessagingService? _messaging;

    public FinanceService(ApplicationDbContext db, ITeamAuthorizationService auth, ITeamMessagingService? messaging = null)
    {
        _db = db;
        _auth = auth;
        _messaging = messaging;
    }

    // =========================================================================
    // 1. MATCH COST FORECAST (DỰ BÁO CHI PHÍ TRẬN ĐẤU)
    // =========================================================================

    public async Task<MatchCostForecastViewModel> GetMatchCostForecastAsync(Guid matchId, Guid teamId, string? userId)
    {
        var match = await _db.Matches
            .Include(m => m.TeamA)
            .Include(m => m.TeamB)
            .FirstOrDefaultAsync(m => m.Id == matchId);

        if (match == null) throw new ArgumentException("Trận đấu không tồn tại.");

        bool canManage = userId != null && await _auth.HasManagerAccessAsync(teamId, userId);

        var opponentName = match.Category == MatchCategory.Internal
            ? "Đá nội bộ CLB"
            : (match.TeamAId == teamId ? (match.TeamB?.Name ?? match.TeamBExternalName ?? "Đối thủ ngoài") : match.TeamA.Name);

        // 1. Tìm đơn đặt sân ĐÃ XÁC NHẬN (chỉ lấy báo giá Accepted)
        var confirmedPitchRequest = await _db.PitchBookingRequests
            .Include(r => r.Offers)
            .Where(r => r.MatchId == matchId && r.TeamId == teamId && r.Status == PitchBookingStatus.Confirmed)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        decimal confirmedPitchCost = 0m;
        bool hasConfirmedPitch = false;
        string pitchNote = "Chưa có đơn đặt sân qua sàn";

        if (confirmedPitchRequest != null)
        {
            var acceptedOffer = confirmedPitchRequest.Offers.FirstOrDefault(o => o.Status == PitchBookingOfferStatus.Accepted);
            if (acceptedOffer != null)
            {
                confirmedPitchCost = acceptedOffer.TotalPriceVND;
                hasConfirmedPitch = true;
                pitchNote = $"Đã xác nhận đặt sân: {acceptedOffer.ConfirmedVenueName} ({confirmedPitchCost:N0} đ)";
            }
        }

        // 2. Tìm yêu cầu trọng tài ĐÃ XÁC NHẬN (chỉ lấy báo giá Accepted)
        var confirmedRefRequest = await _db.RefereeRequests
            .Include(r => r.Proposals)
            .Where(r => r.MatchId == matchId && r.TeamId == teamId && r.Status == RefereeRequestStatus.Confirmed)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        decimal confirmedRefCost = 0m;
        bool hasConfirmedRef = false;
        string refNote = "Chưa có trọng tài xác nhận";

        if (confirmedRefRequest != null)
        {
            var acceptedProposal = confirmedRefRequest.Proposals.FirstOrDefault(p => p.Status == RefereeProposalStatus.Accepted);
            if (acceptedProposal != null)
            {
                confirmedRefCost = acceptedProposal.TotalPriceVND;
                hasConfirmedRef = true;
                refNote = $"Đã chốt trọng tài ({acceptedProposal.TotalPriceVND:N0} đ)";
            }
        }

        // 3. Dự toán chi phí khác do Lãnh đội nhập
        var customEstimates = await _db.MatchExpenseEstimates
            .Where(e => e.MatchId == matchId && e.TeamId == teamId)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new MatchExpenseEstimateItem
            {
                Id = e.Id,
                Description = e.Description,
                Amount = e.EstimatedAmount,
                CreatedAt = e.CreatedAt
            })
            .ToListAsync();

        // 4. Số người dự kiến tham gia (dựa trên RSVP Attending)
        int expectedParticipants = 0;
        var session = await _db.AttendanceSessions
            .Include(s => s.Records)
            .FirstOrDefaultAsync(s => s.MatchId == matchId && s.TeamId == teamId);

        if (session != null)
        {
            expectedParticipants = session.Records.Count(r => r.Response == ResponseStatus.Attending);
        }

        if (expectedParticipants == 0)
        {
            // Dự phòng theo số người đá chính của sơ đồ
            expectedParticipants = match.PitchType == "11 người" ? 11 : (match.PitchType == "5 người" ? 5 : 7);
        }

        // 5. Kiểm tra trận đã quyết toán chưa
        var settlement = await _db.MatchSettlements
            .FirstOrDefaultAsync(s => s.MatchId == matchId && s.TeamId == teamId);

        return new MatchCostForecastViewModel
        {
            MatchId = matchId,
            TeamId = teamId,
            TeamName = match.TeamAId == teamId ? match.TeamA.Name : (match.TeamB?.Name ?? "CLB"),
            MatchDateTime = match.MatchDateTime,
            OpponentName = opponentName,
            PitchName = match.PitchName,
            PitchType = match.PitchType,
            ConfirmedPitchCost = confirmedPitchCost,
            HasConfirmedPitch = hasConfirmedPitch,
            PitchCostNote = pitchNote,
            ConfirmedRefereeCost = confirmedRefCost,
            HasConfirmedReferee = hasConfirmedRef,
            RefereeCostNote = refNote,
            CustomEstimates = customEstimates,
            ExpectedParticipants = expectedParticipants,
            IsSettled = settlement != null,
            SettlementId = settlement?.Id,
            CanManage = canManage
        };
    }

    public async Task<(bool Success, string Message)> AddEstimatedExpenseAsync(Guid matchId, Guid teamId, string description, decimal amount, string userId)
    {
        if (!await _auth.HasManagerAccessAsync(teamId, userId))
            return (false, "Bạn không có quyền quản lý đội bóng để thêm dự toán chi phí.");

        if (string.IsNullOrWhiteSpace(description))
            return (false, "Vui lòng nhập tên khoản chi dự kiến.");

        if (amount <= 0)
            return (false, "Số tiền dự toán phải lớn hơn 0 VNĐ.");

        var estimate = new MatchExpenseEstimate
        {
            MatchId = matchId,
            TeamId = teamId,
            Description = description.Trim(),
            EstimatedAmount = amount,
            CreatedByUserId = userId
        };

        _db.MatchExpenseEstimates.Add(estimate);
        await _db.SaveChangesAsync();

        return (true, "Đã thêm khoản chi phí dự kiến thành công.");
    }

    public async Task<(bool Success, string Message)> DeleteEstimatedExpenseAsync(Guid estimateId, Guid teamId, string userId)
    {
        if (!await _auth.HasManagerAccessAsync(teamId, userId))
            return (false, "Bạn không có quyền quản lý đội bóng.");

        var estimate = await _db.MatchExpenseEstimates.FirstOrDefaultAsync(e => e.Id == estimateId && e.TeamId == teamId);
        if (estimate == null)
            return (false, "Khoản chi dự kiến không tồn tại.");

        _db.MatchExpenseEstimates.Remove(estimate);
        await _db.SaveChangesAsync();

        return (true, "Đã xóa khoản chi phí dự kiến.");
    }

    // =========================================================================
    // 2. SMART SETTLEMENT (QUYẾT TOÁN THÔNG MINH 4 BƯỚC)
    // =========================================================================

    public async Task<(bool Success, string Message, SettlementWizardViewModel? Model)> GetSettlementPreparationDataAsync(Guid matchId, Guid teamId, string userId)
    {
        if (!await _auth.HasManagerAccessAsync(teamId, userId))
            return (false, "Chỉ Quản lý hoặc Chủ sở hữu đội mới có quyền thực hiện quyết toán trận đấu.", null);

        // Chống quyết toán trùng lặp
        var existingSettlement = await _db.MatchSettlements.FirstOrDefaultAsync(s => s.MatchId == matchId && s.TeamId == teamId);
        if (existingSettlement != null)
            return (false, "Trận đấu này đã được quyết toán. Bạn có thể xem chi tiết hoặc chỉnh sửa các khoản thu tại trang chi tiết quyết toán.", null);

        var match = await _db.Matches
            .Include(m => m.TeamA)
            .Include(m => m.TeamB)
            .FirstOrDefaultAsync(m => m.Id == matchId);

        if (match == null)
            return (false, "Trận đấu không tồn tại.", null);

        // BƯỚC 1: Lấy chi phí thực tế đã xác nhận
        var expenses = new List<SettlementExpenseInputItem>();

        // Tiền sân xác nhận
        var confirmedPitchRequest = await _db.PitchBookingRequests
            .Include(r => r.Offers)
            .Where(r => r.MatchId == matchId && r.TeamId == teamId && r.Status == PitchBookingStatus.Confirmed)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        decimal confirmedPitchCost = 0m;
        bool hasConfirmedPitch = false;
        if (confirmedPitchRequest != null)
        {
            var acceptedOffer = confirmedPitchRequest.Offers.FirstOrDefault(o => o.Status == PitchBookingOfferStatus.Accepted);
            if (acceptedOffer != null)
            {
                confirmedPitchCost = acceptedOffer.TotalPriceVND;
                hasConfirmedPitch = true;
                expenses.Add(new SettlementExpenseInputItem
                {
                    Category = SettlementExpenseCategory.Pitch,
                    Description = $"Tiền thuê sân bóng ({acceptedOffer.ConfirmedVenueName})",
                    Amount = confirmedPitchCost,
                    IsFromConfirmedService = true,
                    SourceReferenceId = acceptedOffer.Id.ToString()
                });
            }
        }

        // Tiền trọng tài xác nhận
        var confirmedRefRequest = await _db.RefereeRequests
            .Include(r => r.Proposals)
            .Where(r => r.MatchId == matchId && r.TeamId == teamId && r.Status == RefereeRequestStatus.Confirmed)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        decimal confirmedRefCost = 0m;
        bool hasConfirmedRef = false;
        if (confirmedRefRequest != null)
        {
            var acceptedProposal = confirmedRefRequest.Proposals.FirstOrDefault(p => p.Status == RefereeProposalStatus.Accepted);
            if (acceptedProposal != null)
            {
                confirmedRefCost = acceptedProposal.TotalPriceVND;
                hasConfirmedRef = true;
                expenses.Add(new SettlementExpenseInputItem
                {
                    Category = SettlementExpenseCategory.Referee,
                    Description = "Tiền thù lao tổ trọng tài điều hành trận đấu",
                    Amount = confirmedRefCost,
                    IsFromConfirmedService = true,
                    SourceReferenceId = acceptedProposal.Id.ToString()
                });
            }
        }

        // Nạp các chi phí dự kiến trước đó vào danh mục gợi ý
        var estimates = await _db.MatchExpenseEstimates
            .Where(e => e.MatchId == matchId && e.TeamId == teamId)
            .ToListAsync();

        foreach (var est in estimates)
        {
            expenses.Add(new SettlementExpenseInputItem
            {
                Category = SettlementExpenseCategory.Other,
                Description = est.Description,
                Amount = est.EstimatedAmount,
                IsFromConfirmedService = false
            });
        }

        // BƯỚC 2: Thu thập danh sách người tham gia thực tế (Dựa trên Actual Attendance hoặc Lineup)
        var session = await _db.AttendanceSessions
            .Include(s => s.Records)
                .ThenInclude(r => r.Player)
            .FirstOrDefaultAsync(s => s.MatchId == matchId && s.TeamId == teamId);

        var lineups = await _db.MatchPlayerLineups
            .Where(l => l.MatchId == matchId && l.TeamId == teamId)
            .ToListAsync();

        var teamPlayers = await _db.Players
            .Where(p => p.TeamId == teamId && p.IsActive)
            .OrderBy(p => p.KitNumber ?? 999)
            .ToListAsync();

        var candidates = new List<ParticipantCandidateItem>();
        foreach (var p in teamPlayers)
        {
            var attRecord = session?.Records.FirstOrDefault(r => r.PlayerId == p.Id);
            var lineup = lineups.FirstOrDefault(l => l.PlayerId == p.Id);

            bool hasAtt = attRecord != null && attRecord.Actual == ActualStatus.Present;
            bool hasLineup = lineup != null && (lineup.HasActuallyPlayed || lineup.IsStarting);

            string note = "";
            if (hasAtt && hasLineup) note = "✓ Có mặt trên sân & Đã vào thi đấu";
            else if (hasAtt) note = "✓ Điểm danh thực tế: Có mặt";
            else if (hasLineup) note = "✓ Đội hình thi đấu chính thức";
            else if (attRecord != null && attRecord.Response == ResponseStatus.Attending) note = "Báo tham gia (Chưa chốt điểm danh)";
            else note = "Chưa có ghi nhận tham gia";

            candidates.Add(new ParticipantCandidateItem
            {
                PlayerId = p.Id,
                FullName = p.FullName,
                KitNumber = p.KitNumber,
                Position = p.Position,
                UserId = p.LinkedUserId,
                HasAuthoritativeAttendance = hasAtt,
                HasLineupAppearance = hasLineup,
                AttendanceNote = note,
                IsSelected = (hasAtt || hasLineup) // Chỉ chọn mặc định khi có căn cứ thực tế
            });
        }

        var opponentName = match.Category == MatchCategory.Internal
            ? "Đá nội bộ"
            : (match.TeamAId == teamId ? (match.TeamB?.Name ?? match.TeamBExternalName ?? "Đối thủ ngoài") : match.TeamA.Name);

        var wizard = new SettlementWizardViewModel
        {
            MatchId = matchId,
            TeamId = teamId,
            TeamName = match.TeamAId == teamId ? match.TeamA.Name : (match.TeamB?.Name ?? "CLB"),
            OpponentName = opponentName,
            MatchDateTime = match.MatchDateTime,
            PitchName = match.PitchName,
            ConfirmedPitchCost = confirmedPitchCost,
            HasConfirmedPitch = hasConfirmedPitch,
            ConfirmedRefereeCost = confirmedRefCost,
            HasConfirmedReferee = hasConfirmedRef,
            ExpenseItems = expenses,
            Candidates = candidates
        };

        return (true, "Sẵn sàng quyết toán.", wizard);
    }

    public async Task<(bool Success, string Message, Guid? SettlementId)> CreateSettlementAsync(CreateSettlementInput input, string userId)
    {
        if (!await _auth.HasManagerAccessAsync(input.TeamId, userId))
            return (false, "Chỉ Quản lý hoặc Chủ sở hữu đội mới có quyền thực hiện quyết toán trận đấu.", null);

        // Kiểm tra chống trùng lặp tuyệt đối
        var existing = await _db.MatchSettlements.FirstOrDefaultAsync(s => s.MatchId == input.MatchId && s.TeamId == input.TeamId);
        if (existing != null)
            return (false, "Trận đấu này đã có bản quyết toán được ghi nhận. Không thể tạo quyết toán trùng lặp.", null);

        if (!input.SelectedPlayerIds.Any())
            return (false, "Vui lòng chọn ít nhất một thành viên tham gia để chia sẻ chi phí.", null);

        decimal totalExpenses = input.Expenses.Sum(e => e.Amount);
        if (totalExpenses < 0)
            return (false, "Tổng chi phí quyết toán không thể là số âm.", null);

        // Chuẩn bị danh sách phân bổ chi phí với thuật toán làm tròn VND chuẩn xác
        int n = input.SelectedPlayerIds.Count;
        var selectedPlayers = await _db.Players
            .Where(p => input.SelectedPlayerIds.Contains(p.Id) && p.TeamId == input.TeamId)
            .OrderBy(p => p.KitNumber ?? 999)
            .ToListAsync();

        if (selectedPlayers.Count != n)
            return (false, "Có cầu thủ được chọn không thuộc đội bóng.", null);

        // Thuật toán chia đều và xử lý làm tròn số lẻ VND (Đảm bảo tổng phân bổ = chính xác tổng chi phí)
        decimal step = totalExpenses >= n * 1000m ? 1000m : 1m;
        decimal baseUnits = n > 0 ? Math.Floor((totalExpenses / step) / n) : 0m;
        decimal baseShare = baseUnits * step;
        decimal remainder = totalExpenses - (baseShare * n);
        int remainderCount = (int)(remainder / step);

        var finalAllocations = new List<MatchSettlementMemberAllocation>();

        for (int i = 0; i < n; i++)
        {
            var p = selectedPlayers[i];
            decimal fairCalculated = Math.Round(totalExpenses / n, 2);
            decimal fairAdjusted = (i < remainderCount) ? (baseShare + step) : baseShare;

            // Nếu người dùng có tùy chỉnh thủ công từ client và có lý do
            var clientOverride = input.Allocations.FirstOrDefault(a => a.PlayerId == p.Id);
            string? adjustReason = (i < remainderCount && remainderCount > 0) ? "Làm tròn chênh lệch VNĐ theo quy chuẩn" : null;

            if (clientOverride != null && clientOverride.AdjustedAmount != fairAdjusted)
            {
                if (string.IsNullOrWhiteSpace(clientOverride.AdjustmentReason))
                {
                    return (false, $"Điều chỉnh thủ công cho cầu thủ {p.FullName} bắt buộc phải có lý do.", null);
                }
                fairAdjusted = clientOverride.AdjustedAmount;
                adjustReason = clientOverride.AdjustmentReason.Trim();
            }

            finalAllocations.Add(new MatchSettlementMemberAllocation
            {
                PlayerId = p.Id,
                UserId = p.LinkedUserId,
                CalculatedAmount = fairCalculated,
                AdjustedAmount = fairAdjusted,
                AdjustmentReason = adjustReason,
                AmountPaid = 0m,
                PaymentStatus = PaymentStatus.Unpaid
            });
        }

        // Kiểm tra bảo toàn số học: Tổng phân bổ PHẢI BẰNG ĐÚNG tổng chi phí
        decimal sumAllocated = finalAllocations.Sum(a => a.AdjustedAmount);
        if (sumAllocated != totalExpenses)
        {
            return (false, $"Tổng số tiền phân bổ ({sumAllocated:N0} đ) không khớp với tổng chi phí quyết toán ({totalExpenses:N0} đ). Vui lòng kiểm tra lại các khoản điều chỉnh.", null);
        }

        decimal pitchCost = input.Expenses.Where(e => e.Category == SettlementExpenseCategory.Pitch).Sum(e => e.Amount);
        decimal refCost = input.Expenses.Where(e => e.Category == SettlementExpenseCategory.Referee).Sum(e => e.Amount);
        decimal otherCost = input.Expenses.Where(e => e.Category != SettlementExpenseCategory.Pitch && e.Category != SettlementExpenseCategory.Referee).Sum(e => e.Amount);

        // Tạo snapshot quyết toán
        var settlement = new MatchSettlement
        {
            MatchId = input.MatchId,
            TeamId = input.TeamId,
            SettlementDate = DateTime.UtcNow,
            TotalPitchCost = pitchCost,
            TotalRefereeCost = refCost,
            TotalOtherExpenses = otherCost,
            TotalAmount = totalExpenses,
            ParticipantCount = n,
            PerPersonShare = n > 0 ? Math.Round(totalExpenses / n, 0) : 0m,
            Status = SettlementStatus.Confirmed,
            ConfirmedAt = DateTime.UtcNow,
            ConfirmedByUserId = userId,
            Notes = input.Notes?.Trim()
        };

        foreach (var exp in input.Expenses)
        {
            settlement.Items.Add(new MatchSettlementItem
            {
                Category = exp.Category,
                Description = exp.Description.Trim(),
                Amount = exp.Amount,
                IsFromConfirmedService = exp.IsFromConfirmedService,
                SourceReferenceId = exp.SourceReferenceId
            });
        }

        foreach (var alloc in finalAllocations)
        {
            settlement.Allocations.Add(alloc);
        }

        _db.MatchSettlements.Add(settlement);
        await _db.SaveChangesAsync();

        return (true, "Đã hoàn tất và lưu biên bản quyết toán trận đấu thành công!", settlement.Id);
    }

    public async Task<SettlementDetailViewModel?> GetSettlementDetailAsync(Guid matchId, Guid teamId, string? userId)
    {
        var settlement = await _db.MatchSettlements
            .Include(s => s.Match)
                .ThenInclude(m => m.TeamA)
            .Include(s => s.Match)
                .ThenInclude(m => m.TeamB)
            .Include(s => s.Items)
            .Include(s => s.Allocations)
                .ThenInclude(a => a.Player)
            .Include(s => s.Allocations)
                .ThenInclude(a => a.Payments)
            .Include(s => s.Allocations)
                .ThenInclude(a => a.AdjustmentAudits)
            .FirstOrDefaultAsync(s => s.MatchId == matchId && s.TeamId == teamId);

        if (settlement == null) return null;

        bool isManager = userId != null && await _auth.HasManagerAccessAsync(teamId, userId);
        bool isMember = userId != null && await _auth.HasMemberAccessAsync(teamId, userId);

        var match = settlement.Match;
        var opponentName = match.Category == MatchCategory.Internal
            ? "Đá nội bộ"
            : (match.TeamAId == teamId ? (match.TeamB?.Name ?? match.TeamBExternalName ?? "Đối thủ ngoài") : match.TeamA.Name);

        // Lấy tên người xác nhận quyết toán
        string confirmedByName = "Ban Quản lý";
        if (!string.IsNullOrEmpty(settlement.ConfirmedByUserId))
        {
            var confUser = await _db.Users.FindAsync(settlement.ConfirmedByUserId);
            if (confUser != null) confirmedByName = confUser.FullName;
        }

        var itemsDto = settlement.Items.Select(i => new SettlementItemDto
        {
            Id = i.Id,
            Category = i.Category,
            CategoryName = i.Category switch
            {
                SettlementExpenseCategory.Pitch => "Sân bóng",
                SettlementExpenseCategory.Referee => "Trọng tài",
                SettlementExpenseCategory.Refreshment => "Nước uống",
                SettlementExpenseCategory.Media => "Quay phim / Ảnh",
                _ => "Chi phí khác"
            },
            Description = i.Description,
            Amount = i.Amount,
            IsFromConfirmedService = i.IsFromConfirmedService
        }).ToList();

        // Xử lý phân quyền xem dữ liệu:
        // "Members can only see their own payment information. Managers can see their team's complete records."
        var rawAllocations = settlement.Allocations.AsEnumerable();
        if (!isManager && isMember && userId != null)
        {
            rawAllocations = rawAllocations.Where(a => a.UserId == userId || a.Player.LinkedUserId == userId);
        }

        var allocationsDto = rawAllocations.Select(a => new MemberAllocationDto
        {
            AllocationId = a.Id,
            PlayerId = a.PlayerId,
            FullName = a.Player.FullName,
            KitNumber = a.Player.KitNumber,
            Position = a.Player.Position,
            UserId = a.UserId,
            CalculatedAmount = a.CalculatedAmount,
            AdjustedAmount = a.AdjustedAmount,
            AdjustmentReason = a.AdjustmentReason,
            AmountPaid = a.Payments.Where(p => !p.IsReversed).Sum(p => p.Amount),
            PaymentStatus = a.PaymentStatus,
            LastPaymentDate = a.LastPaymentDate,
            IsSelf = userId != null && (a.UserId == userId || a.Player.LinkedUserId == userId),
            Payments = a.Payments.OrderByDescending(p => p.CreatedAt).Select(p => new PaymentRecordDto
            {
                PaymentId = p.Id,
                Amount = p.Amount,
                PaymentDate = p.PaymentDate,
                PaymentMethod = p.PaymentMethod,
                PaymentMethodName = p.PaymentMethod switch
                {
                    PaymentMethod.Cash => "Tiền mặt",
                    PaymentMethod.BankTransfer => "Chuyển khoản",
                    _ => "Khác"
                },
                ReferenceCode = p.ReferenceCode,
                Note = p.Note,
                RecordedByName = "Quản lý",
                CreatedAt = p.CreatedAt,
                IsReversed = p.IsReversed,
                ReversalReason = p.ReversalReason,
                ReversedAt = p.ReversedAt
            }).ToList(),
            AdjustmentAudits = a.AdjustmentAudits.OrderByDescending(aud => aud.AdjustedAt).Select(aud => new AdjustmentAuditDto
            {
                Id = aud.Id,
                PreviousAmount = aud.PreviousAmount,
                NewAmount = aud.NewAmount,
                MandatoryReason = aud.MandatoryReason,
                AdjustedByName = "Quản lý",
                AdjustedAt = aud.AdjustedAt
            }).ToList()
        }).OrderBy(a => a.KitNumber ?? 999).ToList();

        decimal totalPaidAll = settlement.Allocations.SelectMany(a => a.Payments).Where(p => !p.IsReversed).Sum(p => p.Amount);

        return new SettlementDetailViewModel
        {
            SettlementId = settlement.Id,
            MatchId = matchId,
            TeamId = teamId,
            TeamName = match.TeamAId == teamId ? match.TeamA.Name : (match.TeamB?.Name ?? "CLB"),
            TeamSlug = match.TeamAId == teamId ? match.TeamA.Slug : (match.TeamB?.Slug ?? ""),
            OpponentName = opponentName,
            MatchDateTime = match.MatchDateTime,
            PitchName = match.PitchName,
            TotalAmount = settlement.TotalAmount,
            TotalPitchCost = settlement.TotalPitchCost,
            TotalRefereeCost = settlement.TotalRefereeCost,
            TotalOtherExpenses = settlement.TotalOtherExpenses,
            TotalPaid = totalPaidAll,
            ParticipantCount = settlement.ParticipantCount,
            PerPersonShare = settlement.PerPersonShare,
            Status = settlement.Status,
            ConfirmedAt = settlement.ConfirmedAt,
            ConfirmedByName = confirmedByName,
            Notes = settlement.Notes,
            Items = itemsDto,
            Allocations = allocationsDto,
            IsManager = isManager,
            CurrentUserId = userId
        };
    }

    // =========================================================================
    // 3. PAYMENT TRACKING & REVERSALS (THEO DÕI NỘP TIỀN & ĐẢO KHOẢN THU)
    // =========================================================================

    public async Task<(bool Success, string Message)> RecordPaymentAsync(RecordPaymentInput input, string userId)
    {
        var allocation = await _db.MatchSettlementMemberAllocations
            .Include(a => a.Settlement)
            .Include(a => a.Payments)
            .Include(a => a.Player)
            .FirstOrDefaultAsync(a => a.Id == input.AllocationId);

        if (allocation == null)
            return (false, "Không tìm thấy thông tin phân bổ thanh toán.");

        if (!await _auth.HasManagerAccessAsync(allocation.Settlement.TeamId, userId))
            return (false, "Chỉ Quản lý hoặc Chủ sở hữu đội mới có quyền ghi nhận khoản nộp tiền.");

        if (input.Amount <= 0)
            return (false, "Số tiền nộp phải lớn hơn 0 VNĐ.");

        decimal currentPaid = allocation.Payments.Where(p => !p.IsReversed).Sum(p => p.Amount);
        decimal remaining = allocation.AdjustedAmount - currentPaid;

        if (input.Amount > remaining)
            return (false, $"Số tiền nộp ({input.Amount:N0} đ) vượt quá số dư còn thiếu ({remaining:N0} đ).");

        var payment = new MatchSettlementPayment
        {
            AllocationId = allocation.Id,
            Amount = input.Amount,
            PaymentDate = input.PaymentDate,
            PaymentMethod = input.PaymentMethod,
            ReferenceCode = input.ReferenceCode?.Trim(),
            Note = input.Note?.Trim(),
            RecordedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        _db.MatchSettlementPayments.Add(payment);

        // Cập nhật trạng thái phân bổ
        decimal newTotalPaid = currentPaid + input.Amount;
        allocation.AmountPaid = newTotalPaid;
        allocation.LastPaymentDate = input.PaymentDate;
        if (newTotalPaid >= allocation.AdjustedAmount)
        {
            allocation.PaymentStatus = PaymentStatus.Paid;
        }
        else
        {
            allocation.PaymentStatus = PaymentStatus.PartiallyPaid;
        }

        await _db.SaveChangesAsync();

        return (true, $"Đã ghi nhận thanh toán {input.Amount:N0} đ cho cầu thủ {allocation.Player.FullName} thành công.");
    }

    public async Task<(bool Success, string Message)> ReversePaymentAsync(Guid paymentId, Guid teamId, string reason, string userId)
    {
        if (!await _auth.HasManagerAccessAsync(teamId, userId))
            return (false, "Chỉ Quản lý mới có quyền đảo khoản thanh toán.");

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            return (false, "Bắt buộc phải nhập lý do đảo/hủy khoản thanh toán (tối thiểu 5 ký tự).");

        var payment = await _db.MatchSettlementPayments
            .Include(p => p.Allocation)
                .ThenInclude(a => a.Payments)
            .Include(p => p.Allocation)
                .ThenInclude(a => a.Settlement)
            .FirstOrDefaultAsync(p => p.Id == paymentId);

        if (payment == null)
            return (false, "Bản ghi thanh toán không tồn tại.");

        if (payment.Allocation.Settlement.TeamId != teamId)
            return (false, "Khoản thanh toán không thuộc đội bóng này.");

        if (payment.IsReversed)
            return (false, "Khoản thanh toán này đã được hủy/đảo trước đó.");

        payment.IsReversed = true;
        payment.ReversalReason = reason.Trim();
        payment.ReversedAt = DateTime.UtcNow;
        payment.ReversedByUserId = userId;

        // Cập nhật lại số tiền đã đóng trên allocation
        var remainingPayments = payment.Allocation.Payments.Where(p => p.Id != paymentId && !p.IsReversed).ToList();
        decimal newPaid = remainingPayments.Sum(p => p.Amount);
        payment.Allocation.AmountPaid = newPaid;

        if (newPaid >= payment.Allocation.AdjustedAmount)
        {
            payment.Allocation.PaymentStatus = PaymentStatus.Paid;
        }
        else if (newPaid > 0)
        {
            payment.Allocation.PaymentStatus = PaymentStatus.PartiallyPaid;
        }
        else
        {
            payment.Allocation.PaymentStatus = PaymentStatus.Unpaid;
        }

        await _db.SaveChangesAsync();

        return (true, "Đã đảo/hủy khoản thanh toán thành công và lưu vết kiểm toán.");
    }

    public async Task<(bool Success, string Message)> AdjustAllocationAsync(ManualAdjustmentInput input, string userId)
    {
        var allocation = await _db.MatchSettlementMemberAllocations
            .Include(a => a.Settlement)
                .ThenInclude(s => s.Allocations)
            .Include(a => a.Payments)
            .Include(a => a.Player)
            .FirstOrDefaultAsync(a => a.Id == input.AllocationId);

        if (allocation == null)
            return (false, "Không tìm thấy phân bổ chi phí.");

        if (!await _auth.HasManagerAccessAsync(allocation.Settlement.TeamId, userId))
            return (false, "Chỉ Quản lý mới có quyền điều chỉnh số tiền phân bổ.");

        if (string.IsNullOrWhiteSpace(input.MandatoryReason) || input.MandatoryReason.Trim().Length < 5)
            return (false, "Bắt buộc phải ghi rõ lý do điều chỉnh số tiền (tối thiểu 5 ký tự).");

        decimal previousAmount = allocation.AdjustedAmount;
        if (previousAmount == input.NewAmount)
            return (false, "Số tiền mới trùng với số tiền hiện tại.");

        // Ghi nhận Audit Log
        var audit = new MatchSettlementAdjustmentAudit
        {
            AllocationId = allocation.Id,
            PreviousAmount = previousAmount,
            NewAmount = input.NewAmount,
            MandatoryReason = input.MandatoryReason.Trim(),
            AdjustedByUserId = userId,
            AdjustedAt = DateTime.UtcNow
        };
        _db.MatchSettlementAdjustmentAudits.Add(audit);

        allocation.AdjustedAmount = input.NewAmount;
        allocation.AdjustmentReason = input.MandatoryReason.Trim();

        // Cập nhật lại trạng thái nộp tiền
        decimal paid = allocation.Payments.Where(p => !p.IsReversed).Sum(p => p.Amount);
        if (paid >= input.NewAmount) allocation.PaymentStatus = PaymentStatus.Paid;
        else if (paid > 0) allocation.PaymentStatus = PaymentStatus.PartiallyPaid;
        else allocation.PaymentStatus = PaymentStatus.Unpaid;

        // Đồng bộ tổng tiền quyết toán
        allocation.Settlement.TotalAmount = allocation.Settlement.Allocations.Sum(a => a.Id == allocation.Id ? input.NewAmount : a.AdjustedAmount);

        await _db.SaveChangesAsync();

        return (true, $"Đã cập nhật số tiền phải nộp của {allocation.Player.FullName} thành {input.NewAmount:N0} đ.");
    }

    public async Task<(bool Success, string Message)> SendPaymentReminderAsync(Guid allocationId, Guid teamId, string userId)
    {
        if (!await _auth.HasManagerAccessAsync(teamId, userId))
            return (false, "Bạn không có quyền gửi nhắc nhở thanh toán.");

        var allocation = await _db.MatchSettlementMemberAllocations
            .Include(a => a.Settlement)
                .ThenInclude(s => s.Match)
            .Include(a => a.Player)
            .FirstOrDefaultAsync(a => a.Id == allocationId);

        if (allocation == null)
            return (false, "Không tìm thấy phân bổ thanh toán.");

        if (allocation.PaymentStatus == PaymentStatus.Paid)
            return (false, "Cầu thủ này đã hoàn tất thanh toán.");

        if (string.IsNullOrEmpty(allocation.Player.LinkedUserId))
            return (false, "Cầu thủ này chưa liên kết tài khoản hệ thống AloBall để nhận tin nhắn.");

        decimal remaining = allocation.RemainingBalance;
        var match = allocation.Settlement.Match;

        if (_messaging != null)
        {
            string title = $"[Nhắc thanh toán tiền trận] vs {match.PitchName}";
            string content = $"⚽ THÔNG BÁO QUYẾT TOÁN TRẬN ĐẤU\n" +
                             $"- Cầu thủ: {allocation.Player.FullName}\n" +
                             $"- Trận đấu: {match.MatchDateTime:dd/MM/yyyy} tại {match.PitchName}\n" +
                             $"- Số tiền cần nộp: {allocation.AdjustedAmount:N0} đ\n" +
                             $"- Đã nộp: {allocation.AmountPaid:N0} đ\n" +
                             $"- Còn thiếu: {remaining:N0} đ\n" +
                             $"\nVui lòng chuyển khoản hoặc nộp tiền mặt cho Ban Quản trị đội bóng để hoàn tất quỹ trận!";

            await _messaging.SendLeadershipToMemberMessageAsync(
                teamId,
                userId,
                allocation.Player.LinkedUserId,
                title,
                content);
        }

        return (true, $"Đã gửi thông báo nhắc thanh toán tới {allocation.Player.FullName}.");
    }

    // =========================================================================
    // 4. TEAM FINANCE REPORT & CSV EXPORT (BÁO CÁO TÀI CHÍNH CLB)
    // =========================================================================

    public async Task<TeamFinanceReportViewModel> GetTeamFinanceReportAsync(Guid teamId, string? monthFilter, string userId)
    {
        var team = await _db.Teams.FindAsync(teamId);
        if (team == null) throw new ArgumentException("Đội bóng không tồn tại.");

        bool isManager = await _auth.HasManagerAccessAsync(teamId, userId);

        var query = _db.MatchSettlements
            .Include(s => s.Match)
                .ThenInclude(m => m.TeamA)
            .Include(s => s.Match)
                .ThenInclude(m => m.TeamB)
            .Include(s => s.Allocations)
                .ThenInclude(a => a.Player)
            .Include(s => s.Allocations)
                .ThenInclude(a => a.Payments)
            .Where(s => s.TeamId == teamId);

        var allSettlements = await query.OrderByDescending(s => s.SettlementDate).ToListAsync();

        // Danh sách các tháng có phát sinh quyết toán (YYYY-MM)
        var availableMonths = allSettlements
            .Select(s => s.SettlementDate.ToString("yyyy-MM"))
            .Distinct()
            .OrderByDescending(m => m)
            .ToList();

        // Áp dụng bộ lọc tháng nếu có
        var filteredSettlements = allSettlements;
        if (!string.IsNullOrEmpty(monthFilter))
        {
            filteredSettlements = allSettlements
                .Where(s => s.SettlementDate.ToString("yyyy-MM") == monthFilter)
                .ToList();
        }

        decimal totalExpenses = filteredSettlements.Sum(s => s.TotalAmount);
        decimal totalDue = filteredSettlements.SelectMany(s => s.Allocations).Sum(a => a.AdjustedAmount);
        decimal totalCollected = filteredSettlements.SelectMany(s => s.Allocations)
            .SelectMany(a => a.Payments)
            .Where(p => !p.IsReversed)
            .Sum(p => p.Amount);

        // Tổng kết từng tháng
        var monthlySummaries = allSettlements
            .GroupBy(s => s.SettlementDate.ToString("yyyy-MM"))
            .Select(g =>
            {
                decimal exp = g.Sum(s => s.TotalAmount);
                decimal coll = g.SelectMany(s => s.Allocations).SelectMany(a => a.Payments).Where(p => !p.IsReversed).Sum(p => p.Amount);
                decimal due = g.SelectMany(s => s.Allocations).Sum(a => a.AdjustedAmount);
                return new MonthlyFinanceSummaryDto
                {
                    MonthKey = g.Key,
                    MonthDisplay = $"Tháng {g.Key[5..]}/{g.Key[..4]}",
                    MatchesCount = g.Count(),
                    TotalExpenses = exp,
                    TotalCollected = coll,
                    TotalOutstanding = Math.Max(0m, due - coll)
                };
            })
            .OrderByDescending(m => m.MonthKey)
            .ToList();

        // Lịch sử từng trận quyết toán
        var matchHistory = filteredSettlements.Select(s =>
        {
            var match = s.Match;
            var oppName = match.Category == MatchCategory.Internal
                ? "Đá nội bộ"
                : (match.TeamAId == teamId ? (match.TeamB?.Name ?? match.TeamBExternalName ?? "Đối thủ ngoài") : match.TeamA.Name);
            decimal paid = s.Allocations.SelectMany(a => a.Payments).Where(p => !p.IsReversed).Sum(p => p.Amount);

            return new MatchSettlementSummaryDto
            {
                SettlementId = s.Id,
                MatchId = s.MatchId,
                MatchDateTime = match.MatchDateTime,
                OpponentName = oppName,
                PitchName = match.PitchName,
                TotalAmount = s.TotalAmount,
                TotalPaid = paid,
                ParticipantCount = s.ParticipantCount,
                PerPersonShare = s.PerPersonShare,
                Status = s.Status
            };
        }).ToList();

        // Báo cáo theo từng cá nhân cầu thủ
        var memberRecords = filteredSettlements.SelectMany(s => s.Allocations)
            .GroupBy(a => a.PlayerId)
            .Select(g =>
            {
                var first = g.First();
                decimal due = g.Sum(a => a.AdjustedAmount);
                decimal paid = g.SelectMany(a => a.Payments).Where(p => !p.IsReversed).Sum(p => p.Amount);
                return new MemberFinanceRecordDto
                {
                    PlayerId = g.Key,
                    FullName = first.Player.FullName,
                    KitNumber = first.Player.KitNumber,
                    Position = first.Player.Position,
                    MatchesParticipated = g.Count(),
                    TotalAmountDue = due,
                    TotalAmountPaid = paid,
                    UnpaidCount = g.Count(a => a.PaymentStatus == PaymentStatus.Unpaid),
                    PartialCount = g.Count(a => a.PaymentStatus == PaymentStatus.PartiallyPaid),
                    FullyPaidCount = g.Count(a => a.PaymentStatus == PaymentStatus.Paid)
                };
            })
            .OrderByDescending(m => m.OutstandingBalance)
            .ThenBy(m => m.KitNumber ?? 999)
            .ToList();

        return new TeamFinanceReportViewModel
        {
            TeamId = teamId,
            TeamName = team.Name,
            MonthFilter = monthFilter,
            AvailableMonths = availableMonths,
            TotalMatchExpenses = totalExpenses,
            TotalMemberContributionsDue = totalDue,
            TotalCollected = totalCollected,
            TotalSettledMatches = filteredSettlements.Count,
            MonthlySummaries = monthlySummaries,
            MatchHistory = matchHistory,
            MemberRecords = memberRecords,
            IsManager = isManager
        };
    }

    public async Task<byte[]> ExportTeamFinanceReportCsvAsync(Guid teamId, string? monthFilter, string userId)
    {
        var report = await GetTeamFinanceReportAsync(teamId, monthFilter, userId);

        var sb = new StringBuilder();
        // UTF-8 BOM
        sb.Append('\uFEFF');

        sb.AppendLine($"BÁO CÁO TÀI CHÍNH & QUYẾT TOÁN TRẬN ĐẤU - CLB {report.TeamName.ToUpperInvariant()}");
        sb.AppendLine($"Kỳ báo cáo: {(string.IsNullOrEmpty(monthFilter) ? "Toàn bộ thời gian" : monthFilter)}");
        sb.AppendLine($"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm}");
        sb.AppendLine();

        sb.AppendLine("1. TỔNG QUAN TÀI CHÍNH");
        sb.AppendLine("Chỉ số,Số tiền (VNĐ)");
        sb.AppendLine($"Tổng chi phí các trận,{report.TotalMatchExpenses:0}");
        sb.AppendLine($"Tổng tiền thành viên phải nộp,{report.TotalMemberContributionsDue:0}");
        sb.AppendLine($"Tổng tiền đã thu thực tế,{report.TotalCollected:0}");
        sb.AppendLine($"Tổng tiền còn tồn đọng,{report.TotalOutstanding:0}");
        sb.AppendLine($"Tỉ lệ thu hồi quỹ,{report.CollectionRatePercentage:0.0}%");
        sb.AppendLine();

        sb.AppendLine("2. CHI TIẾT THEO CẦU THỦ");
        sb.AppendLine("Số áo,Họ và tên,Vị trí,Số trận tham gia,Phải nộp (VNĐ),Đã nộp (VNĐ),Còn thiếu (VNĐ),Chưa nộp (trận),Đã xong (trận)");
        foreach (var m in report.MemberRecords)
        {
            sb.AppendLine($"\"{m.KitNumber?.ToString() ?? ""}\",\"{m.FullName}\",\"{m.Position ?? ""}\",{m.MatchesParticipated},{m.TotalAmountDue:0},{m.TotalAmountPaid:0},{m.OutstandingBalance:0},{m.UnpaidCount},{m.FullyPaidCount}");
        }
        sb.AppendLine();

        sb.AppendLine("3. LỊCH SỬ QUYẾT TOÁN CÁC TRẬN ĐẤU");
        sb.AppendLine("Ngày thi đấu,Đối thủ,Sân bóng,Tổng chi phí (VNĐ),Số người chia,Mỗi người (VNĐ),Đã thu (VNĐ),Còn thiếu (VNĐ),Tỉ lệ thu");
        foreach (var m in report.MatchHistory)
        {
            sb.AppendLine($"\"{m.MatchDateTime:dd/MM/yyyy}\",\"{m.OpponentName}\",\"{m.PitchName}\",{m.TotalAmount:0},{m.ParticipantCount},{m.PerPersonShare:0},{m.TotalPaid:0},{m.RemainingBalance:0},{m.CollectionRate:0.0}%");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
