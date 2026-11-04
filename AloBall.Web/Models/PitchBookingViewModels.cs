using System.ComponentModel.DataAnnotations;

namespace AloBall.Web.Models;

public class CreatePitchBookingRequestViewModel
{
    [Required(ErrorMessage = "Vui lòng chọn đội bóng.")]
    public Guid TeamId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn sân bóng.")]
    public Guid PitchVenueId { get; set; }

    public Guid? MatchId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn ngày thi đấu.")]
    [DataType(DataType.Date)]
    public DateTime ServiceDate { get; set; } = DateTime.Today.AddDays(1);

    [Required(ErrorMessage = "Vui lòng chọn giờ bắt đầu.")]
    public TimeSpan StartTime { get; set; } = new TimeSpan(19, 0, 0);

    [Required(ErrorMessage = "Vui lòng chọn giờ kết thúc.")]
    public TimeSpan EndTime { get; set; } = new TimeSpan(20, 30, 0);

    [Required(ErrorMessage = "Vui lòng chọn thể thức sân.")]
    [MaxLength(100)]
    public string PitchFormat { get; set; } = "7 người";

    [Required(ErrorMessage = "Vui lòng nhập số lượng sân.")]
    [Range(1, 20, ErrorMessage = "Số lượng sân phải từ 1 đến 20.")]
    public int PitchCount { get; set; } = 1;

    [Range(0, 100000000, ErrorMessage = "Ngân sách không hợp lệ.")]
    public decimal? BudgetVND { get; set; }

    [MaxLength(1000, ErrorMessage = "Ghi chú không vượt quá 1000 ký tự.")]
    public string? Notes { get; set; }
}

public class SubmitPitchBookingOfferViewModel
{
    [Required]
    public Guid RequestId { get; set; }

    [Required(ErrorMessage = "Tên sân xác nhận là bắt buộc.")]
    [MaxLength(200)]
    public string ConfirmedVenueName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Địa chỉ sân xác nhận là bắt buộc.")]
    [MaxLength(500)]
    public string ConfirmedAddress { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ngày đặt sân là bắt buộc.")]
    [DataType(DataType.Date)]
    public DateTime ConfirmedDate { get; set; }

    [Required(ErrorMessage = "Giờ bắt đầu là bắt buộc.")]
    public TimeSpan ConfirmedStartTime { get; set; }

    [Required(ErrorMessage = "Giờ kết thúc là bắt buộc.")]
    public TimeSpan ConfirmedEndTime { get; set; }

    [Required(ErrorMessage = "Thể thức sân là bắt buộc.")]
    public string ConfirmedPitchFormat { get; set; } = "7 người";

    [Range(1, 20, ErrorMessage = "Số lượng sân từ 1 đến 20.")]
    public int ConfirmedPitchCount { get; set; } = 1;

    [MaxLength(50)]
    public string? SpecificFieldCode { get; set; }

    [Required(ErrorMessage = "Tổng giá báo là bắt buộc.")]
    [Range(0, 100000000, ErrorMessage = "Tổng giá dịch vụ không được âm.")]
    public decimal TotalPriceVND { get; set; }

    [MaxLength(1000)]
    public string? DepositInstructions { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }
}

public class PitchVenueFormViewModel
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "Tên cụm sân là bắt buộc.")]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Tỉnh/Thành phố là bắt buộc.")]
    [MaxLength(100)]
    public string City { get; set; } = "Hà Nội";

    [Required(ErrorMessage = "Quận/Huyện là bắt buộc.")]
    [MaxLength(100)]
    public string District { get; set; } = "Cầu Giấy";

    [Required(ErrorMessage = "Địa chỉ chi tiết là bắt buộc.")]
    [MaxLength(500)]
    public string FullAddress { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số điện thoại hotline sân là bắt buộc.")]
    [MaxLength(50)]
    public string ContactPhone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Thể thức hỗ trợ là bắt buộc (ví dụ: 5 người, 7 người).")]
    [MaxLength(100)]
    public string SupportedFormats { get; set; } = "7 người";

    [Required(ErrorMessage = "Loại mặt sân là bắt buộc.")]
    [MaxLength(100)]
    public string SurfaceType { get; set; } = "Cỏ nhân tạo";

    [MaxLength(500)]
    public string? Facilities { get; set; }

    [MaxLength(100)]
    public string? OpeningHours { get; set; } = "06:00 - 23:00";

    [Range(0, 50000000, ErrorMessage = "Mức giá tham khảo không hợp lệ.")]
    public decimal? IndicativePricePerHour { get; set; }

    [MaxLength(500)]
    public string? PriceNotes { get; set; }

    public string? ImageUrl { get; set; }

    public string? MapLink { get; set; }

    [Range(1, 50, ErrorMessage = "Tổng số sân con phải từ 1 đến 50.")]
    public int TotalPitches { get; set; } = 4;

    public bool IsActive { get; set; } = true;
}
