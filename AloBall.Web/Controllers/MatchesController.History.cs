using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Controllers;

public partial class MatchesController
{
    [HttpGet, Authorize]
    public async Task<IActionResult> Index(Guid teamId, MatchCategory? category = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        if (!await _auth.HasMemberAccessAsync(teamId, user.Id)) return Forbid();
        var team = await _db.Teams.Include(t => t.Achievements).FirstOrDefaultAsync(t => t.Id == teamId);
        if (team == null) return NotFound();
        ViewBag.Team = team;
        ViewBag.IsManager = await _auth.HasManagerAccessAsync(teamId, user.Id);
        ViewBag.Category = category;
        var matches = await _db.Matches.Include(m => m.TeamA).Include(m => m.TeamB)
            .Where(m => (m.TeamAId == teamId || m.TeamBId == teamId) && (!category.HasValue || m.Category == category))
            .OrderByDescending(m => m.MatchDateTime).ToListAsync();
        return View(matches);
    }

    [HttpGet, Authorize]
    public async Task<IActionResult> Schedule(Guid teamId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        if (!await _auth.HasManagerAccessAsync(teamId, user.Id)) return Forbid();
        var team = await _db.Teams.FindAsync(teamId);
        if (team == null) return NotFound();
        ViewBag.Team = team;
        return View(new ScheduleMatchInput { TeamId = teamId, MatchDateTime = CompetitionRules.LocalNow.Date.AddDays(1).AddHours(19), PitchName = team.HomePitch ?? "" });
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Schedule(ScheduleMatchInput input)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        if (!await _auth.HasManagerAccessAsync(input.TeamId, user.Id)) return Forbid();
        ViewBag.Team = await _db.Teams.FindAsync(input.TeamId);
        if (ViewBag.Team == null) return NotFound();
        if (input.Category != MatchCategory.Friendly && input.Category != MatchCategory.Tournament) ModelState.AddModelError("Category", "Chọn giao hữu hoặc đá giải.");
        if (input.MatchDateTime == default) ModelState.AddModelError("MatchDateTime", "Nhập ngày và giờ thi đấu.");
        if (input.PitchType != "5 người" && input.PitchType != "7 người" && input.PitchType != "11 người") ModelState.AddModelError("PitchType", "Chọn loại sân hợp lệ.");
        if (input.Category == MatchCategory.Tournament && string.IsNullOrWhiteSpace(input.Notes)) ModelState.AddModelError("Notes", "Nhập tên giải và vòng đấu.");
        if (await CompetitionRules.IsClosedAsync(_db, input.TeamId, input.MatchDateTime.ToString("yyyy-MM"))) ModelState.AddModelError("", "Kỳ đã chốt. Hãy mở lại kỳ trong Giữ Lửa trước khi bổ sung trận.");
        if (!ModelState.IsValid) return View(input);
        var match = new MatchGame { TeamAId = input.TeamId, Category = input.Category, TeamBExternalName = input.Opponent.Trim(),
            MatchDateTime = input.MatchDateTime, PitchName = input.PitchName.Trim(), PitchType = input.PitchType,
            Notes = input.Notes?.Trim(), IsPublic = input.IsPublic };
        _db.Matches.Add(match); await _db.SaveChangesAsync();
        TempData["Success"] = "Đã tạo trận đấu. Cập nhật đội hình, tỉ số và bàn thắng sau trận.";
        return RedirectToAction(nameof(Detail), new { id = match.Id });
    }
}
