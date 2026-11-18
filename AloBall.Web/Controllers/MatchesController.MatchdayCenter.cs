using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Controllers;

public partial class MatchesController
{
    // ==========================================
    // MATCHDAY CENTER ACTIONS & INTEGRATIONS
    // ==========================================

    // 1. Phản hồi điểm danh (RSVP) cho cầu thủ hoặc Lãnh đội cập nhật
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitMatchRsvp(Guid matchId, Guid recordId, int responseStatus)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var record = await _db.AttendanceRecords
            .Include(r => r.Session)
            .Include(r => r.Player)
            .FirstOrDefaultAsync(r => r.Id == recordId);

        if (record == null) return NotFound(new { success = false, message = "Bản ghi điểm danh không tồn tại." });

        var match = await _db.Matches.FindAsync(matchId);
        if (match == null) return NotFound();

        // Kiểm tra quyền: Hoặc là chính cầu thủ này, hoặc là Lãnh đội/Quản lý của đội
        bool isSelf = !string.IsNullOrEmpty(record.Player.LinkedUserId) && record.Player.LinkedUserId == user.Id;
        bool isManager = await _auth.HasManagerAccessAsync(record.Session.TeamId, user.Id);

        if (!isSelf && !isManager)
            return Forbid();

        // Kiểm tra kỳ đã chốt hoặc buổi đã hủy
        if (record.Session.Status == SessionStatus.Cancelled)
            return BadRequest(new { success = false, message = "Buổi sinh hoạt/thi đấu đã bị hủy." });

        if (await CompetitionRules.IsClosedAsync(_db, record.Session.TeamId, record.Session.StatisticalPeriod))
            return Conflict(new { success = false, message = "Kỳ thống kê đã chốt. Không thể thay đổi trạng thái điểm danh." });

        if (!Enum.IsDefined(typeof(ResponseStatus), responseStatus))
            return BadRequest(new { success = false, message = "Trạng thái phản hồi không hợp lệ." });

        var newResponse = (ResponseStatus)responseStatus;
        record.Response = newResponse;
        record.ResponseTime = DateTime.UtcNow;
        record.IsResponseLate = DateTime.UtcNow > record.Session.ResponseDeadline;

        // Nếu là Manager và muốn đồng bộ luôn trạng thái thực tế
        if (isManager && newResponse == ResponseStatus.Attending && record.Actual == ActualStatus.NotChecked)
        {
            // Để ngỏ cho ngày thi đấu
        }

        // Tự động tính lại quy chế chuyên cần & phạt vi phạm nếu có engine
        if (_calcEngine != null)
        {
            await _calcEngine.RecalculateSessionPenaltiesAsync(record.SessionId);
            if (record.Session.Status == SessionStatus.Finalized)
            {
                await _fireKeeping.SyncAttendanceSessionPointsAsync(record.SessionId, user.Id);
            }
        }
        else
        {
            await _db.SaveChangesAsync();
        }

        TempData["Success"] = isSelf 
            ? (newResponse == ResponseStatus.Attending ? "Đã xác nhận tham gia trận đấu thành công!" : "Đã báo vắng trận đấu.") 
            : $"Đã cập nhật trạng thái của {record.Player.FullName} thành công.";

        return RedirectToAction(nameof(Detail), new { id = matchId });
    }

    // 1b. Fast Matchday RSVP (Dành cho Zalo & Thành viên chưa đăng ký tài khoản / hoặc thao tác 1 chạm)
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FastMatchRsvp(Guid matchId, Guid recordId, int responseStatus, string? rsvpToken)
    {
        var match = await _db.Matches.FindAsync(matchId);
        if (match == null) return NotFound();

        var user = await _userManager.GetUserAsync(User);
        bool hasValidToken = !string.IsNullOrWhiteSpace(rsvpToken) &&
                             !string.IsNullOrWhiteSpace(match.RsvpToken) &&
                             string.Equals(rsvpToken.Trim(), match.RsvpToken.Trim(), StringComparison.OrdinalIgnoreCase);

        var record = await _db.AttendanceRecords
            .Include(r => r.Session)
            .Include(r => r.Player)
            .FirstOrDefaultAsync(r => r.Id == recordId && r.Session.MatchId == matchId);

        if (record == null) return NotFound(new { success = false, message = "Bản ghi điểm danh không tồn tại." });

        bool isSelf = user != null && !string.IsNullOrEmpty(record.Player.LinkedUserId) && record.Player.LinkedUserId == user.Id;
        bool isManager = user != null && await _auth.HasManagerAccessAsync(record.Session.TeamId, user.Id);

        if (!isSelf && !isManager && !(hasValidToken && string.IsNullOrEmpty(record.Player.LinkedUserId)))
        {
            if (hasValidToken && !string.IsNullOrEmpty(record.Player.LinkedUserId))
            {
                TempData["Error"] = $"Hồ sơ {record.Player.FullName} đã được liên kết tài khoản. Vui lòng đăng nhập để điểm danh.";
                return RedirectToAction(nameof(Detail), new { id = matchId, token = rsvpToken });
            }
            return Forbid();
        }

        if (record.Session.Status == SessionStatus.Cancelled)
            return BadRequest(new { success = false, message = "Buổi thi đấu đã bị hủy." });

        if (await CompetitionRules.IsClosedAsync(_db, record.Session.TeamId, record.Session.StatisticalPeriod))
            return Conflict(new { success = false, message = "Kỳ thống kê đã chốt. Không thể thay đổi trạng thái điểm danh." });

        if (!Enum.IsDefined(typeof(ResponseStatus), responseStatus))
            return BadRequest(new { success = false, message = "Trạng thái phản hồi không hợp lệ." });

        var newResponse = (ResponseStatus)responseStatus;
        record.Response = newResponse;
        record.ResponseTime = DateTime.UtcNow;
        record.IsResponseLate = DateTime.UtcNow > record.Session.ResponseDeadline;

        if (_calcEngine != null)
        {
            await _calcEngine.RecalculateSessionPenaltiesAsync(record.SessionId);
            if (record.Session.Status == SessionStatus.Finalized && user != null)
            {
                await _fireKeeping.SyncAttendanceSessionPointsAsync(record.SessionId, user.Id);
            }
        }
        else
        {
            await _db.SaveChangesAsync();
        }

        TempData["Success"] = $"Đã cập nhật điểm danh cho {record.Player.FullName}: {(newResponse == ResponseStatus.Attending ? "Có mặt ⚽" : "Xin nghỉ")}.";

        return RedirectToAction(nameof(Detail), new { id = matchId, token = rsvpToken });
    }

    // 2. Cập nhật hạn chót RSVP cho trận đấu (Dành cho Lãnh đội)
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRsvpDeadline(Guid matchId, Guid sessionId, DateTime responseDeadline)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var session = await _db.AttendanceSessions.FindAsync(sessionId);
        if (session == null) return NotFound();

        if (!await _auth.HasManagerAccessAsync(session.TeamId, user.Id))
            return Forbid();

        if (session.Status == SessionStatus.Cancelled || await CompetitionRules.IsClosedAsync(_db, session.TeamId, session.StatisticalPeriod))
            return Conflict(new { message = "Buổi đã hủy hoặc kỳ đã chốt. Không thể sửa hạn chót." });

        session.ResponseDeadline = responseDeadline;
        await _db.SaveChangesAsync();

        // Tính lại trạng thái nộp trễ của các bản ghi đã phản hồi
        if (_calcEngine != null)
        {
            await _calcEngine.RecalculateSessionPenaltiesAsync(session.Id);
        }

        TempData["Success"] = "Đã cập nhật hạn chót phản hồi điểm danh thành công!";
        return RedirectToAction(nameof(Detail), new { id = matchId });
    }

    // 3. Gửi nhắc nhở điểm danh thủ công đến các cầu thủ chưa phản hồi
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendMatchReminder(Guid matchId, Guid teamId, string? customNote)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasManagerAccessAsync(teamId, user.Id))
            return Forbid();

        var match = await _db.Matches
            .Include(m => m.TeamA)
            .Include(m => m.TeamB)
            .FirstOrDefaultAsync(m => m.Id == matchId);

        if (match == null) return NotFound();
        if (match.TeamAId != teamId && match.TeamBId != teamId) return BadRequest();

        // 1. Kiểm tra rate-limit: Không gửi nhắc nhở quá dồn dập (ít nhất cách nhau 5 phút cho cùng 1 trận)
        string reminderTag = $"[MATCH_REMINDER:{matchId}]";
        var recentReminder = await _db.TeamMessages
            .Where(m => m.TeamId == teamId && m.Content.Contains(reminderTag) && m.CreatedAt > DateTime.UtcNow.AddMinutes(-5))
            .FirstOrDefaultAsync();

        if (recentReminder != null)
        {
            TempData["Error"] = "Lời nhắc đã được gửi gần đây. Vui lòng đợi ít nhất 5 phút giữa các lần gửi để tránh làm phiền thành viên.";
            return RedirectToAction(nameof(Detail), new { id = matchId });
        }

        // 2. Tìm buổi điểm danh và danh sách cầu thủ chưa phản hồi
        var session = await _db.AttendanceSessions
            .Include(s => s.Records)
                .ThenInclude(r => r.Player)
            .FirstOrDefaultAsync(s => s.MatchId == matchId && s.TeamId == teamId);

        if (session == null)
        {
            TempData["Error"] = "Chưa có danh sách điểm danh cho trận đấu này.";
            return RedirectToAction(nameof(Detail), new { id = matchId });
        }

        var unansweredRecords = session.Records
            .Where(r => r.Response == ResponseStatus.NotRecorded || r.Response == ResponseStatus.NoResponse)
            .ToList();

        if (!unansweredRecords.Any())
        {
            TempData["Success"] = "Tất cả cầu thủ trong đội đều đã phản hồi điểm danh!";
            return RedirectToAction(nameof(Detail), new { id = matchId });
        }

        // 3. Soạn nội dung nhắc nhở
        string opponentName = match.Category == MatchCategory.Internal
            ? "Trận đấu nội bộ"
            : (match.TeamAId == teamId ? (match.TeamB?.Name ?? match.TeamBExternalName ?? "Đối thủ ngoài") : match.TeamA.Name);

        var gatheringTime = match.MatchDateTime.AddMinutes(-30);
        string title = $"[Nhắc điểm danh] Trận đấu {match.PitchName} ({match.MatchDateTime:dd/MM})";
        string content = $"⚽ THÔNG BÁO NHẮC ĐIỂM DANH TRẬN ĐẤU\n" +
                         $"- Trận: vs {opponentName}\n" +
                         $"- Ngày thi đấu: {match.MatchDateTime:dd/MM/yyyy}\n" +
                         $"- Giờ tập trung: {gatheringTime:HH:mm} (Bóng lăn: {match.MatchDateTime:HH:mm})\n" +
                         $"- Sân: {match.PitchName} ({match.PitchType})\n" +
                         $"- Hạn chót xác nhận: {session.ResponseDeadline:HH:mm dd/MM/yyyy}\n" +
                         (!string.IsNullOrWhiteSpace(customNote) ? $"- Lưu ý từ BHL: {customNote.Trim()}\n" : "") +
                         $"- Danh sách chưa phản hồi ({unansweredRecords.Count} người): {string.Join(", ", unansweredRecords.Take(10).Select(r => r.Player.FullName))}" +
                         (unansweredRecords.Count > 10 ? $" và {unansweredRecords.Count - 10} cầu thủ khác." : ".") +
                         $"\n\n👉 Vui lòng vào Matchday Center để chốt tham gia hoặc báo vắng gấp!\n{reminderTag}";

        if (_teamMessaging != null)
        {
            // Đăng thông báo toàn đội
            await _teamMessaging.SendTeamAnnouncementAsync(teamId, user.Id, title, content);

            // Gửi trực tiếp đến các thành viên chưa phản hồi có tài khoản liên kết
            foreach (var r in unansweredRecords.Where(r => !string.IsNullOrEmpty(r.Player.LinkedUserId)))
            {
                if (r.Player.LinkedUserId != user.Id)
                {
                    await _teamMessaging.SendLeadershipToMemberMessageAsync(
                        teamId,
                        user.Id,
                        r.Player.LinkedUserId!,
                        $"[Điểm danh gấp] Trận ngày {match.MatchDateTime:dd/MM}",
                        $"Chào bạn, bạn chưa phản hồi điểm danh trận đấu vs {opponentName} lúc {match.MatchDateTime:HH:mm dd/MM/yyyy} tại {match.PitchName}. Hạn chót: {session.ResponseDeadline:HH:mm dd/MM}. Vui lòng xác nhận sớm để BHL sắp xếp đội hình!");
                }
            }
        }

        TempData["Success"] = $"Đã gửi thông báo nhắc nhở đến {unansweredRecords.Count} cầu thủ chưa phản hồi thành công!";
        return RedirectToAction(nameof(Detail), new { id = matchId });
    }

    // 4. Đồng bộ ngày giờ & sân thi đấu theo Đơn đặt sân đã xác nhận (Xử lý chênh lệch)
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncMatchWithConfirmedPitch(Guid matchId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var match = await _db.Matches.FindAsync(matchId);
        if (match == null) return NotFound();

        bool isManagerA = await _auth.HasManagerAccessAsync(match.TeamAId, user.Id);
        bool isManagerB = match.TeamBId.HasValue && await _auth.HasManagerAccessAsync(match.TeamBId.Value, user.Id);

        if (!isManagerA && !isManagerB) return Forbid();

        // Tìm đơn đặt sân đã xác nhận liên kết với trận này
        var confirmedRequest = await _db.PitchBookingRequests
            .Include(r => r.Offers)
            .Include(r => r.PitchVenue)
            .Where(r => r.MatchId == matchId && r.Status == PitchBookingStatus.Confirmed)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        if (confirmedRequest == null)
        {
            TempData["Error"] = "Không tìm thấy đơn đặt sân đã xác nhận nào cho trận này.";
            return RedirectToAction(nameof(Detail), new { id = matchId });
        }

        var acceptedOffer = confirmedRequest.Offers.FirstOrDefault(o => o.Status == PitchBookingOfferStatus.Accepted);
        if (acceptedOffer == null)
        {
            TempData["Error"] = "Đơn đặt sân chưa có báo giá được chấp thuận.";
            return RedirectToAction(nameof(Detail), new { id = matchId });
        }

        DateTime newMatchDateTime = acceptedOffer.ConfirmedDate.Date + acceptedOffer.ConfirmedStartTime;

        // Cập nhật thông tin trận
        match.PitchName = acceptedOffer.ConfirmedVenueName;
        match.MatchDateTime = newMatchDateTime;
        if (!string.IsNullOrWhiteSpace(acceptedOffer.ConfirmedAddress))
        {
            match.Notes = string.IsNullOrWhiteSpace(match.Notes)
                ? $"Địa chỉ sân: {acceptedOffer.ConfirmedAddress}"
                : $"{match.Notes} · Địa chỉ sân: {acceptedOffer.ConfirmedAddress}";
        }

        // Đồng bộ buổi điểm danh liên kết (nếu có)
        var sessions = await _db.AttendanceSessions
            .Where(s => s.MatchId == matchId)
            .ToListAsync();

        foreach (var s in sessions)
        {
            s.PitchName = acceptedOffer.ConfirmedVenueName;
            s.SessionDateTime = newMatchDateTime;
        }

        await _db.SaveChangesAsync();

        TempData["Success"] = "Đã đồng bộ thông tin sân và thời gian thi đấu theo đơn đặt sân thành công!";
        return RedirectToAction(nameof(Detail), new { id = matchId });
    }

    // 5. Phase 1B: Lưu bảng chia tiền sân & Kèo Tăng 2
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveMatchSplitBill(MatchSplitBillInput input)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasManagerAccessAsync(input.TeamId, user.Id))
            return Forbid();

        var result = await _splitBill.SaveMatchSplitBillAsync(input, user.Id);
        if (result.Success) TempData["Success"] = result.Message;
        else TempData["Error"] = result.Message;

        return RedirectToAction(nameof(Detail), null, new { id = input.MatchId, token = input.RsvpToken }, "splitBillSection");
    }

    // 6. Phase 1B: Đánh dấu Đã thu / Chưa đóng cho từng cầu thủ (1 tap)
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleExpenseSharePaid(Guid matchId, Guid shareId, string? rsvpToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var shareLookup = await _db.MatchExpenseShares
            .Include(s => s.MatchExpense)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == shareId && s.MatchExpense.MatchId == matchId);

        if (shareLookup == null) return NotFound();
        if (!await _auth.HasManagerAccessAsync(shareLookup.MatchExpense.TeamId, user.Id))
            return Forbid();

        var result = await _splitBill.ToggleSharePaymentAsync(matchId, shareId, user.Id);

        bool isAjax = string.Equals(Request?.Headers["X-Requested-With"].ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        if (isAjax)
        {
            if (!result.Success) return BadRequest(new { success = false, message = result.Message });
            return Json(new
            {
                success = true,
                message = result.Message,
                isPaid = result.NewStatus == ExpenseSharePaymentStatus.Paid,
                paidAtText = result.PaidAt?.AddHours(7).ToString("HH:mm dd/MM")
            });
        }

        if (result.Success) TempData["Success"] = result.Message;
        else TempData["Error"] = result.Message;

        return RedirectToAction(nameof(Detail), null, new { id = matchId, token = rsvpToken }, "splitBillSection");
    }

    // 7. Phase 1B: Lưu nhanh tài khoản ngân hàng CLB nhận tiền VietQR từ Matchday Detail
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveClubBankQuickSetup(Guid matchId, Guid teamId, string? bankCode, string? accountNumber, string? accountHolderName, string? rsvpToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasManagerAccessAsync(teamId, user.Id))
            return Forbid();

        var result = await _splitBill.SaveClubBankInfoAsync(teamId, bankCode, accountNumber, accountHolderName, user.Id);
        if (result.Success) TempData["Success"] = result.Message;
        else TempData["Error"] = result.Message;

        return RedirectToAction(nameof(Detail), null, new { id = matchId, token = rsvpToken }, "splitBillSection");
    }
}
