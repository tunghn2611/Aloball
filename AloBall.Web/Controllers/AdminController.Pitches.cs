using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Controllers;

public partial class AdminController
{
    // ==========================================
    // 1. HỘP THƯ YÊU CẦU ĐẶT SÂN BÓNG (PITCH BOOKING INBOX)
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> PitchBookingRequests(PitchBookingStatus? status = null, string? search = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        ViewBag.CurrentStatus = status;
        ViewBag.SearchTerm = search;

        ViewBag.TotalPending = await _db.PitchBookingRequests.CountAsync(r => r.Status == PitchBookingStatus.Pending);
        ViewBag.TotalUnderReview = await _db.PitchBookingRequests.CountAsync(r => r.Status == PitchBookingStatus.UnderReview);
        ViewBag.TotalAwaitingDecision = await _db.PitchBookingRequests.CountAsync(r => r.Status == PitchBookingStatus.AwaitingTeamDecision);
        ViewBag.TotalConfirmed = await _db.PitchBookingRequests.CountAsync(r => r.Status == PitchBookingStatus.Confirmed);
        ViewBag.TotalCancelled = await _db.PitchBookingRequests.CountAsync(r => r.Status == PitchBookingStatus.Cancelled);

        var requests = await _pitchService.GetAdminInboxRequestsAsync(status, search);
        return View(requests);
    }

    [HttpGet]
    public async Task<IActionResult> PitchBookingRequestDetails(Guid id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        var req = await _pitchService.GetRequestDetailsAsync(id, user.Id, true);
        if (req == null) return NotFound();

        if (req.Status == PitchBookingStatus.Pending)
        {
            await _pitchService.SetRequestUnderReviewAsync(id, user.Id);
            req.Status = PitchBookingStatus.UnderReview;
        }

        return View(req);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitPitchBookingOffer(SubmitPitchBookingOfferViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Dữ liệu báo giá đặt sân không hợp lệ. Vui lòng kiểm tra lại.";
            return RedirectToAction(nameof(PitchBookingRequestDetails), new { id = model.RequestId });
        }

        try
        {
            var offer = new PitchBookingOffer
            {
                RequestId = model.RequestId,
                AdminId = user.Id,
                ConfirmedVenueName = model.ConfirmedVenueName.Trim(),
                ConfirmedAddress = model.ConfirmedAddress.Trim(),
                ConfirmedDate = model.ConfirmedDate.Date,
                ConfirmedStartTime = model.ConfirmedStartTime,
                ConfirmedEndTime = model.ConfirmedEndTime,
                ConfirmedPitchFormat = model.ConfirmedPitchFormat.Trim(),
                ConfirmedPitchCount = model.ConfirmedPitchCount,
                SpecificFieldCode = model.SpecificFieldCode?.Trim(),
                TotalPriceVND = model.TotalPriceVND,
                DepositInstructions = model.DepositInstructions?.Trim(),
                Notes = model.Notes?.Trim()
            };

            await _pitchService.CreateBookingOfferAsync(model.RequestId, user.Id, offer);
            TempData["Success"] = "Đã gửi phương án báo giá đặt sân thành công tới đội bóng!";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(PitchBookingRequestDetails), new { id = model.RequestId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkPitchBookingUnableToFulfill(Guid requestId, string? reason)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        try
        {
            await _pitchService.MarkUnableToFulfillAsync(requestId, user.Id, reason);
            TempData["Success"] = "Đã cập nhật trạng thái: Không thể đáp ứng đặt sân.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(PitchBookingRequestDetails), new { id = requestId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompletePitchBooking(Guid requestId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        try
        {
            await _pitchService.CompleteBookingAsync(requestId, user.Id);
            TempData["Success"] = "Đã đánh dấu hoàn thành dịch vụ đặt sân!";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(PitchBookingRequestDetails), new { id = requestId });
    }

    // ==========================================
    // 2. QUẢN LÝ DANH BẠ SÂN BÓNG (PITCH VENUES CRUD)
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> PitchVenues()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        var venues = await _pitchService.GetAllVenuesAsync();
        return View(venues);
    }

    [HttpGet]
    public async Task<IActionResult> CreatePitchVenue()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        return View("PitchVenueForm", new PitchVenueFormViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePitchVenue(PitchVenueFormViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        if (!ModelState.IsValid)
            return View("PitchVenueForm", model);

        try
        {
            var venue = new PitchVenue
            {
                Name = model.Name.Trim(),
                Description = model.Description?.Trim(),
                City = model.City.Trim(),
                District = model.District.Trim(),
                FullAddress = model.FullAddress.Trim(),
                ContactPhone = model.ContactPhone.Trim(),
                SupportedFormats = model.SupportedFormats.Trim(),
                SurfaceType = model.SurfaceType.Trim(),
                Facilities = model.Facilities?.Trim(),
                OpeningHours = model.OpeningHours?.Trim(),
                IndicativePricePerHour = model.IndicativePricePerHour,
                PriceNotes = model.PriceNotes?.Trim(),
                ImageUrl = model.ImageUrl?.Trim(),
                MapLink = model.MapLink?.Trim(),
                TotalPitches = model.TotalPitches > 0 ? model.TotalPitches : 1,
                IsActive = model.IsActive
            };

            await _pitchService.CreateVenueAsync(venue, user.Id);
            TempData["Success"] = $"Đã thêm cụm sân {venue.Name} vào hệ thống thành công.";
            return RedirectToAction(nameof(PitchVenues));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("PitchVenueForm", model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> EditPitchVenue(Guid id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        var venue = await _pitchService.GetVenueDetailsAsync(id);
        if (venue == null) return NotFound();

        var model = new PitchVenueFormViewModel
        {
            Id = venue.Id,
            Name = venue.Name,
            Description = venue.Description,
            City = venue.City,
            District = venue.District,
            FullAddress = venue.FullAddress,
            ContactPhone = venue.ContactPhone,
            SupportedFormats = venue.SupportedFormats,
            SurfaceType = venue.SurfaceType,
            Facilities = venue.Facilities,
            OpeningHours = venue.OpeningHours,
            IndicativePricePerHour = venue.IndicativePricePerHour,
            PriceNotes = venue.PriceNotes,
            ImageUrl = venue.ImageUrl,
            MapLink = venue.MapLink,
            TotalPitches = venue.TotalPitches,
            IsActive = venue.IsActive
        };

        return View("PitchVenueForm", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPitchVenue(PitchVenueFormViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        if (!model.Id.HasValue) return BadRequest();

        if (!ModelState.IsValid)
            return View("PitchVenueForm", model);

        try
        {
            var venue = new PitchVenue
            {
                Id = model.Id.Value,
                Name = model.Name.Trim(),
                Description = model.Description?.Trim(),
                City = model.City.Trim(),
                District = model.District.Trim(),
                FullAddress = model.FullAddress.Trim(),
                ContactPhone = model.ContactPhone.Trim(),
                SupportedFormats = model.SupportedFormats.Trim(),
                SurfaceType = model.SurfaceType.Trim(),
                Facilities = model.Facilities?.Trim(),
                OpeningHours = model.OpeningHours?.Trim(),
                IndicativePricePerHour = model.IndicativePricePerHour,
                PriceNotes = model.PriceNotes?.Trim(),
                ImageUrl = model.ImageUrl?.Trim(),
                MapLink = model.MapLink?.Trim(),
                TotalPitches = model.TotalPitches > 0 ? model.TotalPitches : 1,
                IsActive = model.IsActive
            };

            await _pitchService.UpdateVenueAsync(venue, user.Id);
            TempData["Success"] = $"Đã cập nhật thông tin cụm sân {venue.Name}.";
            return RedirectToAction(nameof(PitchVenues));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("PitchVenueForm", model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePitchVenueStatus(Guid id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        var result = await _pitchService.ToggleVenueStatusAsync(id, user.Id);
        if (result) TempData["Success"] = "Đã cập nhật trạng thái hoạt động của cụm sân.";
        else TempData["Error"] = "Không tìm thấy sân bóng.";

        return RedirectToAction(nameof(PitchVenues));
    }
}
