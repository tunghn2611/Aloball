namespace AloBall.Web.Services.TeamDivision;

public static class PositionCompatibilityMatrix
{
    /// <summary>
    /// Chuẩn hóa chuỗi vị trí tiếng Việt tự do sang mã vị trí chuẩn.
    /// Bảo tồn ngữ nghĩa, không tự động suy diễn các tên chung chung như "Hậu vệ" thành mọi cánh.
    /// </summary>
    public static string NormalizePositionString(string? rawPosition)
    {
        if (string.IsNullOrWhiteSpace(rawPosition))
            return PositionCodes.UNASSIGNED;

        var lower = rawPosition.Trim().ToLowerInvariant();

        // 1. Thủ môn
        if (lower.Contains("thủ môn") || lower == "gk" || lower.Contains("goalkeeper"))
            return PositionCodes.GK;

        // 2. Tiền đạo cánh & cắm
        if (lower.Contains("tiền đạo cánh trái") || lower == "lw" || lower.Contains("left wing"))
            return PositionCodes.LW;
        if (lower.Contains("tiền đạo cánh phải") || lower == "rw" || lower.Contains("right wing"))
            return PositionCodes.RW;
        if (lower.Contains("tiền đạo cắm") || lower == "st" || lower.Contains("striker") || lower.Contains("trung phong"))
            return PositionCodes.ST;
        if (lower.Contains("tiền đạo") || lower == "fw" || lower.Contains("forward"))
            return PositionCodes.FW_GENERAL;

        // 3. Tiền vệ
        if (lower.Contains("tiền vệ phòng ngự") || lower == "cdm" || lower.Contains("thòng"))
            return PositionCodes.CDM;
        if (lower.Contains("tiền vệ tấn công") || lower == "cam")
            return PositionCodes.CAM;
        if (lower.Contains("tiền vệ cánh trái") || lower.Contains("tiền vệ trái") || lower == "lm")
            return PositionCodes.LM;
        if (lower.Contains("tiền vệ cánh phải") || lower.Contains("tiền vệ phải") || lower == "rm")
            return PositionCodes.RM;
        if (lower.Contains("tiền vệ trung tâm") || lower.Contains("tiền vệ giữa") || lower == "cm")
            return PositionCodes.CM;
        if (lower.Contains("tiền vệ") || lower == "mf" || lower.Contains("midfield"))
            return PositionCodes.MF_GENERAL;

        // 4. Hậu vệ
        if (lower.Contains("hậu vệ cánh trái") || lower.Contains("hậu vệ trái") || lower == "lb")
            return PositionCodes.LB;
        if (lower.Contains("hậu vệ cánh phải") || lower.Contains("hậu vệ phải") || lower == "rb")
            return PositionCodes.RB;
        if (lower.Contains("trung vệ") || lower == "cb" || lower.Contains("center back"))
            return PositionCodes.CB;
        if (lower.Contains("hậu vệ") || lower == "df" || lower.Contains("defender"))
            return PositionCodes.DF_GENERAL;

        return PositionCodes.UNASSIGNED;
    }

    /// <summary>
    /// Kiểm tra xem một cầu thủ có mã vị trí cho trước có tương thích hợp lệ với ô vị trí slotCode trong sơ đồ không.
    /// </summary>
    public static bool IsSlotCompatible(string? playerPositionCode, string slotCode)
    {
        if (string.IsNullOrWhiteSpace(playerPositionCode) || playerPositionCode == PositionCodes.UNASSIGNED)
            return false;

        // Thủ môn chỉ đá ô GK
        if (playerPositionCode == PositionCodes.GK)
            return slotCode == "GK";

        // Ô GK chỉ nhận người có vị trí GK
        if (slotCode == "GK")
            return playerPositionCode == PositionCodes.GK;

        return playerPositionCode switch
        {
            // Trung vệ
            PositionCodes.CB => slotCode is "CB_L" or "CB_R" or "CB_C" or "DF" or "DF_C",

            // Hậu vệ cánh trái
            PositionCodes.LB or PositionCodes.DF_L => slotCode is "DF_L" or "CB_L" or "DF",

            // Hậu vệ cánh phải
            PositionCodes.RB or PositionCodes.DF_R => slotCode is "DF_R" or "CB_R" or "DF",

            // Hậu vệ chung (DF_GENERAL): cho phép đá các ô trung vệ hoặc hậu vệ thòng
            PositionCodes.DF_GENERAL => slotCode is "CB_L" or "CB_R" or "CB_C" or "DF" or "DF_C",

            // Tiền vệ trung tâm / phòng ngự / tấn công
            PositionCodes.CM or PositionCodes.CDM or PositionCodes.CAM => slotCode is "MF_C" or "MF" or "MF_L" or "MF_R",

            // Tiền vệ cánh trái
            PositionCodes.LM or PositionCodes.MF_L => slotCode is "MF_L" or "MF" or "LW",

            // Tiền vệ cánh phải
            PositionCodes.RM or PositionCodes.MF_R => slotCode is "MF_R" or "MF" or "RW",

            // Tiền vệ chung: có thể đá bất kỳ ô tiền vệ trong sơ đồ
            PositionCodes.MF_GENERAL => slotCode is "MF_C" or "MF_L" or "MF_R" or "MF",

            // Tiền đạo cắm
            PositionCodes.ST or PositionCodes.CF => slotCode is "FW" or "ST",

            // Tiền đạo cánh
            PositionCodes.LW => slotCode is "FW" or "MF_L" or "LW",
            PositionCodes.RW => slotCode is "FW" or "MF_R" or "RW",

            // Tiền đạo chung
            PositionCodes.FW_GENERAL => slotCode is "FW" or "ST",

            _ => false
        };
    }

    /// <summary>
    /// Kiểm tra xem cầu thủ được xếp vào slotCode có được tính là đá ĐÚNG SỞ TRƯỜNG CHÍNH hay không.
    /// </summary>
    public static bool IsPrimaryPositionMatch(string? primaryPositionCode, string slotCode)
    {
        if (string.IsNullOrWhiteSpace(primaryPositionCode) || primaryPositionCode == PositionCodes.UNASSIGNED)
            return false;

        return primaryPositionCode switch
        {
            PositionCodes.GK => slotCode == "GK",

            PositionCodes.CB => slotCode is "CB_L" or "CB_R" or "CB_C" or "DF_C" or "DF",
            PositionCodes.LB or PositionCodes.DF_L => slotCode is "DF_L" or "CB_L",
            PositionCodes.RB or PositionCodes.DF_R => slotCode is "DF_R" or "CB_R",
            PositionCodes.DF_GENERAL => slotCode is "CB_L" or "CB_R" or "CB_C" or "DF_C" or "DF",

            PositionCodes.CM or PositionCodes.CDM or PositionCodes.CAM => slotCode is "MF_C" or "MF",
            PositionCodes.LM or PositionCodes.MF_L => slotCode is "MF_L",
            PositionCodes.RM or PositionCodes.MF_R => slotCode is "MF_R",
            PositionCodes.MF_GENERAL => slotCode is "MF_C" or "MF_L" or "MF_R" or "MF",

            PositionCodes.ST or PositionCodes.CF or PositionCodes.FW_GENERAL => slotCode is "FW" or "ST",
            PositionCodes.LW => slotCode is "LW" or "FW",
            PositionCodes.RW => slotCode is "RW" or "FW",

            _ => false
        };
    }

    /// <summary>
    /// Kiểm tra một cầu thủ có thể đảm nhiệm slotCode dựa trên: Vị trí chính, Danh sách vị trí phụ, hoặc Ngoại lệ trận được cấp.
    /// </summary>
    public static bool CanPlaySlot(
        string? primaryPositionCode,
        IEnumerable<string>? secondaryPositionCodes,
        string slotCode,
        IEnumerable<string>? grantedExceptionSlotCodes)
    {
        // 1. Kiểm tra ngoại lệ vị trí trận đã được cấp phép
        if (grantedExceptionSlotCodes != null && grantedExceptionSlotCodes.Contains(slotCode))
            return true;

        // 2. Kiểm tra sở trường chính
        if (IsSlotCompatible(primaryPositionCode, slotCode))
            return true;

        // 3. Kiểm tra danh sách vị trí phụ
        if (secondaryPositionCodes != null)
        {
            foreach (var sec in secondaryPositionCodes)
            {
                if (IsSlotCompatible(sec, slotCode))
                    return true;
            }
        }

        return false;
    }
}
