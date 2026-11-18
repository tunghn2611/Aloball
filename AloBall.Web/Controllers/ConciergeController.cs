using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AloBall.Web.Controllers;

[ServiceFilter(typeof(DomainMutationFilter))]
public class ConciergeController : Controller
{
    private readonly IMatchConciergeService _concierge;
    private readonly UserManager<ApplicationUser> _userManager;

    public ConciergeController(
        IMatchConciergeService concierge,
        UserManager<ApplicationUser> userManager)
    {
        _concierge = concierge;
        _userManager = userManager;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index(Guid? teamId = null, string? tab = null)
    {
        var user = User?.Identity?.IsAuthenticated == true ? await _userManager.GetUserAsync(User) : null;
        var scheme = Request?.Scheme ?? "https";
        var host = Request?.Host.Value;
        var baseUrl = string.IsNullOrEmpty(host) ? string.Empty : $"{scheme}://{host}";

        var vm = await _concierge.BuildWorkspaceAsync(user?.Id, teamId, tab, baseUrl);
        return View(vm);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ActivateTrial(Guid teamId, PlanTier tier = PlanTier.Plus, string? tab = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message) = await _concierge.ActivatePlusTrialAsync(teamId, user.Id, tier);
        TempData[success ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(Index), new { teamId, tab = tab ?? "concierge" });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateConciergeOrder(ConciergeOrderInput input)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message, _, _) = await _concierge.CreateAndFulfillConciergeOrderAsync(input, user.Id);
        TempData[success ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(Index), new { teamId = input.TeamId, tab = "concierge" });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRecurringSchedule(RecurringScheduleInput input)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message, _) = await _concierge.SaveRecurringScheduleAsync(input, user.Id);
        TempData[success ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(Index), new { teamId = input.TeamId, tab = "autopair" });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunAutoPairing(Guid teamId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message, _) = await _concierge.RunAutoPairingForTeamAsync(teamId, user.Id);
        TempData[success ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(Index), new { teamId, tab = "autopair" });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmAutoPairing(Guid orderId, Guid teamId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message, _) = await _concierge.ConfirmAutoPairingOrderAsync(orderId, user.Id);
        TempData[success ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(Index), new { teamId, tab = "autopair" });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelOrder(Guid orderId, Guid teamId, string? tab = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message) = await _concierge.CancelOrderAsync(orderId, user.Id);
        TempData[success ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(Index), new { teamId, tab = tab ?? "autopair" });
    }
}
