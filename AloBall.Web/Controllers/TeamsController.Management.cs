using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Controllers;

public partial class TeamsController
{
    [HttpGet, Authorize]
    public async Task<IActionResult> Edit(Guid id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        if (!await _auth.HasManagerAccessAsync(id, user.Id)) return Forbid();
        var team = await _db.Teams.FindAsync(id);
        if (team == null) return NotFound();
        return View(new TeamProfileInput { Id = id, Name = team.Name, EstablishedDate = team.EstablishedDate,
            LogoUrl = team.LogoUrl, ActivityArea = team.ActivityArea, HomePitch = team.HomePitch,
            Description = team.Description, IsPublic = team.IsPublic });
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(TeamProfileInput input)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        if (!await _auth.HasManagerAccessAsync(input.Id, user.Id)) return Forbid();
        if (!CompetitionRules.SafeImageUrl(input.LogoUrl)) ModelState.AddModelError(nameof(input.LogoUrl), "Dùng đường dẫn ảnh bắt đầu bằng https://.");
        if (input.EstablishedDate > CompetitionRules.LocalNow.Date) ModelState.AddModelError(nameof(input.EstablishedDate), "Ngày thành lập không được ở tương lai.");
        if (!ModelState.IsValid) return View(input);
        var team = await _db.Teams.FindAsync(input.Id);
        if (team == null) return NotFound();
        team.Name = input.Name.Trim(); team.LogoUrl = input.LogoUrl?.Trim();
        team.EstablishedDate = input.EstablishedDate; team.ActivityArea = input.ActivityArea?.Trim();
        team.HomePitch = input.HomePitch?.Trim(); team.Description = input.Description?.Trim(); team.IsPublic = input.IsPublic;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã cập nhật hồ sơ đội bóng.";
        return RedirectToAction(nameof(Dashboard), new { id = team.Id });
    }

    [HttpGet, Authorize]
    public async Task<IActionResult> PlayerEditor(Guid teamId, Guid? id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        if (!await _auth.HasManagerAccessAsync(teamId, user.Id)) return Forbid();
        ViewBag.Team = await _db.Teams.FindAsync(teamId);
        if (ViewBag.Team == null) return NotFound();
        if (!id.HasValue) return View(new PlayerProfileInput { TeamId = teamId });
        var p = await _db.Players.FirstOrDefaultAsync(p => p.Id == id && p.TeamId == teamId);
        if (p == null) return NotFound();
        return View(new PlayerProfileInput { Id = p.Id, TeamId = teamId, FullName = p.FullName, KitNumber = p.KitNumber,
            Position = p.Position, HeightCm = p.HeightCm, WeightKg = p.WeightKg, ShirtName = p.ShirtName,
            ShirtSize = p.ShirtSize, ShortSize = p.ShortSize, IsActive = p.IsActive, IsPublicProfile = p.IsPublicProfile,
            PublicKitNumber = p.PublicKitNumber, PublicPosition = p.PublicPosition, PublicHeightWeight = p.PublicHeightWeight,
            PreferredFoot = p.PreferredFoot, Experience = p.Experience, Region = p.Region,
            PublicPreferredFoot = p.PublicPreferredFoot, PublicExperience = p.PublicExperience });
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> PlayerEditor(PlayerProfileInput input)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        if (!await _auth.HasManagerAccessAsync(input.TeamId, user.Id)) return Forbid();
        ViewBag.Team = await _db.Teams.FindAsync(input.TeamId);
        if (ViewBag.Team == null) return NotFound();
        if (!ModelState.IsValid) return View(input);
        var player = input.Id.HasValue ? await _db.Players.FirstOrDefaultAsync(p => p.Id == input.Id && p.TeamId == input.TeamId) : new Player { TeamId = input.TeamId };
        if (player == null) return NotFound();
        player.FullName = input.FullName.Trim(); player.KitNumber = input.KitNumber; player.Position = input.Position?.Trim();
        player.HeightCm = input.HeightCm; player.WeightKg = input.WeightKg; player.ShirtName = input.ShirtName?.Trim();
        player.ShirtSize = input.ShirtSize?.Trim(); player.ShortSize = input.ShortSize?.Trim();
        player.PreferredFoot = input.PreferredFoot?.Trim(); player.Experience = input.Experience?.Trim(); player.Region = input.Region?.Trim();
        player.IsActive = input.IsActive; player.InactiveDate = input.IsActive ? null : player.InactiveDate ?? DateTime.UtcNow;
        player.IsPublicProfile = input.IsPublicProfile; player.PublicKitNumber = input.PublicKitNumber;
        player.PublicPosition = input.PublicPosition; player.PublicHeightWeight = input.PublicHeightWeight;
        player.PublicPreferredFoot = input.PublicPreferredFoot; player.PublicExperience = input.PublicExperience;
        if (!input.Id.HasValue) _db.Players.Add(player);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã lưu thông tin cầu thủ và đặt áo.";
        return RedirectToAction(nameof(Players), new { teamId = input.TeamId });
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAchievement(AchievementInput input)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        if (!await _auth.HasManagerAccessAsync(input.TeamId, user.Id)) return Forbid();
        if (!ModelState.IsValid) return BadRequest(new { message = "Nhập đủ tên giải, mùa giải và thành tích." });
        var achievement = input.Id.HasValue ? await _db.CompetitionAchievements.FirstOrDefaultAsync(a => a.Id == input.Id && a.TeamId == input.TeamId) : new CompetitionAchievement { TeamId = input.TeamId };
        if (achievement == null) return NotFound();
        if (input.IsFeatured)
            foreach (var a in await _db.CompetitionAchievements.Where(a => a.TeamId == input.TeamId && a.IsFeatured).ToListAsync()) a.IsFeatured = false;
        achievement.TournamentName = input.TournamentName.Trim(); achievement.SeasonOrYear = input.SeasonOrYear.Trim();
        achievement.TitleOrRank = input.TitleOrRank.Trim(); achievement.IsFeatured = input.IsFeatured;
        if (!input.Id.HasValue) _db.CompetitionAchievements.Add(achievement);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Đã lưu thành tích của đội.";
        return RedirectToAction("Index", "Matches", new { teamId = input.TeamId });
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAchievement(Guid id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var achievement = await _db.CompetitionAchievements.FindAsync(id);
        if (achievement == null) return NotFound();
        if (!await _auth.HasManagerAccessAsync(achievement.TeamId, user.Id)) return Forbid();
        _db.CompetitionAchievements.Remove(achievement); await _db.SaveChangesAsync();
        TempData["Success"] = "Đã xóa thành tích.";
        return RedirectToAction("Index", "Matches", new { teamId = achievement.TeamId });
    }
}
