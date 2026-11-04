using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Controllers;

public partial class AdminController
{
    // ==========================================
    // 1. HỘP THƯ YÊU CẦU ĐIỀU PHỐI TRỌNG TÀI
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> RefereeRequests(RefereeRequestStatus? status = null, string? search = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        ViewBag.CurrentStatus = status;
        ViewBag.SearchTerm = search;

        // Count queries for filter badges
        ViewBag.TotalPending = await _db.RefereeRequests.CountAsync(r => r.Status == RefereeRequestStatus.Pending);
        ViewBag.TotalUnderReview = await _db.RefereeRequests.CountAsync(r => r.Status == RefereeRequestStatus.UnderReview);
        ViewBag.TotalAwaitingDecision = await _db.RefereeRequests.CountAsync(r => r.Status == RefereeRequestStatus.AwaitingTeamDecision);
        ViewBag.TotalConfirmed = await _db.RefereeRequests.CountAsync(r => r.Status == RefereeRequestStatus.Confirmed);
        ViewBag.TotalCancelled = await _db.RefereeRequests.CountAsync(r => r.Status == RefereeRequestStatus.Cancelled);

        var requests = await _refereeService.GetAdminInboxRequestsAsync(status, search);
        return View(requests);
    }

    [HttpGet]
    public async Task<IActionResult> RefereeRequestDetails(Guid id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        var req = await _refereeService.GetRequestDetailsAsync(id, user.Id, true);
        if (req == null) return NotFound();

        // If status is Pending, automatically transition to UnderReview
        if (req.Status == RefereeRequestStatus.Pending)
        {
            await _refereeService.SetRequestUnderReviewAsync(id, user.Id);
            req.Status = RefereeRequestStatus.UnderReview;
        }

        ViewBag.ActiveReferees = await _refereeService.GetActiveRefereesAsync();
        return View(req);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitProposal(SubmitRefereeProposalViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Dữ liệu đề xuất không hợp lệ. Vui lòng kiểm tra lại.";
            return RedirectToAction(nameof(RefereeRequestDetails), new { id = model.RequestId });
        }

        try
        {
            await _refereeService.CreateProposalAsync(
                model.RequestId,
                user.Id,
                model.TotalPriceVND,
                model.ServiceNote,
                model.SelectedRefereeIds);

            TempData["Success"] = "Đã gửi đề xuất báo giá thành công tới đội bóng!";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(RefereeRequestDetails), new { id = model.RequestId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkUnableToFulfill(Guid requestId, string? reason)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        try
        {
            await _refereeService.MarkUnableToFulfillAsync(requestId, user.Id, reason);
            TempData["Success"] = "Đã cập nhật trạng thái: Không thể đáp ứng.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(RefereeRequestDetails), new { id = requestId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteRefereeRequest(Guid requestId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        try
        {
            await _refereeService.CompleteRequestAsync(requestId, user.Id);
            TempData["Success"] = "Đã hoàn thành dịch vụ trọng tài cho trận đấu!";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(RefereeRequestDetails), new { id = requestId });
    }

    // ==========================================
    // 2. DANH BẠ TRỌNG TÀI (SUPER ADMIN QUẢN TRỊ)
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> Referees()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        var referees = await _refereeService.GetAllRefereesAsync();
        return View(referees);
    }

    [HttpGet]
    public async Task<IActionResult> CreateReferee()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        return View("RefereeForm", new RefereeFormViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateReferee(RefereeFormViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        if (!ModelState.IsValid)
        {
            return View("RefereeForm", model);
        }

        try
        {
            var referee = new Referee
            {
                Name = model.Name.Trim(),
                ContactInfo = model.ContactInfo.Trim(),
                ExperienceSummary = model.ExperienceSummary.Trim(),
                SupportedFormats = model.SupportedFormats?.Trim(),
                ServiceNotes = model.ServiceNotes?.Trim(),
                DefaultPriceVND = model.DefaultPriceVND,
                IsActive = model.IsActive
            };

            await _refereeService.CreateRefereeAsync(referee, user.Id);
            TempData["Success"] = $"Đã thêm trọng tài {referee.Name} vào danh bạ thành công.";
            return RedirectToAction(nameof(Referees));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("RefereeForm", model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> EditReferee(Guid id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        var referee = await _refereeService.GetRefereeByIdAsync(id);
        if (referee == null) return NotFound();

        var model = new RefereeFormViewModel
        {
            Id = referee.Id,
            Name = referee.Name,
            ContactInfo = referee.ContactInfo,
            ExperienceSummary = referee.ExperienceSummary,
            SupportedFormats = referee.SupportedFormats,
            ServiceNotes = referee.ServiceNotes,
            DefaultPriceVND = referee.DefaultPriceVND,
            IsActive = referee.IsActive
        };

        return View("RefereeForm", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditReferee(RefereeFormViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        if (!model.Id.HasValue) return BadRequest();

        if (!ModelState.IsValid)
        {
            return View("RefereeForm", model);
        }

        try
        {
            var referee = new Referee
            {
                Id = model.Id.Value,
                Name = model.Name.Trim(),
                ContactInfo = model.ContactInfo.Trim(),
                ExperienceSummary = model.ExperienceSummary.Trim(),
                SupportedFormats = model.SupportedFormats?.Trim(),
                ServiceNotes = model.ServiceNotes?.Trim(),
                DefaultPriceVND = model.DefaultPriceVND,
                IsActive = model.IsActive
            };

            await _refereeService.UpdateRefereeAsync(referee, user.Id);
            TempData["Success"] = $"Đã cập nhật thông tin trọng tài {referee.Name}.";
            return RedirectToAction(nameof(Referees));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("RefereeForm", model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleRefereeStatus(Guid id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || !await _auth.IsPlatformAdminAsync(user.Id))
            return Forbid();

        var result = await _refereeService.ToggleRefereeStatusAsync(id, user.Id);
        if (result)
        {
            TempData["Success"] = "Đã cập nhật trạng thái hoạt động của trọng tài.";
        }
        else
        {
            TempData["Error"] = "Không tìm thấy trọng tài.";
        }

        return RedirectToAction(nameof(Referees));
    }
}
