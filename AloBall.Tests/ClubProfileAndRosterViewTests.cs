using AloBall.Web.Controllers;
using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;
using Xunit;

namespace AloBall.Tests;

public class ClubProfileAndRosterViewTests
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

    private IFormFile CreateMockImageFile(string fileName, string contentType, byte[] headerBytes, int totalLength = 1024)
    {
        var stream = new MemoryStream();
        stream.Write(headerBytes, 0, headerBytes.Length);
        if (totalLength > headerBytes.Length)
        {
            var remaining = new byte[totalLength - headerBytes.Length];
            stream.Write(remaining, 0, remaining.Length);
        }
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    // PNG magic bytes: 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
    private static readonly byte[] PngHeader = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00 };
    // JPEG magic bytes: 0xFF, 0xD8, 0xFF
    private static readonly byte[] JpgHeader = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01 };

    [Fact]
    public async Task LogoUpload_Requires_ManagerAccess_Rejects_UnauthorizedUsers()
    {
        using var db = CreateInMemoryDbContext();
        var userManager = CreateMockUserManager(db);
        var authService = new TeamAuthorizationService(db);

        var owner = new ApplicationUser { Id = "owner-1", UserName = "owner@aloball.vn" };
        var member = new ApplicationUser { Id = "member-1", UserName = "member@aloball.vn" };
        var outsider = new ApplicationUser { Id = "outsider-1", UserName = "outsider@aloball.vn" };
        db.Users.AddRange(owner, member, outsider);

        var team = new Team { Id = Guid.NewGuid(), Name = "Hà Nội Stars", Slug = "hanoi-stars" };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = owner.Id, Role = TeamRole.Owner });
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = member.Id, Role = TeamRole.Member });
        await db.SaveChangesAsync();

        var controller = new TeamsController(db, userManager, authService);
        var mockPng = CreateMockImageFile("logo.png", "image/png", PngHeader);

        // 1. Thành viên thường (Member) thử upload logo -> Bị từ chối 403 Forbidden
        SetUserContext(controller, member);
        var memberResult = await controller.UploadLogo(team.Id, mockPng) as ObjectResult;
        Assert.NotNull(memberResult);
        Assert.Equal(StatusCodes.Status403Forbidden, memberResult.StatusCode);

        // 2. Người ngoài (Outsider) thử upload logo -> Bị từ chối 403 Forbidden
        SetUserContext(controller, outsider);
        var outsiderResult = await controller.UploadLogo(team.Id, mockPng) as ObjectResult;
        Assert.NotNull(outsiderResult);
        Assert.Equal(StatusCodes.Status403Forbidden, outsiderResult.StatusCode);

        // 3. Chủ tịch / Lãnh đội (Owner) upload logo -> Thành công (200 OK)
        SetUserContext(controller, owner);
        var ownerResult = await controller.UploadLogo(team.Id, mockPng) as OkObjectResult;
        Assert.NotNull(ownerResult);

        var updatedTeam = await db.Teams.FindAsync(team.Id);
        Assert.NotNull(updatedTeam?.LogoUrl);
        Assert.StartsWith("/uploads/teams/", updatedTeam.LogoUrl);
    }

    [Fact]
    public async Task LogoUpload_Validates_FileType_And_Size()
    {
        using var db = CreateInMemoryDbContext();
        var userManager = CreateMockUserManager(db);
        var authService = new TeamAuthorizationService(db);

        var owner = new ApplicationUser { Id = "owner-2", UserName = "owner2@aloball.vn" };
        db.Users.Add(owner);

        var team = new Team { Id = Guid.NewGuid(), Name = "Sài Gòn FC", Slug = "saigon-fc" };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = owner.Id, Role = TeamRole.Owner });
        await db.SaveChangesAsync();

        var controller = new TeamsController(db, userManager, authService);
        SetUserContext(controller, owner);

        // 1. File độc hại hoặc sai định dạng (.exe) -> 400 BadRequest
        var exeBytes = Encoding.UTF8.GetBytes("MZ fake executable header");
        var badFile = new FormFile(new MemoryStream(exeBytes), 0, exeBytes.Length, "file", "malware.exe")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/x-msdownload"
        };
        var badExtResult = await controller.UploadLogo(team.Id, badFile) as BadRequestObjectResult;
        Assert.NotNull(badExtResult);

        // 2. File giả mạo extension (.png nhưng nội dung là text rác) -> 400 BadRequest
        var fakePngFile = new FormFile(new MemoryStream(Encoding.UTF8.GetBytes("NOT REAL IMAGE CONTENT")), 0, 22, "file", "fake.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
        var fakeMagicResult = await controller.UploadLogo(team.Id, fakePngFile) as BadRequestObjectResult;
        Assert.NotNull(fakeMagicResult);

        // 3. File dung lượng vượt quá 5MB -> 400 BadRequest
        var oversizeBytes = new byte[6 * 1024 * 1024]; // 6MB
        Array.Copy(PngHeader, oversizeBytes, PngHeader.Length);
        var oversizeFile = new FormFile(new MemoryStream(oversizeBytes), 0, oversizeBytes.Length, "file", "huge.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
        var oversizeResult = await controller.UploadLogo(team.Id, oversizeFile) as BadRequestObjectResult;
        Assert.NotNull(oversizeResult);
    }

    [Fact]
    public async Task LogoDelete_RemovesLogo_OnlyForAuthorizedManager()
    {
        using var db = CreateInMemoryDbContext();
        var userManager = CreateMockUserManager(db);
        var authService = new TeamAuthorizationService(db);

        var manager = new ApplicationUser { Id = "manager-1", UserName = "manager@aloball.vn" };
        var outsider = new ApplicationUser { Id = "outsider-2", UserName = "outsider2@aloball.vn" };
        db.Users.AddRange(manager, outsider);

        var team = new Team { Id = Guid.NewGuid(), Name = "Hải Phòng FC", Slug = "haiphong-fc", LogoUrl = "/uploads/teams/old_logo.png" };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = manager.Id, Role = TeamRole.Manager });
        await db.SaveChangesAsync();

        var controller = new TeamsController(db, userManager, authService);

        // 1. Outsider thử xóa -> 403 Forbidden
        SetUserContext(controller, outsider);
        var outsiderResult = await controller.DeleteLogo(team.Id) as ObjectResult;
        Assert.NotNull(outsiderResult);
        Assert.Equal(StatusCodes.Status403Forbidden, outsiderResult.StatusCode);

        // 2. Manager xóa -> Thành công (LogoUrl về null)
        SetUserContext(controller, manager);
        var managerResult = await controller.DeleteLogo(team.Id) as OkObjectResult;
        Assert.NotNull(managerResult);

        var updatedTeam = await db.Teams.FindAsync(team.Id);
        Assert.Null(updatedTeam?.LogoUrl);
    }

    [Fact]
    public async Task CoverUpload_Applies_Default_70Percent_Opacity_And_Preserves_Existing()
    {
        using var db = CreateInMemoryDbContext();
        var userManager = CreateMockUserManager(db);
        var authService = new TeamAuthorizationService(db);

        var owner = new ApplicationUser { Id = "owner-3", UserName = "owner3@aloball.vn" };
        db.Users.Add(owner);

        // Đội A: Chưa từng có cover và chưa có opacity -> Khi upload phải mặc định 70% (0.7)
        var teamA = new Team { Id = Guid.NewGuid(), Name = "Đà Nẵng City", Slug = "danang-city", CoverOpacity = null };
        // Đội B: Đã tùy chỉnh opacity thành 40% (0.4) -> Khi upload ảnh mới KHÔNG ĐƯỢC ghi đè 70%
        var teamB = new Team { Id = Guid.NewGuid(), Name = "Cần Thơ FC", Slug = "cantho-fc", CoverOpacity = 0.4 };

        db.Teams.AddRange(teamA, teamB);
        db.TeamMembers.Add(new TeamMember { TeamId = teamA.Id, UserId = owner.Id, Role = TeamRole.Owner });
        db.TeamMembers.Add(new TeamMember { TeamId = teamB.Id, UserId = owner.Id, Role = TeamRole.Owner });
        await db.SaveChangesAsync();

        var controller = new TeamsController(db, userManager, authService);
        SetUserContext(controller, owner);

        var mockJpg = CreateMockImageFile("cover.jpg", "image/jpeg", JpgHeader);

        // Upload cho Đội A
        var resultA = await controller.UploadCover(teamA.Id, mockJpg) as OkObjectResult;
        Assert.NotNull(resultA);
        var updatedTeamA = await db.Teams.FindAsync(teamA.Id);
        Assert.NotNull(updatedTeamA?.CoverImageUrl);
        Assert.Equal(0.7, updatedTeamA.CoverOpacity); // Mặc định 70%

        // Upload cho Đội B
        var resultB = await controller.UploadCover(teamB.Id, mockJpg) as OkObjectResult;
        Assert.NotNull(resultB);
        var updatedTeamB = await db.Teams.FindAsync(teamB.Id);
        Assert.NotNull(updatedTeamB?.CoverImageUrl);
        Assert.Equal(0.4, updatedTeamB.CoverOpacity); // Giữ nguyên 40%, không bị ghi đè!
    }

    [Fact]
    public async Task CoverOpacity_Slider_PersistsWithin_0_To_100_Percent()
    {
        using var db = CreateInMemoryDbContext();
        var userManager = CreateMockUserManager(db);
        var authService = new TeamAuthorizationService(db);

        var manager = new ApplicationUser { Id = "manager-2", UserName = "manager2@aloball.vn" };
        db.Users.Add(manager);

        var team = new Team { Id = Guid.NewGuid(), Name = "Bình Dương FC", Slug = "binhduong-fc", CoverImageUrl = "/uploads/teams/cover.jpg", CoverOpacity = 0.7 };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = manager.Id, Role = TeamRole.Manager });
        await db.SaveChangesAsync();

        var controller = new TeamsController(db, userManager, authService);
        SetUserContext(controller, manager);

        // 1. Chỉnh độ mờ về 85%
        var res85 = await controller.UpdateCoverOpacity(team.Id, 85) as OkObjectResult;
        Assert.NotNull(res85);
        var t85 = await db.Teams.FindAsync(team.Id);
        Assert.Equal(0.85, t85?.CoverOpacity);

        // 2. Chỉnh độ mờ về 0% (trong suốt hoàn toàn)
        var res0 = await controller.UpdateCoverOpacity(team.Id, 0) as OkObjectResult;
        Assert.NotNull(res0);
        var t0 = await db.Teams.FindAsync(team.Id);
        Assert.Equal(0.0, t0?.CoverOpacity);

        // 3. Chỉnh độ mờ về 100% (rõ nét nhất)
        var res100 = await controller.UpdateCoverOpacity(team.Id, 100) as OkObjectResult;
        Assert.NotNull(res100);
        var t100 = await db.Teams.FindAsync(team.Id);
        Assert.Equal(1.0, t100?.CoverOpacity);

        // 4. Giá trị ngoài biên (ví dụ: 150%) được clamp về 100% (1.0)
        var resClamp = await controller.UpdateCoverOpacity(team.Id, 150) as OkObjectResult;
        Assert.NotNull(resClamp);
        var tClamp = await db.Teams.FindAsync(team.Id);
        Assert.Equal(1.0, tClamp?.CoverOpacity);
    }

    [Fact]
    public async Task CoverDelete_RemovesCover_OnlyForAuthorizedManager()
    {
        using var db = CreateInMemoryDbContext();
        var userManager = CreateMockUserManager(db);
        var authService = new TeamAuthorizationService(db);

        var owner = new ApplicationUser { Id = "owner-4", UserName = "owner4@aloball.vn" };
        var outsider = new ApplicationUser { Id = "outsider-4", UserName = "outsider4@aloball.vn" };
        db.Users.AddRange(owner, outsider);

        var team = new Team { Id = Guid.NewGuid(), Name = "Viettel FC", Slug = "viettel-fc", CoverImageUrl = "/uploads/teams/cover.jpg" };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = owner.Id, Role = TeamRole.Owner });
        await db.SaveChangesAsync();

        var controller = new TeamsController(db, userManager, authService);

        // 1. Outsider thử xóa -> 403 Forbidden
        SetUserContext(controller, outsider);
        var outsiderResult = await controller.DeleteCover(team.Id) as ObjectResult;
        Assert.NotNull(outsiderResult);
        Assert.Equal(StatusCodes.Status403Forbidden, outsiderResult.StatusCode);

        // 2. Owner xóa -> Thành công (CoverImageUrl về null)
        SetUserContext(controller, owner);
        var ownerResult = await controller.DeleteCover(team.Id) as OkObjectResult;
        Assert.NotNull(ownerResult);

        var updatedTeam = await db.Teams.FindAsync(team.Id);
        Assert.Null(updatedTeam?.CoverImageUrl);
    }

    [Fact]
    public void SafeImageUrl_Validates_AllowedProtocols_And_Rejects_ProtocolRelativeURLs()
    {
        // 1. URL cục bộ an toàn
        Assert.True(CompetitionRules.SafeImageUrl("/uploads/teams/team1_logo.png"));
        Assert.True(CompetitionRules.SafeImageUrl("/images/default-avatar.png"));
        Assert.True(CompetitionRules.SafeImageUrl(null));
        Assert.True(CompetitionRules.SafeImageUrl(""));

        // 2. HTTPS tuyệt đối an toàn
        Assert.True(CompetitionRules.SafeImageUrl("https://cdn.aloball.vn/teams/cover.jpg"));

        // 3. Protocol-relative URL độc hại (bị chặn)
        Assert.False(CompetitionRules.SafeImageUrl("//evil.com/malicious.png"));
        Assert.False(CompetitionRules.SafeImageUrl("/\\evil.com/malicious.png"));

        // 4. HTTP không an toàn và JavaScript pseudo-protocol (bị chặn)
        Assert.False(CompetitionRules.SafeImageUrl("http://insecure-cdn.com/logo.png"));
        Assert.False(CompetitionRules.SafeImageUrl("javascript:alert(1)"));
    }
}

