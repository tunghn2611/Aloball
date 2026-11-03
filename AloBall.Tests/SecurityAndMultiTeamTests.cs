using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AloBall.Tests;

public class SecurityAndMultiTeamTests
{
    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Member_Does_Not_Have_Manager_Access()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);

        var user = new ApplicationUser { Id = "u1", UserName = "member@test.vn" };
        var team = new Team { Name = "FC Test", Slug = "fc-test" };
        db.Users.Add(user);
        db.Teams.Add(team);

        db.TeamMembers.Add(new TeamMember
        {
            TeamId = team.Id,
            UserId = user.Id,
            Role = TeamRole.Member
        });
        await db.SaveChangesAsync();

        var isManager = await authService.HasManagerAccessAsync(team.Id, user.Id);
        var isMember = await authService.HasMemberAccessAsync(team.Id, user.Id);
        var isOwner = await authService.IsOwnerAsync(team.Id, user.Id);

        Assert.False(isManager, "Thành viên thường không được có quyền quản lý.");
        Assert.True(isMember, "Thành viên thường phải có quyền truy cập khu vực nội bộ cơ bản.");
        Assert.False(isOwner, "Thành viên thường không phải là chủ sở hữu.");
    }

    [Fact]
    public async Task User_In_TeamA_Cannot_Access_TeamB_Internal_Data()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);

        var userA = new ApplicationUser { Id = "ua", UserName = "userA@test.vn" };
        var teamA = new Team { Name = "Đội A", Slug = "doi-a" };
        var teamB = new Team { Name = "Đội B", Slug = "doi-b" };

        db.Users.Add(userA);
        db.Teams.AddRange(teamA, teamB);

        // User A là Manager của Đội A, hoàn toàn không thuộc Đội B
        db.TeamMembers.Add(new TeamMember
        {
            TeamId = teamA.Id,
            UserId = userA.Id,
            Role = TeamRole.Manager
        });
        await db.SaveChangesAsync();

        var hasAccessToTeamA = await authService.HasManagerAccessAsync(teamA.Id, userA.Id);
        var hasAccessToTeamB = await authService.HasManagerAccessAsync(teamB.Id, userA.Id);
        var hasMemberAccessToTeamB = await authService.HasMemberAccessAsync(teamB.Id, userA.Id);

        Assert.True(hasAccessToTeamA);
        Assert.False(hasAccessToTeamB, "Quản lý Đội A không được có quyền quản lý Đội B!");
        Assert.False(hasMemberAccessToTeamB, "Người dùng Đội A không được có quyền xem dữ liệu nội bộ Đội B!");
    }

    [Fact]
    public async Task PlatformAdmin_Has_Full_Access_Across_Teams()
    {
        using var db = CreateInMemoryDbContext();
        var authService = new TeamAuthorizationService(db);

        var adminUser = new ApplicationUser { Id = "admin", UserName = "admin@aloball.vn" };
        var team = new Team { Name = "Đội Bất Kỳ", Slug = "bat-ky" };
        db.Users.Add(adminUser);
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        var isPlatformAdmin = await authService.IsPlatformAdminAsync(adminUser.Id);
        var hasManagerAccess = await authService.HasManagerAccessAsync(team.Id, adminUser.Id);

        Assert.True(isPlatformAdmin);
        Assert.True(hasManagerAccess, "PlatformAdmin có toàn quyền quản trị và xử lý trên các đội.");
    }
}
