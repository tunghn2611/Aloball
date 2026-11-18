using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AloBall.Web.Controllers;

[ServiceFilter(typeof(DomainMutationFilter))]
public class PartnerPortalController : Controller
{
    private readonly IPartnerPortalService _partnerPortal;
    private readonly UserManager<ApplicationUser> _userManager;

    public PartnerPortalController(
        IPartnerPortalService partnerPortal,
        UserManager<ApplicationUser> userManager)
    {
        _partnerPortal = partnerPortal;
        _userManager = userManager;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index(string? tab = "pitch", Guid? pitchId = null)
    {
        var user = User?.Identity?.IsAuthenticated == true ? await _userManager.GetUserAsync(User) : null;
        var baseUrl = BuildBaseAppUrl();
        var vm = await _partnerPortal.BuildPortalAsync(user?.Id, tab, pitchId, baseUrl);
        return View(vm);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFlashDeal(CreateFlashDealInput input)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message, _) = await _partnerPortal.CreateFlashDealAsync(input, user.Id);
        TempData[success ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(Index), new { tab = "pitch", pitchId = input.PitchId });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelFlashDeal(Guid dealId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message) = await _partnerPortal.CancelFlashDealAsync(dealId, user.Id);
        TempData[success ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(Index), new { tab = "pitch" });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClaimFlashDeal(Guid dealId, Guid teamId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message, match) = await _partnerPortal.ClaimFlashDealAsync(dealId, teamId, user.Id);
        TempData[success ? "Success" : "Error"] = message;

        if (success && match != null)
            return RedirectToAction("Detail", "Matches", new { id = match.Id });

        return RedirectToAction(nameof(Index), new { tab = "pitch" });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateBookingStatus(Guid requestId, bool approve, string? fieldCode = null, decimal? offerPriceVnd = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message) = await _partnerPortal.UpdateBookingStatusByOwnerAsync(requestId, approve, fieldCode, offerPriceVnd, user.Id);
        TempData[success ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(Index), new { tab = "pitch" });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleRefereeAvailability(Guid refereeId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message) = await _partnerPortal.ToggleRefereeAvailabilityAsync(refereeId, user.Id);
        TempData[success ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(Index), new { tab = "referee" });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterPartnerVenue(RegisterPartnerVenueInput input)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Thông tin sân đối tác chưa hợp lệ.";
            return RedirectToAction(nameof(Index), new { tab = "pitch" });
        }

        var (success, message, venue) = await _partnerPortal.RegisterPartnerVenueAsync(input, user.Id);
        TempData[success ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(Index), new { tab = "pitch", pitchId = venue?.Id });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterRefereePartner(RegisterRefereePartnerInput input)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Thông tin trọng tài chưa hợp lệ.";
            return RedirectToAction(nameof(Index), new { tab = "referee" });
        }

        var (success, message, _) = await _partnerPortal.RegisterRefereePartnerAsync(input, user.Id);
        TempData[success ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(Index), new { tab = "referee" });
    }

    private string BuildBaseAppUrl()
    {
        var scheme = Request?.Scheme ?? "https";
        var host = Request?.Host.Value;
        return string.IsNullOrEmpty(host) ? string.Empty : $"{scheme}://{host}";
    }
}
