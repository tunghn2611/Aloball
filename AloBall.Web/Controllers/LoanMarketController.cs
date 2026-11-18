using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AloBall.Web.Controllers;

[ServiceFilter(typeof(DomainMutationFilter))]
public class LoanMarketController : Controller
{
    private readonly ILoanMarketService _loanMarket;
    private readonly UserManager<ApplicationUser> _userManager;

    public LoanMarketController(
        ILoanMarketService loanMarket,
        UserManager<ApplicationUser> userManager)
    {
        _loanMarket = loanMarket;
        _userManager = userManager;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index(
        string? tab = null,
        string? region = null,
        DateTime? playDate = null,
        string? position = null,
        string? feeFilter = null)
    {
        var user = (User?.Identity?.IsAuthenticated == true) ? await _userManager.GetUserAsync(User) : null;
        var scheme = Request?.Scheme ?? "https";
        var host = Request?.Host.Value;
        var baseUrl = string.IsNullOrEmpty(host) ? "/LoanMarket" : $"{scheme}://{host}/LoanMarket";

        var vm = await _loanMarket.BuildMarketplaceAsync(
            tab,
            region,
            playDate,
            position,
            feeFilter,
            user?.Id,
            baseUrl);

        return View(vm);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateListing(CreateLoanListingInput input)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message, _) = await _loanMarket.CreateListingAsync(input, user.Id);
        if (success)
            TempData["Success"] = message;
        else
            TempData["Error"] = message;

        string targetTab = input.ListingType == LoanListingType.PlayerAvailable ? "players" : "teams";
        return RedirectToAction(nameof(Index), new { tab = targetTab });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(ApplyLoanInput input, string? returnTab = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message, _) = await _loanMarket.ApplyToListingAsync(input, user.Id);
        if (success)
            TempData["Success"] = message;
        else
            TempData["Error"] = message;

        return RedirectToAction(nameof(Index), new { tab = returnTab ?? "teams" });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptApplication(Guid listingId, Guid applicationId, string? returnTab = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message, _) = await _loanMarket.AcceptApplicationAsync(listingId, applicationId, user.Id);
        if (success)
            TempData["Success"] = message;
        else
            TempData["Error"] = message;

        return RedirectToAction(nameof(Index), new { tab = returnTab ?? "teams" });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DirectRecruit(
        Guid listingId,
        Guid recruitingTeamId,
        Guid? matchId,
        string? position)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message, _) = await _loanMarket.DirectRecruitAvailablePlayerAsync(
            listingId,
            recruitingTeamId,
            matchId,
            position,
            user.Id);

        if (success)
            TempData["Success"] = message;
        else
            TempData["Error"] = message;

        return RedirectToAction(nameof(Index), new { tab = "players" });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(Guid listingId, string? returnTab = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message) = await _loanMarket.CompleteLoanListingAsync(listingId, user.Id);
        if (success)
            TempData["Success"] = message;
        else
            TempData["Error"] = message;

        return RedirectToAction(nameof(Index), new { tab = returnTab ?? "teams" });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid listingId, string? returnTab = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message) = await _loanMarket.CancelListingAsync(listingId, user.Id);
        if (success)
            TempData["Success"] = message;
        else
            TempData["Error"] = message;

        return RedirectToAction(nameof(Index), new { tab = returnTab ?? "teams" });
    }
}
