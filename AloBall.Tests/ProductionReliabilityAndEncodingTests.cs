using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;
using AloBall.Web.Controllers;
using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace AloBall.Tests;

public class ProductionReliabilityAndEncodingTests
{
    [Fact]
    public async Task SeedDemoDataAsync_SeedsAuthenticVietnameseCharacters_WithoutMojibake()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: $"AloBall_VietnameseTest_{Guid.NewGuid()}"));

        services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();

        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await DbInitializer.SeedDemoDataAsync(serviceProvider, db);

        // Verify specific known Vietnamese player names
        var hoangDuc = await db.Players.FirstOrDefaultAsync(p => p.FullName == "Nguyễn Hoàng Đức");
        Assert.NotNull(hoangDuc);
        Assert.Equal("Tiền vệ tấn công", hoangDuc.Position);

        var quangHai = await db.Players.FirstOrDefaultAsync(p => p.FullName == "Nguyễn Quang Hải");
        Assert.NotNull(quangHai);

        var tienDung = await db.Players.FirstOrDefaultAsync(p => p.FullName == "Bùi Tiến Dũng");
        Assert.NotNull(tienDung);

        // Verify Team names and descriptions
        var saoVang = await db.Teams.FirstOrDefaultAsync(t => t.Slug == "sao-vang-fc");
        Assert.NotNull(saoVang);
        Assert.Equal("Sao Vàng FC", saoVang.Name);
        Assert.Contains("fair-play", saoVang.Description);
        Assert.Equal("Nam Từ Liêm, Hà Nội", saoVang.ActivityArea);

        // Ensure NO player name or team description contains corrupted double-encoded UTF-8
        var allPlayers = await db.Players.ToListAsync();
        foreach (var p in allPlayers)
        {
            Assert.DoesNotContain("Ã", p.FullName);
            Assert.DoesNotContain("á»", p.FullName);
            Assert.DoesNotContain("Ä‘", p.FullName);
        }

        var allTeams = await db.Teams.ToListAsync();
        foreach (var t in allTeams)
        {
            Assert.DoesNotContain("Ã", t.Name);
            Assert.DoesNotContain("á»", t.Name);
            Assert.DoesNotContain("Ä‘", t.Name);
        }
    }

    [Fact]
    public async Task SeedDemoDataAsync_NeverOverwritesExistingUserModifications_OnSubsequentRuns()
    {
        var dbName = $"AloBall_NonDestructiveTest_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: dbName));

        services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();

        using (var scope = serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await DbInitializer.SeedDemoDataAsync(serviceProvider, db);

            // Simulate user modification to a player
            var player = await db.Players.FirstOrDefaultAsync(p => p.FullName == "Nguyễn Hoàng Đức");
            Assert.NotNull(player);

            player.KitNumber = 99; // User customized kit number
            player.Position = "Hậu vệ biên tùy chỉnh";
            player.StandardPrimaryPosition = "LB";
            await db.SaveChangesAsync();
        }

        // Run seed a second time (simulating server restart)
        using (var scope2 = serviceProvider.CreateScope())
        {
            var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await DbInitializer.SeedDemoDataAsync(serviceProvider, db2);

            var playerAfterSeed = await db2.Players.FirstOrDefaultAsync(p => p.FullName == "Nguyễn Hoàng Đức");
            Assert.NotNull(playerAfterSeed);

            // Assert that user modifications were PRESERVED and not overwritten
            Assert.Equal(99, playerAfterSeed.KitNumber);
            Assert.Equal("Hậu vệ biên tùy chỉnh", playerAfterSeed.Position);
            Assert.Equal("LB", playerAfterSeed.StandardPrimaryPosition);
        }
    }

    [Fact]
    public async Task SeedDemoDataAsync_IsIdempotent_DoesNotDuplicateRecords()
    {
        var dbName = $"AloBall_IdempotentTest_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: dbName));

        services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();

        int initialTeamCount, initialPlayerCount, initialMatchCount;

        using (var scope1 = serviceProvider.CreateScope())
        {
            var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await DbInitializer.SeedDemoDataAsync(serviceProvider, db1);
            initialTeamCount = await db1.Teams.CountAsync();
            initialPlayerCount = await db1.Players.CountAsync();
            initialMatchCount = await db1.Matches.CountAsync();
        }

        // Run seed again
        using (var scope2 = serviceProvider.CreateScope())
        {
            var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await DbInitializer.SeedDemoDataAsync(serviceProvider, db2);

            var secondTeamCount = await db2.Teams.CountAsync();
            var secondPlayerCount = await db2.Players.CountAsync();
            var secondMatchCount = await db2.Matches.CountAsync();

            Assert.Equal(initialTeamCount, secondTeamCount);
            Assert.Equal(initialPlayerCount, secondPlayerCount);
            Assert.Equal(initialMatchCount, secondMatchCount);
        }
    }

    [Fact]
    public async Task EnsureDatabaseSchemaAsync_BypassesSqliteRawSql_WhenNotSqlite()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"AloBall_SchemaGuard_{Guid.NewGuid()}")
            .Options;

        using var db = new ApplicationDbContext(options);

        // EnsureDatabaseSchemaAsync should return immediately without throwing syntax exceptions on non-SQLite providers
        var exception = await Record.ExceptionAsync(() => DbInitializer.EnsureDatabaseSchemaAsync(db));
        Assert.Null(exception);
    }

    [Fact]
    public void DataProtection_KeysPersistedToDbContext_CanDecryptAcrossInstances()
    {
        var sharedDbName = $"AloBall_DP_Shared_{Guid.NewGuid()}";

        // Instance 1
        var services1 = new ServiceCollection();
        services1.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(sharedDbName));
        services1.AddDataProtection()
            .PersistKeysToDbContext<ApplicationDbContext>()
            .SetApplicationName("AloBall");

        var sp1 = services1.BuildServiceProvider();
        var dataProtectionProvider1 = sp1.GetRequiredService<IDataProtectionProvider>();
        var protector1 = dataProtectionProvider1.CreateProtector("AloBall.Session.AuthCookie");

        var secretMessage = "UserSessionToken:User-Admin-12345";
        var protectedPayload = protector1.Protect(secretMessage);

        // Instance 2 (Simulating a different Vercel container instance sharing the same DB)
        var services2 = new ServiceCollection();
        services2.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(sharedDbName));
        services2.AddDataProtection()
            .PersistKeysToDbContext<ApplicationDbContext>()
            .SetApplicationName("AloBall");

        var sp2 = services2.BuildServiceProvider();
        var dataProtectionProvider2 = sp2.GetRequiredService<IDataProtectionProvider>();
        var protector2 = dataProtectionProvider2.CreateProtector("AloBall.Session.AuthCookie");

        var unencryptedMessage = protector2.Unprotect(protectedPayload);

        Assert.Equal(secretMessage, unencryptedMessage);
    }

    [Theory]
    [InlineData("Production", "", true)]
    [InlineData("Production", null, true)]
    [InlineData("Production", "postgres://user:pass@host:5432/db", false)]
    [InlineData("Development", "", false)]
    public void DatabaseProviderPolicy_ProductionRequiresPostgreSql_FailsFastOnMissing(
        string environmentName, string? pgConn, bool shouldThrow)
    {
        var isProduction = string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase);

        Action validateProvider = () =>
        {
            if (isProduction && string.IsNullOrWhiteSpace(pgConn))
            {
                throw new InvalidOperationException(
                    "CRITICAL: Production environment requires a valid PostgreSQL connection string via 'ConnectionStrings:PostgreSqlConnection', 'POSTGRES_CONNECTION', 'POSTGRES_URL', or 'DATABASE_URL'. Ephemeral SQLite fallback is strictly prohibited in Production to protect data persistence.");
            }
        };

        if (shouldThrow)
        {
            var ex = Assert.Throws<InvalidOperationException>(validateProvider);
            Assert.Contains("strictly prohibited in Production", ex.Message);
        }
        else
        {
            validateProvider(); // Should succeed without exception
        }
    }

    [Fact]
    public void AccessDenied_WhenUnauthenticated_RedirectsToLoginWithSafeReturnUrl()
    {
        var controller = CreateAccountController(isAuthenticated: false);

        var result = controller.AccessDenied(returnUrl: "/Teams/Manage");

        var redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Login", redirectResult.ActionName);
        Assert.Equal("/Teams/Manage", redirectResult.RouteValues?["returnUrl"]);
    }

    [Fact]
    public void AccessDenied_WhenUnauthenticatedWithUntrustedReturnUrl_SanitizesReturnUrl()
    {
        var controller = CreateAccountController(isAuthenticated: false);

        var result = controller.AccessDenied(returnUrl: "https://example.com/external");

        var redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Login", redirectResult.ActionName);
        Assert.Null(redirectResult.RouteValues?["returnUrl"]);
    }

    [Fact]
    public void AccessDenied_WhenAuthenticated_ReturnsForbidden403WithView()
    {
        var controller = CreateAccountController(isAuthenticated: true);

        var result = controller.AccessDenied(returnUrl: "/Teams/Admin");

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, controller.Response.StatusCode);
        Assert.Equal("/Teams/Admin", controller.ViewData["ReturnUrl"]);
    }

    private class TestUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext => new ActionContext();
        public string? Action(UrlActionContext actionContext) => actionContext.Action;
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return url.StartsWith("/") && !url.StartsWith("//") && !url.StartsWith("/\\");
        }
        public string? Link(string? routeName, object? values) => "/" + routeName;
        public string? RouteUrl(UrlRouteContext routeContext) => "/route";
    }

    private static AccountController CreateAccountController(bool isAuthenticated)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(options);

        var userStore = new UserStore<ApplicationUser>(db);
        var userManager = new UserManager<ApplicationUser>(
            userStore,
            null!,
            new PasswordHasher<ApplicationUser>(),
            null!, null!, null!,
            new IdentityErrorDescriber(),
            null!, null!);

        var contextAccessor = new HttpContextAccessor();
        var claimsFactory = new UserClaimsPrincipalFactory<ApplicationUser>(userManager, Options.Create(new IdentityOptions()));
        var signInManager = new SignInManager<ApplicationUser>(
            userManager,
            contextAccessor,
            claimsFactory,
            null!, null!, null!, null!);

        var authService = new TeamAuthorizationService(db);

        var controller = new AccountController(signInManager, userManager, db, authService);

        var httpContext = new DefaultHttpContext();
        if (isAuthenticated)
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
                new Claim(ClaimTypes.Name, "member@klbfc.vn")
            }, "TestAuthType");
            httpContext.User = new ClaimsPrincipal(identity);
        }
        else
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        }

        controller.Url = new TestUrlHelper();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return controller;
    }

    [Fact]
    public async Task AdminBootstrap_ProvisionsSuperAdmin_WhenConfiguredAndNoAdminExists()
    {
        var dbName = $"AloBall_AdminBootstrap_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        var adminEmail = $"admin_{Guid.NewGuid():N}@test.vn";

        try
        {
            await AdminBootstrap.ProvisionSuperAdminAsync(serviceProvider, adminEmail, "SuperSecurePassword123!");

            using var scope = serviceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(adminEmail);
            Assert.NotNull(user);
            Assert.Equal("Super Administrator", user.FullName);
            Assert.True(user.EmailConfirmed);

            // Verify password hashing
            var passwordValid = await userManager.CheckPasswordAsync(user, "SuperSecurePassword123!");
            Assert.True(passwordValid);

            // Verify role assignment
            var inRole = await userManager.IsInRoleAsync(user, AdminBootstrap.PlatformAdminRole);
            Assert.True(inRole);

            // Verify authorization service recognizes newly bootstrapped admin
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var authService = new TeamAuthorizationService(db);
            var isPlatformAdmin = await authService.IsPlatformAdminAsync(user.Id);
            Assert.True(isPlatformAdmin);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SUPER_ADMIN_EMAIL", null);
            Environment.SetEnvironmentVariable("SUPER_ADMIN_PASSWORD", null);
        }
    }

    [Fact]
    public async Task AdminBootstrap_DoesNotOverwrite_WhenAdminAlreadyExists()
    {
        var dbName = $"AloBall_AdminBootstrap_Existing_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();

        var adminEmail = $"admin_{Guid.NewGuid():N}@test.vn";
        using (var scope = serviceProvider.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var existingUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FullName = "Original Administrator",
                EmailConfirmed = true
            };
            await userManager.CreateAsync(existingUser, "OriginalPassword123!");
        }

        try
        {
            await AdminBootstrap.ProvisionSuperAdminAsync(serviceProvider, adminEmail, "NewPasswordShouldBeIgnored123!");

            using var scope = serviceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(adminEmail);
            Assert.NotNull(user);
            Assert.Equal("Original Administrator", user.FullName);

            // Password must remain original password
            var isOriginalPassword = await userManager.CheckPasswordAsync(user, "OriginalPassword123!");
            Assert.True(isOriginalPassword);

            var isNewPassword = await userManager.CheckPasswordAsync(user, "NewPasswordShouldBeIgnored123!");
            Assert.False(isNewPassword);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SUPER_ADMIN_EMAIL", null);
            Environment.SetEnvironmentVariable("SUPER_ADMIN_PASSWORD", null);
        }
    }

    [Fact]
    public async Task TeamAuthorizationService_RecognizesSuperAdmin_ViaEnvironmentOrRoleOrClaim()
    {
        var dbName = $"AloBall_AuthTest_{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        using var db = new ApplicationDbContext(options);

        // 1. Dev admin account
        var devAdmin = new ApplicationUser { Id = "dev-admin-id", UserName = "admin@aloball.vn", Email = "admin@aloball.vn" };
        db.Users.Add(devAdmin);

        // 2. Normal user
        var normalUser = new ApplicationUser { Id = "normal-user-id", UserName = "player@team.vn", Email = "player@team.vn" };
        db.Users.Add(normalUser);

        // 3. User with PlatformAdmin claim
        var claimUser = new ApplicationUser { Id = "claim-user-id", UserName = "claim@admin.vn", Email = "claim@admin.vn" };
        db.Users.Add(claimUser);
        db.UserClaims.Add(new IdentityUserClaim<string> { UserId = "claim-user-id", ClaimType = "PlatformAdmin", ClaimValue = "true" });

        // 4. User with PlatformAdmin role
        var roleUser = new ApplicationUser { Id = "role-user-id", UserName = "role@admin.vn", Email = "role@admin.vn" };
        var adminRole = new IdentityRole { Id = "role-admin-id", Name = "PlatformAdmin", NormalizedName = "PLATFORMADMIN" };
        db.Users.Add(roleUser);
        db.Roles.Add(adminRole);
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = "role-user-id", RoleId = "role-admin-id" });

        await db.SaveChangesAsync();

        var authService = new TeamAuthorizationService(db);

        // Assertions
        Assert.True(await authService.IsPlatformAdminAsync("dev-admin-id"));
        Assert.True(await authService.IsPlatformAdminAsync("claim-user-id"));
        Assert.True(await authService.IsPlatformAdminAsync("role-user-id"));
        Assert.False(await authService.IsPlatformAdminAsync("normal-user-id"));
        Assert.False(await authService.IsPlatformAdminAsync("non-existent-user"));
    }

    [Fact]
    public void IdempotentMigrationScript_ContainsHistoryRecordingAndGuardChecks()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        string? scriptPath = null;
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "migrations", "postgres_idempotent.sql");
            if (File.Exists(candidate))
            {
                scriptPath = candidate;
                break;
            }
            dir = dir.Parent;
        }

        Assert.True(File.Exists(scriptPath), $"Script should exist at {scriptPath}");
        var scriptContent = File.ReadAllText(scriptPath);

        // Verify idempotent guards
        Assert.Contains("CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\"", scriptContent);
        Assert.Contains("IF NOT EXISTS(SELECT 1 FROM \"__EFMigrationsHistory\"", scriptContent);
        Assert.Contains("INSERT INTO \"__EFMigrationsHistory\"", scriptContent);
        Assert.Contains("20261105092442_InitialPostgreSqlBaseline", scriptContent);
    }

    [Fact]
    public async Task TeamAuthorizationService_InProduction_RejectsDevAdminShortcut_AndRequiresPersistedRolesOrClaims()
    {
        var dbName = $"AloBall_AuthProdTest_{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        using var db = new ApplicationDbContext(options);

        // 1. Dev admin account (no persisted role/claim)
        var devAdmin = new ApplicationUser { Id = "dev-admin-id", UserName = "admin@aloball.vn", Email = "admin@aloball.vn" };
        db.Users.Add(devAdmin);

        // 2. Normal user (no persisted role/claim)
        var normalUser = new ApplicationUser { Id = "normal-user-id", UserName = "player@team.vn", Email = "player@team.vn" };
        db.Users.Add(normalUser);

        // 3. User with matching env var email but NO persisted role/claim
        var envUser = new ApplicationUser { Id = "env-user-id", UserName = "envadmin@aloball.vn", Email = "envadmin@aloball.vn" };
        db.Users.Add(envUser);

        // 4. User with persisted PlatformAdmin claim
        var claimUser = new ApplicationUser { Id = "claim-user-id", UserName = "claim@admin.vn", Email = "claim@admin.vn" };
        db.Users.Add(claimUser);
        db.UserClaims.Add(new IdentityUserClaim<string> { UserId = "claim-user-id", ClaimType = "PlatformAdmin", ClaimValue = "true" });

        // 5. User with persisted PlatformAdmin role
        var roleUser = new ApplicationUser { Id = "role-user-id", UserName = "role@admin.vn", Email = "role@admin.vn" };
        var adminRole = new IdentityRole { Id = "role-admin-id", Name = "PlatformAdmin", NormalizedName = "PLATFORMADMIN" };
        db.Users.Add(roleUser);
        db.Roles.Add(adminRole);
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = "role-user-id", RoleId = "role-admin-id" });

        await db.SaveChangesAsync();

        var prodEnv = new TestHostEnvironment { EnvironmentName = "Production" };
        var authService = new TeamAuthorizationService(db, prodEnv);

        Environment.SetEnvironmentVariable("SUPER_ADMIN_EMAIL", "envadmin@aloball.vn");

        try
        {
            // In Production, dev admin shortcut MUST NOT grant access without persisted role/claim
            Assert.False(await authService.IsPlatformAdminAsync("dev-admin-id"));

            // In Production, matching env var alone MUST NOT bypass persisted authorization
            Assert.False(await authService.IsPlatformAdminAsync("env-user-id"));

            // Normal user must not have access
            Assert.False(await authService.IsPlatformAdminAsync("normal-user-id"));

            // Only users with persisted Identity role or claim have platform admin access
            Assert.True(await authService.IsPlatformAdminAsync("claim-user-id"));
            Assert.True(await authService.IsPlatformAdminAsync("role-user-id"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SUPER_ADMIN_EMAIL", null);
        }
    }

    [Fact]
    public async Task AdminBootstrap_ConcurrentCalls_AreSafeAndDoNotCreateDuplicates()
    {
        var dbName = $"AloBall_ConcurrentBootstrap_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        var adminEmail = $"concurrent_admin_{Guid.NewGuid():N}@test.vn";

        // Launch multiple concurrent bootstrap attempts simultaneously
        var tasks = Enumerable.Range(0, 5).Select(_ =>
            Task.Run(() => AdminBootstrap.ProvisionSuperAdminAsync(serviceProvider, adminEmail, "ConcurrentPass123!"))
        );

        await Task.WhenAll(tasks);

        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Must create exactly ONE admin user
        var adminUsers = await db.Users.Where(u => u.Email == adminEmail).ToListAsync();
        Assert.Single(adminUsers);
    }

    [Fact]
    public void AntiforgeryValidationFailedFilter_DeletesStaleCookie_AndPreventsLoginRedirectLoop()
    {
        var filter = new AntiforgeryValidationFailedFilter();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/Account/Login";
        httpContext.Request.QueryString = new QueryString("?returnUrl=%2FAccount%2FLogin");

        var actionContext = new ActionContext(
            httpContext,
            new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());

        var resultExecutingContext = new ResultExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new TestAntiforgeryFailedResult(),
            new object());

        filter.OnResultExecuting(resultExecutingContext);

        // 1. Verify result is redirected to Login
        var redirectResult = Assert.IsType<RedirectToActionResult>(resultExecutingContext.Result);
        Assert.Equal("Login", redirectResult.ActionName);
        Assert.Equal("Account", redirectResult.ControllerName);

        // 2. returnUrl should NOT contain /Account/Login (to prevent redirect loops)
        Assert.Null(redirectResult.RouteValues?["returnUrl"]);
        Assert.Equal("expired", redirectResult.RouteValues?["err"]);

        // 3. Stale cookie must be deleted in response headers
        var setCookieHeader = httpContext.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains(".AloBall.Antiforgery", setCookieHeader);
    }

    private class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "AloBall";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private class TestAntiforgeryFailedResult : IActionResult, Microsoft.AspNetCore.Mvc.Core.Infrastructure.IAntiforgeryValidationFailedResult
    {
        public Task ExecuteResultAsync(ActionContext context) => Task.CompletedTask;
    }
}