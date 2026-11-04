using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace AloBall.Web.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly ITeamAuthorizationService _auth;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db,
        ITeamAuthorizationService auth)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _db = db;
        _auth = auth;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(model);

        var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: false);
        if (result.Succeeded)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction("Index", "Teams");
        }

        ModelState.AddModelError(string.Empty, "Email hoặc mật khẩu không chính xác.");
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult Register(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(model);

        var existingUser = await _userManager.FindByEmailAsync(model.Email);
        if (existingUser != null)
        {
            ModelState.AddModelError("Email", "Email này đã được sử dụng.");
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            FullName = model.FullName.Trim(),
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (result.Succeeded)
        {
            await _signInManager.SignInAsync(user, isPersistent: true);
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction(nameof(Onboarding));
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }
        return View(model);
    }

    [HttpGet, Authorize]
    public async Task<IActionResult> Onboarding(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var model = new OnboardingViewModel
        {
            FullName = user.FullName,
            PreferredFoot = user.PreferredFoot ?? "",
            Experience = user.Experience ?? "",
            Region = user.Region ?? "",
            PrimaryPosition = user.PrimaryPosition
        };
        return View(model);
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Onboarding(OnboardingViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (string.IsNullOrWhiteSpace(model.FullName))
            ModelState.AddModelError(nameof(model.FullName), "Vui lòng nhập Họ và tên.");

        if (string.IsNullOrWhiteSpace(model.PreferredFoot))
            ModelState.AddModelError(nameof(model.PreferredFoot), "Vui lòng chọn chân thuận.");

        if (string.IsNullOrWhiteSpace(model.Experience))
            ModelState.AddModelError(nameof(model.Experience), "Vui lòng chọn hoặc nhập kinh nghiệm.");

        if (string.IsNullOrWhiteSpace(model.Region))
            ModelState.AddModelError(nameof(model.Region), "Vui lòng nhập khu vực hoạt động.");

        if (!ModelState.IsValid)
            return View(model);

        user.FullName = model.FullName.Trim();
        user.PreferredFoot = model.PreferredFoot.Trim();
        user.Experience = model.Experience.Trim();
        user.Region = model.Region.Trim();
        user.PrimaryPosition = model.PrimaryPosition?.Trim();
        user.HasCompletedOnboarding = true;
        user.OnboardingCompletedAt = DateTime.UtcNow;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            foreach (var err in updateResult.Errors)
                ModelState.AddModelError(string.Empty, err.Description);
            return View(model);
        }

        // Đồng bộ hồ sơ sang Player liên kết nếu có
        var linkedPlayers = await _db.Players.Where(p => p.LinkedUserId == user.Id).ToListAsync();
        foreach (var lp in linkedPlayers)
        {
            lp.FullName = user.FullName;
            lp.PreferredFoot = user.PreferredFoot;
            lp.Experience = user.Experience;
            lp.Region = user.Region;
            // Không ghi đè vị trí đội bóng đã được lãnh đội phân công
            if (IsUnassignedPosition(lp.Position) && !string.IsNullOrWhiteSpace(user.PrimaryPosition))
            {
                lp.Position = user.PrimaryPosition.Trim();
            }
        }
        await _db.SaveChangesAsync();

        if (TempData != null) TempData["Success"] = "Chào mừng bạn gia nhập AloBall! Hồ sơ cầu thủ đã sẵn sàng.";
        if (!string.IsNullOrEmpty(returnUrl) && Url != null && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction("Index", "Teams");
    }

    [HttpGet, Authorize]
    public async Task<IActionResult> Profile(string? userId = null)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser == null) return Challenge();

        ApplicationUser targetUser = currentUser;
        if (!string.IsNullOrEmpty(userId) && userId != currentUser.Id)
        {
            if (!await _auth.IsPlatformAdminAsync(currentUser.Id))
                return Forbid();

            var found = await _userManager.FindByIdAsync(userId);
            if (found == null) return NotFound("Không tìm thấy người dùng.");
            targetUser = found;
        }

        var model = new UserProfileViewModel
        {
            UserId = targetUser.Id,
            FullName = targetUser.FullName,
            Email = targetUser.Email ?? "",
            PreferredFoot = targetUser.PreferredFoot,
            Experience = targetUser.Experience,
            Region = targetUser.Region,
            PrimaryPosition = targetUser.PrimaryPosition,
            PhoneNumber = targetUser.PhoneNumber,
            HasCompletedOnboarding = targetUser.HasCompletedOnboarding,
            OnboardingCompletedAt = targetUser.OnboardingCompletedAt,
            IsSelf = (targetUser.Id == currentUser.Id)
        };
        return View(model);
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(UserProfileViewModel model)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser == null) return Challenge();

        ApplicationUser targetUser = currentUser;
        if (!string.IsNullOrEmpty(model.UserId) && model.UserId != currentUser.Id)
        {
            if (!await _auth.IsPlatformAdminAsync(currentUser.Id))
                return Forbid();

            var found = await _userManager.FindByIdAsync(model.UserId);
            if (found == null) return NotFound("Không tìm thấy người dùng.");
            targetUser = found;
        }

        if (string.IsNullOrWhiteSpace(model.FullName))
            ModelState.AddModelError(nameof(model.FullName), "Vui lòng nhập Họ và tên.");

        if (!ModelState.IsValid)
        {
            model.Email = targetUser.Email ?? "";
            model.IsSelf = (targetUser.Id == currentUser.Id);
            return View(model);
        }

        targetUser.FullName = model.FullName.Trim();
        targetUser.PreferredFoot = model.PreferredFoot?.Trim();
        targetUser.Experience = model.Experience?.Trim();
        targetUser.Region = model.Region?.Trim();
        targetUser.PrimaryPosition = model.PrimaryPosition?.Trim();
        targetUser.PhoneNumber = model.PhoneNumber?.Trim();

        var result = await _userManager.UpdateAsync(targetUser);
        if (!result.Succeeded)
        {
            foreach (var err in result.Errors)
                ModelState.AddModelError(string.Empty, err.Description);
            model.Email = targetUser.Email ?? "";
            model.IsSelf = (targetUser.Id == currentUser.Id);
            return View(model);
        }

        // Đồng bộ hồ sơ sang Player liên kết nếu có
        var linkedPlayers = await _db.Players.Where(p => p.LinkedUserId == targetUser.Id).ToListAsync();
        foreach (var lp in linkedPlayers)
        {
            lp.FullName = targetUser.FullName;
            if (!string.IsNullOrEmpty(targetUser.PreferredFoot)) lp.PreferredFoot = targetUser.PreferredFoot;
            if (!string.IsNullOrEmpty(targetUser.Experience)) lp.Experience = targetUser.Experience;
            if (!string.IsNullOrEmpty(targetUser.Region)) lp.Region = targetUser.Region;
            // Không ghi đè vị trí đội bóng đã được lãnh đội phân công
            if (IsUnassignedPosition(lp.Position) && !string.IsNullOrWhiteSpace(targetUser.PrimaryPosition))
            {
                lp.Position = targetUser.PrimaryPosition.Trim();
            }
        }
        await _db.SaveChangesAsync();

        if (TempData != null) TempData["Success"] = "Đã lưu cập nhật hồ sơ thành công!";
        return RedirectToAction(nameof(Profile), new { userId = (targetUser.Id == currentUser.Id ? null : targetUser.Id) });
    }

    private static bool IsUnassignedPosition(string? position)
    {
        if (string.IsNullOrWhiteSpace(position))
            return true;

        var trimmed = position.Trim();
        return string.Equals(trimmed, "Chưa chọn", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "Chưa xác định", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "UNASSIGNED", StringComparison.OrdinalIgnoreCase);
    }
}

public class LoginViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập Email")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập Mật khẩu")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; } = true;
}

public class RegisterViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập Họ và tên")]
    [MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập Email")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập Mật khẩu")]
    [StringLength(100, ErrorMessage = "{0} phải có độ dài ít nhất {2} ký tự.", MinimumLength = 6)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Compare("Password", ErrorMessage = "Mật khẩu xác nhận không khớp.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class UserProfileViewModel
{
    public string? UserId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập Họ và tên")]
    [MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? PreferredFoot { get; set; }

    [MaxLength(50)]
    public string? Experience { get; set; }

    [MaxLength(100)]
    public string? Region { get; set; }

    [MaxLength(50)]
    public string? PrimaryPosition { get; set; }

    [Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    public bool HasCompletedOnboarding { get; set; }
    public DateTime? OnboardingCompletedAt { get; set; }
    public bool IsSelf { get; set; } = true;
}

public class OnboardingViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập Họ và tên")]
    [MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng chọn chân thuận")]
    [MaxLength(20)]
    public string PreferredFoot { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng chọn hoặc nhập kinh nghiệm")]
    [MaxLength(50)]
    public string Experience { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập khu vực hoạt động")]
    [MaxLength(100)]
    public string Region { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? PrimaryPosition { get; set; }
}
