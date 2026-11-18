using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AloBall.Web.Controllers;

[ServiceFilter(typeof(DomainMutationFilter))]
public class TournamentsController : Controller
{
    private readonly ITournamentService _tournaments;
    private readonly UserManager<ApplicationUser> _userManager;

    public TournamentsController(ITournamentService tournaments, UserManager<ApplicationUser> userManager)
    {
        _tournaments = tournaments;
        _userManager = userManager;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index(string? region = null, TournamentStatus? status = null)
    {
        var user = User?.Identity?.IsAuthenticated == true ? await _userManager.GetUserAsync(User) : null;
        var vm = await _tournaments.BuildIndexAsync(region, status, user?.Id);
        return View(vm);
    }

    [HttpGet]
    [Authorize]
    public IActionResult Create()
    {
        return View(new CreateTournamentInput
        {
            StartDate = CompetitionRules.LocalNow.Date.AddDays(3),
            Region = "Cầu Giấy, Hà Nội",
            PitchVenue = "Sân bóng Thành Phát"
        });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateTournamentInput input)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!ModelState.IsValid)
            return View(input);

        var (success, message, created) = await _tournaments.CreateTournamentAsync(input, user.Id);
        if (!success || created == null)
        {
            TempData["Error"] = message;
            return View(input);
        }

        TempData["Success"] = message;
        return RedirectToAction(nameof(Details), new { id = created.Id });
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Details(Guid id, string? tab = "standings")
    {
        var user = User?.Identity?.IsAuthenticated == true ? await _userManager.GetUserAsync(User) : null;
        var scheme = Request?.Scheme ?? "https";
        var host = Request?.Host.Value;
        var baseUrl = string.IsNullOrEmpty(host) ? string.Empty : $"{scheme}://{host}";

        var vm = await _tournaments.BuildDetailsAsync(id, user?.Id, tab, baseUrl);
        if (vm == null) return NotFound();

        return View(vm);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddTeam(Guid tournamentId, Guid teamId, string? groupName = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message) = await _tournaments.AddTeamByOrganizerAsync(tournamentId, teamId, groupName, user.Id);
        TempData[success ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(Details), new { id = tournamentId });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> JoinByCode(string inviteCode, Guid teamId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message, tournamentId) = await _tournaments.JoinByInviteCodeAsync(inviteCode, teamId, user.Id);
        TempData[success ? "Success" : "Error"] = message;

        if (tournamentId.HasValue)
            return RedirectToAction(nameof(Details), new { id = tournamentId.Value });

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveTeam(Guid tournamentId, Guid teamId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message) = await _tournaments.RemoveTeamAsync(tournamentId, teamId, user.Id);
        TempData[success ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(Details), new { id = tournamentId });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateFixtures(Guid tournamentId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message, _) = await _tournaments.GenerateFixturesAsync(tournamentId, user.Id);
        TempData[success ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(Details), new { id = tournamentId, tab = "fixtures" });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateNextRound(Guid tournamentId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message, _) = await _tournaments.GenerateNextRoundAsync(tournamentId, user.Id);
        TempData[success ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(Details), new { id = tournamentId, tab = "fixtures" });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteTournament(Guid tournamentId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var (success, message) = await _tournaments.CompleteTournamentAsync(tournamentId, user.Id);
        TempData[success ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(Details), new { id = tournamentId });
    }
}
