using System.ComponentModel.DataAnnotations;
using AloBall.Web.Data.Entities;

namespace AloBall.Web.Models;

public class MatchCostForecastViewModel
{
    public Guid MatchId { get; set; }
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public DateTime MatchDateTime { get; set; }
    public string OpponentName { get; set; } = string.Empty;
    public string PitchName { get; set; } = string.Empty;
    public string PitchType { get; set; } = "7 người";

    // Confirmed booking cost
    public decimal ConfirmedPitchCost { get; set; } = 0m;
    public bool HasConfirmedPitch { get; set; } = false;
    public string PitchCostNote { get; set; } = string.Empty;

    // Confirmed referee cost
    public decimal ConfirmedRefereeCost { get; set; } = 0m;
    public bool HasConfirmedReferee { get; set; } = false;
    public string RefereeCostNote { get; set; } = string.Empty;

    // Custom estimated expenses
    public List<MatchExpenseEstimateItem> CustomEstimates { get; set; } = new();
    public decimal TotalCustomEstimates => CustomEstimates.Sum(e => e.Amount);

    // Totals and averages
    public decimal TotalEstimatedCost => ConfirmedPitchCost + ConfirmedRefereeCost + TotalCustomEstimates;
    public int ExpectedParticipants { get; set; } = 0; // Positive RSVP count
    public decimal EstimatedCostPerPerson => ExpectedParticipants > 0 ? Math.Round(TotalEstimatedCost / ExpectedParticipants, 0) : TotalEstimatedCost;

    // Status
    public bool IsSettled { get; set; } = false;
    public Guid? SettlementId { get; set; }
    public bool CanManage { get; set; } = false;
}

public class MatchExpenseEstimateItem
{
    public Guid Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AddEstimatedExpenseInput
{
    public Guid MatchId { get; set; }
    public Guid TeamId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên khoản chi dự kiến."), StringLength(150)]
    public string Description { get; set; } = string.Empty;

    [Range(1000, 100000000, ErrorMessage = "Số tiền phải từ 1.000 VNĐ đến 100.000.000 VNĐ.")]
    public decimal Amount { get; set; }
}

public class SettlementExpenseInputItem
{
    public SettlementExpenseCategory Category { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsFromConfirmedService { get; set; }
    public string? SourceReferenceId { get; set; }
}

public class ParticipantCandidateItem
{
    public Guid PlayerId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public int? KitNumber { get; set; }
    public string? Position { get; set; }
    public string? UserId { get; set; }
    public bool HasAuthoritativeAttendance { get; set; } // Present in attendance
    public bool HasLineupAppearance { get; set; }        // Played in lineup
    public string AttendanceNote { get; set; } = string.Empty;
    public bool IsSelected { get; set; } = true;
}

public class MemberAllocationPreviewItem
{
    public Guid PlayerId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public int? KitNumber { get; set; }
    public string? Position { get; set; }
    public string? UserId { get; set; }
    public decimal CalculatedAmount { get; set; }
    public decimal AdjustedAmount { get; set; }
    public string? AdjustmentReason { get; set; }
}

public class SettlementWizardViewModel
{
    public Guid MatchId { get; set; }
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string OpponentName { get; set; } = string.Empty;
    public DateTime MatchDateTime { get; set; }
    public string PitchName { get; set; } = string.Empty;

    public int CurrentStep { get; set; } = 1;

    // Step 1: Expenses
    public decimal ConfirmedPitchCost { get; set; }
    public bool HasConfirmedPitch { get; set; }
    public decimal ConfirmedRefereeCost { get; set; }
    public bool HasConfirmedReferee { get; set; }
    public List<SettlementExpenseInputItem> ExpenseItems { get; set; } = new();
    public decimal TotalActualExpenses => ExpenseItems.Sum(e => e.Amount);

    // Step 2: Participants
    public List<ParticipantCandidateItem> Candidates { get; set; } = new();
    public int SelectedCount => Candidates.Count(c => c.IsSelected);

    // Step 3: Allocation preview
    public List<MemberAllocationPreviewItem> Allocations { get; set; } = new();
    public decimal TotalAllocated => Allocations.Sum(a => a.AdjustedAmount);
    public decimal RoundingDifference => TotalActualExpenses - TotalAllocated;

    // Step 4: Notes
    public string? SettlementNotes { get; set; }
}

public class CreateSettlementInput
{
    public Guid MatchId { get; set; }
    public Guid TeamId { get; set; }
    public List<SettlementExpenseInputItem> Expenses { get; set; } = new();
    public List<Guid> SelectedPlayerIds { get; set; } = new();
    public List<MemberAllocationPreviewItem> Allocations { get; set; } = new();
    public string? Notes { get; set; }
}

public class SettlementDetailViewModel
{
    public Guid SettlementId { get; set; }
    public Guid MatchId { get; set; }
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string TeamSlug { get; set; } = string.Empty;
    public string OpponentName { get; set; } = string.Empty;
    public DateTime MatchDateTime { get; set; }
    public string PitchName { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }
    public decimal TotalPitchCost { get; set; }
    public decimal TotalRefereeCost { get; set; }
    public decimal TotalOtherExpenses { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalRemaining => Math.Max(0m, TotalAmount - TotalPaid);
    public double CollectionRate => TotalAmount > 0 ? Math.Round((double)TotalPaid / (double)TotalAmount * 100, 1) : 100;

    public int ParticipantCount { get; set; }
    public decimal PerPersonShare { get; set; }

    public SettlementStatus Status { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string ConfirmedByName { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public List<SettlementItemDto> Items { get; set; } = new();
    public List<MemberAllocationDto> Allocations { get; set; } = new();

    public bool IsManager { get; set; }
    public string? CurrentUserId { get; set; }
}

public class SettlementItemDto
{
    public Guid Id { get; set; }
    public SettlementExpenseCategory Category { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsFromConfirmedService { get; set; }
}

public class MemberAllocationDto
{
    public Guid AllocationId { get; set; }
    public Guid PlayerId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public int? KitNumber { get; set; }
    public string? Position { get; set; }
    public string? UserId { get; set; }
    public decimal CalculatedAmount { get; set; }
    public decimal AdjustedAmount { get; set; }
    public string? AdjustmentReason { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal RemainingBalance => Math.Max(0m, AdjustedAmount - AmountPaid);
    public PaymentStatus PaymentStatus { get; set; }
    public DateTime? LastPaymentDate { get; set; }
    public bool IsSelf { get; set; }

    public List<PaymentRecordDto> Payments { get; set; } = new();
    public List<AdjustmentAuditDto> AdjustmentAudits { get; set; } = new();
}

public class PaymentRecordDto
{
    public Guid PaymentId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string PaymentMethodName { get; set; } = string.Empty;
    public string? ReferenceCode { get; set; }
    public string? Note { get; set; }
    public string RecordedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsReversed { get; set; }
    public string? ReversalReason { get; set; }
    public DateTime? ReversedAt { get; set; }
}

public class AdjustmentAuditDto
{
    public Guid Id { get; set; }
    public decimal PreviousAmount { get; set; }
    public decimal NewAmount { get; set; }
    public string MandatoryReason { get; set; } = string.Empty;
    public string AdjustedByName { get; set; } = string.Empty;
    public DateTime AdjustedAt { get; set; }
}

public class RecordPaymentInput
{
    public Guid AllocationId { get; set; }
    public Guid SettlementId { get; set; }

    [Range(1000, 100000000, ErrorMessage = "Số tiền thanh toán phải từ 1.000 VNĐ.")]
    public decimal Amount { get; set; }

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.BankTransfer;

    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

    [StringLength(100)]
    public string? ReferenceCode { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}

public class ReversePaymentInput
{
    public Guid PaymentId { get; set; }
    public Guid SettlementId { get; set; }

    [Required(ErrorMessage = "Bắt buộc phải ghi rõ lý do hủy/đảo khoản thu."), StringLength(500, MinimumLength = 5)]
    public string ReversalReason { get; set; } = string.Empty;
}

public class ManualAdjustmentInput
{
    public Guid AllocationId { get; set; }
    public Guid SettlementId { get; set; }

    [Range(0, 100000000, ErrorMessage = "Số tiền phải từ 0 VNĐ.")]
    public decimal NewAmount { get; set; }

    [Required(ErrorMessage = "Bắt buộc phải nêu rõ lý do điều chỉnh số tiền."), StringLength(500, MinimumLength = 5)]
    public string MandatoryReason { get; set; } = string.Empty;
}

public class TeamFinanceReportViewModel
{
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string? MonthFilter { get; set; }
    public List<string> AvailableMonths { get; set; } = new();

    public decimal TotalMatchExpenses { get; set; }
    public decimal TotalMemberContributionsDue { get; set; }
    public decimal TotalCollected { get; set; }
    public decimal TotalOutstanding => Math.Max(0m, TotalMemberContributionsDue - TotalCollected);
    public double CollectionRatePercentage => TotalMemberContributionsDue > 0 ? Math.Round((double)TotalCollected / (double)TotalMemberContributionsDue * 100, 1) : 100;

    public int TotalSettledMatches { get; set; }

    public List<MonthlyFinanceSummaryDto> MonthlySummaries { get; set; } = new();
    public List<MatchSettlementSummaryDto> MatchHistory { get; set; } = new();
    public List<MemberFinanceRecordDto> MemberRecords { get; set; } = new();

    public bool IsManager { get; set; }
}

public class MonthlyFinanceSummaryDto
{
    public string MonthKey { get; set; } = string.Empty; // YYYY-MM
    public string MonthDisplay { get; set; } = string.Empty;
    public int MatchesCount { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal TotalCollected { get; set; }
    public decimal TotalOutstanding { get; set; }
    public double CollectionRate => (TotalCollected + TotalOutstanding) > 0 ? Math.Round((double)TotalCollected / (double)(TotalCollected + TotalOutstanding) * 100, 1) : 100;
}

public class MatchSettlementSummaryDto
{
    public Guid SettlementId { get; set; }
    public Guid MatchId { get; set; }
    public DateTime MatchDateTime { get; set; }
    public string OpponentName { get; set; } = string.Empty;
    public string PitchName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal RemainingBalance => Math.Max(0m, TotalAmount - TotalPaid);
    public int ParticipantCount { get; set; }
    public decimal PerPersonShare { get; set; }
    public SettlementStatus Status { get; set; }
    public double CollectionRate => TotalAmount > 0 ? Math.Round((double)TotalPaid / (double)TotalAmount * 100, 1) : 100;
}

public class MemberFinanceRecordDto
{
    public Guid PlayerId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public int? KitNumber { get; set; }
    public string? Position { get; set; }
    public int MatchesParticipated { get; set; }
    public decimal TotalAmountDue { get; set; }
    public decimal TotalAmountPaid { get; set; }
    public decimal OutstandingBalance => Math.Max(0m, TotalAmountDue - TotalAmountPaid);
    public int UnpaidCount { get; set; }
    public int PartialCount { get; set; }
    public int FullyPaidCount { get; set; }
}
