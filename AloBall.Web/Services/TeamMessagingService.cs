using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Services;

public class ConversationSummaryDto
{
    public Guid ConversationId { get; set; }
    public Guid TeamId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string OtherPartyName { get; set; } = string.Empty;
    public string OtherPartyRole { get; set; } = string.Empty;
    public string? OtherPartyAvatar { get; set; }
    public string LastMessageContent { get; set; } = string.Empty;
    public DateTime LastMessageAt { get; set; }
    public int UnreadCount { get; set; }
    public bool IsAnnouncement { get; set; }
}

public class ConversationDetailDto
{
    public Guid ConversationId { get; set; }
    public Guid TeamId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsAnnouncement { get; set; }
    public string MemberUserId { get; set; } = string.Empty;
    public List<MessageItemDto> Messages { get; set; } = new();
}

public class MessageItemDto
{
    public Guid Id { get; set; }
    public string SenderUserId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string SenderRole { get; set; } = string.Empty;
    public string? SenderAvatar { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsMe { get; set; }
    public bool IsRead { get; set; }
}

public interface ITeamMessagingService
{
    Task<(bool Success, string Message, Guid? ConversationId)> SendMemberToCaptainMessageAsync(
        Guid teamId, string senderUserId, string title, string content);

    Task<(bool Success, string Message, Guid? ConversationId)> SendLeadershipToMemberMessageAsync(
        Guid teamId, string senderUserId, string recipientUserId, string title, string content);

    Task<(bool Success, string Message, Guid? MessageId)> SendTeamAnnouncementAsync(
        Guid teamId, string senderUserId, string title, string content);

    Task<(bool Success, string Message)> ReplyToConversationAsync(
        Guid teamId, Guid conversationId, string senderUserId, string content);

    Task<List<TeamMessage>> GetTeamAnnouncementsAsync(Guid teamId, string currentUserId);

    Task<List<ConversationSummaryDto>> GetConversationsForUserAsync(Guid teamId, string currentUserId);

    Task<ConversationDetailDto?> GetConversationDetailAsync(Guid teamId, Guid conversationId, string currentUserId);

    Task<int> GetUnreadCountAsync(Guid teamId, string currentUserId);

    Task<bool> MarkConversationAsReadAsync(Guid teamId, Guid conversationId, string currentUserId);
}

public class TeamMessagingService : ITeamMessagingService
{
    private readonly ApplicationDbContext _db;
    private readonly ITeamAuthorizationService _auth;

    public TeamMessagingService(ApplicationDbContext db, ITeamAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<(bool Success, string Message, Guid? ConversationId)> SendMemberToCaptainMessageAsync(
        Guid teamId, string senderUserId, string title, string content)
    {
        // 1. Kiểm tra quyền thành viên hiện tại của người gửi
        var role = await _auth.GetUserRoleInTeamAsync(teamId, senderUserId);
        var isPlatformAdmin = await _auth.IsPlatformAdminAsync(senderUserId);
        if (!role.HasValue && !isPlatformAdmin)
            return (false, "Bạn không phải là thành viên hiện tại của đội bóng này.", null);

        // 2. Kiểm tra tính hợp lệ của dữ liệu đầu vào
        title = title?.Trim() ?? string.Empty;
        content = content?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(title))
            return (false, "Tiêu đề tin nhắn không được để trống.", null);

        if (title.Length > 200)
            return (false, "Tiêu đề tin nhắn không được vượt quá 200 ký tự.", null);

        if (string.IsNullOrWhiteSpace(content))
            return (false, "Nội dung tin nhắn không được để trống.", null);

        if (content.Length > 2000)
            return (false, "Nội dung tin nhắn không được vượt quá 2000 ký tự.", null);

        // 3. Chống Spam & Giới hạn tần suất gửi tin (Rate Limit)
        var rateCheck = await CheckRateLimitAsync(teamId, senderUserId, content);
        if (!rateCheck.Allowed)
            return (false, rateCheck.ErrorMessage!, null);

        // 4. Tìm Đội trưởng / Ban cán sự nhận tin từ dữ liệu máy chủ tin cậy
        var team = await _db.Teams.Include(t => t.Members).FirstOrDefaultAsync(t => t.Id == teamId);
        if (team == null)
            return (false, "Không tìm thấy đội bóng.", null);

        var captainMember = team.Members.FirstOrDefault(m => m.Role == TeamRole.Owner)
                         ?? team.Members.FirstOrDefault(m => m.Role == TeamRole.Manager);
        var recipientUserId = captainMember?.UserId;

        var conversationId = Guid.NewGuid();
        var msg = new TeamMessage
        {
            TeamId = teamId,
            SenderUserId = senderUserId,
            RecipientUserId = recipientUserId,
            ConversationId = conversationId,
            MessageType = TeamMessageType.Direct,
            Title = title,
            Content = content,
            CreatedAt = DateTime.UtcNow,
            IsRead = false
        };

        _db.TeamMessages.Add(msg);
        await _db.SaveChangesAsync();

        return (true, "Đã gửi tin nhắn đến Đội trưởng / Ban cán sự thành công.", conversationId);
    }

    public async Task<(bool Success, string Message, Guid? ConversationId)> SendLeadershipToMemberMessageAsync(
        Guid teamId, string senderUserId, string recipientUserId, string title, string content)
    {
        // 1. Kiểm tra quyền lãnh đạo của người gửi
        if (!await _auth.HasManagerAccessAsync(teamId, senderUserId))
            return (false, "Chỉ Đội trưởng hoặc Ban cán sự mới có quyền gửi tin nhắn từ vai trò lãnh đạo.", null);

        // 2. Xác thực người nhận là thành viên hiện tại của đội bóng từ dữ liệu máy chủ
        if (recipientUserId == senderUserId)
            return (false, "Không thể gửi tin nhắn lãnh đội cho chính mình.", null);

        var isRecipientMember = await _db.TeamMembers.AnyAsync(m => m.TeamId == teamId && m.UserId == recipientUserId);
        if (!isRecipientMember)
            return (false, "Người nhận không phải là thành viên hiện tại của đội bóng.", null);

        // 3. Kiểm tra tính hợp lệ dữ liệu
        title = title?.Trim() ?? string.Empty;
        content = content?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(title))
            return (false, "Tiêu đề tin nhắn không được để trống.", null);

        if (title.Length > 200)
            return (false, "Tiêu đề tin nhắn không được vượt quá 200 ký tự.", null);

        if (string.IsNullOrWhiteSpace(content))
            return (false, "Nội dung tin nhắn không được để trống.", null);

        if (content.Length > 2000)
            return (false, "Nội dung tin nhắn không được vượt quá 2000 ký tự.", null);

        // 4. Rate Limit & Chống gửi lặp
        var rateCheck = await CheckRateLimitAsync(teamId, senderUserId, content);
        if (!rateCheck.Allowed)
            return (false, rateCheck.ErrorMessage!, null);

        var conversationId = Guid.NewGuid();
        var msg = new TeamMessage
        {
            TeamId = teamId,
            SenderUserId = senderUserId,
            RecipientUserId = recipientUserId,
            ConversationId = conversationId,
            MessageType = TeamMessageType.Direct,
            Title = title,
            Content = content,
            CreatedAt = DateTime.UtcNow,
            IsRead = false
        };

        _db.TeamMessages.Add(msg);
        await _db.SaveChangesAsync();

        return (true, "Đã gửi tin nhắn đến thành viên thành công.", conversationId);
    }

    public async Task<(bool Success, string Message, Guid? MessageId)> SendTeamAnnouncementAsync(
        Guid teamId, string senderUserId, string title, string content)
    {
        // 1. Kiểm tra quyền lãnh đạo
        if (!await _auth.HasManagerAccessAsync(teamId, senderUserId))
            return (false, "Chỉ Đội trưởng hoặc Ban cán sự mới có quyền đăng thông báo toàn đội.", null);

        title = title?.Trim() ?? string.Empty;
        content = content?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(title))
            return (false, "Tiêu đề thông báo không được để trống.", null);

        if (title.Length > 200)
            return (false, "Tiêu đề thông báo không được vượt quá 200 ký tự.", null);

        if (string.IsNullOrWhiteSpace(content))
            return (false, "Nội dung thông báo không được để trống.", null);

        if (content.Length > 2000)
            return (false, "Nội dung thông báo không được vượt quá 2000 ký tự.", null);

        var rateCheck = await CheckRateLimitAsync(teamId, senderUserId, content);
        if (!rateCheck.Allowed)
            return (false, rateCheck.ErrorMessage!, null);


        var msg = new TeamMessage
        {
            TeamId = teamId,
            SenderUserId = senderUserId,
            RecipientUserId = null, // null biểu thị thông báo phát sóng toàn đội
            ConversationId = Guid.NewGuid(),
            MessageType = TeamMessageType.Announcement,
            Title = title,
            Content = content,
            CreatedAt = DateTime.UtcNow,
            IsRead = false
        };

        _db.TeamMessages.Add(msg);
        await _db.SaveChangesAsync();

        return (true, "Đã đăng thông báo toàn đội thành công.", msg.Id);
    }

    public async Task<(bool Success, string Message)> ReplyToConversationAsync(
        Guid teamId, Guid conversationId, string senderUserId, string content)
    {
        // 1. Kiểm tra tư cách thành viên hiện tại
        var role = await _auth.GetUserRoleInTeamAsync(teamId, senderUserId);
        var isManager = await _auth.HasManagerAccessAsync(teamId, senderUserId);
        if (!role.HasValue && !isManager)
            return (false, "Bạn không phải là thành viên hiện tại của đội bóng này.");

        // 2. Tìm kiếm cuộc trò chuyện
        var messages = await _db.TeamMessages
            .Where(m => m.TeamId == teamId && m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        if (!messages.Any())
            return (false, "Không tìm thấy cuộc trò chuyện.");

        var firstMsg = messages.First();
        if (firstMsg.MessageType == TeamMessageType.Announcement)
            return (false, "Không thể gửi phản hồi vào thông báo chung của đội.");

        // 3. Phân quyền tham gia cuộc trò chuyện:
        // Lãnh đội (Manager/Owner) được quyền xem & trả lời mọi tin nhắn gửi về Ban cán sự
        // Thành viên thường chỉ được trả lời cuộc trò chuyện do chính họ tạo hoặc được gửi đích danh cho họ
        bool isParticipant = messages.Any(m => m.SenderUserId == senderUserId || m.RecipientUserId == senderUserId);
        if (!isManager && !isParticipant)
            return (false, "Bạn không có quyền tham gia cuộc trò chuyện này.");

        // 4. Validate nội dung & Anti-spam
        content = content?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
            return (false, "Nội dung phản hồi không được để trống.");

        if (content.Length > 2000)
            return (false, "Nội dung phản hồi không được vượt quá 2000 ký tự.");

        var rateCheck = await CheckRateLimitAsync(teamId, senderUserId, content);
        if (!rateCheck.Allowed)
            return (false, rateCheck.ErrorMessage!);

        // 5. Xác định người nhận phản hồi
        var managerUserIds = await _db.TeamMembers
            .Where(m => m.TeamId == teamId && (m.Role == TeamRole.Owner || m.Role == TeamRole.Manager))
            .Select(m => m.UserId)
            .ToListAsync();

        // Xác định ID thành viên thường của cuộc trò chuyện 1-1
        string memberUserId;
        if (managerUserIds.Contains(firstMsg.SenderUserId))
        {
            memberUserId = firstMsg.RecipientUserId ?? firstMsg.SenderUserId;
        }
        else
        {
            memberUserId = firstMsg.SenderUserId;
        }

        string? recipientUserId;
        if (isManager)
        {
            // Bất kỳ Lãnh đội nào trả lời thì người nhận luôn là Thành viên thường đó
            recipientUserId = memberUserId;
        }
        else
        {
            // Thành viên thường trả lời -> gửi đến Lãnh đội (người đã nhắn gần nhất hoặc Captain ban đầu)
            var lastManagerMsg = messages.LastOrDefault(m => managerUserIds.Contains(m.SenderUserId));
            recipientUserId = lastManagerMsg?.SenderUserId ?? firstMsg.RecipientUserId ?? firstMsg.SenderUserId;
        }

        var replyTitle = firstMsg.Title.StartsWith("Re: ") ? firstMsg.Title : $"Re: {firstMsg.Title}";
        var reply = new TeamMessage
        {
            TeamId = teamId,
            SenderUserId = senderUserId,
            RecipientUserId = recipientUserId,
            ConversationId = conversationId,
            MessageType = TeamMessageType.Direct,
            Title = replyTitle,
            Content = content,
            CreatedAt = DateTime.UtcNow,
            IsRead = false
        };

        _db.TeamMessages.Add(reply);

        // Đánh dấu đã đọc các tin nhắn trước đó của bên kia
        if (isManager)
        {
            foreach (var m in messages.Where(m => m.SenderUserId == memberUserId && !m.IsRead))
            {
                m.IsRead = true;
                m.ReadAt = DateTime.UtcNow;
            }
        }
        else
        {
            foreach (var m in messages.Where(m => managerUserIds.Contains(m.SenderUserId) && !m.IsRead))
            {
                m.IsRead = true;
                m.ReadAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();
        return (true, "Đã gửi câu trả lời thành công.");
    }

    public async Task<List<TeamMessage>> GetTeamAnnouncementsAsync(Guid teamId, string currentUserId)
    {
        if (!await _auth.HasMemberAccessAsync(teamId, currentUserId))
            return new List<TeamMessage>();

        return await _db.TeamMessages
            .Include(m => m.SenderUser)
            .Where(m => m.TeamId == teamId && m.MessageType == TeamMessageType.Announcement)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<ConversationSummaryDto>> GetConversationsForUserAsync(Guid teamId, string currentUserId)
    {
        if (!await _auth.HasMemberAccessAsync(teamId, currentUserId))
            return new List<ConversationSummaryDto>();

        var isManager = await _auth.HasManagerAccessAsync(teamId, currentUserId);

        // Lấy danh sách thành viên đội để tra cứu vai trò và tên hiển thị (tránh lộ email/phone)
        var members = await _db.TeamMembers
            .Include(m => m.User)
            .Where(m => m.TeamId == teamId)
            .ToListAsync();
        var membersDict = members.ToDictionary(m => m.UserId, m => m);
        var managerUserIds = members.Where(m => m.Role == TeamRole.Owner || m.Role == TeamRole.Manager).Select(m => m.UserId).ToHashSet();

        IQueryable<TeamMessage> query = _db.TeamMessages
            .Include(m => m.SenderUser)
            .Include(m => m.RecipientUser)
            .Where(m => m.TeamId == teamId && m.MessageType == TeamMessageType.Direct);

        if (!isManager)
        {
            // Thành viên thường chỉ xem các cuộc hội thoại của chính mình
            query = query.Where(m => m.SenderUserId == currentUserId || m.RecipientUserId == currentUserId);
        }

        var allMessages = await query.ToListAsync();
        var grouped = allMessages.GroupBy(m => m.ConversationId);

        var list = new List<ConversationSummaryDto>();

        foreach (var g in grouped)
        {
            var ordered = g.OrderBy(m => m.CreatedAt).ToList();
            var first = ordered.First();
            var last = ordered.Last();

            // Xác định ID thành viên thường trong cuộc trò chuyện 1-1
            string memberUserId = managerUserIds.Contains(first.SenderUserId)
                ? (first.RecipientUserId ?? first.SenderUserId)
                : first.SenderUserId;

            string otherPartyName;
            string otherPartyRole;
            string? otherPartyAvatar = null;

            if (isManager)
            {
                // Đối với bất kỳ Lãnh đội nào, người đối thoại bên kia luôn là Thành viên thường
                if (membersDict.TryGetValue(memberUserId, out var mem))
                {
                    otherPartyName = string.IsNullOrWhiteSpace(mem.User.FullName) ? mem.User.UserName ?? "Cầu thủ" : mem.User.FullName;
                    otherPartyRole = FormatRole(mem.Role);
                    otherPartyAvatar = mem.User.AvatarUrl;
                }
                else
                {
                    otherPartyName = "Cầu thủ / Thành viên";
                    otherPartyRole = "Thành viên";
                }
            }
            else
            {
                // Đối với Thành viên thường, người đối thoại bên kia là Lãnh đội / Đội trưởng
                var leaderUserId = first.SenderUserId == currentUserId ? first.RecipientUserId : first.SenderUserId;
                if (leaderUserId != null && membersDict.TryGetValue(leaderUserId, out var leaderMem))
                {
                    otherPartyName = string.IsNullOrWhiteSpace(leaderMem.User.FullName) ? leaderMem.User.UserName ?? "Đội trưởng" : leaderMem.User.FullName;
                    otherPartyRole = FormatRole(leaderMem.Role);
                    otherPartyAvatar = leaderMem.User.AvatarUrl;
                }
                else
                {
                    otherPartyName = "Ban cán sự / Đội trưởng";
                    otherPartyRole = "Lãnh đội";
                }
            }

            int unread = isManager
                ? ordered.Count(m => !managerUserIds.Contains(m.SenderUserId) && !m.IsRead)
                : ordered.Count(m => m.RecipientUserId == currentUserId && !m.IsRead);

            list.Add(new ConversationSummaryDto
            {
                ConversationId = g.Key,
                TeamId = teamId,
                Title = first.Title,
                OtherPartyName = otherPartyName,
                OtherPartyRole = otherPartyRole,
                OtherPartyAvatar = otherPartyAvatar,
                LastMessageContent = last.Content,
                LastMessageAt = last.CreatedAt,
                UnreadCount = unread,
                IsAnnouncement = false
            });
        }

        return list.OrderByDescending(c => c.LastMessageAt).ToList();
    }

    public async Task<ConversationDetailDto?> GetConversationDetailAsync(Guid teamId, Guid conversationId, string currentUserId)
    {
        if (!await _auth.HasMemberAccessAsync(teamId, currentUserId))
            return null;

        var isManager = await _auth.HasManagerAccessAsync(teamId, currentUserId);

        var messages = await _db.TeamMessages
            .Include(m => m.SenderUser)
            .Where(m => m.TeamId == teamId && m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        if (!messages.Any())
            return null;

        var first = messages.First();

        // Kiểm tra quyền: nếu không phải Lãnh đội, bắt buộc phải là người gửi hoặc người nhận trong thread
        bool isParticipant = messages.Any(m => m.SenderUserId == currentUserId || m.RecipientUserId == currentUserId);
        if (!isManager && !isParticipant && first.MessageType != TeamMessageType.Announcement)
        {
            return null; // Chặn IDOR và xem trộm tin nhắn riêng tư
        }

        var members = await _db.TeamMembers
            .Include(m => m.User)
            .Where(m => m.TeamId == teamId)
            .ToListAsync();
        var membersDict = members.ToDictionary(m => m.UserId, m => m);

        // Tự động đánh dấu đã đọc các tin nhắn gửi đến người dùng hiện tại (chỉ áp dụng cho Direct, không sửa cờ Announcement)
        bool updated = false;
        if (first.MessageType == TeamMessageType.Direct)
        {
            var managerUserIds = members.Where(m => m.Role == TeamRole.Owner || m.Role == TeamRole.Manager).Select(m => m.UserId).ToHashSet();
            if (isManager)
            {
                foreach (var m in messages.Where(m => !managerUserIds.Contains(m.SenderUserId) && !m.IsRead))
                {
                    m.IsRead = true;
                    m.ReadAt = DateTime.UtcNow;
                    updated = true;
                }
            }
            else
            {
                foreach (var m in messages.Where(m => m.RecipientUserId == currentUserId && !m.IsRead))
                {
                    m.IsRead = true;
                    m.ReadAt = DateTime.UtcNow;
                    updated = true;
                }
            }

            if (updated)
            {
                await _db.SaveChangesAsync();
            }
        }

        var items = messages.Select(m =>
        {
            string senderRole = "Thành viên";
            string senderName = string.IsNullOrWhiteSpace(m.SenderUser?.FullName) ? m.SenderUser?.UserName ?? "Thành viên" : m.SenderUser.FullName;
            string? avatar = m.SenderUser?.AvatarUrl;

            if (membersDict.TryGetValue(m.SenderUserId, out var tm))
            {
                senderRole = FormatRole(tm.Role);
                if (!string.IsNullOrWhiteSpace(tm.User.FullName)) senderName = tm.User.FullName;
            }

            return new MessageItemDto
            {
                Id = m.Id,
                SenderUserId = m.SenderUserId,
                SenderName = senderName,
                SenderRole = senderRole,
                SenderAvatar = avatar,
                Content = m.Content,
                CreatedAt = m.CreatedAt,
                IsMe = m.SenderUserId == currentUserId,
                IsRead = m.IsRead
            };
        }).ToList();

        return new ConversationDetailDto
        {
            ConversationId = conversationId,
            TeamId = teamId,
            Title = first.Title,
            IsAnnouncement = first.MessageType == TeamMessageType.Announcement,
            MemberUserId = first.SenderUserId,
            Messages = items
        };
    }

    public async Task<int> GetUnreadCountAsync(Guid teamId, string currentUserId)
    {
        if (!await _auth.HasMemberAccessAsync(teamId, currentUserId))
            return 0;

        var isManager = await _auth.HasManagerAccessAsync(teamId, currentUserId);

        if (isManager)
        {
            var managerUserIds = await _db.TeamMembers
                .Where(m => m.TeamId == teamId && (m.Role == TeamRole.Owner || m.Role == TeamRole.Manager))
                .Select(m => m.UserId)
                .ToListAsync();

            // Lãnh đội: số tin nhắn chưa đọc từ các thành viên gửi đến
            return await _db.TeamMessages
                .CountAsync(m => m.TeamId == teamId && m.MessageType == TeamMessageType.Direct && !managerUserIds.Contains(m.SenderUserId) && !m.IsRead);
        }
        else
        {
            // Thành viên thường: số tin nhắn chưa đọc gửi đích danh cho mình
            return await _db.TeamMessages
                .CountAsync(m => m.TeamId == teamId && m.MessageType == TeamMessageType.Direct && m.RecipientUserId == currentUserId && !m.IsRead);
        }
    }

    public async Task<bool> MarkConversationAsReadAsync(Guid teamId, Guid conversationId, string currentUserId)
    {
        var isManager = await _auth.HasManagerAccessAsync(teamId, currentUserId);
        var messages = await _db.TeamMessages
            .Where(m => m.TeamId == teamId && m.ConversationId == conversationId && m.MessageType == TeamMessageType.Direct && !m.IsRead)
            .ToListAsync();

        if (!messages.Any()) return false;

        var managerUserIds = await _db.TeamMembers
            .Where(m => m.TeamId == teamId && (m.Role == TeamRole.Owner || m.Role == TeamRole.Manager))
            .Select(m => m.UserId)
            .ToListAsync();

        bool updated = false;
        if (isManager)
        {
            foreach (var m in messages.Where(m => !managerUserIds.Contains(m.SenderUserId)))
            {
                m.IsRead = true;
                m.ReadAt = DateTime.UtcNow;
                updated = true;
            }
        }
        else
        {
            foreach (var m in messages.Where(m => m.RecipientUserId == currentUserId))
            {
                m.IsRead = true;
                m.ReadAt = DateTime.UtcNow;
                updated = true;
            }
        }

        if (updated)
        {
            await _db.SaveChangesAsync();
        }
        return updated;
    }

    private async Task<(bool Allowed, string? ErrorMessage)> CheckRateLimitAsync(Guid teamId, string senderUserId, string content)
    {
        // 1. Chống gửi lặp nội dung giống hệt trong 60 giây (tránh double-click / duplicate post)
        var duplicateWindow = DateTime.UtcNow.AddSeconds(-60);
        if (await _db.TeamMessages.AnyAsync(m => m.TeamId == teamId && m.SenderUserId == senderUserId && m.Content == content && m.CreatedAt >= duplicateWindow))
        {
            return (false, "Tin nhắn có nội dung tương tự đã được gửi gần đây. Vui lòng không gửi lặp lại.");
        }

        // 2. Cooldown 15 giây giữa 2 tin nhắn bất kỳ của cùng người gửi
        var cooldownTime = DateTime.UtcNow.AddSeconds(-15);
        if (await _db.TeamMessages.AnyAsync(m => m.TeamId == teamId && m.SenderUserId == senderUserId && m.CreatedAt >= cooldownTime))
        {
            return (false, "Bạn đang gửi tin nhắn quá nhanh. Vui lòng chờ 15 giây trước khi gửi tiếp.");
        }

        // 3. Rate limit 10 tin nhắn trong 5 phút
        var rateLimitWindow = DateTime.UtcNow.AddMinutes(-5);
        var recentCount = await _db.TeamMessages.CountAsync(m => m.TeamId == teamId && m.SenderUserId == senderUserId && m.CreatedAt >= rateLimitWindow);
        if (recentCount >= 10)
        {
            return (false, "Bạn đã gửi quá nhiều tin nhắn trong thời gian ngắn. Vui lòng chờ vài phút trước khi gửi tiếp.");
        }

        return (true, null);
    }

    private static string FormatRole(TeamRole role) => role switch
    {
        TeamRole.Owner => "Đội trưởng (Chủ nhiệm)",
        TeamRole.Manager => "Ban cán sự / Đội phó",
        _ => "Cầu thủ / Thành viên"
    };
}
