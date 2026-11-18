using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Services;

public record BankOption(string Code, string Name);

public interface IMatchSplitBillService
{
    Task<MatchSplitBillPanelViewModel?> BuildMatchSplitBillPanelAsync(MatchGame match, Guid teamId, string baseMatchUrl, AttendanceSession? attendanceSession = null);
    Task<(bool Success, string Message, MatchExpense? Expense)> SaveMatchSplitBillAsync(MatchSplitBillInput input, string managerUserId);
    Task<(bool Success, string Message, ExpenseSharePaymentStatus? NewStatus, DateTime? PaidAt)> ToggleSharePaymentAsync(Guid matchId, Guid shareId, string managerUserId);
    Task<(bool Success, string Message)> SaveClubBankInfoAsync(Guid teamId, string? bankCode, string? accountNumber, string? accountHolderName, string managerUserId);
}

public class MatchSplitBillService : IMatchSplitBillService
{
    public static readonly IReadOnlyList<BankOption> SupportedBanks = new List<BankOption>
    {
        new("VCB", "Vietcombank (VCB)"),
        new("TCB", "Techcombank (TCB)"),
        new("MBBank", "MB Bank (MBBank)"),
        new("BIDV", "BIDV"),
        new("ACB", "ACB"),
        new("TPBank", "TPBank"),
        new("VPBank", "VPBank"),
        new("VietinBank", "VietinBank"),
        new("Sacombank", "Sacombank"),
        new("VIB", "VIB"),
        new("MSB", "MSB"),
        new("HDBank", "HDBank"),
        new("OCB", "OCB")
    };

    private readonly ApplicationDbContext _db;
    private readonly ITeamAuthorizationService _auth;

    public MatchSplitBillService(ApplicationDbContext db, ITeamAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public static decimal GetTeamShareMultiplier(string? splitRatio) => splitRatio?.Trim() switch
    {
        "50-50" => 0.50m,
        "60-40" => 0.60m,
        "40-60" => 0.40m,
        "70-30" => 0.70m,
        "30-70" => 0.30m,
        _ => 1.00m
    };

    public static string NormalizeSplitRatio(string? splitRatio) => splitRatio?.Trim() switch
    {
        "50-50" or "60-40" or "40-60" or "70-30" or "30-70" => splitRatio.Trim(),
        _ => "100"
    };

    public static string GetSplitRatioLabel(string? splitRatio) => splitRatio?.Trim() switch
    {
        "50-50" => "Chia đôi 50-50 (Đội trả 50%)",
        "60-40" => "Kèo 60-40 (Đội trả 60%)",
        "40-60" => "Kèo 60-40 (Đội trả 40%)",
        "70-30" => "Kèo 70-30 (Đội trả 70%)",
        "30-70" => "Kèo 70-30 (Đội trả 30%)",
        _ => "100% đội chịu"
    };

    public static decimal RoundToNearest1000(decimal amount)
    {
        if (amount <= 0m) return 0m;
        return Math.Round(amount / 1000m, 0, MidpointRounding.AwayFromZero) * 1000m;
    }

    public static string StripVietnameseDiacritics(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var decomposed = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c == 'đ') sb.Append('d');
            else if (c == 'Đ') sb.Append('D');
            else sb.Append(c);
        }
        var ascii = Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC), @"[^a-zA-Z0-9\s]", " ");
        return Regex.Replace(ascii, @"\s+", " ").Trim();
    }

    public static string BuildTransferNote(Guid matchId, string playerName)
    {
        var shortMatch = matchId.ToString("N")[..6].ToUpperInvariant();
        var cleanName = StripVietnameseDiacritics(playerName).ToUpperInvariant();
        var note = string.IsNullOrEmpty(cleanName) ? $"ALOBALL M{shortMatch}" : $"ALOBALL M{shortMatch} {cleanName}";
        return note.Length > 50 ? note[..50].Trim() : note;
    }

    public static string? BuildVietQrUrl(string? bankCode, string? accountNumber, string? accountHolderName, decimal amount, Guid matchId, string playerName)
    {
        if (string.IsNullOrWhiteSpace(bankCode) || string.IsNullOrWhiteSpace(accountNumber) || amount <= 0m)
            return null;

        var cleanBank = bankCode.Trim();
        var cleanAcc = Regex.Replace(accountNumber.Trim(), @"\s+", "");
        if (string.IsNullOrEmpty(cleanAcc)) return null;

        var cleanHolder = string.IsNullOrWhiteSpace(accountHolderName)
            ? string.Empty
            : StripVietnameseDiacritics(accountHolderName).ToUpperInvariant();
        var amountInt = (long)Math.Round(amount, 0, MidpointRounding.AwayFromZero);
        var note = BuildTransferNote(matchId, playerName);

        return $"https://img.vietqr.io/image/{Uri.EscapeDataString(cleanBank)}-{Uri.EscapeDataString(cleanAcc)}-compact2.png" +
               $"?amount={amountInt}&addInfo={Uri.EscapeDataString(note)}&accountName={Uri.EscapeDataString(cleanHolder)}";
    }

    public async Task<(bool Success, string Message)> SaveClubBankInfoAsync(
        Guid teamId, string? bankCode, string? accountNumber, string? accountHolderName, string managerUserId)
    {
        if (!await _auth.HasManagerAccessAsync(teamId, managerUserId))
            return (false, "Chỉ Đội trưởng / Quản lý mới được cấu hình tài khoản ngân hàng của CLB.");

        var team = await _db.Teams.FindAsync(teamId);
        if (team == null) return (false, "Đội bóng không tồn tại.");

        var cleanBank = bankCode?.Trim();
        var cleanAcc = string.IsNullOrWhiteSpace(accountNumber) ? null : Regex.Replace(accountNumber.Trim(), @"\s+", "");
        var cleanHolder = string.IsNullOrWhiteSpace(accountHolderName) ? null : accountHolderName.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(cleanBank) || string.IsNullOrWhiteSpace(cleanAcc))
            return (false, "Vui lòng chọn ngân hàng và nhập số tài khoản hợp lệ.");

        if (!Regex.IsMatch(cleanAcc, @"^[a-zA-Z0-9]{4,35}$"))
            return (false, "Số tài khoản chỉ gồm chữ hoặc số (từ 4 đến 35 ký tự).");

        team.BankCode = cleanBank;
        team.AccountNumber = cleanAcc;
        team.AccountHolderName = cleanHolder;
        await _db.SaveChangesAsync();

        return (true, $"Đã lưu tài khoản nhận tiền ({cleanBank} - {cleanAcc}) cho {team.Name}.");
    }

    public async Task<(bool Success, string Message, MatchExpense? Expense)> SaveMatchSplitBillAsync(MatchSplitBillInput input, string managerUserId)
    {
        if (!await _auth.HasManagerAccessAsync(input.TeamId, managerUserId))
            return (false, "Chỉ Đội trưởng / Thủ quỹ mới được phép lưu bảng chia tiền sân.", null);

        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == input.MatchId);
        if (match == null)
            return (false, "Trận đấu không tồn tại.", null);

        if (match.TeamAId != input.TeamId && match.TeamBId != input.TeamId)
            return (false, "Đội bóng không thuộc trận đấu này.", null);

        if (input.PitchFee < 0 || input.WaterFee < 0 || input.RefereeFee < 0 || input.Tang2Fee < 0)
            return (false, "Các khoản chi phí không được mang giá trị âm.", null);

        var matchSet = input.MatchParticipantPlayerIds.Distinct().ToHashSet();
        var tang2Set = input.Tang2ParticipantPlayerIds.Distinct().ToHashSet();
        var allSelectedIds = matchSet.Union(tang2Set).ToHashSet();

        if (!allSelectedIds.Any())
            return (false, "Vui lòng chọn ít nhất 1 cầu thủ tham gia để chia tiền.", null);

        var teamPlayers = await _db.Players
            .Where(p => p.TeamId == input.TeamId && allSelectedIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        if (teamPlayers.Count != allSelectedIds.Count)
            return (false, "Danh sách có cầu thủ không thuộc CLB.", null);

        var splitRatio = NormalizeSplitRatio(input.SplitRatio);
        var multiplier = GetTeamShareMultiplier(splitRatio);
        var stadiumBill = input.PitchFee + input.WaterFee + input.RefereeFee;
        var matchTotalPayable = Math.Round(stadiumBill * multiplier, 0, MidpointRounding.AwayFromZero);

        if (matchTotalPayable > 0 && matchSet.Count == 0)
            return (false, "Có chi phí sân/nước/trọng tài nên cần chọn ít nhất 1 người đá sân (Tăng 1).", null);

        if (input.Tang2Fee > 0 && tang2Set.Count == 0)
            return (false, "Đã nhập tiền Tăng 2, vui lòng chọn ít nhất 1 người tham gia Tăng 2.", null);

        var matchPerPerson = matchSet.Count > 0 ? RoundToNearest1000(matchTotalPayable / matchSet.Count) : 0m;
        var tang2PerPerson = (input.Tang2Fee > 0 && tang2Set.Count > 0) ? RoundToNearest1000(input.Tang2Fee / tang2Set.Count) : 0m;

        var expense = await _db.MatchExpenses
            .Include(e => e.Shares)
            .FirstOrDefaultAsync(e => e.MatchId == input.MatchId && e.TeamId == input.TeamId);

        var previousSharesByPlayer = expense?.Shares.ToDictionary(s => s.PlayerId) ?? new Dictionary<Guid, MatchExpenseShare>();

        if (expense == null)
        {
            expense = new MatchExpense
            {
                MatchId = input.MatchId,
                TeamId = input.TeamId,
                CreatedByUserId = managerUserId,
                CreatedAt = DateTime.UtcNow
            };
            _db.MatchExpenses.Add(expense);
        }

        expense.PitchFee = input.PitchFee;
        expense.WaterFee = input.WaterFee;
        expense.RefereeFee = input.RefereeFee;
        expense.SplitRatio = splitRatio;
        expense.Tang2Fee = input.Tang2Fee;
        expense.MatchTotalPayable = matchTotalPayable;
        expense.MatchPerPersonShare = matchPerPerson;
        expense.Tang2PerPersonShare = tang2PerPerson;
        expense.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        expense.UpdatedAt = DateTime.UtcNow;

        // Xóa share của những người bị bỏ tích hoàn toàn khỏi cả Tăng 1 và Tăng 2
        var toRemove = expense.Shares.Where(s => !allSelectedIds.Contains(s.PlayerId)).ToList();
        foreach (var rem in toRemove)
        {
            _db.MatchExpenseShares.Remove(rem);
        }

        foreach (var playerId in allSelectedIds)
        {
            var player = teamPlayers[playerId];
            bool inMatch = matchSet.Contains(playerId);
            bool inTang2 = tang2Set.Contains(playerId);
            decimal matchAmt = inMatch ? matchPerPerson : 0m;
            decimal tang2Amt = inTang2 ? tang2PerPerson : 0m;
            decimal totalAmt = matchAmt + tang2Amt;

            if (previousSharesByPlayer.TryGetValue(playerId, out var existing))
            {
                bool amountChanged = existing.TotalAmount != totalAmt;
                existing.LinkedUserId = player.LinkedUserId;
                existing.ParticipatesInMatch = inMatch;
                existing.ParticipatesInTang2 = inTang2;
                existing.MatchAmount = matchAmt;
                existing.Tang2Amount = tang2Amt;
                existing.TotalAmount = totalAmt;

                // Giữ nguyên trạng thái Đã thu nếu số tiền không thay đổi
                if (amountChanged)
                {
                    existing.PaymentStatus = ExpenseSharePaymentStatus.Unpaid;
                    existing.PaidAt = null;
                    existing.MarkedByUserId = null;
                }
            }
            else
            {
                var share = new MatchExpenseShare
                {
                    MatchExpenseId = expense.Id,
                    PlayerId = playerId,
                    LinkedUserId = player.LinkedUserId,
                    ParticipatesInMatch = inMatch,
                    ParticipatesInTang2 = inTang2,
                    MatchAmount = matchAmt,
                    Tang2Amount = tang2Amt,
                    TotalAmount = totalAmt,
                    PaymentStatus = ExpenseSharePaymentStatus.Unpaid
                };
                expense.Shares.Add(share);
                _db.MatchExpenseShares.Add(share);
            }
        }

        await _db.SaveChangesAsync();
        return (true, $"Đã chia tiền thành công ({matchPerPerson:N0}đ/người đá sân{(tang2PerPerson > 0 ? $", +{tang2PerPerson:N0}đ Tăng 2" : "")}).", expense);
    }

    public async Task<(bool Success, string Message, ExpenseSharePaymentStatus? NewStatus, DateTime? PaidAt)> ToggleSharePaymentAsync(
        Guid matchId, Guid shareId, string managerUserId)
    {
        var share = await _db.MatchExpenseShares
            .Include(s => s.MatchExpense)
            .Include(s => s.Player)
            .FirstOrDefaultAsync(s => s.Id == shareId && s.MatchExpense.MatchId == matchId);

        if (share == null)
            return (false, "Không tìm thấy khoản chia tiền của cầu thủ.", null, null);

        if (!await _auth.HasManagerAccessAsync(share.MatchExpense.TeamId, managerUserId))
            return (false, "Chỉ Đội trưởng / Quản lý mới được xác nhận Đã thu tiền.", null, null);

        if (share.PaymentStatus == ExpenseSharePaymentStatus.Unpaid)
        {
            share.PaymentStatus = ExpenseSharePaymentStatus.Paid;
            share.PaidAt = DateTime.UtcNow;
            share.MarkedByUserId = managerUserId;
        }
        else
        {
            share.PaymentStatus = ExpenseSharePaymentStatus.Unpaid;
            share.PaidAt = null;
            share.MarkedByUserId = managerUserId;
        }

        await _db.SaveChangesAsync();

        var msg = share.PaymentStatus == ExpenseSharePaymentStatus.Paid
            ? $"Đã xác nhận {share.Player.FullName} đóng {share.TotalAmount:N0}đ."
            : $"Đã chuyển {share.Player.FullName} về trạng thái Chưa đóng.";

        return (true, msg, share.PaymentStatus, share.PaidAt);
    }

    public async Task<MatchSplitBillPanelViewModel?> BuildMatchSplitBillPanelAsync(
        MatchGame match, Guid teamId, string baseMatchUrl, AttendanceSession? attendanceSession = null)
    {
        var team = await _db.Teams.FindAsync(teamId);
        if (team == null) return null;

        var expense = await _db.MatchExpenses
            .Include(e => e.Shares).ThenInclude(s => s.Player)
            .FirstOrDefaultAsync(e => e.MatchId == match.Id && e.TeamId == teamId);

        var players = await _db.Players
            .Where(p => p.TeamId == teamId && p.IsActive)
            .OrderBy(p => p.KitNumber ?? 999)
            .ThenBy(p => p.FullName)
            .ToListAsync();

        // Đảm bảo hiển thị đủ cả cầu thủ trong share cũ nếu đã bị đánh dấu inactive
        if (expense != null)
        {
            var activeIds = players.Select(p => p.Id).ToHashSet();
            foreach (var s in expense.Shares)
            {
                if (!activeIds.Contains(s.PlayerId) && s.Player != null)
                    players.Add(s.Player);
            }
        }

        var attByPlayer = attendanceSession?.Records.ToDictionary(r => r.PlayerId) ?? new Dictionary<Guid, AttendanceRecord>();
        var lineupIds = match.Lineups.Where(l => l.TeamId == teamId).Select(l => l.PlayerId).ToHashSet();
        var existingSharesByPlayer = expense?.Shares.ToDictionary(s => s.PlayerId) ?? new Dictionary<Guid, MatchExpenseShare>();

        // Gợi ý phí sân & trọng tài từ booking đã chốt nếu chưa tạo MatchExpense
        decimal defaultPitchFee = 0m;
        decimal defaultRefFee = 0m;
        if (expense == null)
        {
            var pitchOffer = await _db.PitchBookingOffers
                .Where(o => o.Request.MatchId == match.Id && o.Request.TeamId == teamId
                         && o.Request.Status == PitchBookingStatus.Confirmed && o.Status == PitchBookingOfferStatus.Accepted)
                .Select(o => (decimal?)o.TotalPriceVND)
                .FirstOrDefaultAsync();
            defaultPitchFee = pitchOffer ?? 0m;

            var refProposal = await _db.RefereeProposals
                .Where(p => p.Request.MatchId == match.Id && p.Request.TeamId == teamId
                         && p.Request.Status == RefereeRequestStatus.Confirmed && p.Status == RefereeProposalStatus.Accepted)
                .Select(p => (decimal?)p.TotalPriceVND)
                .FirstOrDefaultAsync();
            defaultRefFee = refProposal ?? 0m;
        }

        var candidates = new List<MatchSplitBillCandidateRow>();
        foreach (var p in players)
        {
            attByPlayer.TryGetValue(p.Id, out var att);
            bool inMatch;
            bool inTang2 = false;
            string hint = "Trong danh sách đội";

            if (existingSharesByPlayer.TryGetValue(p.Id, out var sh))
            {
                inMatch = sh.ParticipatesInMatch;
                inTang2 = sh.ParticipatesInTang2;
            }
            else
            {
                bool isPresent = att != null && att.Actual == ActualStatus.Present;
                bool isAttending = att != null && att.Response == ResponseStatus.Attending;
                bool inLineup = lineupIds.Contains(p.Id);
                inMatch = isPresent || isAttending || inLineup;
            }

            if (att != null && att.Actual == ActualStatus.Present) hint = "Đã check-in có mặt";
            else if (att != null && att.Response == ResponseStatus.Attending) hint = "Báo có mặt";
            else if (lineupIds.Contains(p.Id)) hint = "Trong đội hình";
            else if (att != null && att.Response == ResponseStatus.Excused) hint = "Xin nghỉ";

            candidates.Add(new MatchSplitBillCandidateRow
            {
                PlayerId = p.Id,
                FullName = p.FullName,
                KitNumber = p.KitNumber,
                Position = p.Position,
                IsClaimed = !string.IsNullOrEmpty(p.LinkedUserId),
                InMatch = inMatch,
                InTang2 = inTang2,
                AttendanceHint = hint
            });
        }

        var shareRows = new List<MatchSplitBillShareRow>();
        if (expense != null)
        {
            foreach (var s in expense.Shares.OrderBy(x => x.PaymentStatus).ThenBy(x => x.Player.KitNumber ?? 999).ThenBy(x => x.Player.FullName))
            {
                var pName = s.Player?.FullName ?? "Cầu thủ";
                shareRows.Add(new MatchSplitBillShareRow
                {
                    ShareId = s.Id,
                    PlayerId = s.PlayerId,
                    FullName = pName,
                    KitNumber = s.Player?.KitNumber,
                    Position = s.Player?.Position,
                    IsClaimed = !string.IsNullOrEmpty(s.Player?.LinkedUserId ?? s.LinkedUserId),
                    LinkedUserId = s.Player?.LinkedUserId ?? s.LinkedUserId,
                    ParticipatesInMatch = s.ParticipatesInMatch,
                    ParticipatesInTang2 = s.ParticipatesInTang2,
                    MatchAmount = s.MatchAmount,
                    Tang2Amount = s.Tang2Amount,
                    TotalAmount = s.TotalAmount,
                    PaymentStatus = s.PaymentStatus,
                    PaidAt = s.PaidAt,
                    VietQrUrl = BuildVietQrUrl(team.BankCode, team.AccountNumber, team.AccountHolderName, s.TotalAmount, match.Id, pName),
                    TransferNote = BuildTransferNote(match.Id, pName)
                });
            }
        }

        var vm = new MatchSplitBillPanelViewModel
        {
            MatchId = match.Id,
            TeamId = teamId,
            TeamName = team.Name,
            ExpenseId = expense?.Id,
            PitchFee = expense?.PitchFee ?? defaultPitchFee,
            WaterFee = expense?.WaterFee ?? 0m,
            RefereeFee = expense?.RefereeFee ?? defaultRefFee,
            SplitRatio = expense?.SplitRatio ?? "100",
            Tang2Fee = expense?.Tang2Fee ?? 0m,
            MatchTotalPayable = expense?.MatchTotalPayable ?? 0m,
            MatchPerPersonShare = expense?.MatchPerPersonShare ?? 0m,
            Tang2PerPersonShare = expense?.Tang2PerPersonShare ?? 0m,
            Notes = expense?.Notes,
            BankCode = team.BankCode,
            AccountNumber = team.AccountNumber,
            AccountHolderName = team.AccountHolderName,
            Shares = shareRows,
            Candidates = candidates
        };

        if (expense != null)
        {
            vm.ZaloCollectionText = BuildZaloCollectionMessage(match, team, expense, shareRows, baseMatchUrl);
        }

        return vm;
    }

    public static string BuildZaloCollectionMessage(
        MatchGame match, Team team, MatchExpense expense, IReadOnlyList<MatchSplitBillShareRow> shares, string matchUrl)
    {
        var sb = new StringBuilder();
        var opponent = match.Category == MatchCategory.Internal
            ? "Nội bộ"
            : (!string.IsNullOrEmpty(match.TeamBExternalName) ? match.TeamBExternalName : match.TeamB?.Name ?? "Đối thủ");

        sb.AppendLine($"💸 CHIA TIỀN SÂN & VIETQR - {team.Name.ToUpperInvariant()}");
        sb.AppendLine($"⚽ Trận: {team.Name} vs {opponent} ({match.MatchDateTime:HH:mm · dd/MM/yyyy})");
        sb.AppendLine($"🏟 Sân: {match.PitchName}");
        sb.AppendLine($"📊 Tiền sân/nước/TT ({GetSplitRatioLabel(expense.SplitRatio)}): {expense.MatchPerPersonShare:N0}đ/người");
        if (expense.Tang2Fee > 0 && expense.Tang2PerPersonShare > 0)
        {
            sb.AppendLine($"🍻 Tăng 2 hậu trận (chỉ tính người tham gia): {expense.Tang2PerPersonShare:N0}đ/người");
        }

        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(team.BankCode) && !string.IsNullOrWhiteSpace(team.AccountNumber))
        {
            sb.AppendLine($"🏦 CK: {team.BankCode} - {team.AccountNumber}" + (!string.IsNullOrWhiteSpace(team.AccountHolderName) ? $" ({team.AccountHolderName})" : ""));
            sb.AppendLine($"📝 Nội dung CK: ALOBALL M{match.Id.ToString("N")[..6].ToUpperInvariant()} [Tên bạn]");
        }
        else
        {
            sb.AppendLine("🏦 CK: Đóng trực tiếp cho Đội trưởng / Thủ quỹ");
        }

        var paid = shares.Where(s => s.PaymentStatus == ExpenseSharePaymentStatus.Paid).ToList();
        var unpaid = shares.Where(s => s.PaymentStatus == ExpenseSharePaymentStatus.Unpaid).ToList();

        sb.AppendLine();
        sb.AppendLine($"✅ ĐÃ ĐÓNG ({paid.Count}):");
        if (paid.Any())
        {
            for (int i = 0; i < paid.Count; i++)
                sb.AppendLine($"{i + 1}. {paid[i].FullName} ({paid[i].TotalAmount:N0}đ)");
        }
        else sb.AppendLine("(Chưa có)");

        sb.AppendLine();
        sb.AppendLine($"⏳ CHƯA ĐÓNG ({unpaid.Count}):");
        if (unpaid.Any())
        {
            for (int i = 0; i < unpaid.Count; i++)
            {
                var u = unpaid[i];
                var tag = u.ParticipatesInTang2 ? (u.ParticipatesInMatch ? " [Sân + Tăng 2]" : " [Chỉ Tăng 2]") : "";
                sb.AppendLine($"{i + 1}. {u.FullName} - {u.TotalAmount:N0}đ{tag}");
            }
        }
        else sb.AppendLine("🎉 Tất cả đã đóng đủ!");

        sb.AppendLine();
        sb.AppendLine("👉 Bấm link để mở đúng mã QR VietQR số tiền của bạn:");
        sb.AppendLine(matchUrl);

        return sb.ToString();
    }

    public static MatchExpense BuildPreFilledExpense(
        Guid matchId,
        Guid teamId,
        decimal pitchFee,
        decimal refereeFee,
        string splitRatio,
        string notes,
        string createdByUserId,
        IEnumerable<Player> activePlayers)
    {
        var playersList = activePlayers.ToList();
        var normRatio = NormalizeSplitRatio(splitRatio);
        decimal teamShare = Math.Round((pitchFee + refereeFee) * GetTeamShareMultiplier(normRatio), 0, MidpointRounding.AwayFromZero);
        decimal perPerson = playersList.Count > 0 ? RoundToNearest1000(teamShare / playersList.Count) : teamShare;

        var expense = new MatchExpense
        {
            MatchId = matchId,
            TeamId = teamId,
            PitchFee = pitchFee,
            WaterFee = 0m,
            RefereeFee = refereeFee,
            SplitRatio = normRatio,
            MatchTotalPayable = teamShare,
            MatchPerPersonShare = perPerson,
            Tang2PerPersonShare = 0m,
            Notes = notes,
            CreatedByUserId = createdByUserId
        };

        foreach (var p in playersList)
        {
            expense.Shares.Add(new MatchExpenseShare
            {
                MatchExpenseId = expense.Id,
                PlayerId = p.Id,
                LinkedUserId = p.LinkedUserId,
                ParticipatesInMatch = true,
                MatchAmount = perPerson,
                TotalAmount = perPerson
            });
        }

        return expense;
    }
}
