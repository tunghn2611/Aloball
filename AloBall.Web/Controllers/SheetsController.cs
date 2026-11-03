using AloBall.Web.Data;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AloBall.Web.Controllers;

[Authorize]
public class SheetsController : Controller
{
    private readonly ISheetsIntegrationService _sheetsService;
    private readonly ITeamAuthorizationService _auth;
    private readonly ApplicationDbContext _db;

    public SheetsController(ISheetsIntegrationService sheetsService, ITeamAuthorizationService auth, ApplicationDbContext db)
    {
        _sheetsService = sheetsService;
        _auth = auth;
        _db = db;
    }

    // 1. Xuất Excel / Sheets báo cáo chuyên cần theo tháng
    [HttpGet]
    public async Task<IActionResult> ExportAttendance(Guid teamId, string? period)
    {
        if (!await _auth.HasMemberAccessAsync(teamId, User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "")) return Forbid();
        period ??= DateTime.Today.ToString("yyyy-MM");

        var fileBytes = await _sheetsService.ExportAttendanceReportAsync(teamId, period);
        var fileName = $"ChuyenCan_{period}_{teamId.ToString().Substring(0, 8)}.xlsx";

        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    // 2. Xuất danh sách cầu thủ & kích thước áo
    [HttpGet]
    public async Task<IActionResult> ExportPlayers(Guid teamId)
    {
        if (!await _auth.HasMemberAccessAsync(teamId, User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "")) return Forbid();
        var fileBytes = await _sheetsService.ExportPlayersAndKitsAsync(teamId);
        var fileName = $"DanhSach_DatAo_{teamId.ToString().Substring(0, 8)}.xlsx";

        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    // 3. Xem trước (Preview) file Sheets/Excel import có kiểm soát
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreviewImport(Guid teamId, IFormFile excelFile)
    {
        if (!await _auth.HasManagerAccessAsync(teamId, User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "")) return Forbid();
        if (excelFile == null || excelFile.Length == 0)
        {
            TempData["Error"] = "Vui lòng chọn file Excel / Sheets hợp lệ.";
            return RedirectToAction("Players", "Teams", new { teamId });
        }

        using var stream = excelFile.OpenReadStream();
        var previewChanges = await _sheetsService.PreviewPlayersImportAsync(teamId, stream);

        ViewBag.TeamId = teamId;
        return View(previewChanges);
    }

    // 4. Áp dụng các thay đổi từ file Excel sau khi Leader xác nhận Preview
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyImport(Guid teamId, List<SheetsImportPreviewItem> approvedChanges)
    {
        if (!await _auth.HasManagerAccessAsync(teamId, User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "")) return Forbid();
        var result = await _sheetsService.ApplyPlayersImportAsync(teamId, approvedChanges);
        TempData["Success"] = $"Đã cập nhật an toàn {result.UpdatedCount} bản ghi từ file!";
        return RedirectToAction("Players", "Teams", new { teamId });
    }
}
