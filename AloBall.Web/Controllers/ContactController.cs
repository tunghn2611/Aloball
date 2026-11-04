using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Controllers;

[Authorize]
public class ContactController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITeamAuthorizationService _auth;
    private readonly IRefereeCoordinationService _refereeService;
    private readonly IPitchBookingService _pitchService;

    public ContactController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ITeamAuthorizationService auth,
        IRefereeCoordinationService refereeService,
        IPitchBookingService pitchService)
    {
        _db = db;
        _userManager = userManager;
        _auth = auth;
        _refereeService = refereeService;
        _pitchService = pitchService;
    }

    // ==========================================
    // 1. TỔNG QUAN LIÊN HỆ (TRỌNG TÀI & SÂN BÓNG)
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> Index(
        string? tab = "referee",
        string? search = null,
        string? city = null,
        string? district = null,
        string? format = null,
        string? surface = null,
        decimal? maxPrice = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var isPlatformAdmin = await _auth.IsPlatformAdminAsync(user.Id);
        ViewBag.IsPlatformAdmin = isPlatformAdmin;
        ViewBag.CurrentTab = string.Equals(tab, "stadium", StringComparison.OrdinalIgnoreCase) ? "stadium" : "referee";

        // Fetch user's managed teams
        var managedTeamIds = await _db.TeamMembers
            .Where(m => m.UserId == user.Id && (m.Role == TeamRole.Manager || m.Role == TeamRole.Owner))
            .Select(m => m.TeamId)
            .ToListAsync();

        var managedTeams = await _db.Teams
            .Where(t => managedTeamIds.Contains(t.Id))
            .ToListAsync();

        ViewBag.ManagedTeams = managedTeams;

        // Referee Requests
        var userRequests = await _refereeService.GetUserManageableRequestsAsync(user.Id);

        // Pitch Discovery & Pitch Bookings
        ViewBag.Venues = await _pitchService.GetActiveVenuesAsync(search, city, district, format, surface, maxPrice);
        ViewBag.AvailableCities = await _pitchService.GetAvailableCitiesAsync();
        ViewBag.AvailableDistricts = await _pitchService.GetAvailableDistrictsAsync(city);
        ViewBag.PitchBookingRequests = await _pitchService.GetUserManageableRequestsAsync(user.Id);

        ViewBag.SelectedSearch = search;
        ViewBag.SelectedCity = city;
        ViewBag.SelectedDistrict = district;
        ViewBag.SelectedFormat = format;
        ViewBag.SelectedSurface = surface;
        ViewBag.SelectedMaxPrice = maxPrice;

        return View(userRequests);
    }

    // ==========================================
    // 2. REFEREE COORDINATION (ĐIỀU PHỐI TRỌNG TÀI)
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> CreateRefereeRequest(Guid? teamId = null, Guid? matchId = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var managedTeamIds = await _db.TeamMembers
            .Where(m => m.UserId == user.Id && (m.Role == TeamRole.Manager || m.Role == TeamRole.Owner))
            .Select(m => m.TeamId)
            .ToListAsync();

        var managedTeams = await _db.Teams
            .Where(t => managedTeamIds.Contains(t.Id))
            .ToListAsync();

        if (!managedTeams.Any() && !await _auth.IsPlatformAdminAsync(user.Id))
        {
            TempData["Error"] = "Bạn cần là Đội trưởng hoặc Ban cán sự của ít nhất 1 đội bóng để tạo yêu cầu trọng tài.";
            return RedirectToAction(nameof(Index));
        }

        ViewBag.ManagedTeams = managedTeams;

        var model = new CreateRefereeRequestViewModel();

        if (teamId.HasValue && managedTeams.Any(t => t.Id == teamId.Value))
        {
            model.TeamId = teamId.Value;
        }
        else if (managedTeams.Any())
        {
            model.TeamId = managedTeams.First().Id;
        }

        if (matchId.HasValue)
        {
            var match = await _db.Matches
                .Include(m => m.TeamA)
                .Include(m => m.TeamB)
                .FirstOrDefaultAsync(m => m.Id == matchId.Value);

            if (match != null)
            {
                model.MatchId = match.Id;
                model.ServiceDate = match.MatchDateTime.Date;
                model.StartTime = match.MatchDateTime.TimeOfDay;
                model.EndTime = model.StartTime.Add(TimeSpan.FromMinutes(90));
                model.VenueName = match.PitchName ?? string.Empty;
                model.VenueAddress = match.PitchName ?? string.Empty;
                model.MatchFormat = !string.IsNullOrWhiteSpace(match.PitchType) ? match.PitchType : "7 người";

                if (managedTeams.Any(t => t.Id == match.TeamAId))
                {
                    model.TeamId = match.TeamAId;
                }
                else if (match.TeamBId.HasValue && managedTeams.Any(t => t.Id == match.TeamBId.Value))
                {
                    model.TeamId = match.TeamBId.Value;
                }
            }
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRefereeRequest(CreateRefereeRequestViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var managedTeamIds = await _db.TeamMembers
            .Where(m => m.UserId == user.Id && (m.Role == TeamRole.Manager || m.Role == TeamRole.Owner))
            .Select(m => m.TeamId)
            .ToListAsync();

        var managedTeams = await _db.Teams
            .Where(t => managedTeamIds.Contains(t.Id))
            .ToListAsync();

        ViewBag.ManagedTeams = managedTeams;

        if (model.ServiceDate.Date < DateTime.Today)
            ModelState.AddModelError(nameof(model.ServiceDate), "Ngày thi đấu không được ở trong quá khứ.");
        if (model.EndTime <= model.StartTime)
            ModelState.AddModelError(nameof(model.EndTime), "Giờ kết thúc phải muộn hơn giờ bắt đầu.");
        if (model.RefereeCount <= 0)
            ModelState.AddModelError(nameof(model.RefereeCount), "Số lượng trọng tài phải từ 1 người trở lên.");
        if (model.BudgetVND.HasValue && model.BudgetVND.Value < 0)
            ModelState.AddModelError(nameof(model.BudgetVND), "Ngân sách không được âm.");

        if (!ModelState.IsValid)
            return View(model);

        try
        {
            var req = new RefereeRequest
            {
                TeamId = model.TeamId,
                MatchId = model.MatchId,
                ServiceDate = model.ServiceDate.Date,
                StartTime = model.StartTime,
                EndTime = model.EndTime,
                VenueName = model.VenueName.Trim(),
                VenueAddress = model.VenueAddress.Trim(),
                MatchFormat = model.MatchFormat.Trim(),
                RefereeCount = model.RefereeCount,
                PreferredExperience = model.PreferredExperience?.Trim(),
                BudgetVND = model.BudgetVND,
                Notes = model.Notes?.Trim()
            };

            var created = await _refereeService.CreateRequestAsync(req, user.Id);
            TempData["Success"] = "Đã gửi yêu cầu trọng tài thành công! Quản trị viên sẽ sớm kiểm tra và gửi phương án báo giá.";
            return RedirectToAction(nameof(RefereeRequestDetails), new { id = created.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> RefereeRequestDetails(Guid id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var isPlatformAdmin = await _auth.IsPlatformAdminAsync(user.Id);
        ViewBag.IsPlatformAdmin = isPlatformAdmin;

        try
        {
            var req = await _refereeService.GetRequestDetailsAsync(id, user.Id, isPlatformAdmin);
            if (req == null) return NotFound();

            var canManage = isPlatformAdmin || await _auth.HasManagerAccessAsync(req.TeamId, user.Id);
            ViewBag.CanManage = canManage;

            return View(req);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelRefereeRequest(Guid id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            var result = await _refereeService.CancelRequestAsync(id, user.Id);
            if (result) TempData["Success"] = "Đã hủy yêu cầu trọng tài thành công.";
            else TempData["Error"] = "Không tìm thấy yêu cầu.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(RefereeRequestDetails), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptProposal(Guid proposalId, Guid requestId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            var result = await _refereeService.AcceptProposalAsync(proposalId, user.Id);
            if (result) TempData["Success"] = "Đã chấp nhận báo giá thành công! Dịch vụ đã được xác nhận. Thông tin liên hệ của trọng tài đã được mở khóa bên dưới.";
            else TempData["Error"] = "Không thể chốt báo giá này.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(RefereeRequestDetails), new { id = requestId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeclineProposal(Guid proposalId, Guid requestId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            var result = await _refereeService.DeclineProposalAsync(proposalId, user.Id);
            if (result) TempData["Success"] = "Đã từ chối phương án báo giá. Quản trị viên sẽ được thông báo để gửi lại phương án mới.";
            else TempData["Error"] = "Không thể từ chối báo giá này.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(RefereeRequestDetails), new { id = requestId });
    }

    // ==========================================
    // 3. PITCH DISCOVERY & BOOKING (ĐẶT SÂN BÓNG)
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> PitchVenueDetails(Guid id)
    {
        var venue = await _pitchService.GetVenueDetailsAsync(id);
        if (venue == null) return NotFound();

        var user = await _userManager.GetUserAsync(User);
        if (user != null)
        {
            var managedTeamIds = await _db.TeamMembers
                .Where(m => m.UserId == user.Id && (m.Role == TeamRole.Manager || m.Role == TeamRole.Owner))
                .Select(m => m.TeamId)
                .ToListAsync();

            ViewBag.CanBook = managedTeamIds.Any() || await _auth.IsPlatformAdminAsync(user.Id);
        }
        else
        {
            ViewBag.CanBook = false;
        }

        return View(venue);
    }

    [HttpGet]
    public async Task<IActionResult> CreatePitchBookingRequest(Guid? venueId = null, Guid? matchId = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var managedTeamIds = await _db.TeamMembers
            .Where(m => m.UserId == user.Id && (m.Role == TeamRole.Manager || m.Role == TeamRole.Owner))
            .Select(m => m.TeamId)
            .ToListAsync();

        var managedTeams = await _db.Teams
            .Where(t => managedTeamIds.Contains(t.Id))
            .ToListAsync();

        if (!managedTeams.Any() && !await _auth.IsPlatformAdminAsync(user.Id))
        {
            TempData["Error"] = "Bạn cần là Đội trưởng hoặc Ban cán sự của ít nhất 1 đội bóng để đặt sân.";
            return RedirectToAction(nameof(Index), new { tab = "stadium" });
        }

        ViewBag.ManagedTeams = managedTeams;
        ViewBag.ActiveVenues = await _pitchService.GetActiveVenuesAsync();

        var model = new CreatePitchBookingRequestViewModel();

        if (managedTeams.Any())
        {
            model.TeamId = managedTeams.First().Id;
        }

        if (venueId.HasValue)
        {
            model.PitchVenueId = venueId.Value;
        }
        else
        {
            var firstVenue = (ViewBag.ActiveVenues as List<PitchVenue>)?.FirstOrDefault();
            if (firstVenue != null) model.PitchVenueId = firstVenue.Id;
        }

        // Prefill from Match if requested
        if (matchId.HasValue)
        {
            var match = await _db.Matches
                .Include(m => m.TeamA)
                .Include(m => m.TeamB)
                .FirstOrDefaultAsync(m => m.Id == matchId.Value);

            if (match != null)
            {
                model.MatchId = match.Id;
                model.ServiceDate = match.MatchDateTime.Date;
                model.StartTime = match.MatchDateTime.TimeOfDay;
                model.EndTime = model.StartTime.Add(TimeSpan.FromMinutes(90));
                model.PitchFormat = !string.IsNullOrWhiteSpace(match.PitchType) ? match.PitchType : "7 người";

                if (managedTeams.Any(t => t.Id == match.TeamAId))
                {
                    model.TeamId = match.TeamAId;
                }
                else if (match.TeamBId.HasValue && managedTeams.Any(t => t.Id == match.TeamBId.Value))
                {
                    model.TeamId = match.TeamBId.Value;
                }

                // Try to find matching venue by name
                if (!venueId.HasValue && !string.IsNullOrWhiteSpace(match.PitchName))
                {
                    var matchedVenue = await _db.PitchVenues
                        .FirstOrDefaultAsync(v => v.IsActive && v.Name.ToLower().Contains(match.PitchName.ToLower()));
                    if (matchedVenue != null)
                    {
                        model.PitchVenueId = matchedVenue.Id;
                    }
                }
            }
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePitchBookingRequest(CreatePitchBookingRequestViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var managedTeamIds = await _db.TeamMembers
            .Where(m => m.UserId == user.Id && (m.Role == TeamRole.Manager || m.Role == TeamRole.Owner))
            .Select(m => m.TeamId)
            .ToListAsync();

        var managedTeams = await _db.Teams
            .Where(t => managedTeamIds.Contains(t.Id))
            .ToListAsync();

        ViewBag.ManagedTeams = managedTeams;
        ViewBag.ActiveVenues = await _pitchService.GetActiveVenuesAsync();

        if (model.ServiceDate.Date < DateTime.Today)
            ModelState.AddModelError(nameof(model.ServiceDate), "Ngày đặt sân không được ở trong quá khứ.");
        if (model.EndTime <= model.StartTime)
            ModelState.AddModelError(nameof(model.EndTime), "Giờ kết thúc phải muộn hơn giờ bắt đầu.");
        if (model.PitchCount <= 0)
            ModelState.AddModelError(nameof(model.PitchCount), "Số lượng sân phải từ 1 sân trở lên.");
        if (model.BudgetVND.HasValue && model.BudgetVND.Value < 0)
            ModelState.AddModelError(nameof(model.BudgetVND), "Ngân sách không được âm.");

        if (!ModelState.IsValid)
            return View(model);

        try
        {
            var req = new PitchBookingRequest
            {
                TeamId = model.TeamId,
                PitchVenueId = model.PitchVenueId,
                MatchId = model.MatchId,
                ServiceDate = model.ServiceDate.Date,
                StartTime = model.StartTime,
                EndTime = model.EndTime,
                PitchFormat = model.PitchFormat.Trim(),
                PitchCount = model.PitchCount,
                BudgetVND = model.BudgetVND,
                Notes = model.Notes?.Trim()
            };

            var created = await _pitchService.CreateBookingRequestAsync(req, user.Id);
            TempData["Success"] = "Đã gửi yêu cầu đặt sân thành công! AloBall sẽ liên hệ chủ sân để xác nhận tình trạng sân và gửi báo giá cho đội.";
            return RedirectToAction(nameof(PitchBookingRequestDetails), new { id = created.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> PitchBookingRequestDetails(Guid id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var isPlatformAdmin = await _auth.IsPlatformAdminAsync(user.Id);
        ViewBag.IsPlatformAdmin = isPlatformAdmin;

        try
        {
            var req = await _pitchService.GetRequestDetailsAsync(id, user.Id, isPlatformAdmin);
            if (req == null) return NotFound();

            var canManage = isPlatformAdmin || await _auth.HasManagerAccessAsync(req.TeamId, user.Id);
            ViewBag.CanManage = canManage;

            return View(req);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelPitchBookingRequest(Guid id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            var result = await _pitchService.CancelBookingRequestAsync(id, user.Id);
            if (result) TempData["Success"] = "Đã hủy yêu cầu đặt sân thành công.";
            else TempData["Error"] = "Không tìm thấy yêu cầu đặt sân.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(PitchBookingRequestDetails), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptPitchBookingOffer(Guid offerId, Guid requestId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            var result = await _pitchService.AcceptBookingOfferAsync(offerId, user.Id);
            if (result) TempData["Success"] = "Đã chấp nhận báo giá đặt sân thành công! Lịch đặt sân đã được xác nhận (Confirmed).";
            else TempData["Error"] = "Không thể chốt báo giá đặt sân này.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(PitchBookingRequestDetails), new { id = requestId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeclinePitchBookingOffer(Guid offerId, Guid requestId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        try
        {
            var result = await _pitchService.DeclineBookingOfferAsync(offerId, user.Id);
            if (result) TempData["Success"] = "Đã từ chối phương án đặt sân. Quản trị viên sẽ được thông báo để kiểm tra lại.";
            else TempData["Error"] = "Không thể từ chối báo giá này.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(PitchBookingRequestDetails), new { id = requestId });
    }
}
