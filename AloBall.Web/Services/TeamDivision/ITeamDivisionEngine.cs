namespace AloBall.Web.Services.TeamDivision;

public enum DivisionEngineStatus
{
    OptimalProven = 1,        // Có phương án hợp lệ, đã chứng minh tối ưu toàn cục
    FeasibleSuboptimal = 2,   // Có phương án hợp lệ, dừng do nhánh cận/thời gian, chưa chứng minh tối ưu tuyệt đối
    InfeasibleProven = 3,     // Đã xác định chắc chắn không tồn tại phương án nào thỏa mãn ràng buộc
    SearchLimitExceeded = 4   // Chưa tìm được phương án trong giới hạn tìm kiếm (Timeout)
}

public class PlayerDivisionInput
{
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int? KitNumber { get; set; }
    public string RawPosition { get; set; } = string.Empty;
    public string StandardPrimaryPosition { get; set; } = PositionCodes.UNASSIGNED;
    public List<string> SecondaryPositions { get; set; } = new();

    // Ràng buộc cố định do đội trưởng chỉ định
    public string? LockedTeamSide { get; set; } // "A", "B", hoặc null
    public string? LockedSlotCode { get; set; } // "GK", "CB_L"... hoặc null
    public bool IsExplicitSubstitute { get; set; } // Đội trưởng chọn rõ làm dự bị

    // Ngoại lệ vị trí trận đã được cấp phép
    public List<string> GrantedExceptionSlots { get; set; } = new();
}

public class SlotAssignment
{
    public string SlotCode { get; set; } = string.Empty;
    public string SlotName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int? KitNumber { get; set; }
    public bool IsPrimaryPosition { get; set; }
    public bool IsSecondaryPosition { get; set; }
    public bool IsPositionException { get; set; }
    public bool IsLockedSide { get; set; }
    public bool IsLockedSlot { get; set; }
}

public class SubstitutePlayerDto
{
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int? KitNumber { get; set; }
    public string? DesignatedSide { get; set; } // "A", "B", hoặc null (dự bị chung)
    public string StandardPrimaryPosition { get; set; } = string.Empty;
    public string RawPosition { get; set; } = string.Empty;
    public bool IsConfirmedByManager { get; set; }
}

public class TeamDivisionResult
{
    public bool IsSuccess { get; set; }
    public DivisionEngineStatus Status { get; set; } = DivisionEngineStatus.SearchLimitExceeded;

    public string FormationCode { get; set; } = string.Empty;
    public string FormationName { get; set; } = string.Empty;
    public int PlayersPerSide { get; set; }

    // Đội hình Bên A
    public List<SlotAssignment> TeamASlots { get; set; } = new();
    public int PrimaryPositionCountA { get; set; }
    public int SecondaryPositionCountA { get; set; }

    // Đội hình Bên B
    public List<SlotAssignment> TeamBSlots { get; set; } = new();
    public int PrimaryPositionCountB { get; set; }
    public int SecondaryPositionCountB { get; set; }

    // Danh sách dự bị đã xác nhận
    public List<SubstitutePlayerDto> Substitutes { get; set; } = new();

    // Điểm số tối ưu hóa
    public int TotalPrimaryMatches { get; set; }
    public int SecondaryDifference { get; set; }

    // Chẩn đoán lỗi và khuyến nghị khi thất bại
    public string? DiagnosticErrorCode { get; set; }
    public string? DiagnosticMessage { get; set; }
    public List<string> RemediationSuggestions { get; set; } = new();

    public int ExecutionTimeMs { get; set; }
    public int SeedUsed { get; set; }
    public string EngineVersion { get; set; } = "1.0.0";
}

public interface ITeamDivisionEngine
{
    TeamDivisionResult DivideTeams(
        List<PlayerDivisionInput> players,
        FormationDefinition formation,
        int? seed = null,
        int timeoutMs = 500);
}
