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
public class FinanceController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IFinanceService _finance;
    private readonly ITeamAuthorizationService _auth;
    private readonly UserManager<ApplicationUser> _userManager;

    public FinanceController(
        ApplicationDbContext db,
        IFinanceService finance,
        ITeamAuthorizationService auth,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _finance = finance;
        _auth = auth;
        _userManager = userManager;
    }

    // =========================================================================
    // 1. DỰ BÁO CHI PHÍ TRẬN ĐẤU (MATCH COST FORECAST)
    // =========================================================================

    [HttpGet]
    public async Task<IActionResult> MatchFinance(Guid matchId, Guid? teamId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var match = await _db.Matches.FindAsync(matchId);
        if (match == null) return NotFound("Trận đấu không tồn tại.");

        Guid selectedTeamId = teamId ?? match.TeamAId;
        if (match.TeamAId != selectedTeamId && match.TeamBId != selectedTeamId)
            return BadRequest("Đội bóng không tham gia trận đấu này.");

        if (!await _auth.HasMemberAccessAsync(selectedTeamId, user.Id))
            return Forbid();

        var model = await _finance.GetMatchCostForecastAsync(matchId, selectedTeamId, user.Id);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddEstimatedExpense(AddEstimatedExpenseInput input)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Dữ liệu nhập không hợp lệ. Vui lòng kiểm tra lại.";
            return RedirectToAction(nameof(MatchFinance), new { matchId = input.MatchId, teamId = input.TeamId });
        }

        var result = await _finance.AddEstimatedExpenseAsync(input.MatchId, input.TeamId, input.Description, input.Amount, user.Id);
        if (result.Success) TempData["Success"] = result.Message;
        else TempData["Error"] = result.Message;

        return RedirectToAction(nameof(MatchFinance), new { matchId = input.MatchId, teamId = input.TeamId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteEstimatedExpense(Guid estimateId, Guid matchId, Guid teamId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var result = await _finance.DeleteEstimatedExpenseAsync(estimateId, teamId, user.Id);
        if (result.Success) TempData["Success"] = result.Message;
        else TempData["Error"] = result.Message;

        return RedirectToAction(nameof(MatchFinance), new { matchId = matchId, teamId = teamId });
    }

    // =========================================================================
    // 2. SMART SETTLEMENT WIZARD & DETAIL
    // =========================================================================

    [HttpGet]
    public async Task<IActionResult> CreateSettlement(Guid matchId, Guid? teamId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var match = await _db.Matches.FindAsync(matchId);
        if (match == null) return NotFound("Trận đấu không tồn tại.");

        Guid selectedTeamId = teamId ?? match.TeamAId;
        if (!await _auth.HasManagerAccessAsync(selectedTeamId, user.Id))
            return Forbid();

        var prep = await _finance.GetSettlementPreparationDataAsync(matchId, selectedTeamId, user.Id);
        if (!prep.Success)
        {
            TempData["Error"] = prep.Message;
            return RedirectToAction(nameof(SettlementDetail), new { matchId = matchId, teamId = selectedTeamId });
        }

        return View(prep.Model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSettlement(CreateSettlementInput input)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var result = await _finance.CreateSettlementAsync(input, user.Id);
        if (result.Success)
        {
            TempData["Success"] = result.Message;
            return RedirectToAction(nameof(SettlementDetail), new { matchId = input.MatchId, teamId = input.TeamId });
        }

        TempData["Error"] = result.Message;
        return RedirectToAction(nameof(CreateSettlement), new { matchId = input.MatchId, teamId = input.TeamId });
    }

    [HttpGet]
    public async Task<IActionResult> SettlementDetail(Guid matchId, Guid? teamId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var match = await _db.Matches.FindAsync(matchId);
        if (match == null) return NotFound("Trận đấu không tồn tại.");

        Guid selectedTeamId = teamId ?? match.TeamAId;
        if (!await _auth.HasMemberAccessAsync(selectedTeamId, user.Id))
            return Forbid();

        var model = await _finance.GetSettlementDetailAsync(matchId, selectedTeamId, user.Id);
        if (model == null)
        {
            TempData["Error"] = "Trận đấu này chưa có bản quyết toán nào được xác nhận.";
            return RedirectToAction(nameof(MatchFinance), new { matchId = matchId, teamId = selectedTeamId });
        }

        return View(model);
    }

    // =========================================================================
    // 3. PAYMENT TRACKING, REVERSALS & REMINDERS
    // =========================================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordPayment(RecordPaymentInput input, Guid matchId, Guid teamId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Số tiền hoặc dữ liệu thanh toán không hợp lệ.";
            return RedirectToAction(nameof(SettlementDetail), new { matchId = matchId, teamId = teamId });
        }

        var result = await _finance.RecordPaymentAsync(input, user.Id);
        if (result.Success) TempData["Success"] = result.Message;
        else TempData["Error"] = result.Message;

        return RedirectToAction(nameof(SettlementDetail), new { matchId = matchId, teamId = teamId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReversePayment(ReversePaymentInput input, Guid matchId, Guid teamId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var result = await _finance.ReversePaymentAsync(input.PaymentId, teamId, input.ReversalReason, user.Id);
        if (result.Success) TempData["Success"] = result.Message;
        else TempData["Error"] = result.Message;

        return RedirectToAction(nameof(SettlementDetail), new { matchId = matchId, teamId = teamId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdjustAllocation(ManualAdjustmentInput input, Guid matchId, Guid teamId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var result = await _finance.AdjustAllocationAsync(input, user.Id);
        if (result.Success) TempData["Success"] = result.Message;
        else TempData["Error"] = result.Message;

        return RedirectToAction(nameof(SettlementDetail), new { matchId = matchId, teamId = teamId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendPaymentReminder(Guid allocationId, Guid matchId, Guid teamId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var result = await _finance.SendPaymentReminderAsync(allocationId, teamId, user.Id);
        if (result.Success) TempData["Success"] = result.Message;
        else TempData["Error"] = result.Message;

        return RedirectToAction(nameof(SettlementDetail), new { matchId = matchId, teamId = teamId });
    }

    // =========================================================================
    // 4. TEAM FINANCE REPORT & CSV EXPORT
    // =========================================================================

    [HttpGet]
    public async Task<IActionResult> TeamReport(Guid teamId, string? month)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasMemberAccessAsync(teamId, user.Id))
            return Forbid();

        var report = await _finance.GetTeamFinanceReportAsync(teamId, month, user.Id);
        return View(report);
    }

    [HttpGet]
    public async Task<IActionResult> ExportCsv(Guid teamId, string? month)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (!await _auth.HasManagerAccessAsync(teamId, user.Id))
            return Forbid();

        var csvBytes = await _finance.ExportTeamFinanceReportCsvAsync(teamId, month, user.Id);
        string filename = $"AloBall_QuyTran_{teamId:N}_{(string.IsNullOrEmpty(month) ? "All" : month)}.csv";

        return File(csvBytes, "text/csv; charset=utf-8", filename);
    }
}
