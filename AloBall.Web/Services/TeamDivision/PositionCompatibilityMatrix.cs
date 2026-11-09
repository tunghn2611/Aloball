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
        if (lower == "lw" || lower.Contains("tiền đạo cánh trái") || lower.Contains("cánh trái") && (lower.Contains("tiền đạo") || lower.Contains("công")) || lower.Contains("left wing"))
            return PositionCodes.LW;
        if (lower == "rw" || lower.Contains("tiền đạo cánh phải") || lower.Contains("cánh phải") && (lower.Contains("tiền đạo") || lower.Contains("công")) || lower.Contains("right wing"))
            return PositionCodes.RW;
        if (lower == "cf" || lower.Contains("tiền đạo lùi") || lower.Contains("hộ công cắm"))
            return PositionCodes.CF;
        if (lower == "st" || lower.Contains("tiền đạo cắm") || lower.Contains("striker") || lower.Contains("trung phong"))
            return PositionCodes.ST;
        if (lower == "fw" || lower.Contains("tiền đạo") || lower.Contains("forward"))
            return PositionCodes.FW_GENERAL;

        // 3. Tiền vệ
        if (lower == "ldm" || lower.Contains("phòng ngự trái"))
            return PositionCodes.LDM;
        if (lower == "rdm" || lower.Contains("phòng ngự phải"))
            return PositionCodes.RDM;
        if (lower == "cdm" || lower == "dm" || lower.Contains("tiền vệ phòng ngự") || lower.Contains("tiền vệ trụ") || lower.Contains("thòng"))
            return PositionCodes.CDM;

        if (lower == "lam" || lower.Contains("hộ công trái"))
            return PositionCodes.LAM;
        if (lower == "ram" || lower.Contains("hộ công phải"))
            return PositionCodes.RAM;
        if (lower == "cam" || lower == "am" || lower.Contains("tiền vệ tấn công") || lower.Contains("hộ công"))
            return PositionCodes.CAM;

        if (lower == "lm" || lower.Contains("tiền vệ cánh trái") || lower.Contains("tiền vệ trái"))
            return PositionCodes.LM;
        if (lower == "rm" || lower.Contains("tiền vệ cánh phải") || lower.Contains("tiền vệ phải"))
            return PositionCodes.RM;

        if (lower == "lcm" || lower.Contains("giữa lệch trái"))
            return PositionCodes.LCM;
        if (lower == "rcm" || lower.Contains("giữa lệch phải"))
            return PositionCodes.RCM;
        if (lower == "cm" || lower.Contains("tiền vệ trung tâm") || lower.Contains("tiền vệ giữa"))
            return PositionCodes.CM;
        if (lower == "mf" || lower.Contains("tiền vệ") || lower.Contains("midfield"))
            return PositionCodes.MF_GENERAL;

        // 4. Hậu vệ
        if (lower == "lwb" || lower.Contains("wing-back trái") || lower.Contains("biên trái"))
            return PositionCodes.LWB;
        if (lower == "rwb" || lower.Contains("wing-back phải") || lower.Contains("biên phải"))
            return PositionCodes.RWB;
        if (lower == "lb" || lower.Contains("hậu vệ cánh trái") || lower.Contains("hậu vệ trái"))
            return PositionCodes.LB;
        if (lower == "rb" || lower.Contains("hậu vệ cánh phải") || lower.Contains("hậu vệ phải"))
            return PositionCodes.RB;
        if (lower == "lcb" || lower.Contains("trung vệ lệch trái"))
            return PositionCodes.LCB;
        if (lower == "rcb" || lower.Contains("trung vệ lệch phải"))
            return PositionCodes.RCB;
        if (lower == "cb" || lower.Contains("trung vệ") || lower.Contains("center back"))
            return PositionCodes.CB;
        if (lower == "df" || lower.Contains("hậu vệ") || lower.Contains("defender"))
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
            PositionCodes.CB or PositionCodes.LCB or PositionCodes.RCB =>
                slotCode is "CB" or "CB_L" or "CB_R" or "CB_C" or "LCB" or "RCB" or "DF" or "DF_C",

            // Hậu vệ cánh trái & LWB
            PositionCodes.LB or PositionCodes.DF_L =>
                slotCode is "LB" or "DF_L" or "CB_L" or "DF" or "LWB",
            PositionCodes.LWB =>
                slotCode is "LWB" or "LB" or "DF_L" or "LM" or "MF_L" or "DF" or "CB_L",

            // Hậu vệ cánh phải & RWB
            PositionCodes.RB or PositionCodes.DF_R =>
                slotCode is "RB" or "DF_R" or "CB_R" or "DF" or "RWB",
            PositionCodes.RWB =>
                slotCode is "RWB" or "RB" or "DF_R" or "RM" or "MF_R" or "DF" or "CB_R",

            // Hậu vệ chung (DF_GENERAL): cho phép đá các ô trung vệ hoặc hậu vệ thòng
            PositionCodes.DF_GENERAL =>
                slotCode is "CB" or "CB_L" or "CB_R" or "CB_C" or "LCB" or "RCB" or "LB" or "RB" or "DF" or "DF_C" or "DF_L" or "DF_R",

            // Tiền vệ phòng ngự / mỏ neo
            PositionCodes.CDM or PositionCodes.DM or PositionCodes.LDM or PositionCodes.RDM =>
                slotCode is "CDM" or "DM" or "LDM" or "RDM" or "CM" or "CM_L" or "CM_R" or "LCM" or "RCM" or "MF_C" or "MF" or "MF_L" or "MF_R",

            // Tiền vệ tấn công / hộ công
            PositionCodes.CAM or PositionCodes.AM or PositionCodes.LAM or PositionCodes.RAM =>
                slotCode is "CAM" or "AM" or "LAM" or "RAM" or "CM" or "CM_L" or "CM_R" or "LCM" or "RCM" or "MF_C" or "MF" or "MF_L" or "MF_R" or "CF",

            // Tiền vệ trung tâm
            PositionCodes.CM or PositionCodes.LCM or PositionCodes.RCM =>
                slotCode is "CM" or "CM_L" or "CM_R" or "LCM" or "RCM" or "MF_C" or "MF" or "MF_L" or "MF_R" or "CDM" or "CAM",

            // Tiền vệ cánh trái
            PositionCodes.LM or PositionCodes.MF_L =>
                slotCode is "LM" or "MF_L" or "MF" or "LW" or "LWB" or "CM_L",

            // Tiền vệ cánh phải
            PositionCodes.RM or PositionCodes.MF_R =>
                slotCode is "RM" or "MF_R" or "MF" or "RW" or "RWB" or "CM_R",

            // Tiền vệ chung: có thể đá bất kỳ ô tiền vệ trong sơ đồ
            PositionCodes.MF_GENERAL =>
                slotCode is "CM" or "CM_L" or "CM_R" or "LCM" or "RCM" or "CDM" or "CAM" or "LM" or "RM" or "MF_C" or "MF_L" or "MF_R" or "MF" or "LAM" or "RAM",

            // Tiền đạo cắm & Tiền đạo lùi
            PositionCodes.ST or PositionCodes.CF =>
                slotCode is "FW" or "ST" or "ST_L" or "ST_R" or "CF" or "FW_L" or "FW_R" or "CAM",

            // Tiền đạo cánh
            PositionCodes.LW =>
                slotCode is "FW" or "LW" or "ST" or "ST_L" or "FW_L" or "MF_L" or "LM",
            PositionCodes.RW =>
                slotCode is "FW" or "RW" or "ST" or "ST_R" or "FW_R" or "MF_R" or "RM",

            // Tiền đạo chung
            PositionCodes.FW_GENERAL =>
                slotCode is "FW" or "ST" or "ST_L" or "ST_R" or "CF" or "FW_L" or "FW_R" or "LW" or "RW",

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

            PositionCodes.CB or PositionCodes.LCB or PositionCodes.RCB =>
                slotCode is "CB" or "CB_L" or "CB_R" or "CB_C" or "LCB" or "RCB" or "DF_C" or "DF",
            PositionCodes.LB or PositionCodes.DF_L =>
                slotCode is "LB" or "DF_L" or "CB_L",
            PositionCodes.LWB =>
                slotCode is "LWB" or "LB" or "DF_L",
            PositionCodes.RB or PositionCodes.DF_R =>
                slotCode is "RB" or "DF_R" or "CB_R",
            PositionCodes.RWB =>
                slotCode is "RWB" or "RB" or "DF_R",
            PositionCodes.DF_GENERAL =>
                slotCode is "CB" or "CB_L" or "CB_R" or "CB_C" or "DF_C" or "DF",

            PositionCodes.CDM or PositionCodes.DM or PositionCodes.LDM or PositionCodes.RDM =>
                slotCode is "CDM" or "DM" or "LDM" or "RDM" or "MF_C" or "CM",
            PositionCodes.CAM or PositionCodes.AM or PositionCodes.LAM or PositionCodes.RAM =>
                slotCode is "CAM" or "AM" or "LAM" or "RAM" or "MF_C" or "CM",
            PositionCodes.CM or PositionCodes.LCM or PositionCodes.RCM =>
                slotCode is "CM" or "CM_L" or "CM_R" or "LCM" or "RCM" or "MF_C" or "MF",
            PositionCodes.LM or PositionCodes.MF_L =>
                slotCode is "LM" or "MF_L",
            PositionCodes.RM or PositionCodes.MF_R =>
                slotCode is "RM" or "MF_R",
            PositionCodes.MF_GENERAL =>
                slotCode is "CM" or "CM_L" or "CM_R" or "MF_C" or "MF_L" or "MF_R" or "MF",

            PositionCodes.ST or PositionCodes.CF =>
                slotCode is "FW" or "ST" or "ST_L" or "ST_R" or "CF",
            PositionCodes.LW =>
                slotCode is "LW" or "FW_L" or "FW",
            PositionCodes.RW =>
                slotCode is "RW" or "FW_R" or "FW",
            PositionCodes.FW_GENERAL =>
                slotCode is "FW" or "ST" or "ST_L" or "ST_R" or "FW_L" or "FW_R",

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
