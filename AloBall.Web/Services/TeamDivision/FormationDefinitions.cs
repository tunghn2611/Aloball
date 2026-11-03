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
    public const string LB = "LB";
    public const string RB = "RB";
    public const string DF_L = "DF_L";
    public const string DF_R = "DF_R";
    public const string DF_C = "DF_C";
    public const string DF_GENERAL = "DF_GENERAL";

    // Midfielders
    public const string CDM = "CDM";
    public const string CM = "CM";
    public const string CAM = "CAM";
    public const string LM = "LM";
    public const string RM = "RM";
    public const string MF_L = "MF_L";
    public const string MF_R = "MF_R";
    public const string MF_C = "MF_C";
    public const string MF_GENERAL = "MF_GENERAL";

    // Forwards
    public const string ST = "ST";
    public const string CF = "CF";
    public const string LW = "LW";
    public const string RW = "RW";
    public const string FW = "FW";
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
}

public class FormationDefinition
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PitchType { get; set; } = "7 người"; // "5 người", "7 người"
    public int PlayersPerSide { get; set; }
    public List<FormationSlot> Slots { get; set; } = new();
}

public static class FormationRegistry
{
    // Sân 7: 2-3-1 (1 GK, 2 CB, 3 MF, 1 FW)
    public static readonly FormationDefinition S7_231 = new()
    {
        Code = "7_231",
        Name = "2-3-1 (Cân bằng Sân 7)",
        PitchType = "7 người",
        PlayersPerSide = 7,
        Slots = new List<FormationSlot>
        {
            new() { SlotCode = "GK", SlotName = "Thủ môn", Category = "GK", DisplayOrder = 1 },
            new() { SlotCode = "CB_L", SlotName = "Hậu vệ trái", Category = "DF", DisplayOrder = 2 },
            new() { SlotCode = "CB_R", SlotName = "Hậu vệ phải", Category = "DF", DisplayOrder = 3 },
            new() { SlotCode = "MF_L", SlotName = "Tiền vệ trái", Category = "MF", DisplayOrder = 4 },
            new() { SlotCode = "MF_C", SlotName = "Tiền vệ trung tâm", Category = "MF", DisplayOrder = 5 },
            new() { SlotCode = "MF_R", SlotName = "Tiền vệ phải", Category = "MF", DisplayOrder = 6 },
            new() { SlotCode = "FW", SlotName = "Tiền đạo", Category = "FW", DisplayOrder = 7 }
        }
    };

    // Sân 7: 3-2-1 (1 GK, 3 DF, 2 MF, 1 FW)
    public static readonly FormationDefinition S7_321 = new()
    {
        Code = "7_321",
        Name = "3-2-1 (Phòng ngự Sân 7)",
        PitchType = "7 người",
        PlayersPerSide = 7,
        Slots = new List<FormationSlot>
        {
            new() { SlotCode = "GK", SlotName = "Thủ môn", Category = "GK", DisplayOrder = 1 },
            new() { SlotCode = "DF_L", SlotName = "Hậu vệ cánh trái", Category = "DF", DisplayOrder = 2 },
            new() { SlotCode = "DF_C", SlotName = "Trung vệ giữa", Category = "DF", DisplayOrder = 3 },
            new() { SlotCode = "DF_R", SlotName = "Hậu vệ cánh phải", Category = "DF", DisplayOrder = 4 },
            new() { SlotCode = "MF_L", SlotName = "Tiền vệ trái", Category = "MF", DisplayOrder = 5 },
            new() { SlotCode = "MF_R", SlotName = "Tiền vệ phải", Category = "MF", DisplayOrder = 6 },
            new() { SlotCode = "FW", SlotName = "Tiền đạo", Category = "FW", DisplayOrder = 7 }
        }
    };

    // Sân 5: 1-2-1 Diamond (1 GK, 1 DF, 2 MF, 1 FW)
    public static readonly FormationDefinition S5_121 = new()
    {
        Code = "5_121",
        Name = "1-2-1 Kim Cương (Sân 5)",
        PitchType = "5 người",
        PlayersPerSide = 5,
        Slots = new List<FormationSlot>
        {
            new() { SlotCode = "GK", SlotName = "Thủ môn", Category = "GK", DisplayOrder = 1 },
            new() { SlotCode = "DF", SlotName = "Hậu vệ thòng", Category = "DF", DisplayOrder = 2 },
            new() { SlotCode = "MF_L", SlotName = "Tiền vệ cánh trái", Category = "MF", DisplayOrder = 3 },
            new() { SlotCode = "MF_R", SlotName = "Tiền vệ cánh phải", Category = "MF", DisplayOrder = 4 },
            new() { SlotCode = "FW", SlotName = "Tiền đạo cắm", Category = "FW", DisplayOrder = 5 }
        }
    };

    // Sân 5: 2-1-1 (1 GK, 2 DF, 1 MF, 1 FW)
    public static readonly FormationDefinition S5_211 = new()
    {
        Code = "5_211",
        Name = "2-1-1 (Sân 5 chắc chắn)",
        PitchType = "5 người",
        PlayersPerSide = 5,
        Slots = new List<FormationSlot>
        {
            new() { SlotCode = "GK", SlotName = "Thủ môn", Category = "GK", DisplayOrder = 1 },
            new() { SlotCode = "DF_L", SlotName = "Hậu vệ trái", Category = "DF", DisplayOrder = 2 },
            new() { SlotCode = "DF_R", SlotName = "Hậu vệ phải", Category = "DF", DisplayOrder = 3 },
            new() { SlotCode = "MF_C", SlotName = "Tiền vệ giữa", Category = "MF", DisplayOrder = 4 },
            new() { SlotCode = "FW", SlotName = "Tiền đạo", Category = "FW", DisplayOrder = 5 }
        }
    };

    public static readonly List<FormationDefinition> AllFormations = new()
    {
        S7_231, S7_321, S5_121, S5_211
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

        return S7_231;
    }
}
