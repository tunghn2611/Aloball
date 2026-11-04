using AloBall.Web.Controllers;
using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace AloBall.Tests;

public class PlayerProfileAndOnboardingTests
{
    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private (UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager) CreateMockIdentityManagers(ApplicationDbContext db)
    {
        var userStore = new Microsoft.AspNetCore.Identity.EntityFrameworkCore.UserStore<ApplicationUser>(db);
        var userManager = new UserManager<ApplicationUser>(
            userStore,
            null!,
            new PasswordHasher<ApplicationUser>(),
            null!,
            null!,
            null!,
            new IdentityErrorDescriber(),
            null!,
            null!);

        var contextAccessor = new HttpContextAccessor();
        var claimsFactory = new UserClaimsPrincipalFactory<ApplicationUser>(userManager, Microsoft.Extensions.Options.Options.Create(new IdentityOptions()));
        var signInManager = new SignInManager<ApplicationUser>(
            userManager,
            contextAccessor,
            claimsFactory,
            null!,
            null!,
            null!,
            null!);

        return (userManager, signInManager);
    }

    private void SetUserContext(Controller controller, ApplicationUser user)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.UserName ?? user.Email ?? "testuser")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
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

    // 1. Profile fields are persisted and retrieved correctly
    [Fact]
    public async Task ProfileFields_ArePersistedAndRetrievedCorrectly()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, _) = CreateMockIdentityManagers(db);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "player1@aloball.vn",
            Email = "player1@aloball.vn",
            FullName = "Nguyễn Văn Cầu Thủ",
            PreferredFoot = "Phải",
            Experience = "3 - 5 năm",
            Region = "Cầu Giấy, Hà Nội",
            PrimaryPosition = "Tiền đạo",
            HasCompletedOnboarding = true,
            OnboardingCompletedAt = DateTime.UtcNow
        };
        await userManager.CreateAsync(user);

        var team = new Team { Name = "FC Thử Nghiệm", Slug = "fc-thu-nghiem" };
        db.Teams.Add(team);

        var player = new Player
        {
            TeamId = team.Id,
            LinkedUserId = user.Id,
            FullName = user.FullName,
            PreferredFoot = user.PreferredFoot,
            Experience = user.Experience,
            Region = user.Region,
            Position = user.PrimaryPosition,
            PublicPreferredFoot = false,
            PublicExperience = false
        };
        db.Players.Add(player);
        await db.SaveChangesAsync();

        // Retrieve and verify
        var retrievedUser = await userManager.FindByIdAsync(user.Id);
        Assert.NotNull(retrievedUser);
        Assert.Equal("Phải", retrievedUser!.PreferredFoot);
        Assert.Equal("3 - 5 năm", retrievedUser.Experience);
        Assert.Equal("Cầu Giấy, Hà Nội", retrievedUser.Region);
        Assert.Equal("Tiền đạo", retrievedUser.PrimaryPosition);
        Assert.True(retrievedUser.HasCompletedOnboarding);
        Assert.NotNull(retrievedUser.OnboardingCompletedAt);

        var retrievedPlayer = await db.Players.FirstOrDefaultAsync(p => p.LinkedUserId == user.Id);
        Assert.NotNull(retrievedPlayer);
        Assert.Equal("Phải", retrievedPlayer!.PreferredFoot);
        Assert.Equal("3 - 5 năm", retrievedPlayer.Experience);
        Assert.Equal("Cầu Giấy, Hà Nội", retrievedPlayer.Region);
        Assert.False(retrievedPlayer.PublicPreferredFoot);
        Assert.False(retrievedPlayer.PublicExperience);
    }

    // 2. Invalid profile values are rejected during onboarding
    [Fact]
    public async Task Onboarding_InvalidValues_AreRejected_AndStatusNotUpdated()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, signInManager) = CreateMockIdentityManagers(db);
        var authService = new TeamAuthorizationService(db);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "newuser@aloball.vn",
            Email = "newuser@aloball.vn",
            FullName = "Tân Binh",
            HasCompletedOnboarding = false
        };
        await userManager.CreateAsync(user);

        var controller = new AccountController(signInManager, userManager, db, authService);
        SetUserContext(controller, user);

        // Missing required fields (PreferredFoot, Experience, Region)
        var invalidModel = new OnboardingViewModel
        {
            FullName = "Tân Binh",
            PreferredFoot = "", // Invalid: required
            Experience = "",    // Invalid: required
            Region = ""         // Invalid: required
        };

        var result = await controller.Onboarding(invalidModel);
        Assert.IsType<ViewResult>(result);

        // Verify status remains incomplete
        var refreshedUser = await userManager.FindByIdAsync(user.Id);
        Assert.False(refreshedUser!.HasCompletedOnboarding);
        Assert.Null(refreshedUser.OnboardingCompletedAt);
    }

    // 3. Onboarding completion requires valid required information
    [Fact]
    public async Task Onboarding_ValidValues_UpdatesStatusAndSyncsLinkedPlayers()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, signInManager) = CreateMockIdentityManagers(db);
        var authService = new TeamAuthorizationService(db);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "completeuser@aloball.vn",
            Email = "completeuser@aloball.vn",
            FullName = "Cầu Thủ Mới",
            HasCompletedOnboarding = false
        };
        await userManager.CreateAsync(user);

        var team = new Team { Name = "FC AloBall", Slug = "fc-aloball" };
        db.Teams.Add(team);
        var linkedPlayer = new Player
        {
            TeamId = team.Id,
            LinkedUserId = user.Id,
            FullName = user.FullName
        };
        db.Players.Add(linkedPlayer);
        await db.SaveChangesAsync();

        var controller = new AccountController(signInManager, userManager, db, authService);
        SetUserContext(controller, user);

        var validModel = new OnboardingViewModel
        {
            FullName = "Cầu Thủ Chuẩn",
            PreferredFoot = "Trái",
            Experience = "1 - 3 năm",
            Region = "Đống Đa, Hà Nội",
            PrimaryPosition = "Tiền vệ"
        };

        var result = await controller.Onboarding(validModel);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Teams", redirect.ControllerName);

        // Verify user updated
        var refreshedUser = await userManager.FindByIdAsync(user.Id);
        Assert.True(refreshedUser!.HasCompletedOnboarding);
        Assert.NotNull(refreshedUser.OnboardingCompletedAt);
        Assert.Equal("Trái", refreshedUser.PreferredFoot);
        Assert.Equal("1 - 3 năm", refreshedUser.Experience);
        Assert.Equal("Đống Đa, Hà Nội", refreshedUser.Region);

        // Verify linked player synced
        var refreshedPlayer = await db.Players.FindAsync(linkedPlayer.Id);
        Assert.Equal("Cầu Thủ Chuẩn", refreshedPlayer!.FullName);
        Assert.Equal("Trái", refreshedPlayer.PreferredFoot);
        Assert.Equal("1 - 3 năm", refreshedPlayer.Experience);
        Assert.Equal("Đống Đa, Hà Nội", refreshedPlayer.Region);
        Assert.Equal("Tiền vệ", refreshedPlayer.Position);
    }

    // 4. Users cannot modify another user's profile unless PlatformAdmin
    [Fact]
    public async Task RegularUser_CannotModifyAnotherUserProfile_ReturnsForbid()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, signInManager) = CreateMockIdentityManagers(db);
        var authService = new TeamAuthorizationService(db);

        var userA = new ApplicationUser { Id = "user-a", UserName = "usera@aloball.vn", Email = "usera@aloball.vn", FullName = "User A" };
        var userB = new ApplicationUser { Id = "user-b", UserName = "userb@aloball.vn", Email = "userb@aloball.vn", FullName = "User B" };
        await userManager.CreateAsync(userA);
        await userManager.CreateAsync(userB);

        var controller = new AccountController(signInManager, userManager, db, authService);
        SetUserContext(controller, userA);

        // User A attempts to edit User B's profile
        var attackModel = new UserProfileViewModel
        {
            UserId = userB.Id,
            FullName = "Hacked Name",
            PreferredFoot = "Trái"
        };

        var postResult = await controller.Profile(attackModel);
        Assert.IsType<ForbidResult>(postResult);

        var getResult = await controller.Profile(userB.Id);
        Assert.IsType<ForbidResult>(getResult);

        // Verify User B's data is untouched
        var userBAfter = await userManager.FindByIdAsync(userB.Id);
        Assert.Equal("User B", userBAfter!.FullName);
    }

    // 5. Platform Admin can inspect/manage another user's profile
    [Fact]
    public async Task PlatformAdmin_CanAccessAndModifyAnyUserProfile()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, signInManager) = CreateMockIdentityManagers(db);
        var authService = new TeamAuthorizationService(db);

        var adminUser = new ApplicationUser { Id = "admin", UserName = "admin@aloball.vn", Email = "admin@aloball.vn", FullName = "Admin" };
        var targetUser = new ApplicationUser { Id = "target", UserName = "target@aloball.vn", Email = "target@aloball.vn", FullName = "Target User" };
        await userManager.CreateAsync(adminUser);
        await userManager.CreateAsync(targetUser);

        var controller = new AccountController(signInManager, userManager, db, authService);
        SetUserContext(controller, adminUser);

        var editModel = new UserProfileViewModel
        {
            UserId = targetUser.Id,
            FullName = "Target User Updated",
            PreferredFoot = "Hai chân",
            Experience = "Trên 5 năm",
            Region = "Thanh Xuân, Hà Nội"
        };

        var result = await controller.Profile(editModel);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AccountController.Profile), redirect.ActionName);

        var refreshedTarget = await userManager.FindByIdAsync(targetUser.Id);
        Assert.Equal("Target User Updated", refreshedTarget!.FullName);
        Assert.Equal("Hai chân", refreshedTarget.PreferredFoot);
        Assert.Equal("Trên 5 năm", refreshedTarget.Experience);
        Assert.Equal("Thanh Xuân, Hà Nội", refreshedTarget.Region);
    }

    // 6. Existing users with incomplete profiles remain compatible
    [Fact]
    public async Task ExistingUsers_WithIncompleteProfiles_RemainCompatible()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, signInManager) = CreateMockIdentityManagers(db);
        var authService = new TeamAuthorizationService(db);

        // Legacy user with null profile fields and HasCompletedOnboarding = false
        var legacyUser = new ApplicationUser
        {
            Id = "legacy-user",
            UserName = "legacy@aloball.vn",
            Email = "legacy@aloball.vn",
            FullName = "Cầu Thủ Cũ",
            PreferredFoot = null,
            Experience = null,
            Region = null,
            HasCompletedOnboarding = false,
            OnboardingCompletedAt = null
        };
        await userManager.CreateAsync(legacyUser);

        var controller = new AccountController(signInManager, userManager, db, authService);
        SetUserContext(controller, legacyUser);

        // GET Profile should load without error
        var profileResult = await controller.Profile();
        var viewResult = Assert.IsType<ViewResult>(profileResult);
        var model = Assert.IsType<UserProfileViewModel>(viewResult.Model);
        Assert.Equal("Cầu Thủ Cũ", model.FullName);
        Assert.False(model.HasCompletedOnboarding);
        Assert.Null(model.PreferredFoot);

        // GET Onboarding should load without error
        var onboardingResult = await controller.Onboarding();
        var onboardingView = Assert.IsType<ViewResult>(onboardingResult);
        var onboardingModel = Assert.IsType<OnboardingViewModel>(onboardingView.Model);
        Assert.Equal("Cầu Thủ Cũ", onboardingModel.FullName);
        Assert.Empty(onboardingModel.PreferredFoot);
    }

    // 7. Privacy: Public profile does not expose private information by default
    [Fact]
    public async Task PublicProfile_KeepsPrivateFieldsHiddenByDefault()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, _) = CreateMockIdentityManagers(db);
        var authService = new TeamAuthorizationService(db);

        var team = new Team
        {
            Id = Guid.NewGuid(),
            Name = "FC Bảo Mật",
            Slug = "fc-bao-mat",
            IsPublic = true,
            IsDraft = false
        };
        db.Teams.Add(team);

        var player = new Player
        {
            TeamId = team.Id,
            FullName = "Nguyễn Văn Bảo Mật",
            KitNumber = 10,
            Position = "Tiền đạo",
            PreferredFoot = "Trái",
            Experience = "10 năm chuyên nghiệp",
            Region = "Khu Vực Riêng Tư",
            HeightCm = 180,
            WeightKg = 75,
            IsActive = true,
            IsPublicProfile = true,
            PublicKitNumber = true,
            PublicPosition = true,
            PublicHeightWeight = false,  // Private by default
            PublicPreferredFoot = false, // Private by default
            PublicExperience = false     // Private by default
        };
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var controller = new TeamsController(db, userManager, authService);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
        };
        var result = await controller.PublicProfile(team.Slug);
        var viewResult = Assert.IsType<ViewResult>(result);
        var loadedTeam = Assert.IsType<Team>(viewResult.Model);

        var loadedPlayer = loadedTeam.Players.First();
        Assert.False(loadedPlayer.PublicPreferredFoot, "Chân thuận phải ẩn mặc định.");
        Assert.False(loadedPlayer.PublicExperience, "Kinh nghiệm phải ẩn mặc định.");
        Assert.False(loadedPlayer.PublicHeightWeight, "Chiều cao cân nặng phải ẩn mặc định.");
    }

    // 8. Updating personal PrimaryPosition does not overwrite existing team-assigned position (Bug B)
    [Fact]
    public async Task Updating_Personal_PrimaryPosition_DoesNotOverwrite_ExistingTeamPosition()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, signInManager) = CreateMockIdentityManagers(db);
        var authService = new TeamAuthorizationService(db);

        var user = new ApplicationUser
        {
            Id = "user-team-player",
            UserName = "player@aloball.vn",
            Email = "player@aloball.vn",
            FullName = "Nguyễn Văn Cầu Thủ",
            PrimaryPosition = "Tiền vệ"
        };
        await userManager.CreateAsync(user);

        var team = new Team { Name = "FC Test", Slug = "fc-test" };
        db.Teams.Add(team);

        // Manager previously assigned "Trung vệ" to this player on the team roster
        var player = new Player
        {
            TeamId = team.Id,
            LinkedUserId = user.Id,
            FullName = user.FullName,
            Position = "Trung vệ"
        };
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var controller = new AccountController(signInManager, userManager, db, authService);
        SetUserContext(controller, user);

        // User updates their personal profile setting PrimaryPosition to "Tiền đạo"
        var profileInput = new UserProfileViewModel
        {
            UserId = user.Id,
            FullName = "Nguyễn Văn Cầu Thủ",
            PreferredFoot = "Phải",
            Experience = "3 năm",
            Region = "Hà Nội",
            PrimaryPosition = "Tiền đạo"
        };

        var result = await controller.Profile(profileInput);
        Assert.IsType<RedirectToActionResult>(result);

        // Verify ApplicationUser profile was updated
        var updatedUser = await userManager.FindByIdAsync(user.Id);
        Assert.Equal("Tiền đạo", updatedUser!.PrimaryPosition);

        // Verify team roster player position was NOT overwritten and remains "Trung vệ"
        var rosterPlayer = await db.Players.FirstAsync(p => p.Id == player.Id);
        Assert.Equal("Trung vệ", rosterPlayer.Position);
    }

    // 9. Unassigned team position receives initial synchronization when user sets PrimaryPosition
    [Fact]
    public async Task Unassigned_PlayerPosition_Receives_Initial_Synchronization()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, signInManager) = CreateMockIdentityManagers(db);
        var authService = new TeamAuthorizationService(db);

        var user = new ApplicationUser
        {
            Id = "user-unassigned-player",
            UserName = "unassigned@aloball.vn",
            Email = "unassigned@aloball.vn",
            FullName = "Cầu Thủ Mới"
        };
        await userManager.CreateAsync(user);

        var team = new Team { Name = "FC Unassigned", Slug = "fc-unassigned" };
        db.Teams.Add(team);

        // Player roster with null / "Chưa chọn" position
        var player1 = new Player { TeamId = team.Id, LinkedUserId = user.Id, FullName = user.FullName, Position = null };
        var player2 = new Player { TeamId = team.Id, LinkedUserId = user.Id, FullName = user.FullName, Position = "Chưa chọn" };
        db.Players.AddRange(player1, player2);
        await db.SaveChangesAsync();

        var controller = new AccountController(signInManager, userManager, db, authService);
        SetUserContext(controller, user);

        var profileInput = new UserProfileViewModel
        {
            UserId = user.Id,
            FullName = "Cầu Thủ Mới",
            PreferredFoot = "Trái",
            Experience = "1 năm",
            Region = "TPHCM",
            PrimaryPosition = "Hậu vệ biên"
        };

        await controller.Profile(profileInput);

        // Both unassigned roster positions should now receive "Hậu vệ biên"
        var p1Updated = await db.Players.FindAsync(player1.Id);
        var p2Updated = await db.Players.FindAsync(player2.Id);
        Assert.Equal("Hậu vệ biên", p1Updated!.Position);
        Assert.Equal("Hậu vệ biên", p2Updated!.Position);
    }

    // 10. Team manager can edit Player position independently via PlayerEditor
    [Fact]
    public async Task TeamManager_PositionEditing_RemainsFunctional()
    {
        using var db = CreateInMemoryDbContext();
        var (userManager, _) = CreateMockIdentityManagers(db);
        var authService = new TeamAuthorizationService(db);

        var managerUser = new ApplicationUser { Id = "mgr-1", UserName = "mgr@aloball.vn", Email = "mgr@aloball.vn" };
        var playerUser = new ApplicationUser { Id = "ply-1", UserName = "ply@aloball.vn", Email = "ply@aloball.vn", PrimaryPosition = "Tiền đạo" };
        await userManager.CreateAsync(managerUser);
        await userManager.CreateAsync(playerUser);

        var team = new Team { Name = "FC Quản Lý", Slug = "fc-quan-ly" };
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        // Add manager membership
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = managerUser.Id, Role = TeamRole.Manager });

        // Add player
        var player = new Player
        {
            TeamId = team.Id,
            LinkedUserId = playerUser.Id,
            FullName = "Cầu Thủ Đội",
            Position = "Tiền đạo"
        };
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var controller = new TeamsController(db, userManager, authService);
        SetUserContext(controller, managerUser);

        // Manager changes position to "Thủ môn"
        var editInput = new PlayerProfileInput
        {
            Id = player.Id,
            TeamId = team.Id,
            FullName = "Cầu Thủ Đội",
            Position = "Thủ môn",
            IsActive = true
        };

        var result = await controller.PlayerEditor(editInput);
        Assert.IsType<RedirectToActionResult>(result);

        // Verify player position updated on team roster
        var updatedPlayer = await db.Players.FindAsync(player.Id);
        Assert.Equal("Thủ môn", updatedPlayer!.Position);

        // Verify user's personal PrimaryPosition was untouched
        var userAfter = await userManager.FindByIdAsync(playerUser.Id);
        Assert.Equal("Tiền đạo", userAfter!.PrimaryPosition);
    }
}
