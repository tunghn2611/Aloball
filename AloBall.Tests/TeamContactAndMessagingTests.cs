using AloBall.Web.Controllers;
using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace AloBall.Tests;

public class TeamContactAndMessagingTests
{
    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private UserManager<ApplicationUser> CreateMockUserManager(ApplicationDbContext db)
    {
        var userStore = new Microsoft.AspNetCore.Identity.EntityFrameworkCore.UserStore<ApplicationUser>(db);
        return new UserManager<ApplicationUser>(
            userStore,
            null!,
            new PasswordHasher<ApplicationUser>(),
            null!,
            null!,
            null!,
            new IdentityErrorDescriber(),
            null!,
            null!);
    }

    private void SetUserContext(Controller controller, ApplicationUser user)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.UserName ?? user.Email ?? "testuser")
        };
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
        controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
            httpContext,
            new TestTempDataProvider());
    }

    private class TestTempDataProvider : Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    [Fact]
    public async Task Member_Can_Contact_Captain_Successfully()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var messagingService = new TeamMessagingService(db, authService);

        var owner = new ApplicationUser { Id = "captain-1", UserName = "captain@aloball.vn", FullName = "Nguyễn Văn Đội Trưởng" };
        var member = new ApplicationUser { Id = "member-1", UserName = "member@aloball.vn", FullName = "Trần Cầu Thủ" };
        db.Users.AddRange(owner, member);

        var team = new Team { Id = Guid.NewGuid(), Name = "Hà Nội Phoenix", Slug = "hanoi-phoenix" };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = owner.Id, Role = TeamRole.Owner });
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = member.Id, Role = TeamRole.Member });
        await db.SaveChangesAsync();

        // Cầu thủ gửi tin nhắn cho Đội trưởng
        var result = await messagingService.SendMemberToCaptainMessageAsync(
            team.Id, member.Id, "Xin phép vắng mặt trận tới", "Thứ 7 này em có việc gia đình xin vắng mặt ạ.");

        Assert.True(result.Success);
        Assert.NotNull(result.ConversationId);

        // Kiểm tra trong cơ sở dữ liệu
        var msg = await db.TeamMessages.FirstOrDefaultAsync(m => m.ConversationId == result.ConversationId);
        Assert.NotNull(msg);
        Assert.Equal(team.Id, msg.TeamId);
        Assert.Equal(member.Id, msg.SenderUserId);
        Assert.Equal(owner.Id, msg.RecipientUserId);
        Assert.Equal(TeamMessageType.Direct, msg.MessageType);
        Assert.False(msg.IsRead);
    }

    [Fact]
    public async Task Captain_Receives_Notification_And_Can_View_And_Reply()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var messagingService = new TeamMessagingService(db, authService);

        var captain = new ApplicationUser { Id = "captain-2", UserName = "captain2@aloball.vn", FullName = "Lê Đội Trưởng" };
        var member = new ApplicationUser { Id = "member-2", UserName = "member2@aloball.vn", FullName = "Phạm Cầu Thủ" };
        db.Users.AddRange(captain, member);

        var team = new Team { Id = Guid.NewGuid(), Name = "Sài Gòn FC", Slug = "saigon-fc" };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = captain.Id, Role = TeamRole.Owner });
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = member.Id, Role = TeamRole.Member });
        await db.SaveChangesAsync();

        // 1. Member gửi tin nhắn
        var sendResult = await messagingService.SendMemberToCaptainMessageAsync(
            team.Id, member.Id, "Đăng ký đổi số áo", "Em muốn đăng ký đổi sang áo số 10 được không ạ?");
        Assert.True(sendResult.Success);
        var conversationId = sendResult.ConversationId!.Value;

        // 2. Kiểm tra số tin chưa đọc của Đội trưởng
        int captainUnread = await messagingService.GetUnreadCountAsync(team.Id, captain.Id);
        Assert.Equal(1, captainUnread);

        // 3. Đội trưởng mở xem chi tiết cuộc trò chuyện -> Tự động đánh dấu đã đọc
        var detail = await messagingService.GetConversationDetailAsync(team.Id, conversationId, captain.Id);
        Assert.NotNull(detail);
        Assert.Single(detail.Messages);

        int captainUnreadAfter = await messagingService.GetUnreadCountAsync(team.Id, captain.Id);
        Assert.Equal(0, captainUnreadAfter);

        // 4. Đội trưởng phản hồi tin nhắn
        var replyResult = await messagingService.ReplyToConversationAsync(
            team.Id, conversationId, captain.Id, "Được em nhé, số 10 hiện đang trống, anh đã lưu.");
        Assert.True(replyResult.Success);

        // 5. Kiểm tra cuộc trò chuyện có 2 tin nhắn và thành viên nhận được thông báo tin chưa đọc
        var detailAfterReply = await messagingService.GetConversationDetailAsync(team.Id, conversationId, member.Id);
        Assert.NotNull(detailAfterReply);
        Assert.Equal(2, detailAfterReply.Messages.Count);

        int memberUnread = await messagingService.GetUnreadCountAsync(team.Id, member.Id);
        Assert.Equal(0, memberUnread); // Vì GetConversationDetailAsync đã tự đánh dấu đã đọc khi xem
    }

    [Fact]
    public async Task Captain_Can_Contact_Individual_Current_Member()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var messagingService = new TeamMessagingService(db, authService);

        var manager = new ApplicationUser { Id = "manager-3", UserName = "manager3@aloball.vn", FullName = "Hoàng Ban Cán Sự" };
        var member = new ApplicationUser { Id = "member-3", UserName = "member3@aloball.vn", FullName = "Vũ Cầu Thủ" };
        db.Users.AddRange(manager, member);

        var team = new Team { Id = Guid.NewGuid(), Name = "Hải Phòng FC", Slug = "haiphong-fc" };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = manager.Id, Role = TeamRole.Manager });
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = member.Id, Role = TeamRole.Member });
        await db.SaveChangesAsync();

        // Ban cán sự chủ động gửi tin cho thành viên
        var result = await messagingService.SendLeadershipToMemberMessageAsync(
            team.Id, manager.Id, member.Id, "Nhắc nhở mang đồng phục phụ", "Trận thứ 4 mang thêm tất đen nhé em.");

        Assert.True(result.Success);
        Assert.NotNull(result.ConversationId);

        // Thành viên có 1 tin chưa đọc
        int memberUnread = await messagingService.GetUnreadCountAsync(team.Id, member.Id);
        Assert.Equal(1, memberUnread);

        var convs = await messagingService.GetConversationsForUserAsync(team.Id, member.Id);
        Assert.Single(convs);
        Assert.Equal("Nhắc nhở mang đồng phục phụ", convs[0].Title);
    }

    [Fact]
    public async Task Captain_Can_Send_Team_Announcement_And_All_Members_Can_Read()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var messagingService = new TeamMessagingService(db, authService);

        var captain = new ApplicationUser { Id = "captain-4", UserName = "captain4@aloball.vn" };
        var member1 = new ApplicationUser { Id = "member-4a", UserName = "m4a@aloball.vn" };
        var member2 = new ApplicationUser { Id = "member-4b", UserName = "m4b@aloball.vn" };
        db.Users.AddRange(captain, member1, member2);

        var team = new Team { Id = Guid.NewGuid(), Name = "Đà Nẵng United", Slug = "danang-united" };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = captain.Id, Role = TeamRole.Owner });
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = member1.Id, Role = TeamRole.Member });
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = member2.Id, Role = TeamRole.Member });
        await db.SaveChangesAsync();

        // 1. Đội trưởng đăng thông báo
        var result = await messagingService.SendTeamAnnouncementAsync(
            team.Id, captain.Id, "Lịch thi đấu tuần này", "Thứ 4: Sân Tuyên Sơn lúc 19h. Thứ 7: Giao lưu nội bộ.");
        Assert.True(result.Success);

        // 2. Cả 2 thành viên đều đọc được thông báo
        var ann1 = await messagingService.GetTeamAnnouncementsAsync(team.Id, member1.Id);
        var ann2 = await messagingService.GetTeamAnnouncementsAsync(team.Id, member2.Id);

        Assert.Single(ann1);
        Assert.Single(ann2);
        Assert.Equal("Lịch thi đấu tuần này", ann1[0].Title);
        Assert.Equal("Lịch thi đấu tuần này", ann2[0].Title);
    }

    [Fact]
    public async Task Regular_Member_Cannot_Send_Team_Announcement()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var messagingService = new TeamMessagingService(db, authService);

        var member = new ApplicationUser { Id = "member-5", UserName = "member5@aloball.vn" };
        db.Users.Add(member);

        var team = new Team { Id = Guid.NewGuid(), Name = "Cần Thơ FC", Slug = "cantho-fc" };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = member.Id, Role = TeamRole.Member });
        await db.SaveChangesAsync();

        // Cố gắng đăng thông báo với quyền thành viên thường -> Bị từ chối
        var result = await messagingService.SendTeamAnnouncementAsync(
            team.Id, member.Id, "Thông báo giả mạo", "Nội dung spam...");

        Assert.False(result.Success);
        Assert.Contains("Chỉ Đội trưởng hoặc Ban cán sự", result.Message);
    }

    [Fact]
    public async Task NonMember_Or_ExMember_Cannot_Send_Or_Read_Team_Messages()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var messagingService = new TeamMessagingService(db, authService);

        var outsider = new ApplicationUser { Id = "outsider-6", UserName = "outsider@aloball.vn" };
        var exMember = new ApplicationUser { Id = "exmember-6", UserName = "exmember@aloball.vn" };
        var captain = new ApplicationUser { Id = "captain-6", UserName = "captain6@aloball.vn" };
        db.Users.AddRange(outsider, exMember, captain);

        var team = new Team { Id = Guid.NewGuid(), Name = "Bình Dương FC", Slug = "binhduong-fc" };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = captain.Id, Role = TeamRole.Owner });
        // exMember từng là thành viên nhưng đã rời đội (bị xóa khỏi TeamMembers)
        await db.SaveChangesAsync();

        // 1. Outsider thử gửi tin nhắn
        var outSend = await messagingService.SendMemberToCaptainMessageAsync(team.Id, outsider.Id, "Alo", "Nội dung");
        Assert.False(outSend.Success);
        Assert.Contains("không phải là thành viên", outSend.Message);

        // 2. ExMember thử gửi tin nhắn
        var exSend = await messagingService.SendMemberToCaptainMessageAsync(team.Id, exMember.Id, "Alo", "Nội dung");
        Assert.False(exSend.Success);
        Assert.Contains("không phải là thành viên", exSend.Message);

        // 3. Outsider thử đọc danh sách cuộc trò chuyện
        var outConvs = await messagingService.GetConversationsForUserAsync(team.Id, outsider.Id);
        Assert.Empty(outConvs);

        // 4. Outsider thử đọc thông báo đội
        var outAnns = await messagingService.GetTeamAnnouncementsAsync(team.Id, outsider.Id);
        Assert.Empty(outAnns);
    }

    [Fact]
    public async Task Other_Member_Cannot_Access_Private_Member_Conversation()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var messagingService = new TeamMessagingService(db, authService);

        var captain = new ApplicationUser { Id = "captain-7", UserName = "captain7@aloball.vn" };
        var memberA = new ApplicationUser { Id = "member-7a", UserName = "m7a@aloball.vn" };
        var memberB = new ApplicationUser { Id = "member-7b", UserName = "m7b@aloball.vn" };
        db.Users.AddRange(captain, memberA, memberB);

        var team = new Team { Id = Guid.NewGuid(), Name = "Viettel FC", Slug = "viettel-fc" };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = captain.Id, Role = TeamRole.Owner });
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = memberA.Id, Role = TeamRole.Member });
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = memberB.Id, Role = TeamRole.Member });
        await db.SaveChangesAsync();

        // Member A gửi tin nhắn riêng cho Đội trưởng
        var sendResult = await messagingService.SendMemberToCaptainMessageAsync(
            team.Id, memberA.Id, "Việc cá nhân của A", "Em có chuyện riêng muốn thưa với Đội trưởng.");
        Assert.True(sendResult.Success);
        var convId = sendResult.ConversationId!.Value;

        // Member B cố tình truy cập cuộc trò chuyện riêng giữa A và Đội trưởng (chống IDOR)
        var detailB = await messagingService.GetConversationDetailAsync(team.Id, convId, memberB.Id);
        Assert.Null(detailB); // Bị chặn!

        // Member B cố tình gửi câu trả lời vào cuộc trò chuyện của A
        var replyB = await messagingService.ReplyToConversationAsync(team.Id, convId, memberB.Id, "Em hóng hớt");
        Assert.False(replyB.Success);
        Assert.Contains("không có quyền tham gia", replyB.Message);
    }

    [Fact]
    public async Task AntiSpam_RateLimit_Prevents_Rapid_Fire_Messages()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var messagingService = new TeamMessagingService(db, authService);

        var captain = new ApplicationUser { Id = "captain-8", UserName = "captain8@aloball.vn" };
        var member = new ApplicationUser { Id = "member-8", UserName = "member8@aloball.vn" };
        db.Users.AddRange(captain, member);

        var team = new Team { Id = Guid.NewGuid(), Name = "HAGL FC", Slug = "hagl-fc" };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = captain.Id, Role = TeamRole.Owner });
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = member.Id, Role = TeamRole.Member });
        await db.SaveChangesAsync();

        // Tin nhắn thứ 1: Thành công
        var res1 = await messagingService.SendMemberToCaptainMessageAsync(
            team.Id, member.Id, "Tin nhắn 1", "Nội dung 1");
        Assert.True(res1.Success);

        // Tin nhắn thứ 2 được gửi ngay lập tức (< 15 giây): Bị chặn bởi Anti-spam
        var res2 = await messagingService.SendMemberToCaptainMessageAsync(
            team.Id, member.Id, "Tin nhắn 2", "Nội dung 2");
        Assert.False(res2.Success);
        Assert.Contains("quá nhanh", res2.Message);
    }

    [Fact]
    public async Task InputValidation_Rejects_Empty_Or_Overlength_Content()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var messagingService = new TeamMessagingService(db, authService);

        var captain = new ApplicationUser { Id = "captain-9", UserName = "captain9@aloball.vn" };
        var member = new ApplicationUser { Id = "member-9", UserName = "member9@aloball.vn" };
        db.Users.AddRange(captain, member);

        var team = new Team { Id = Guid.NewGuid(), Name = "SLNA FC", Slug = "slna-fc" };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = captain.Id, Role = TeamRole.Owner });
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = member.Id, Role = TeamRole.Member });
        await db.SaveChangesAsync();

        // 1. Tiêu đề trống
        var resEmptyTitle = await messagingService.SendMemberToCaptainMessageAsync(team.Id, member.Id, "   ", "Nội dung");
        Assert.False(resEmptyTitle.Success);

        // 2. Nội dung trống
        var resEmptyContent = await messagingService.SendMemberToCaptainMessageAsync(team.Id, member.Id, "Tiêu đề", "   ");
        Assert.False(resEmptyContent.Success);

        // 3. Tiêu đề quá 200 ký tự
        var longTitle = new string('A', 201);
        var resLongTitle = await messagingService.SendMemberToCaptainMessageAsync(team.Id, member.Id, longTitle, "Nội dung");
        Assert.False(resLongTitle.Success);

        // 4. Nội dung quá 2000 ký tự
        var longContent = new string('B', 2001);
        var resLongContent = await messagingService.SendMemberToCaptainMessageAsync(team.Id, member.Id, "Tiêu đề", longContent);
        Assert.False(resLongContent.Success);
    }

    [Fact]
    public async Task Captain_Cannot_Send_Direct_Message_To_NonMember()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var messagingService = new TeamMessagingService(db, authService);

        var captain = new ApplicationUser { Id = "captain-10", UserName = "captain10@aloball.vn" };
        var outsider = new ApplicationUser { Id = "outsider-10", UserName = "outsider10@aloball.vn" };
        db.Users.AddRange(captain, outsider);

        var team = new Team { Id = Guid.NewGuid(), Name = "Nam Định FC", Slug = "namdinh-fc" };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = captain.Id, Role = TeamRole.Owner });
        await db.SaveChangesAsync();

        // Đội trưởng thử gửi tin nhắn cho người không thuộc đội
        var result = await messagingService.SendLeadershipToMemberMessageAsync(
            team.Id, captain.Id, outsider.Id, "Lời mời vào đội", "Nội dung...");

        Assert.False(result.Success);
        Assert.Contains("không phải là thành viên hiện tại", result.Message);
    }

    [Fact]
    public async Task Leader_Cannot_Send_Direct_Message_To_Self()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var messagingService = new TeamMessagingService(db, authService);

        var captain = new ApplicationUser { Id = "captain-self", UserName = "captain_self@aloball.vn" };
        db.Users.Add(captain);

        var team = new Team { Id = Guid.NewGuid(), Name = "Self FC", Slug = "self-fc" };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = captain.Id, Role = TeamRole.Owner });
        await db.SaveChangesAsync();

        var result = await messagingService.SendLeadershipToMemberMessageAsync(
            team.Id, captain.Id, captain.Id, "Tự gửi cho mình", "Nội dung");

        Assert.False(result.Success);
        Assert.Contains("Không thể gửi tin nhắn lãnh đội cho chính mình", result.Message);
    }

    [Fact]
    public async Task MultiManager_Unread_Count_Does_Not_Count_Other_Manager_Messages()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var messagingService = new TeamMessagingService(db, authService);

        var owner = new ApplicationUser { Id = "owner-multi", UserName = "owner@aloball.vn" };
        var manager = new ApplicationUser { Id = "mgr-multi", UserName = "mgr@aloball.vn" };
        var member = new ApplicationUser { Id = "member-multi", UserName = "mbr@aloball.vn" };
        db.Users.AddRange(owner, manager, member);

        var team = new Team { Id = Guid.NewGuid(), Name = "Multi Leader FC", Slug = "multi-leader" };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = owner.Id, Role = TeamRole.Owner });
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = manager.Id, Role = TeamRole.Manager });
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = member.Id, Role = TeamRole.Member });
        await db.SaveChangesAsync();

        // 1. Manager gửi tin nhắn cho member
        var sendMgrResult = await messagingService.SendLeadershipToMemberMessageAsync(
            team.Id, manager.Id, member.Id, "Nhắc nhở từ quản lý", "Nội dung...");
        Assert.True(sendMgrResult.Success);

        // Owner kiểm tra unread count: Không được tính tin nhắn do manager khác gửi là "tin chưa đọc"
        int ownerUnread = await messagingService.GetUnreadCountAsync(team.Id, owner.Id);
        Assert.Equal(0, ownerUnread);

        // Member phải có 1 tin chưa đọc
        int memberUnread = await messagingService.GetUnreadCountAsync(team.Id, member.Id);
        Assert.Equal(1, memberUnread);
    }

    [Fact]
    public async Task AntiSpam_RateLimit_Prevents_Duplicate_Content()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);
        var messagingService = new TeamMessagingService(db, authService);

        var captain = new ApplicationUser { Id = "captain-dup", UserName = "captain_dup@aloball.vn" };
        var member = new ApplicationUser { Id = "member-dup", UserName = "member_dup@aloball.vn" };
        db.Users.AddRange(captain, member);

        var team = new Team { Id = Guid.NewGuid(), Name = "Dup Check FC", Slug = "dup-check" };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = captain.Id, Role = TeamRole.Owner });
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = member.Id, Role = TeamRole.Member });
        await db.SaveChangesAsync();

        var res1 = await messagingService.SendMemberToCaptainMessageAsync(
            team.Id, member.Id, "Tiêu đề trùng", "Nội dung hoàn toàn trùng khớp");
        Assert.True(res1.Success);

        // Thử gửi cùng nội dung
        var resDup = await messagingService.SendMemberToCaptainMessageAsync(
            team.Id, member.Id, "Tiêu đề khác", "Nội dung hoàn toàn trùng khớp");
        Assert.False(resDup.Success);
        Assert.Contains("Tin nhắn có nội dung tương tự đã được gửi", resDup.Message);
    }
}
