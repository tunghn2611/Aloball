using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

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

    public TeamAuthorizationService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<bool> IsPlatformAdminAsync(string userId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return false;
        // User admin@aloball.vn hoặc user được gán claim PlatformAdmin
        return user.UserName?.Equals("admin@aloball.vn", StringComparison.OrdinalIgnoreCase) == true;
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
