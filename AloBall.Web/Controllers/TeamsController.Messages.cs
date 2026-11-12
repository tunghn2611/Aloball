using System.Text.RegularExpressions;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Controllers;

public partial class TeamsController
{
    // =========================================================================
    // 1. TRUNG TÂM HỘP THƯ & DANH SÁCH CẦU THỦ ĐỘI BÓNG (INSTAGRAM DIRECT STYLE)
    // =========================================================================
    [HttpGet, Authorize]
    public async Task<IActionResult> Messages(Guid teamId, string? tab = "inbox")
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

        // Lấy danh sách toàn bộ cầu thủ trong đội (Roster)
        var players = await _db.Players
            .Where(p => p.TeamId == teamId && p.IsActive)
            .OrderBy(p => p.KitNumber ?? 999)
            .ThenBy(p => p.FullName)
            .ToListAsync();

        // Lấy danh sách thành viên đội bóng
        var members = await _db.TeamMembers
            .Include(m => m.User)
            .Where(m => m.TeamId == teamId)
            .ToListAsync();

        // Lấy thông báo toàn đội và các cuộc trò chuyện
        var announcements = await _messaging.GetTeamAnnouncementsAsync(teamId, user.Id);
        var conversations = await _messaging.GetConversationsForUserAsync(teamId, user.Id);
        var unreadCount = await _messaging.GetUnreadCountAsync(teamId, user.Id);

        // Lấy tất cả tin nhắn trực tiếp liên quan đến user hiện tại để map với từng cầu thủ
        var directMessages = await _db.TeamMessages
            .Where(m => m.TeamId == teamId && m.MessageType == TeamMessageType.Direct &&
                        (m.SenderUserId == user.Id || m.RecipientUserId == user.Id))
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();

        // Xây dựng dictionary thông tin tin nhắn gần nhất và unread cho từng user
        var latestMessageMap = new Dictionary<string, (string Content, DateTime CreatedAt, int Unread, Guid ConversationId)>();
        var userIdsInTeam = members.Select(m => m.UserId).ToHashSet();

        foreach (var msg in directMessages)
        {
            string otherPartyId = msg.SenderUserId == user.Id ? (msg.RecipientUserId ?? string.Empty) : msg.SenderUserId;
            if (string.IsNullOrEmpty(otherPartyId)) continue;

            if (!latestMessageMap.ContainsKey(otherPartyId))
            {
                int unread = directMessages.Count(m => m.SenderUserId == otherPartyId && m.RecipientUserId == user.Id && !m.IsRead);
                latestMessageMap[otherPartyId] = (msg.Content, msg.CreatedAt, unread, msg.ConversationId);
            }
        }

        ViewBag.Team = team;
        ViewBag.IsManager = isManager;
        ViewBag.UserRole = userRole;
        ViewBag.CurrentUser = user;
        ViewBag.Players = players;
        ViewBag.TeamMembers = members;
        ViewBag.Announcements = announcements;
        ViewBag.Conversations = conversations;
        ViewBag.UnreadCount = unreadCount;
        ViewBag.LatestMessageMap = latestMessageMap;
        ViewBag.ActiveTab = tab ?? "inbox";

        return View();
    }

    // =========================================================================
    // 2. GIAO DIỆN NHẮN TIN TRỰC TIẾP 1-1 (BOX CHAT INSTAGRAM DIRECT)
    // =========================================================================
    [HttpGet, Authorize]
    public async Task<IActionResult> Chat(Guid teamId, Guid? conversationId, Guid? playerId, string? targetUserId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasMemberAccessAsync(teamId, user.Id))
            return Forbid();

        var team = await _db.Teams.FindAsync(teamId);
        if (team == null) return NotFound("Không tìm thấy đội bóng.");

        Player? targetPlayer = null;
        ApplicationUser? targetUser = null;

        // 1. Xác định đối phương trò chuyện
        if (playerId.HasValue)
        {
            targetPlayer = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId.Value && p.TeamId == teamId);
            if (targetPlayer == null) return NotFound("Không tìm thấy cầu thủ.");

            targetUser = await EnsurePlayerUserAsync(targetPlayer, team);
            targetUserId = targetUser.Id;
        }
        else if (!string.IsNullOrEmpty(targetUserId))
        {
            targetUser = await _userManager.FindByIdAsync(targetUserId);
            if (targetUser == null) return NotFound("Không tìm thấy người dùng.");

            targetPlayer = await _db.Players.FirstOrDefaultAsync(p => p.TeamId == teamId && p.LinkedUserId == targetUserId);
        }
        else if (conversationId.HasValue)
        {
            var firstMsg = await _db.TeamMessages
                .Include(m => m.SenderUser)
                .Include(m => m.RecipientUser)
                .Where(m => m.TeamId == teamId && m.ConversationId == conversationId.Value)
                .OrderBy(m => m.CreatedAt)
                .FirstOrDefaultAsync();

            if (firstMsg == null) return NotFound("Cuộc trò chuyện không tồn tại.");

            targetUserId = firstMsg.SenderUserId == user.Id ? firstMsg.RecipientUserId : firstMsg.SenderUserId;
            if (string.IsNullOrEmpty(targetUserId))
            {
                // Fallback nếu recipient null (ví dụ gửi cho captain)
                var captain = await _db.TeamMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.TeamId == teamId && m.Role == TeamRole.Owner);
                targetUserId = captain?.UserId;
            }

            if (!string.IsNullOrEmpty(targetUserId))
            {
                targetUser = await _userManager.FindByIdAsync(targetUserId);
                targetPlayer = await _db.Players.FirstOrDefaultAsync(p => p.TeamId == teamId && p.LinkedUserId == targetUserId);
            }
        }

        if (targetUser == null || string.IsNullOrEmpty(targetUserId))
        {
            return RedirectToAction(nameof(Messages), new { teamId });
        }

        // 2. Tìm cuộc hội thoại sẵn có giữa 2 người hoặc tạo ID hội thoại mới
        var existingMsg = await _db.TeamMessages
            .Where(m => m.TeamId == teamId && m.MessageType == TeamMessageType.Direct &&
                        ((m.SenderUserId == user.Id && m.RecipientUserId == targetUserId) ||
                         (m.SenderUserId == targetUserId && m.RecipientUserId == user.Id)))
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync();

        var convId = conversationId ?? existingMsg?.ConversationId ?? Guid.NewGuid();

        // 3. Tải toàn bộ tin nhắn trong cuộc trò chuyện
        var messages = await _db.TeamMessages
            .Include(m => m.SenderUser)
            .Where(m => m.TeamId == teamId && m.ConversationId == convId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        // 4. Đánh dấu đã đọc các tin nhắn gửi đến mình
        var unreadMessages = messages.Where(m => m.RecipientUserId == user.Id && !m.IsRead).ToList();
        if (unreadMessages.Any())
        {
            foreach (var m in unreadMessages)
            {
                m.IsRead = true;
                m.ReadAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();
        }

        // Lấy vai trò của đối phương trong đội
        var targetMember = await _db.TeamMembers.FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == targetUserId);

        ViewBag.Team = team;
        ViewBag.CurrentUser = user;
        ViewBag.TargetUser = targetUser;
        ViewBag.TargetPlayer = targetPlayer;
        ViewBag.TargetMember = targetMember;
        ViewBag.ConversationId = convId;
        ViewBag.TargetUserId = targetUserId;
        ViewBag.IsManager = await _auth.HasManagerAccessAsync(teamId, user.Id);

        return View(messages);
    }

    // =========================================================================
    // 3. GỬI TIN NHẮN TRỰC TIẾP (AJAX & FORM POST)
    // =========================================================================
    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> SendDirectMessage(Guid teamId, Guid conversationId, string targetUserId, string content)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasMemberAccessAsync(teamId, user.Id))
            return Forbid();

        content = content?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json"))
                return Json(new { success = false, message = "Nội dung tin nhắn không được để trống." });

            TempData["Error"] = "Nội dung tin nhắn không được để trống.";
            return RedirectToAction(nameof(Chat), new { teamId, conversationId, targetUserId });
        }

        if (content.Length > 2000)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json"))
                return Json(new { success = false, message = "Nội dung tin nhắn không được vượt quá 2000 ký tự." });

            TempData["Error"] = "Nội dung tin nhắn không được vượt quá 2000 ký tự.";
            return RedirectToAction(nameof(Chat), new { teamId, conversationId, targetUserId });
        }

        var msg = new TeamMessage
        {
            TeamId = teamId,
            SenderUserId = user.Id,
            RecipientUserId = targetUserId,
            ConversationId = conversationId,
            MessageType = TeamMessageType.Direct,
            Title = "Trò chuyện trực tiếp",
            Content = content,
            CreatedAt = DateTime.UtcNow,
            IsRead = false
        };

        _db.TeamMessages.Add(msg);
        await _db.SaveChangesAsync();

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json"))
        {
            return Json(new
            {
                success = true,
                messageId = msg.Id,
                content = msg.Content,
                createdAt = msg.CreatedAt.ToLocalTime().ToString("HH:mm"),
                isMe = true
            });
        }

        return RedirectToAction(nameof(Chat), new { teamId, conversationId, targetUserId });
    }

    // =========================================================================
    // 4. BẢO TOÀN ROUTE CONVERSATION (TỰ ĐỘNG CHUYỂN HƯỚNG SANG CHAT MỚI)
    // =========================================================================
    [HttpGet, Authorize]
    public IActionResult Conversation(Guid teamId, Guid conversationId)
    {
        return RedirectToAction(nameof(Chat), new { teamId, conversationId });
    }

    // =========================================================================
    // 5. THÔNG BÁO TOÀN ĐỘI
    // =========================================================================
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
        }
        else
        {
            TempData["Success"] = result.Message;
        }

        return RedirectToAction(nameof(Messages), new { teamId, tab = "announcements" });
    }

    // =========================================================================
    // 6. PHẢN HỒI TIN NHẮN (LEGACY SUPPORT)
    // =========================================================================
    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReplyMessage(Guid teamId, Guid conversationId, string content)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasMemberAccessAsync(teamId, user.Id))
            return Forbid();

        var existingMsg = await _db.TeamMessages.FirstOrDefaultAsync(m => m.TeamId == teamId && m.ConversationId == conversationId);
        string? targetUserId = existingMsg?.SenderUserId == user.Id ? existingMsg?.RecipientUserId : existingMsg?.SenderUserId;

        return await SendDirectMessage(teamId, conversationId, targetUserId ?? string.Empty, content);
    }

    // =========================================================================
    // HELPER: TỰ ĐỘNG LIÊN KẾT / TẠO TÀI KHOẢN CHO CẦU THỦ CHƯA CÓ LINKED USER
    // =========================================================================
    private async Task<ApplicationUser> EnsurePlayerUserAsync(Player player, Team team)
    {
        if (!string.IsNullOrEmpty(player.LinkedUserId))
        {
            var existingUser = await _userManager.FindByIdAsync(player.LinkedUserId);
            if (existingUser != null) return existingUser;
        }

        // Tạo email & username an toàn không dấu
        string cleanName = RemoveDiacritics(player.FullName).ToLower();
        cleanName = Regex.Replace(cleanName, @"[^a-z0-9]", "");
        if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "player" + (player.KitNumber ?? 99);

        string email = $"{cleanName}.{player.KitNumber ?? 0}@{team.Slug}.aloball.vn";

        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            user = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = email,
                Email = email,
                FullName = player.FullName,
                AvatarUrl = player.PhotoUrl,
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow
            };
            var res = await _userManager.CreateAsync(user, "AloBall@123456");
            if (!res.Succeeded)
            {
                // Fallback nếu trùng username
                user.UserName = $"{cleanName}.{Guid.NewGuid().ToString().Substring(0, 5)}@{team.Slug}.aloball.vn";
                await _userManager.CreateAsync(user, "AloBall@123456");
            }
        }

        player.LinkedUserId = user.Id;

        // Đảm bảo là thành viên trong đội
        if (!await _db.TeamMembers.AnyAsync(m => m.TeamId == team.Id && m.UserId == user.Id))
        {
            _db.TeamMembers.Add(new TeamMember
            {
                TeamId = team.Id,
                UserId = user.Id,
                Role = TeamRole.Member,
                JoinedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        return user;
    }

    private static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var normalizedString = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var c in normalizedString)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC).Replace("đ", "d").Replace("Đ", "D");
    }
}
