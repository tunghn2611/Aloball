using AloBall.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace AloBall.Web.Data;

/// <summary>
/// Cung cấp cơ chế khởi tạo an toàn (one-time bootstrap) cho tài khoản Super Admin đầu tiên
/// trên môi trường Production mà không sử dụng dữ liệu mẫu hay mật khẩu mặc định trong mã nguồn.
/// </summary>
public static class AdminBootstrap
{
    public const string PlatformAdminRole = "PlatformAdmin";
    public const string PlatformAdminClaimType = "PlatformAdmin";
    private static readonly SemaphoreSlim _bootstrapLock = new(1, 1);

    public static async Task ProvisionSuperAdminAsync(
        IServiceProvider serviceProvider,
        string? overrideEmail = null,
        string? overridePassword = null)
    {
        await _bootstrapLock.WaitAsync();
        try
        {
            using var scope = serviceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var logger = scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("AdminBootstrap");

            var adminEmail = overrideEmail
                ?? Environment.GetEnvironmentVariable("SUPER_ADMIN_EMAIL")
                ?? Environment.GetEnvironmentVariable("ADMIN_EMAIL");
            var adminPassword = overridePassword
                ?? Environment.GetEnvironmentVariable("SUPER_ADMIN_PASSWORD")
                ?? Environment.GetEnvironmentVariable("ADMIN_PASSWORD");

            if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
            {
                return;
            }

            // Đảm bảo Role "PlatformAdmin" tồn tại
            if (!await roleManager.RoleExistsAsync(PlatformAdminRole))
            {
                await roleManager.CreateAsync(new IdentityRole(PlatformAdminRole));
            }

            // 1. Kiểm tra nếu email này đã tồn tại trong hệ thống
            var existingUser = await userManager.FindByEmailAsync(adminEmail);
            if (existingUser != null)
            {
                // Tài khoản đã tồn tại, tuyệt đối không ghi đè hoặc thay đổi mật khẩu
                return;
            }

            // 2. Kiểm tra nếu đã có bất kỳ Platform Admin nào tồn tại trong hệ thống (qua Role hoặc Claim)
            var adminRole = await roleManager.FindByNameAsync(PlatformAdminRole);
            if (adminRole != null)
            {
                var roleAssigned = await db.UserRoles.AnyAsync(ur => ur.RoleId == adminRole.Id);
                if (roleAssigned)
                {
                    logger?.LogInformation("Admin bootstrap skipped: PlatformAdmin role is already assigned to an existing user.");
                    return;
                }
            }

            var claimAssigned = await db.UserClaims.AnyAsync(uc => uc.ClaimType == PlatformAdminClaimType || (uc.ClaimType == ClaimTypes.Role && uc.ClaimValue == PlatformAdminRole));
            if (claimAssigned)
            {
                logger?.LogInformation("Admin bootstrap skipped: PlatformAdmin claim is already assigned to an existing user.");
                return;
            }

            // 3. Khởi tạo tài khoản Super Admin với mật khẩu được băm qua Identity
            var newAdmin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FullName = "Super Administrator",
                EmailConfirmed = true,
                HasCompletedOnboarding = true,
                OnboardingCompletedAt = DateTime.UtcNow
            };

            var result = await userManager.CreateAsync(newAdmin, adminPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(newAdmin, PlatformAdminRole);
                await userManager.AddClaimAsync(newAdmin, new Claim(PlatformAdminClaimType, "true"));
                await userManager.AddClaimAsync(newAdmin, new Claim(ClaimTypes.Role, PlatformAdminRole));

                logger?.LogInformation("Super Admin bootstrap completed successfully for account: {Email}", adminEmail);
            }
            else
            {
                // Xử lý an toàn trường hợp cạnh tranh (race condition): nếu lỗi là trùng UserName/Email do instance khác tạo trước
                if (result.Errors.Any(e => e.Code == "DuplicateUserName" || e.Code == "DuplicateEmail"))
                {
                    logger?.LogInformation("Concurrent bootstrap safely detected: user {Email} already created by another instance.", adminEmail);
                }
                else
                {
                    logger?.LogWarning("Super Admin bootstrap failed: {Errors}",
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
        }
        catch (Exception ex)
        {
            var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger("AdminBootstrap");
            logger?.LogError(ex, "Error occurred during AdminBootstrap.ProvisionSuperAdminAsync.");
        }
        finally
        {
            _bootstrapLock.Release();
        }
    }
}