using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using System.Security.Claims;

namespace AloBall.Web.Services;

public interface ITeamAuthorizationService
{
    Task<bool> IsPlatformAdminAsync(string userId);
    Task<TeamRole?> GetUserRoleInTeamAsync(Guid teamId, string userId);
    Task<bool> HasManagerAccessAsync(Guid teamId, string userId);
    Task<bool> HasMemberAccessAsync(Guid teamId, string userId);
    Task<bool> IsOwnerAsync(Guid teamId, string userId);
    Task<bool> CanTransferOwnershipAsync(Guid teamId, string currentUserId, string targetUserId);
}

public class TeamAuthorizationService : ITeamAuthorizationService
{
    private readonly ApplicationDbContext _db;
    private readonly IHostEnvironment? _environment;

    public TeamAuthorizationService(ApplicationDbContext db, IHostEnvironment? environment = null)
    {
        _db = db;
        _environment = environment;
    }

    public async Task<bool> IsPlatformAdminAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return false;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return false;

        // 1. Chỉ cho phép tài khoản dev shortcut trong môi trường Development
        if (_environment == null || _environment.IsDevelopment())
        {
            if (user.UserName?.Equals("admin@aloball.vn", StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }

        // 2. Trong môi trường Production (hoặc bất kỳ môi trường nào ngoài Development):
        // Phải dựa hoàn toàn vào dữ liệu quyền đã được lưu trữ bền vững (persisted Identity roles & claims)
        // Tuyệt đối không dựa vào email shortcut hay biến môi trường động trên mỗi request.
        
        // Kiểm tra UserClaims của ASP.NET Core Identity
        var hasAdminClaim = await _db.UserClaims
            .AnyAsync(c => c.UserId == userId && (c.ClaimType == "PlatformAdmin" || (c.ClaimType == ClaimTypes.Role && c.ClaimValue == "PlatformAdmin")));
        if (hasAdminClaim) return true;

        // Kiểm tra UserRoles của ASP.NET Core Identity
        var adminRoleIds = await _db.Roles
            .Where(r => r.Name == "PlatformAdmin" || r.Name == "Admin")
            .Select(r => r.Id)
            .ToListAsync();
        if (adminRoleIds.Count > 0 && await _db.UserRoles.AnyAsync(ur => ur.UserId == userId && adminRoleIds.Contains(ur.RoleId)))
        {
            return true;
        }

        return false;
    }

    public async Task<TeamRole?> GetUserRoleInTeamAsync(Guid teamId, string userId)
    {
        var membership = await _db.TeamMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == userId);

        return membership?.Role;
    }

    public async Task<bool> HasManagerAccessAsync(Guid teamId, string userId)
    {
        if (await IsPlatformAdminAsync(userId)) return true;

        var role = await GetUserRoleInTeamAsync(teamId, userId);
        return role == TeamRole.Manager || role == TeamRole.Owner;
    }

    public async Task<bool> HasMemberAccessAsync(Guid teamId, string userId)
    {
        if (await IsPlatformAdminAsync(userId)) return true;

        var role = await GetUserRoleInTeamAsync(teamId, userId);
        return role.HasValue;
    }

    public async Task<bool> IsOwnerAsync(Guid teamId, string userId)
    {
        if (await IsPlatformAdminAsync(userId)) return true;

        var role = await GetUserRoleInTeamAsync(teamId, userId);
        return role == TeamRole.Owner;
    }

    public async Task<bool> CanTransferOwnershipAsync(Guid teamId, string currentUserId, string targetUserId)
    {
        if (!await IsOwnerAsync(teamId, currentUserId)) return false;

        var targetMembership = await _db.TeamMembers.FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == targetUserId);
        return targetMembership != null;
    }
}