using System.Diagnostics;

namespace AloBall.Web.Services.TeamDivision;

public class TeamDivisionEngine : ITeamDivisionEngine
{
    public const string CurrentEngineVersion = "1.0.0";

    public TeamDivisionResult DivideTeams(
        List<PlayerDivisionInput> players,
        FormationDefinition formation,
        int? seed = null,
        int timeoutMs = 500)
    {
        var sw = Stopwatch.StartNew();
        int actualSeed = seed ?? 42;
        var rng = new Random(actualSeed);

        var result = new TeamDivisionResult
        {
            FormationCode = formation.Code,
            FormationName = formation.Name,
            PlayersPerSide = formation.PlayersPerSide,
            SeedUsed = actualSeed,
            EngineVersion = CurrentEngineVersion
        };

        // 1. Tách danh sách dự bị rõ ràng
        var explicitSubs = players.Where(p => p.IsExplicitSubstitute).ToList();
        var mainPool = players.Where(p => !p.IsExplicitSubstitute).ToList();

        int neededStarters = formation.PlayersPerSide * 2;

        // Nếu số lượng cầu thủ chính chưa đủ hoặc vượt quá mà chưa xác nhận dự bị
        if (mainPool.Count < neededStarters)
        {
            result.IsSuccess = false;
            result.Status = DivisionEngineStatus.InfeasibleProven;
            result.DiagnosticErrorCode = "InsufficientPlayersCount";
            result.DiagnosticMessage = $"Số cầu thủ đá chính hiện có ({mainPool.Count} người) không đủ cho sơ đồ {formation.Name} (Cần đúng {neededStarters} người: {formation.PlayersPerSide} người mỗi bên).";
            result.RemediationSuggestions.Add($"Vui lòng bổ sung thêm {neededStarters - mainPool.Count} cầu thủ vào danh sách tham gia hoặc chọn sơ đồ sân nhỏ hơn.");
            sw.Stop();
            result.ExecutionTimeMs = (int)sw.ElapsedMilliseconds;
            return result;
        }

        if (mainPool.Count > neededStarters)
        {
            int extraCount = mainPool.Count - neededStarters;
            result.IsSuccess = false;
            result.Status = DivisionEngineStatus.InfeasibleProven;
            result.DiagnosticErrorCode = "UnconfirmedSubstitutes";
            result.DiagnosticMessage = $"Danh sách có {mainPool.Count} cầu thủ, vượt quá sức chứa sơ đồ ({neededStarters} người). Cần xác nhận {extraCount} người dự bị trước khi chia tự động.";
            result.RemediationSuggestions.Add($"Đội trưởng hãy chọn {extraCount} cầu thủ làm dự bị và thử lại.");
            sw.Stop();
            result.ExecutionTimeMs = (int)sw.ElapsedMilliseconds;
            return result;
        }

        // 2. Chuẩn hóa Input để giữ tính tất định (Deterministic Normalization)
        var normalizedPlayers = mainPool
            .OrderBy(p => p.PlayerId)
            .Select(p => new NormalizedPlayer(p))
            .ToList();

        // Chuẩn hóa danh sách ô vị trí của 2 đội
        var slotsA = formation.Slots
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.SlotCode)
            .Select(s => new TargetSlot("A", s.SlotCode, s.SlotName, s.Category))
            .ToList();

        var slotsB = formation.Slots
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.SlotCode)
            .Select(s => new TargetSlot("B", s.SlotCode, s.SlotName, s.Category))
            .ToList();

        var allSlots = new List<TargetSlot>();
        allSlots.AddRange(slotsA);
        allSlots.AddRange(slotsB);

        // 3. Kiểm tra các xung đột khóa sơ bộ (Pre-check Lock Conflicts)
        foreach (var p in normalizedPlayers)
        {
            if (!string.IsNullOrEmpty(p.LockedSlotCode))
            {
                // Kiểm tra xem vị trí khóa có thuộc sơ đồ không
                bool existsInFormation = formation.Slots.Any(s => s.SlotCode == p.LockedSlotCode);
                if (!existsInFormation)
                {
                    result.IsSuccess = false;
                    result.Status = DivisionEngineStatus.InfeasibleProven;
                    result.DiagnosticErrorCode = "InvalidLockedSlot";
                    result.DiagnosticMessage = $"Cầu thủ {p.PlayerName} bị khóa vào vị trí '{p.LockedSlotCode}' nhưng sơ đồ {formation.Name} không có vị trí này.";
                    result.RemediationSuggestions.Add($"Vui lòng mở khóa vị trí của {p.PlayerName} hoặc đổi sơ đồ.");
                    sw.Stop();
                    result.ExecutionTimeMs = (int)sw.ElapsedMilliseconds;
                    return result;
                }

                // Kiểm tra khóa có phù hợp sở trường không (Khóa không tự cấp ngoại lệ)
                bool canPlayLocked = PositionCompatibilityMatrix.CanPlaySlot(
                    p.StandardPrimaryPosition,
                    p.SecondaryPositions,
                    p.LockedSlotCode,
                    p.GrantedExceptionSlots);

                if (!canPlayLocked)
                {
                    result.IsSuccess = false;
                    result.Status = DivisionEngineStatus.InfeasibleProven;
                    result.DiagnosticErrorCode = "LockPositionMismatch";
                    result.DiagnosticMessage = $"Cầu thủ {p.PlayerName} bị khóa vào ô '{p.LockedSlotCode}' nhưng vị trí này không thuộc sở trường ({p.RawPosition}). Khóa không tự cấp ngoại lệ.";
                    result.RemediationSuggestions.Add($"Hãy cấp 'Ngoại lệ vị trí trận' cho {p.PlayerName} đá {p.LockedSlotCode}, hoặc mở khóa vị trí.");
                    sw.Stop();
                    result.ExecutionTimeMs = (int)sw.ElapsedMilliseconds;
                    return result;
                }
            }
        }

        // Kiểm tra xung đột khóa: Nhiều người bị khóa vào cùng 1 ô cụ thể
        var lockedToExactSlot = normalizedPlayers
            .Where(p => !string.IsNullOrEmpty(p.LockedTeamSide) && !string.IsNullOrEmpty(p.LockedSlotCode))
            .GroupBy(p => $"{p.LockedTeamSide}_{p.LockedSlotCode}")
            .Where(g => g.Count() > 1)
            .ToList();

        if (lockedToExactSlot.Any())
        {
            var conflict = lockedToExactSlot.First();
            var names = string.Join(", ", conflict.Select(p => p.PlayerName));
            result.IsSuccess = false;
            result.Status = DivisionEngineStatus.InfeasibleProven;
            result.DiagnosticErrorCode = "LockConflict";
            result.DiagnosticMessage = $"Xung đột khóa vị trí: Các cầu thủ ({names}) đều bị khóa vào cùng ô {conflict.Key}.";
            result.RemediationSuggestions.Add("Vui lòng phân bổ các cầu thủ vào các ô khác nhau hoặc mở khóa.");
            sw.Stop();
            result.ExecutionTimeMs = (int)sw.ElapsedMilliseconds;
            return result;
        }

        // 4. Giải thuật Phân Bổ Vị Trí Tối Ưu (Branch-and-Bound / Constraint Satisfaction)
        var solver = new DivisionSolver(normalizedPlayers, allSlots, formation, timeoutMs, rng);
        var solution = solver.Solve();

        sw.Stop();
        result.ExecutionTimeMs = (int)sw.ElapsedMilliseconds;

        if (solution == null || !solution.IsFeasible)
        {
            result.IsSuccess = false;
            result.Status = solver.SearchTimedOut ? DivisionEngineStatus.SearchLimitExceeded : DivisionEngineStatus.InfeasibleProven;
            result.DiagnosticErrorCode = solver.DiagnosticErrorCode ?? "NoFeasibleAssignment";
            result.DiagnosticMessage = solver.DiagnosticMessage ?? "Không tìm thấy cách chia đội nào thỏa mãn điều kiện vị trí của các cầu thủ.";
            result.RemediationSuggestions = solver.RemediationSuggestions;
            return result;
        }

        // 5. Gán kết quả thành công
        result.IsSuccess = true;
        result.Status = solver.ProvenOptimal ? DivisionEngineStatus.OptimalProven : DivisionEngineStatus.FeasibleSuboptimal;

        foreach (var a in solution.Assignments)
        {
            var p = normalizedPlayers.First(x => x.PlayerId == a.PlayerId);
            var slotInfo = formation.Slots.First(s => s.SlotCode == a.Slot.SlotCode);

            bool isPrimary = PositionCompatibilityMatrix.IsPrimaryPositionMatch(p.StandardPrimaryPosition, a.Slot.SlotCode);
            bool isException = p.GrantedExceptionSlots.Contains(a.Slot.SlotCode) && !isPrimary;
            bool isSecondary = !isPrimary && !isException;

            string reason = isPrimary
                ? $"Sở trường {slotInfo.Category} ({p.RawPosition})"
                : isSecondary
                    ? $"Vị trí phụ khả dụng ({p.RawPosition})"
                    : isException
                        ? "Ngoại lệ vị trí được cấp"
                        : "Được xếp vào vị trí";
            if (!string.IsNullOrEmpty(p.LockedSlotCode))
                reason = $"Đội trưởng chỉ định {p.LockedSlotCode}";

            var assignmentDto = new SlotAssignment
            {
                SlotCode = a.Slot.SlotCode,
                SlotName = slotInfo.SlotName,
                Category = slotInfo.Category,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                KitNumber = p.KitNumber,
                IsPrimaryPosition = isPrimary,
                IsSecondaryPosition = isSecondary,
                IsPositionException = isException,
                IsLockedSide = !string.IsNullOrEmpty(p.LockedTeamSide),
                IsLockedSlot = !string.IsNullOrEmpty(p.LockedSlotCode),
                PhotoUrl = p.PhotoUrl,
                PreferredFoot = p.PreferredFoot,
                Experience = p.Experience,
                SkillScore = p.SkillScore,
                AssignmentReason = reason,
                XPercent = slotInfo.XPercent,
                YPercent = slotInfo.YPercent
            };

            if (a.Slot.TeamSide == "A")
            {
                result.TeamASlots.Add(assignmentDto);
                if (isPrimary) result.PrimaryPositionCountA++;
                if (isSecondary) result.SecondaryPositionCountA++;
            }
            else
            {
                result.TeamBSlots.Add(assignmentDto);
                if (isPrimary) result.PrimaryPositionCountB++;
                if (isSecondary) result.SecondaryPositionCountB++;
            }
        }

        // Sắp xếp slot theo thứ tự hiển thị của sơ đồ
        result.TeamASlots = result.TeamASlots.OrderBy(s => formation.Slots.First(x => x.SlotCode == s.SlotCode).DisplayOrder).ToList();
        result.TeamBSlots = result.TeamBSlots.OrderBy(s => formation.Slots.First(x => x.SlotCode == s.SlotCode).DisplayOrder).ToList();

        result.TotalPrimaryMatches = result.PrimaryPositionCountA + result.PrimaryPositionCountB;
        result.SecondaryDifference = Math.Abs(result.SecondaryPositionCountA - result.SecondaryPositionCountB);

        // Đưa danh sách dự bị vào kết quả
        foreach (var sub in explicitSubs)
        {
            result.Substitutes.Add(new SubstitutePlayerDto
            {
                PlayerId = sub.PlayerId,
                PlayerName = sub.PlayerName,
                KitNumber = sub.KitNumber,
                DesignatedSide = sub.LockedTeamSide,
                StandardPrimaryPosition = sub.StandardPrimaryPosition,
                RawPosition = sub.RawPosition,
                IsConfirmedByManager = true,
                PhotoUrl = sub.PhotoUrl,
                PreferredFoot = sub.PreferredFoot,
                Experience = sub.Experience,
                SkillScore = sub.SkillScore,
                SubstituteReason = "Dự bị chiến thuật - sẵn sàng thay người"
            });
        }

        return result;
    }

    public TeamDivisionResult SmartDivideTeams(
        List<PlayerDivisionInput> players,
        FormationDefinition formation,
        int? seed = null,
        bool autoSelectSubstitutes = true,
        int timeoutMs = 500)
    {
        int neededStarters = formation.PlayersPerSide * 2;
        var explicitSubs = players.Where(p => p.IsExplicitSubstitute).ToList();
        var mainPool = players.Where(p => !p.IsExplicitSubstitute).ToList();

        if (autoSelectSubstitutes && mainPool.Count > neededStarters)
        {
            // Tự động phân bổ dự bị dựa trên vị trí sở trường, mức độ ưu tiên và cân bằng
            var lockedPlayers = mainPool.Where(p => !string.IsNullOrEmpty(p.LockedSlotCode) || !string.IsNullOrEmpty(p.LockedTeamSide)).ToList();
            var unlockedPlayers = mainPool.Where(p => string.IsNullOrEmpty(p.LockedSlotCode) && string.IsNullOrEmpty(p.LockedTeamSide)).ToList();

            var gks = unlockedPlayers.Where(p => PositionCompatibilityMatrix.IsSlotCompatible(p.StandardPrimaryPosition, "GK")).ToList();
            var dfs = unlockedPlayers.Where(p => formation.Slots.Where(s => s.Category == "DF").Any(s => PositionCompatibilityMatrix.IsSlotCompatible(p.StandardPrimaryPosition, s.SlotCode))).Except(gks).ToList();
            var mfs = unlockedPlayers.Where(p => formation.Slots.Where(s => s.Category == "MF").Any(s => PositionCompatibilityMatrix.IsSlotCompatible(p.StandardPrimaryPosition, s.SlotCode))).Except(gks).Except(dfs).ToList();
            var fws = unlockedPlayers.Where(p => formation.Slots.Where(s => s.Category == "FW").Any(s => PositionCompatibilityMatrix.IsSlotCompatible(p.StandardPrimaryPosition, s.SlotCode))).Except(gks).Except(dfs).Except(mfs).ToList();

            int GetExpRank(string? exp) => exp switch
            {
                "Trên 5 năm" => 4,
                "3 - 5 năm" => 3,
                "1 - 3 năm" => 2,
                "Dưới 1 năm" => 1,
                _ => 0
            };

            gks = gks.OrderByDescending(p => p.SkillScore ?? 0).ThenByDescending(p => GetExpRank(p.Experience)).ThenBy(p => p.KitNumber ?? 999).ToList();
            dfs = dfs.OrderByDescending(p => p.SkillScore ?? 0).ThenByDescending(p => GetExpRank(p.Experience)).ThenBy(p => p.KitNumber ?? 999).ToList();
            mfs = mfs.OrderByDescending(p => p.SkillScore ?? 0).ThenByDescending(p => GetExpRank(p.Experience)).ThenBy(p => p.KitNumber ?? 999).ToList();
            fws = fws.OrderByDescending(p => p.SkillScore ?? 0).ThenByDescending(p => GetExpRank(p.Experience)).ThenBy(p => p.KitNumber ?? 999).ToList();

            var selectedStarters = new List<PlayerDivisionInput>(lockedPlayers);
            int neededGk = Math.Max(0, (formation.Slots.Count(s => s.Category == "GK") * 2) - selectedStarters.Count(p => PositionCompatibilityMatrix.IsSlotCompatible(p.StandardPrimaryPosition, "GK")));
            int neededDf = Math.Max(0, (formation.Slots.Count(s => s.Category == "DF") * 2) - selectedStarters.Count(p => formation.Slots.Where(s => s.Category == "DF").Any(s => PositionCompatibilityMatrix.IsSlotCompatible(p.StandardPrimaryPosition, s.SlotCode))));
            int neededMf = Math.Max(0, (formation.Slots.Count(s => s.Category == "MF") * 2) - selectedStarters.Count(p => formation.Slots.Where(s => s.Category == "MF").Any(s => PositionCompatibilityMatrix.IsSlotCompatible(p.StandardPrimaryPosition, s.SlotCode))));
            int neededFw = Math.Max(0, (formation.Slots.Count(s => s.Category == "FW") * 2) - selectedStarters.Count(p => formation.Slots.Where(s => s.Category == "FW").Any(s => PositionCompatibilityMatrix.IsSlotCompatible(p.StandardPrimaryPosition, s.SlotCode))));

            selectedStarters.AddRange(gks.Take(neededGk));
            selectedStarters.AddRange(dfs.Take(neededDf));
            selectedStarters.AddRange(mfs.Take(neededMf));
            selectedStarters.AddRange(fws.Take(neededFw));

            var remainingUnlocked = unlockedPlayers.Except(selectedStarters).OrderByDescending(p => GetExpRank(p.Experience)).ToList();
            while (selectedStarters.Count < neededStarters && remainingUnlocked.Any())
            {
                var next = remainingUnlocked.First();
                selectedStarters.Add(next);
                remainingUnlocked.RemoveAt(0);
            }

            var extraSubs = mainPool.Except(selectedStarters).Select(p => new PlayerDivisionInput
            {
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                KitNumber = p.KitNumber,
                RawPosition = p.RawPosition,
                StandardPrimaryPosition = p.StandardPrimaryPosition,
                SecondaryPositions = p.SecondaryPositions,
                IsExplicitSubstitute = true,
                LockedTeamSide = p.LockedTeamSide,
                LockedSlotCode = p.LockedSlotCode,
                GrantedExceptionSlots = p.GrantedExceptionSlots,
                PhotoUrl = p.PhotoUrl,
                PreferredFoot = p.PreferredFoot,
                Experience = p.Experience,
                SkillScore = p.SkillScore
            }).ToList();

            var combinedList = selectedStarters.Concat(explicitSubs).Concat(extraSubs).ToList();
            return DivideTeams(combinedList, formation, seed, timeoutMs);
        }

        return DivideTeams(players, formation, seed, timeoutMs);
    }

    #region Helper Classes for Solver

    private class NormalizedPlayer
    {
        public Guid PlayerId { get; }
        public string PlayerName { get; }
        public int? KitNumber { get; }
        public string RawPosition { get; }
        public string StandardPrimaryPosition { get; }
        public List<string> SecondaryPositions { get; }
        public string? LockedTeamSide { get; }
        public string? LockedSlotCode { get; }
        public List<string> GrantedExceptionSlots { get; }
        public string? PhotoUrl { get; }
        public string? PreferredFoot { get; }
        public string? Experience { get; }
        public double? SkillScore { get; }

        public NormalizedPlayer(PlayerDivisionInput p)
        {
            PlayerId = p.PlayerId;
            PlayerName = p.PlayerName;
            KitNumber = p.KitNumber;
            RawPosition = p.RawPosition;
            PhotoUrl = p.PhotoUrl;
            PreferredFoot = p.PreferredFoot;
            Experience = p.Experience;
            SkillScore = p.SkillScore;
            StandardPrimaryPosition = string.IsNullOrEmpty(p.StandardPrimaryPosition) || p.StandardPrimaryPosition == PositionCodes.UNASSIGNED
                ? PositionCompatibilityMatrix.NormalizePositionString(p.RawPosition)
                : p.StandardPrimaryPosition;

            SecondaryPositions = (p.SecondaryPositions ?? new List<string>())
                .Select(PositionCompatibilityMatrix.NormalizePositionString)
                .Where(s => s != PositionCodes.UNASSIGNED)
                .OrderBy(s => s)
                .Distinct()
                .ToList();

            LockedTeamSide = p.LockedTeamSide;
            LockedSlotCode = p.LockedSlotCode;
            GrantedExceptionSlots = (p.GrantedExceptionSlots ?? new List<string>()).OrderBy(s => s).Distinct().ToList();
        }

        public bool CanPlay(TargetSlot slot)
        {
            // 1. Kiểm tra khóa bên
            if (!string.IsNullOrEmpty(LockedTeamSide) && LockedTeamSide != slot.TeamSide)
                return false;

            // 2. Kiểm tra khóa ô vị trí
            if (!string.IsNullOrEmpty(LockedSlotCode) && LockedSlotCode != slot.SlotCode)
                return false;

            // 3. Kiểm tra tính tương thích vị trí
            return PositionCompatibilityMatrix.CanPlaySlot(
                StandardPrimaryPosition,
                SecondaryPositions,
                slot.SlotCode,
                GrantedExceptionSlots);
        }
    }

    private class TargetSlot
    {
        public string TeamSide { get; } // "A" hoặc "B"
        public string SlotCode { get; }
        public string SlotName { get; }
        public string Category { get; }
        public string FullKey => $"{TeamSide}_{SlotCode}";

        public TargetSlot(string teamSide, string slotCode, string slotName, string category)
        {
            TeamSide = teamSide;
            SlotCode = slotCode;
            SlotName = slotName;
            Category = category;
        }
    }

    private class SingleAssignment
    {
        public Guid PlayerId { get; set; }
        public TargetSlot Slot { get; set; } = null!;
    }

    private class DivisionSolution
    {
        public bool IsFeasible { get; set; }
        public List<SingleAssignment> Assignments { get; set; } = new();
        public int PrimaryMatches { get; set; }
        public int SecondaryDifference { get; set; }

        public bool IsBetterThan(DivisionSolution other)
        {
            // Ưu tiên 1: Số người đá đúng sở trường chính nhiều hơn
            if (PrimaryMatches > other.PrimaryMatches) return true;
            if (PrimaryMatches < other.PrimaryMatches) return false;

            // Ưu tiên 2: Giảm chênh lệch số người đá vị trí phụ giữa 2 bên
            return SecondaryDifference < other.SecondaryDifference;
        }
    }

    private class DivisionSolver
    {
        private readonly List<NormalizedPlayer> _players;
        private readonly List<TargetSlot> _slots;
        private readonly FormationDefinition _formation;
        private readonly int _timeoutMs;
        private readonly Random _rng;
        private readonly Stopwatch _stopwatch;

        public bool SearchTimedOut { get; private set; }
        public bool ProvenOptimal { get; private set; }

        public string? DiagnosticErrorCode { get; private set; }
        public string? DiagnosticMessage { get; private set; }
        public List<string> RemediationSuggestions { get; } = new();

        private DivisionSolution? _bestSolution;
        private long _nodesVisited = 0;

        public DivisionSolver(
            List<NormalizedPlayer> players,
            List<TargetSlot> slots,
            FormationDefinition formation,
            int timeoutMs,
            Random rng)
        {
            _players = players;
            _slots = slots;
            _formation = formation;
            _timeoutMs = timeoutMs;
            _rng = rng;
            _stopwatch = new Stopwatch();
        }

        public DivisionSolution? Solve()
        {
            _stopwatch.Start();

            // Kiểm tra phân tích chẩn đoán số lượng theo vị trí trước
            if (!PrecheckFeasibility())
            {
                return null;
            }

            // Sắp xếp thứ tự các slot: Slot có ít ứng viên phù hợp nhất (Most Constrained) xét trước
            var sortedSlots = _slots
                .OrderBy(s => _players.Count(p => p.CanPlay(s)))
                .ThenBy(s => s.FullKey)
                .ToList();

            var currentAssignments = new SingleAssignment[_slots.Count];
            var usedPlayerIds = new HashSet<Guid>();

            ProvenOptimal = false;
            SearchTimedOut = false;

            Backtrack(0, sortedSlots, currentAssignments, usedPlayerIds);

            if (_bestSolution != null && !SearchTimedOut)
            {
                ProvenOptimal = true;
            }

            return _bestSolution;
        }

        private bool PrecheckFeasibility()
        {
            // Kiểm tra số lượng thủ môn có thể bắt cho bên A và bên B
            int gkSlots = _slots.Count(s => s.SlotCode == "GK");
            var gkCandidates = _players.Where(p => _slots.Where(s => s.SlotCode == "GK").Any(p.CanPlay)).ToList();
            if (gkCandidates.Count < gkSlots)
            {
                DiagnosticErrorCode = "MissingGoalkeeper";
                DiagnosticMessage = $"Thiếu thủ môn: Cần {gkSlots} thủ môn cho 2 đội nhưng chỉ có {gkCandidates.Count} cầu thủ có thể bắt gôn.";
                RemediationSuggestions.Add("Vui lòng phân công hoặc cấp ngoại lệ bắt gôn cho ít nhất một cầu thủ khác.");
                return false;
            }

            // Kiểm tra số lượng hậu vệ
            int dfSlots = _slots.Count(s => s.Category == "DF");
            var dfCandidates = _players.Where(p => _slots.Where(s => s.Category == "DF").Any(p.CanPlay)).ToList();
            if (dfCandidates.Count < dfSlots)
            {
                DiagnosticErrorCode = "InsufficientDefenders";
                DiagnosticMessage = $"Thiếu hậu vệ: Cần {dfSlots} hậu vệ ({dfSlots / 2} người mỗi bên) nhưng chỉ có {dfCandidates.Count} cầu thủ phù hợp.";
                RemediationSuggestions.Add("Hãy bổ sung thêm hậu vệ hoặc cấp ngoại lệ cho tiền vệ đá trung vệ/hậu vệ.");
                return false;
            }

            return true;
        }

        private void Backtrack(
            int slotIndex,
            List<TargetSlot> sortedSlots,
            SingleAssignment[] currentAssignments,
            HashSet<Guid> usedPlayerIds)
        {
            _nodesVisited++;

            if (_stopwatch.ElapsedMilliseconds > _timeoutMs)
            {
                SearchTimedOut = true;
                return;
            }

            // Nếu đã gán hết tất cả các ô
            if (slotIndex == sortedSlots.Count)
            {
                var candidate = EvaluateSolution(currentAssignments);
                if (_bestSolution == null || candidate.IsBetterThan(_bestSolution))
                {
                    _bestSolution = candidate;
                }

                // Nếu đã đạt điểm số hoàn hảo tuyệt đối (toàn bộ 100% đúng sở trường chính và chênh lệch vị trí phụ = 0)
                if (_bestSolution.PrimaryMatches == sortedSlots.Count && _bestSolution.SecondaryDifference == 0)
                {
                    ProvenOptimal = true;
                }
                return;
            }

            var slot = sortedSlots[slotIndex];

            // Tìm tất cả các cầu thủ có thể đá ô này và chưa được gán
            var eligiblePlayers = _players
                .Where(p => !usedPlayerIds.Contains(p.PlayerId) && p.CanPlay(slot))
                // Ưu tiên cầu thủ đá đúng sở trường chính trước để tìm lời giải tốt nhanh hơn (Heuristic pruning)
                .OrderByDescending(p => PositionCompatibilityMatrix.IsPrimaryPositionMatch(p.StandardPrimaryPosition, slot.SlotCode))
                .ThenBy(p => p.PlayerId)
                .ToList();

            if (eligiblePlayers.Count == 0)
            {
                // Nhánh cụt
                return;
            }

            foreach (var player in eligiblePlayers)
            {
                currentAssignments[slotIndex] = new SingleAssignment
                {
                    PlayerId = player.PlayerId,
                    Slot = slot
                };
                usedPlayerIds.Add(player.PlayerId);

                Backtrack(slotIndex + 1, sortedSlots, currentAssignments, usedPlayerIds);

                usedPlayerIds.Remove(player.PlayerId);

                if (SearchTimedOut || ProvenOptimal) return;
            }
        }

        private DivisionSolution EvaluateSolution(SingleAssignment[] assignments)
        {
            int primaryCount = 0;
            int secCountA = 0;
            int secCountB = 0;

            var list = new List<SingleAssignment>();

            foreach (var a in assignments)
            {
                list.Add(new SingleAssignment { PlayerId = a.PlayerId, Slot = a.Slot });
                var p = _players.First(x => x.PlayerId == a.PlayerId);
                bool isPrimary = PositionCompatibilityMatrix.IsPrimaryPositionMatch(p.StandardPrimaryPosition, a.Slot.SlotCode);
                bool isException = p.GrantedExceptionSlots.Contains(a.Slot.SlotCode) && !isPrimary;
                bool isSecondary = !isPrimary && !isException;

                if (isPrimary) primaryCount++;
                if (isSecondary)
                {
                    if (a.Slot.TeamSide == "A") secCountA++;
                    else secCountB++;
                }
            }

            return new DivisionSolution
            {
                IsFeasible = true,
                Assignments = list,
                PrimaryMatches = primaryCount,
                SecondaryDifference = Math.Abs(secCountA - secCountB)
            };
        }
    }

    #endregion
}
