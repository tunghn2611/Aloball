using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Controllers;

public partial class TeamsController
{
    // 1. Trang trung tâm tin nhắn & liên hệ nội bộ đội bóng
    [HttpGet, Authorize]
    public async Task<IActionResult> Messages(Guid teamId, string? tab = "inbox", string? contactUserId = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        // Kiểm tra phân quyền: bắt buộc phải là thành viên hiện tại của đội
        if (!await _auth.HasMemberAccessAsync(teamId, user.Id))
            return Forbid();

        var team = await _db.Teams.FindAsync(teamId);
        if (team == null) return NotFound("Không tìm thấy đội bóng.");

        var isManager = await _auth.HasManagerAccessAsync(teamId, user.Id);
        var userRole = await _auth.GetUserRoleInTeamAsync(teamId, user.Id);

        var announcements = await _messaging.GetTeamAnnouncementsAsync(teamId, user.Id);
        var conversations = await _messaging.GetConversationsForUserAsync(teamId, user.Id);
        var unreadCount = await _messaging.GetUnreadCountAsync(teamId, user.Id);

        // Lấy danh sách thành viên hiện tại (dành cho Lãnh đội muốn chọn người để gửi tin)
        var members = isManager
            ? await _db.TeamMembers
                .Include(m => m.User)
                .Where(m => m.TeamId == teamId && m.UserId != user.Id)
                .OrderBy(m => m.User.FullName)
                .ToListAsync()
            : new List<TeamMember>();

        // Nếu có chỉ định contactUserId cụ thể từ danh sách cầu thủ, chỉ chấp nhận nếu là thành viên hiện tại của đội
        ApplicationUser? preselectedUser = null;
        if (!string.IsNullOrEmpty(contactUserId) && isManager)
        {
            var targetMember = members.FirstOrDefault(m => m.UserId == contactUserId);
            preselectedUser = targetMember?.User;
        }

        ViewBag.Team = team;
        ViewBag.IsManager = isManager;
        ViewBag.UserRole = userRole;
        ViewBag.Announcements = announcements;
        ViewBag.Conversations = conversations;
        ViewBag.UnreadCount = unreadCount;
        ViewBag.TeamMembers = members;
        ViewBag.ActiveTab = tab ?? "inbox";
        ViewBag.PreselectedUser = preselectedUser;

        return View();
    }

    // 2. Xem chi tiết và trả lời cuộc hội thoại
    [HttpGet, Authorize]
    public async Task<IActionResult> Conversation(Guid teamId, Guid conversationId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasMemberAccessAsync(teamId, user.Id))
            return Forbid();

        var team = await _db.Teams.FindAsync(teamId);
        if (team == null) return NotFound("Không tìm thấy đội bóng.");

        var detail = await _messaging.GetConversationDetailAsync(teamId, conversationId, user.Id);
        if (detail == null)
            return StatusCode(StatusCodes.Status403Forbidden, "Bạn không có quyền truy cập hoặc cuộc trò chuyện không tồn tại.");

        ViewBag.Team = team;
        ViewBag.IsManager = await _auth.HasManagerAccessAsync(teamId, user.Id);
        ViewBag.UserRole = await _auth.GetUserRoleInTeamAsync(teamId, user.Id);

        return View(detail);
    }

    // 3. Thành viên gửi tin nhắn liên hệ Đội trưởng / Ban cán sự
    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> SendMessageToCaptain(Guid teamId, string title, string content)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasMemberAccessAsync(teamId, user.Id))
            return Forbid();

        var result = await _messaging.SendMemberToCaptainMessageAsync(teamId, user.Id, title, content);
        if (!result.Success)
        {
            TempData["Error"] = result.Message;
            return RedirectToAction(nameof(Messages), new { teamId, tab = "inbox" });
        }

        TempData["Success"] = result.Message;
        if (result.ConversationId.HasValue)
        {
            return RedirectToAction(nameof(Conversation), new { teamId, conversationId = result.ConversationId.Value });
        }

        return RedirectToAction(nameof(Messages), new { teamId });
    }

    // 4. Lãnh đạo gửi tin nhắn trực tiếp cho 1 thành viên cụ thể
    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> SendMessageToMember(Guid teamId, string recipientUserId, string title, string content)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasManagerAccessAsync(teamId, user.Id))
            return Forbid();

        var result = await _messaging.SendLeadershipToMemberMessageAsync(teamId, user.Id, recipientUserId, title, content);
        if (!result.Success)
        {
            TempData["Error"] = result.Message;
            return RedirectToAction(nameof(Messages), new { teamId, tab = "inbox" });
        }

        TempData["Success"] = result.Message;
        if (result.ConversationId.HasValue)
        {
            return RedirectToAction(nameof(Conversation), new { teamId, conversationId = result.ConversationId.Value });
        }

        return RedirectToAction(nameof(Messages), new { teamId });
    }

    // 5. Lãnh đạo phát thông báo toàn đội
    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> SendAnnouncement(Guid teamId, string title, string content)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasManagerAccessAsync(teamId, user.Id))
            return Forbid();

        var result = await _messaging.SendTeamAnnouncementAsync(teamId, user.Id, title, content);
        if (!result.Success)
        {
            TempData["Error"] = result.Message;
            return RedirectToAction(nameof(Messages), new { teamId, tab = "announcements" });
        }

        TempData["Success"] = result.Message;
        return RedirectToAction(nameof(Messages), new { teamId, tab = "announcements" });
    }

    // 6. Gửi phản hồi trong cuộc trò chuyện
    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReplyMessage(Guid teamId, Guid conversationId, string content)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasMemberAccessAsync(teamId, user.Id))
            return Forbid();

        var result = await _messaging.ReplyToConversationAsync(teamId, conversationId, user.Id, content);
        if (!result.Success)
        {
            TempData["Error"] = result.Message;
        }
        else
        {
            TempData["Success"] = result.Message;
        }

        return RedirectToAction(nameof(Conversation), new { teamId, conversationId });
    }
}
