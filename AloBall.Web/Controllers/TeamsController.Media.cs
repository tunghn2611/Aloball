using AloBall.Web.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Controllers;

public partial class TeamsController
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp"
    };

    // 1. Upload / Thay thế Logo CLB
    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadLogo(Guid teamId, IFormFile? logoFile)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return Unauthorized(new { success = false, message = "Vui lòng đăng nhập để thực hiện." });

        if (!await _auth.HasManagerAccessAsync(teamId, user.Id))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "Chỉ Đội trưởng hoặc Ban quản trị CLB mới có quyền đổi logo." });

        var team = await _db.Teams.FindAsync(teamId);
        if (team == null)
            return NotFound(new { success = false, message = "Không tìm thấy đội bóng." });

        var (isValid, errorMsg) = ValidateImageFile(logoFile, 5 * 1024 * 1024);
        if (!isValid)
            return BadRequest(new { success = false, message = errorMsg });

        var ext = Path.GetExtension(logoFile!.FileName).ToLowerInvariant();
        var safeFileName = $"{teamId}_logo_{Guid.NewGuid():N}{ext}";

        var webRoot = GetWebRootPath();
        var uploadDir = Path.Combine(webRoot, "uploads", "teams");
        Directory.CreateDirectory(uploadDir);

        var fullPath = Path.Combine(uploadDir, safeFileName);
        if (!Path.GetFullPath(fullPath).StartsWith(Path.GetFullPath(uploadDir)))
            return BadRequest(new { success = false, message = "Đường dẫn file không hợp lệ." });

        // Xóa logo cũ nếu là file cục bộ
        DeleteLocalTeamFile(team.LogoUrl, webRoot);

        using (var stream = new FileStream(fullPath, FileMode.Create))
        {
            await logoFile.CopyToAsync(stream);
        }

        team.LogoUrl = $"/uploads/teams/{safeFileName}";
        await _db.SaveChangesAsync();

        if (IsAjaxRequest())
        {
            return Ok(new { success = true, logoUrl = team.LogoUrl, message = "Đã cập nhật logo CLB thành công!" });
        }

        TempData["Success"] = "Đã cập nhật logo CLB thành công!";
        return RedirectToAction(nameof(PublicProfile), new { slug = team.Slug });
    }

    // 2. Xóa Logo CLB
    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteLogo(Guid teamId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return Unauthorized(new { success = false, message = "Vui lòng đăng nhập để thực hiện." });

        if (!await _auth.HasManagerAccessAsync(teamId, user.Id))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "Chỉ Đội trưởng hoặc Ban quản trị CLB mới có quyền xóa logo." });

        var team = await _db.Teams.FindAsync(teamId);
        if (team == null)
            return NotFound(new { success = false, message = "Không tìm thấy đội bóng." });

        var webRoot = GetWebRootPath();
        DeleteLocalTeamFile(team.LogoUrl, webRoot);

        team.LogoUrl = null;
        await _db.SaveChangesAsync();

        if (IsAjaxRequest())
        {
            return Ok(new { success = true, message = "Đã xóa logo CLB thành công!" });
        }

        TempData["Success"] = "Đã xóa logo CLB thành công!";
        return RedirectToAction(nameof(PublicProfile), new { slug = team.Slug });
    }

    // 3. Upload / Thay thế Ảnh bìa CLB (Facebook-style)
    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadCover(Guid teamId, IFormFile? coverFile)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return Unauthorized(new { success = false, message = "Vui lòng đăng nhập để thực hiện." });

        if (!await _auth.HasManagerAccessAsync(teamId, user.Id))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "Chỉ Đội trưởng hoặc Ban quản trị CLB mới có quyền đổi ảnh bìa." });

        var team = await _db.Teams.FindAsync(teamId);
        if (team == null)
            return NotFound(new { success = false, message = "Không tìm thấy đội bóng." });

        var (isValid, errorMsg) = ValidateImageFile(coverFile, 10 * 1024 * 1024);
        if (!isValid)
            return BadRequest(new { success = false, message = errorMsg });

        var ext = Path.GetExtension(coverFile!.FileName).ToLowerInvariant();
        var safeFileName = $"{teamId}_cover_{Guid.NewGuid():N}{ext}";

        var webRoot = GetWebRootPath();
        var uploadDir = Path.Combine(webRoot, "uploads", "teams");
        Directory.CreateDirectory(uploadDir);

        var fullPath = Path.Combine(uploadDir, safeFileName);
        if (!Path.GetFullPath(fullPath).StartsWith(Path.GetFullPath(uploadDir)))
            return BadRequest(new { success = false, message = "Đường dẫn file không hợp lệ." });

        // Xóa ảnh bìa cũ nếu là file cục bộ
        DeleteLocalTeamFile(team.CoverImageUrl, webRoot);

        using (var stream = new FileStream(fullPath, FileMode.Create))
        {
            await coverFile.CopyToAsync(stream);
        }

        team.CoverImageUrl = $"/uploads/teams/{safeFileName}";
        // Mặc định độ mờ là 70% (0.7) nếu chưa từng được cấu hình trước đó; giữ nguyên nếu đã có cấu hình
        team.CoverOpacity ??= 0.7;

        await _db.SaveChangesAsync();

        var opacityPercent = (int)Math.Round((team.CoverOpacity ?? 0.7) * 100);

        if (IsAjaxRequest())
        {
            return Ok(new { success = true, coverUrl = team.CoverImageUrl, opacity = opacityPercent, message = "Đã cập nhật ảnh bìa CLB thành công!" });
        }

        TempData["Success"] = "Đã cập nhật ảnh bìa CLB thành công!";
        return RedirectToAction(nameof(PublicProfile), new { slug = team.Slug });
    }

    // 4. Cập nhật độ mờ Ảnh bìa (0 - 100%)
    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCoverOpacity(Guid teamId, int opacityPercent)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return Unauthorized(new { success = false, message = "Vui lòng đăng nhập để thực hiện." });

        if (!await _auth.HasManagerAccessAsync(teamId, user.Id))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "Chỉ Đội trưởng hoặc Ban quản trị CLB mới có quyền chỉnh độ mờ ảnh bìa." });

        var team = await _db.Teams.FindAsync(teamId);
        if (team == null)
            return NotFound(new { success = false, message = "Không tìm thấy đội bóng." });

        var clamped = Math.Clamp(opacityPercent, 0, 100);
        team.CoverOpacity = clamped / 100.0;
        await _db.SaveChangesAsync();

        if (IsAjaxRequest())
        {
            return Ok(new { success = true, opacity = clamped, message = "Đã lưu độ mờ ảnh bìa thành công!" });
        }

        TempData["Success"] = "Đã lưu độ mờ ảnh bìa thành công!";
        return RedirectToAction(nameof(PublicProfile), new { slug = team.Slug });
    }

    // 5. Xóa Ảnh bìa CLB
    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCover(Guid teamId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return Unauthorized(new { success = false, message = "Vui lòng đăng nhập để thực hiện." });

        if (!await _auth.HasManagerAccessAsync(teamId, user.Id))
            return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "Chỉ Đội trưởng hoặc Ban quản trị CLB mới có quyền xóa ảnh bìa." });

        var team = await _db.Teams.FindAsync(teamId);
        if (team == null)
            return NotFound(new { success = false, message = "Không tìm thấy đội bóng." });

        var webRoot = GetWebRootPath();
        DeleteLocalTeamFile(team.CoverImageUrl, webRoot);

        team.CoverImageUrl = null;
        await _db.SaveChangesAsync();

        if (IsAjaxRequest())
        {
            return Ok(new { success = true, message = "Đã xóa ảnh bìa CLB thành công!" });
        }

        TempData["Success"] = "Đã xóa ảnh bìa CLB thành công!";
        return RedirectToAction(nameof(PublicProfile), new { slug = team.Slug });
    }

    // Helper kiểm tra tính hợp lệ và nội dung an toàn của file ảnh
    private static (bool IsValid, string? Error) ValidateImageFile(IFormFile? file, long maxBytes)
    {
        if (file == null || file.Length == 0)
            return (false, "Vui lòng chọn file ảnh để tải lên.");

        if (file.Length > maxBytes)
            return (false, $"Dung lượng ảnh vượt quá giới hạn cho phép ({maxBytes / (1024 * 1024)}MB).");

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedExtensions.Contains(ext))
            return (false, "Định dạng file không được hỗ trợ. Vui lòng chọn ảnh JPG, PNG hoặc WEBP.");

        if (!AllowedMimeTypes.Contains(file.ContentType))
            return (false, "Kiểu nội dung MIME không hợp lệ.");

        // Kiểm tra magic bytes
        try
        {
            using var stream = file.OpenReadStream();
            var header = new byte[12];
            int bytesRead = stream.Read(header, 0, header.Length);
            if (bytesRead < 4)
                return (false, "File ảnh bị lỗi hoặc không có nội dung hợp lệ.");

            bool isJpeg = header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
            bool isPng = header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;
            bool isWebp = bytesRead >= 12 &&
                          header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
                          header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50;

            if (!isJpeg && !isPng && !isWebp)
                return (false, "Nội dung file không phải là ảnh hợp lệ.");
        }
        catch
        {
            return (false, "Không thể xác thực nội dung file ảnh.");
        }

        return (true, null);
    }

    private static void DeleteLocalTeamFile(string? relativeUrl, string webRoot)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl) || !relativeUrl.StartsWith("/uploads/teams/"))
            return;

        try
        {
            var cleanPath = relativeUrl.Split('?', '#')[0];
            var fileName = Path.GetFileName(cleanPath);
            if (string.IsNullOrWhiteSpace(fileName))
                return;

            var uploadDir = Path.Combine(webRoot, "uploads", "teams");
            var filePath = Path.Combine(uploadDir, fileName);
            if (Path.GetFullPath(filePath).StartsWith(Path.GetFullPath(uploadDir)) && System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }
        }
        catch
        {
            // Bỏ qua lỗi dọn file cũ để không gián đoạn luồng chính
        }
    }

    private string GetWebRootPath()
    {
        var webRoot = _env?.WebRootPath;
        if (!string.IsNullOrEmpty(webRoot) && Directory.Exists(webRoot))
            return webRoot;

        var contentRoot = _env?.ContentRootPath ?? Directory.GetCurrentDirectory();
        var candidate = Path.Combine(contentRoot, "wwwroot");
        if (Directory.Exists(candidate))
            return candidate;

        return webRoot ?? candidate;
    }

    private bool IsAjaxRequest()
    {
        return Request.Headers["X-Requested-With"] == "XMLHttpRequest"
            || (Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase));
    }
}
