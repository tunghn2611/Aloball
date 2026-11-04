using System.ComponentModel.DataAnnotations;
using AloBall.Web.Data.Entities;

namespace AloBall.Web.Models;

public class CreateRefereeRequestViewModel
{
    [Required(ErrorMessage = "Vui lòng chọn đội bóng.")]
    public Guid TeamId { get; set; }

    public Guid? MatchId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn ngày thi đấu.")]
    [DataType(DataType.Date)]
    public DateTime ServiceDate { get; set; } = DateTime.Today.AddDays(1);

    [Required(ErrorMessage = "Vui lòng chọn giờ bắt đầu.")]
    public TimeSpan StartTime { get; set; } = new TimeSpan(19, 0, 0);

    [Required(ErrorMessage = "Vui lòng chọn giờ kết thúc.")]
    public TimeSpan EndTime { get; set; } = new TimeSpan(20, 30, 0);

    [Required(ErrorMessage = "Vui lòng nhập tên sân thi đấu.")]
    [MaxLength(200, ErrorMessage = "Tên sân không vượt quá 200 ký tự.")]
    public string VenueName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập địa chỉ cụ thể của sân.")]
    [MaxLength(500, ErrorMessage = "Địa chỉ không vượt quá 500 ký tự.")]
    public string VenueAddress { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng chọn thể thức thi đấu.")]
    [MaxLength(100)]
    public string MatchFormat { get; set; } = "7 người";

    [Required(ErrorMessage = "Vui lòng nhập số lượng trọng tài cần.")]
    [Range(1, 10, ErrorMessage = "Số lượng trọng tài phải từ 1 đến 10 người.")]
    public int RefereeCount { get; set; } = 1;

    [MaxLength(500, ErrorMessage = "Yêu cầu kinh nghiệm không vượt quá 500 ký tự.")]
    public string? PreferredExperience { get; set; }

    [Range(0, 100000000, ErrorMessage = "Ngân sách dự kiến không hợp lệ.")]
    public decimal? BudgetVND { get; set; }

    [MaxLength(1000, ErrorMessage = "Ghi chú không vượt quá 1000 ký tự.")]
    public string? Notes { get; set; }
}

public class SubmitRefereeProposalViewModel
{
    [Required]
    public Guid RequestId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tổng giá báo (VNĐ).")]
    [Range(0, 100000000, ErrorMessage = "Tổng giá dịch vụ không được âm.")]
    public decimal TotalPriceVND { get; set; }

    public string? ServiceNote { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn danh sách trọng tài.")]
    public List<Guid> SelectedRefereeIds { get; set; } = new();
}

public class RefereeFormViewModel
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "Họ tên trọng tài là bắt buộc.")]
    [MaxLength(150, ErrorMessage = "Họ tên không vượt quá 150 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số điện thoại / email liên hệ là bắt buộc.")]
    [MaxLength(250, ErrorMessage = "Thông tin liên hệ không vượt quá 250 ký tự.")]
    public string ContactInfo { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tóm tắt kinh nghiệm / chuyên môn là bắt buộc.")]
    [MaxLength(500, ErrorMessage = "Tóm tắt kinh nghiệm không vượt quá 500 ký tự.")]
    public string ExperienceSummary { get; set; } = string.Empty;

    public string? SupportedFormats { get; set; } = "7 người";

    public string? ServiceNotes { get; set; }

    [Range(0, 50000000, ErrorMessage = "Mức giá mặc định không hợp lệ.")]
    public decimal? DefaultPriceVND { get; set; }

    public bool IsActive { get; set; } = true;
}
