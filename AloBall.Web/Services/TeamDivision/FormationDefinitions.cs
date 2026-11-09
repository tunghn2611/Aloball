namespace AloBall.Web.Services.TeamDivision;

public static class PositionCodes
{
    // Goalkeeper
    public const string GK = "GK";

    // Defenders
    public const string CB = "CB";
    public const string CB_L = "CB_L";
    public const string CB_R = "CB_R";
    public const string CB_C = "CB_C";
    public const string LCB = "LCB";
    public const string RCB = "RCB";
    public const string LB = "LB";
    public const string RB = "RB";
    public const string LWB = "LWB";
    public const string RWB = "RWB";
    public const string DF_L = "DF_L";
    public const string DF_R = "DF_R";
    public const string DF_C = "DF_C";
    public const string DF_GENERAL = "DF_GENERAL";

    // Midfielders
    public const string DM = "DM";
    public const string CDM = "CDM";
    public const string LDM = "LDM";
    public const string RDM = "RDM";
    public const string CM = "CM";
    public const string LCM = "LCM";
    public const string RCM = "RCM";
    public const string AM = "AM";
    public const string CAM = "CAM";
    public const string LAM = "LAM";
    public const string RAM = "RAM";
    public const string LM = "LM";
    public const string RM = "RM";
    public const string MF_L = "MF_L";
    public const string MF_R = "MF_R";
    public const string MF_C = "MF_C";
    public const string MF_GENERAL = "MF_GENERAL";

    // Forwards
    public const string ST = "ST";
    public const string ST_L = "ST_L";
    public const string ST_R = "ST_R";
    public const string CF = "CF";
    public const string LW = "LW";
    public const string RW = "RW";
    public const string FW = "FW";
    public const string FW_L = "FW_L";
    public const string FW_R = "FW_R";
    public const string FW_GENERAL = "FW_GENERAL";

    // Unknown / Unassigned
    public const string UNASSIGNED = "UNASSIGNED";
}

public class FormationSlot
{
    public string SlotCode { get; set; } = string.Empty;
    public string SlotName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // GK, DF, MF, FW
    public int DisplayOrder { get; set; }
    public double XPercent { get; set; } = 50;
    public double YPercent { get; set; } = 50;
}

public class FormationDefinition
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PitchType { get; set; } = "7 người"; // "5 người", "7 người", "11 người"
    public int PlayersPerSide { get; set; }
    public List<FormationSlot> Slots { get; set; } = new();
}

public static class FormationRegistry
{
    // =========================================================================
    // SÂN 5: 3 Sơ đồ (1-2-1, 2-1-1, 2-2)
    // =========================================================================

    // Sân 5: 1-2-1 Diamond (1 GK, 1 DF, 2 MF, 1 FW)
    public static readonly FormationDefinition S5_121 = new()
    {
        Code = "5_121",
        Name = "1-2-1 (Sân 5 - Kim Cương)",
        PitchType = "5 người",
        PlayersPerSide = 5,
        Slots = new List<FormationSlot>
        {
            new() { SlotCode = "GK", SlotName = "Thủ môn", Category = "GK", DisplayOrder = 1, XPercent = 50, YPercent = 88 },
            new() { SlotCode = "DF", SlotName = "Hậu vệ thòng", Category = "DF", DisplayOrder = 2, XPercent = 50, YPercent = 68 },
            new() { SlotCode = "MF_L", SlotName = "Tiền vệ cánh trái", Category = "MF", DisplayOrder = 3, XPercent = 24, YPercent = 44 },
            new() { SlotCode = "MF_R", SlotName = "Tiền vệ cánh phải", Category = "MF", DisplayOrder = 4, XPercent = 76, YPercent = 44 },
            new() { SlotCode = "FW", SlotName = "Tiền đạo cắm", Category = "FW", DisplayOrder = 5, XPercent = 50, YPercent = 20 }
        }
    };

    // Sân 5: 2-1-1 (1 GK, 2 DF, 1 MF, 1 FW)
    public static readonly FormationDefinition S5_211 = new()
    {
        Code = "5_211",
        Name = "2-1-1 (Sân 5 - Chắc chắn)",
        PitchType = "5 người",
        PlayersPerSide = 5,
        Slots = new List<FormationSlot>
        {
            new() { SlotCode = "GK", SlotName = "Thủ môn", Category = "GK", DisplayOrder = 1, XPercent = 50, YPercent = 88 },
            new() { SlotCode = "DF_L", SlotName = "Hậu vệ trái", Category = "DF", DisplayOrder = 2, XPercent = 30, YPercent = 68 },
            new() { SlotCode = "DF_R", SlotName = "Hậu vệ phải", Category = "DF", DisplayOrder = 3, XPercent = 70, YPercent = 68 },
            new() { SlotCode = "MF_C", SlotName = "Tiền vệ giữa", Category = "MF", DisplayOrder = 4, XPercent = 50, YPercent = 44 },
            new() { SlotCode = "FW", SlotName = "Tiền đạo cắm", Category = "FW", DisplayOrder = 5, XPercent = 50, YPercent = 20 }
        }
    };

    // Sân 5: 2-2 (1 GK, 2 DF, 2 FW)
    public static readonly FormationDefinition S5_22 = new()
    {
        Code = "5_22",
        Name = "2-2 (Sân 5 - Cân bằng)",
        PitchType = "5 người",
        PlayersPerSide = 5,
        Slots = new List<FormationSlot>
        {
            new() { SlotCode = "GK", SlotName = "Thủ môn", Category = "GK", DisplayOrder = 1, XPercent = 50, YPercent = 88 },
            new() { SlotCode = "CB_L", SlotName = "Hậu vệ trái", Category = "DF", DisplayOrder = 2, XPercent = 30, YPercent = 66 },
            new() { SlotCode = "CB_R", SlotName = "Hậu vệ phải", Category = "DF", DisplayOrder = 3, XPercent = 70, YPercent = 66 },
            new() { SlotCode = "FW_L", SlotName = "Tiền đạo trái", Category = "FW", DisplayOrder = 4, XPercent = 32, YPercent = 24 },
            new() { SlotCode = "FW_R", SlotName = "Tiền đạo phải", Category = "FW", DisplayOrder = 5, XPercent = 68, YPercent = 24 }
        }
    };

    // =========================================================================
    // SÂN 7: 3 Sơ đồ (2-3-1, 3-2-1, 2-1-2-1)
    // =========================================================================

    // Sân 7: 2-3-1 (1 GK, 2 CB, 3 MF, 1 FW)
    public static readonly FormationDefinition S7_231 = new()
    {
        Code = "7_231",
        Name = "2-3-1 (Sân 7 - Cân bằng)",
        PitchType = "7 người",
        PlayersPerSide = 7,
        Slots = new List<FormationSlot>
        {
            new() { SlotCode = "GK", SlotName = "Thủ môn", Category = "GK", DisplayOrder = 1, XPercent = 50, YPercent = 88 },
            new() { SlotCode = "CB_L", SlotName = "Hậu vệ trái", Category = "DF", DisplayOrder = 2, XPercent = 28, YPercent = 68 },
            new() { SlotCode = "CB_R", SlotName = "Hậu vệ phải", Category = "DF", DisplayOrder = 3, XPercent = 72, YPercent = 68 },
            new() { SlotCode = "MF_L", SlotName = "Tiền vệ trái", Category = "MF", DisplayOrder = 4, XPercent = 18, YPercent = 44 },
            new() { SlotCode = "MF_C", SlotName = "Tiền vệ trung tâm", Category = "MF", DisplayOrder = 5, XPercent = 50, YPercent = 46 },
            new() { SlotCode = "MF_R", SlotName = "Tiền vệ phải", Category = "MF", DisplayOrder = 6, XPercent = 82, YPercent = 44 },
            new() { SlotCode = "FW", SlotName = "Tiền đạo cắm", Category = "FW", DisplayOrder = 7, XPercent = 50, YPercent = 20 }
        }
    };

    // Sân 7: 3-2-1 (1 GK, 3 DF, 2 MF, 1 FW)
    public static readonly FormationDefinition S7_321 = new()
    {
        Code = "7_321",
        Name = "3-2-1 (Sân 7 - Phòng ngự)",
        PitchType = "7 người",
        PlayersPerSide = 7,
        Slots = new List<FormationSlot>
        {
            new() { SlotCode = "GK", SlotName = "Thủ môn", Category = "GK", DisplayOrder = 1, XPercent = 50, YPercent = 88 },
            new() { SlotCode = "DF_L", SlotName = "Hậu vệ cánh trái", Category = "DF", DisplayOrder = 2, XPercent = 20, YPercent = 68 },
            new() { SlotCode = "DF_C", SlotName = "Trung vệ giữa", Category = "DF", DisplayOrder = 3, XPercent = 50, YPercent = 70 },
            new() { SlotCode = "DF_R", SlotName = "Hậu vệ cánh phải", Category = "DF", DisplayOrder = 4, XPercent = 80, YPercent = 68 },
            new() { SlotCode = "MF_L", SlotName = "Tiền vệ trái", Category = "MF", DisplayOrder = 5, XPercent = 32, YPercent = 44 },
            new() { SlotCode = "MF_R", SlotName = "Tiền vệ phải", Category = "MF", DisplayOrder = 6, XPercent = 68, YPercent = 44 },
            new() { SlotCode = "FW", SlotName = "Tiền đạo cắm", Category = "FW", DisplayOrder = 7, XPercent = 50, YPercent = 20 }
        }
    };

    // Sân 7: 2-1-2-1 (1 GK, 2 DF, 1 DM, 2 AM, 1 FW)
    public static readonly FormationDefinition S7_2121 = new()
    {
        Code = "7_2121",
        Name = "2-1-2-1 (Sân 7 - Kiểm soát)",
        PitchType = "7 người",
        PlayersPerSide = 7,
        Slots = new List<FormationSlot>
        {
            new() { SlotCode = "GK", SlotName = "Thủ môn", Category = "GK", DisplayOrder = 1, XPercent = 50, YPercent = 88 },
            new() { SlotCode = "CB_L", SlotName = "Hậu vệ trái", Category = "DF", DisplayOrder = 2, XPercent = 28, YPercent = 70 },
            new() { SlotCode = "CB_R", SlotName = "Hậu vệ phải", Category = "DF", DisplayOrder = 3, XPercent = 72, YPercent = 70 },
            new() { SlotCode = "CDM", SlotName = "Tiền vệ mỏ neo", Category = "MF", DisplayOrder = 4, XPercent = 50, YPercent = 54 },
            new() { SlotCode = "LAM", SlotName = "Hộ công trái", Category = "MF", DisplayOrder = 5, XPercent = 30, YPercent = 36 },
            new() { SlotCode = "RAM", SlotName = "Hộ công phải", Category = "MF", DisplayOrder = 6, XPercent = 70, YPercent = 36 },
            new() { SlotCode = "FW", SlotName = "Tiền đạo cắm", Category = "FW", DisplayOrder = 7, XPercent = 50, YPercent = 18 }
        }
    };

    // =========================================================================
    // SÂN 11: 4 Sơ đồ (4-3-3, 4-4-2, 4-2-3-1, 3-5-2)
    // =========================================================================

    // Sân 11: 4-3-3 (1 GK, 4 DF, 3 MF, 3 FW)
    public static readonly FormationDefinition S11_433 = new()
    {
        Code = "11_433",
        Name = "4-3-3 (Sân 11 - Tấn công)",
        PitchType = "11 người",
        PlayersPerSide = 11,
        Slots = new List<FormationSlot>
        {
            new() { SlotCode = "GK", SlotName = "Thủ môn", Category = "GK", DisplayOrder = 1, XPercent = 50, YPercent = 90 },
            new() { SlotCode = "LB", SlotName = "Hậu vệ trái", Category = "DF", DisplayOrder = 2, XPercent = 15, YPercent = 72 },
            new() { SlotCode = "CB_L", SlotName = "Trung vệ trái", Category = "DF", DisplayOrder = 3, XPercent = 38, YPercent = 74 },
            new() { SlotCode = "CB_R", SlotName = "Trung vệ phải", Category = "DF", DisplayOrder = 4, XPercent = 62, YPercent = 74 },
            new() { SlotCode = "RB", SlotName = "Hậu vệ phải", Category = "DF", DisplayOrder = 5, XPercent = 85, YPercent = 72 },
            new() { SlotCode = "LCM", SlotName = "Tiền vệ lệch trái", Category = "MF", DisplayOrder = 6, XPercent = 30, YPercent = 50 },
            new() { SlotCode = "CM", SlotName = "Tiền vệ trung tâm", Category = "MF", DisplayOrder = 7, XPercent = 50, YPercent = 52 },
            new() { SlotCode = "RCM", SlotName = "Tiền vệ lệch phải", Category = "MF", DisplayOrder = 8, XPercent = 70, YPercent = 50 },
            new() { SlotCode = "LW", SlotName = "Tiền đạo cánh trái", Category = "FW", DisplayOrder = 9, XPercent = 20, YPercent = 24 },
            new() { SlotCode = "ST", SlotName = "Tiền đạo cắm", Category = "FW", DisplayOrder = 10, XPercent = 50, YPercent = 18 },
            new() { SlotCode = "RW", SlotName = "Tiền đạo cánh phải", Category = "FW", DisplayOrder = 11, XPercent = 80, YPercent = 24 }
        }
    };

    // Sân 11: 4-4-2 (1 GK, 4 DF, 4 MF, 2 FW)
    public static readonly FormationDefinition S11_442 = new()
    {
        Code = "11_442",
        Name = "4-4-2 (Sân 11 - Truyền thống)",
        PitchType = "11 người",
        PlayersPerSide = 11,
        Slots = new List<FormationSlot>
        {
            new() { SlotCode = "GK", SlotName = "Thủ môn", Category = "GK", DisplayOrder = 1, XPercent = 50, YPercent = 90 },
            new() { SlotCode = "LB", SlotName = "Hậu vệ trái", Category = "DF", DisplayOrder = 2, XPercent = 15, YPercent = 72 },
            new() { SlotCode = "CB_L", SlotName = "Trung vệ trái", Category = "DF", DisplayOrder = 3, XPercent = 38, YPercent = 74 },
            new() { SlotCode = "CB_R", SlotName = "Trung vệ phải", Category = "DF", DisplayOrder = 4, XPercent = 62, YPercent = 74 },
            new() { SlotCode = "RB", SlotName = "Hậu vệ phải", Category = "DF", DisplayOrder = 5, XPercent = 85, YPercent = 72 },
            new() { SlotCode = "LM", SlotName = "Tiền vệ cánh trái", Category = "MF", DisplayOrder = 6, XPercent = 18, YPercent = 48 },
            new() { SlotCode = "CM_L", SlotName = "Tiền vệ giữa trái", Category = "MF", DisplayOrder = 7, XPercent = 40, YPercent = 50 },
            new() { SlotCode = "CM_R", SlotName = "Tiền vệ giữa phải", Category = "MF", DisplayOrder = 8, XPercent = 60, YPercent = 50 },
            new() { SlotCode = "RM", SlotName = "Tiền vệ cánh phải", Category = "MF", DisplayOrder = 9, XPercent = 82, YPercent = 48 },
            new() { SlotCode = "ST_L", SlotName = "Tiền đạo trái", Category = "FW", DisplayOrder = 10, XPercent = 36, YPercent = 20 },
            new() { SlotCode = "ST_R", SlotName = "Tiền đạo phải", Category = "FW", DisplayOrder = 11, XPercent = 64, YPercent = 20 }
        }
    };

    // Sân 11: 4-2-3-1 (1 GK, 4 DF, 2 CDM, 3 AM, 1 ST)
    public static readonly FormationDefinition S11_4231 = new()
    {
        Code = "11_4231",
        Name = "4-2-3-1 (Sân 11 - Hiện đại)",
        PitchType = "11 người",
        PlayersPerSide = 11,
        Slots = new List<FormationSlot>
        {
            new() { SlotCode = "GK", SlotName = "Thủ môn", Category = "GK", DisplayOrder = 1, XPercent = 50, YPercent = 90 },
            new() { SlotCode = "LB", SlotName = "Hậu vệ trái", Category = "DF", DisplayOrder = 2, XPercent = 15, YPercent = 72 },
            new() { SlotCode = "CB_L", SlotName = "Trung vệ trái", Category = "DF", DisplayOrder = 3, XPercent = 38, YPercent = 74 },
            new() { SlotCode = "CB_R", SlotName = "Trung vệ phải", Category = "DF", DisplayOrder = 4, XPercent = 62, YPercent = 74 },
            new() { SlotCode = "RB", SlotName = "Hậu vệ phải", Category = "DF", DisplayOrder = 5, XPercent = 85, YPercent = 72 },
            new() { SlotCode = "LDM", SlotName = "Tiền vệ phòng ngự trái", Category = "MF", DisplayOrder = 6, XPercent = 36, YPercent = 56 },
            new() { SlotCode = "RDM", SlotName = "Tiền vệ phòng ngự phải", Category = "MF", DisplayOrder = 7, XPercent = 64, YPercent = 56 },
            new() { SlotCode = "LAM", SlotName = "Hộ công trái", Category = "MF", DisplayOrder = 8, XPercent = 22, YPercent = 36 },
            new() { SlotCode = "CAM", SlotName = "Hộ công trung tâm", Category = "MF", DisplayOrder = 9, XPercent = 50, YPercent = 34 },
            new() { SlotCode = "RAM", SlotName = "Hộ công phải", Category = "MF", DisplayOrder = 10, XPercent = 78, YPercent = 36 },
            new() { SlotCode = "ST", SlotName = "Tiền đạo cắm", Category = "FW", DisplayOrder = 11, XPercent = 50, YPercent = 18 }
        }
    };

    // Sân 11: 3-5-2 (1 GK, 3 CB, 5 MF, 2 FW)
    public static readonly FormationDefinition S11_352 = new()
    {
        Code = "11_352",
        Name = "3-5-2 (Sân 11 - Tổng lực)",
        PitchType = "11 người",
        PlayersPerSide = 11,
        Slots = new List<FormationSlot>
        {
            new() { SlotCode = "GK", SlotName = "Thủ môn", Category = "GK", DisplayOrder = 1, XPercent = 50, YPercent = 90 },
            new() { SlotCode = "LCB", SlotName = "Trung vệ lệch trái", Category = "DF", DisplayOrder = 2, XPercent = 25, YPercent = 74 },
            new() { SlotCode = "CB", SlotName = "Trung vệ thòng", Category = "DF", DisplayOrder = 3, XPercent = 50, YPercent = 76 },
            new() { SlotCode = "RCB", SlotName = "Trung vệ lệch phải", Category = "DF", DisplayOrder = 4, XPercent = 75, YPercent = 74 },
            new() { SlotCode = "LWB", SlotName = "Chạy cánh trái (Wing-back)", Category = "MF", DisplayOrder = 5, XPercent = 14, YPercent = 50 },
            new() { SlotCode = "LCM", SlotName = "Tiền vệ lệch trái", Category = "MF", DisplayOrder = 6, XPercent = 36, YPercent = 52 },
            new() { SlotCode = "CM", SlotName = "Tiền vệ trung tâm", Category = "MF", DisplayOrder = 7, XPercent = 50, YPercent = 54 },
            new() { SlotCode = "RCM", SlotName = "Tiền vệ lệch phải", Category = "MF", DisplayOrder = 8, XPercent = 64, YPercent = 52 },
            new() { SlotCode = "RWB", SlotName = "Chạy cánh phải (Wing-back)", Category = "MF", DisplayOrder = 9, XPercent = 86, YPercent = 50 },
            new() { SlotCode = "ST_L", SlotName = "Tiền đạo trái", Category = "FW", DisplayOrder = 10, XPercent = 36, YPercent = 20 },
            new() { SlotCode = "ST_R", SlotName = "Tiền đạo phải", Category = "FW", DisplayOrder = 11, XPercent = 64, YPercent = 20 }
        }
    };

    public static readonly List<FormationDefinition> AllFormations = new()
    {
        // 5-a-side
        S5_121, S5_211, S5_22,
        // 7-a-side
        S7_231, S7_321, S7_2121,
        // 11-a-side
        S11_433, S11_442, S11_4231, S11_352
    };

    public static FormationDefinition GetByCode(string? code, string? defaultPitchType = "7 người")
    {
        if (!string.IsNullOrEmpty(code))
        {
            var found = AllFormations.FirstOrDefault(f => f.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;
        }

        if (defaultPitchType?.Contains("5") == true)
        {
            return S5_121;
        }
        if (defaultPitchType?.Contains("11") == true)
        {
            return S11_433;
        }

        return S7_231;
    }

    public static List<FormationDefinition> GetFormationsByPitchType(string? pitchType)
    {
        if (string.IsNullOrWhiteSpace(pitchType)) return AllFormations;
        if (pitchType.Contains("5")) return AllFormations.Where(f => f.PitchType == "5 người").ToList();
        if (pitchType.Contains("11")) return AllFormations.Where(f => f.PitchType == "11 người").ToList();
        return AllFormations.Where(f => f.PitchType == "7 người").ToList();
    }
}
