using System;
using System.Linq;
using System.Threading.Tasks;
using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Controllers;

public partial class TeamsController
{
    public class UpdatePlayStylesDto
    {
        public Guid PlayerId { get; set; }
        public string? PlayStyles { get; set; }
    }

    [HttpPost("/club/member/playstyles")]
    [Authorize]
    public async Task<IActionResult> UpdatePlayStyles([FromBody] UpdatePlayStylesDto dto)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized(new { success = false, message = "Vui lòng đăng nhập." });

        if (dto == null || dto.PlayerId == Guid.Empty)
            return BadRequest(new { success = false, message = "Thiếu mã định danh cầu thủ." });

        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == dto.PlayerId);
        if (player == null)
            return NotFound(new { success = false, message = "Không tìm thấy hồ sơ cầu thủ." });

        bool canManage = await _auth.HasManagerAccessAsync(player.TeamId, user.Id);
        if (!canManage)
            return StatusCode(403, new { success = false, message = "Chỉ Đội trưởng hoặc Ban cán sự mới có quyền gán PlayStyles cho cầu thủ." });

        var (isValid, error, formatted) = PlayStyleCatalog.ValidateAndFormat(dto.PlayStyles);
        if (!isValid)
            return BadRequest(new { success = false, message = error });

        player.PlayStyles = formatted;
        await _db.SaveChangesAsync();

        var assigned = PlayStyleCatalog.Parse(formatted);
        return Ok(new
        {
            success = true,
            playStyles = formatted,
            assignedCount = assigned.Count,
            hasPlus = assigned.Any(a => a.IsPlus),
            message = $"Đã cập nhật PlayStyles cho cầu thủ {player.FullName} thành công."
        });
    }

    [HttpGet("/club/member/playstyles")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPlayerPlayStyles(Guid playerId)
    {
        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId);
        if (player == null) return NotFound(new { success = false, message = "Không tìm thấy cầu thủ." });

        var assigned = PlayStyleCatalog.Parse(player.PlayStyles);
        return Ok(new
        {
            playerId = player.Id,
            fullName = player.FullName,
            playStyles = player.PlayStyles,
            assigned = assigned.Select(a => new
            {
                code = a.Definition.Code,
                nameVi = a.Definition.NameVi,
                nameEn = a.Definition.NameEn,
                category = a.Definition.Category,
                isPlus = a.IsPlus,
                svgPath = a.Definition.SvgPath
            })
        });
    }

    [HttpGet("/api/playstyles/catalog")]
    [AllowAnonymous]
    public IActionResult GetPlayStylesCatalog()
    {
        var grouped = PlayStyleCatalog.All
            .GroupBy(s => s.Category)
            .Select(g => new
            {
                category = g.Key,
                skills = g.Select(s => new
                {
                    code = s.Code,
                    nameVi = s.NameVi,
                    nameEn = s.NameEn,
                    category = s.Category,
                    svgPath = s.SvgPath
                })
            });

        return Ok(new
        {
            categories = grouped,
            allowedVibes = PlayStyleCatalog.AllowedVibeTags
        });
    }
}
