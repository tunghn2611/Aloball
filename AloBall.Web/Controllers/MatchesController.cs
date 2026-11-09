using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Controllers;

[ServiceFilter(typeof(DomainMutationFilter))]
public partial class MatchesController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITeamAuthorizationService _auth;
    private readonly IMatchmakingService _matchmaking;
    private readonly IFireKeepingService _fireKeeping;
    private readonly AloBall.Web.Services.TeamDivision.ITeamDivisionEngine _divisionEngine;
    private readonly ITeamMessagingService? _teamMessaging;
    private readonly IAttendanceCalculationEngine? _calcEngine;

    public MatchesController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ITeamAuthorizationService auth,
        IMatchmakingService matchmaking,
        IFireKeepingService fireKeeping,
        AloBall.Web.Services.TeamDivision.ITeamDivisionEngine divisionEngine,
        ITeamMessagingService? teamMessaging = null,
        IAttendanceCalculationEngine? calcEngine = null)
    {
        _db = db;
        _userManager = userManager;
        _auth = auth;
        _matchmaking = matchmaking;
        _fireKeeping = fireKeeping;
        _divisionEngine = divisionEngine;
        _teamMessaging = teamMessaging;
        _calcEngine = calcEngine;
    }

    // 1. Chi tiết trận đấu & Đội hình 2 bên (Matchday Center)
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Detail(Guid id)
    {
        var match = await _db.Matches
            .Include(m => m.TeamA).ThenInclude(t => t.Players)
            .Include(m => m.TeamB).ThenInclude(t => t!.Players)
            .Include(m => m.Lineups).ThenInclude(l => l.Player)
            .Include(m => m.PositionExceptions).ThenInclude(e => e.Player)
            .Include(m => m.GoalEvents).ThenInclude(g => g.Scorer)
            .Include(m => m.GoalEvents).ThenInclude(g => g.Assist)
            .Include(m => m.Reviews).ThenInclude(r => r.ReviewerTeam)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (match == null) return NotFound();

        var user = await _userManager.GetUserAsync(User);
        bool isManagerA = false;
        bool isManagerB = false;

        if ((!match.IsPublic || !match.TeamA.IsPublic || match.TeamA.IsDraft ||
             (match.TeamB != null && (!match.TeamB.IsPublic || match.TeamB.IsDraft))) &&
            (user == null || !(await _auth.HasMemberAccessAsync(match.TeamAId, user.Id) ||
              (match.TeamBId.HasValue && await _auth.HasMemberAccessAsync(match.TeamBId.Value, user.Id)))))
            return NotFound();

        if (user != null)
        {
            isManagerA = await _auth.HasManagerAccessAsync(match.TeamAId, user.Id);
            if (match.TeamBId.HasValue)
            {
                isManagerB = await _auth.HasManagerAccessAsync(match.TeamBId.Value, user.Id);
            }
        }

        bool memberA = user != null && await _auth.HasMemberAccessAsync(match.TeamAId, user.Id);
        bool memberB = user != null && match.TeamBId.HasValue && await _auth.HasMemberAccessAsync(match.TeamBId.Value, user.Id);

        ViewBag.IsManagerA = isManagerA;
        ViewBag.IsManagerB = isManagerB;
        ViewBag.MemberA = memberA;
        ViewBag.MemberB = memberB;
        ViewBag.CanReview = (isManagerA || isManagerB) && match.Status == MatchStatus.Completed && match.TeamBId.HasValue && match.ConfirmedByTeamA && match.ConfirmedByTeamB && match.MatchDateTime <= CompetitionRules.LocalNow;

        // 1. Tích hợp Sân bóng (PitchBookingRequests) & Phát hiện lệch lịch
        var pitchRequests = await _db.PitchBookingRequests
            .Include(p => p.PitchVenue)
            .Include(p => p.Offers)
            .Where(p => p.MatchId == match.Id)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        var confirmedPitchRequest = pitchRequests.FirstOrDefault(r => r.Status == PitchBookingStatus.Confirmed);
        var acceptedPitchOffer = confirmedPitchRequest?.Offers.FirstOrDefault(o => o.Status == PitchBookingOfferStatus.Accepted);
        bool hasPitchDiscrepancy = false;
        DateTime? confirmedPitchDateTime = null;
        if (acceptedPitchOffer != null)
        {
            confirmedPitchDateTime = acceptedPitchOffer.ConfirmedDate.Date + acceptedPitchOffer.ConfirmedStartTime;
            bool pitchNameDiff = !string.Equals(match.PitchName?.Trim(), acceptedPitchOffer.ConfirmedVenueName?.Trim(), StringComparison.OrdinalIgnoreCase);
            bool timeDiff = Math.Abs((match.MatchDateTime - confirmedPitchDateTime.Value).TotalMinutes) > 5;
            hasPitchDiscrepancy = pitchNameDiff || timeDiff;
        }

        // 2. Tích hợp Trọng tài (RefereeRequests) - Tuyệt đối không để lộ thông tin liên lạc nhạy cảm trước khi chốt
        var refereeRequests = await _db.RefereeRequests
            .Include(r => r.Proposals)
                .ThenInclude(p => p.Assignments)
                    .ThenInclude(a => a.Referee)
            .Where(r => r.MatchId == match.Id)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var confirmedRefereeRequest = refereeRequests.FirstOrDefault(r => r.Status == RefereeRequestStatus.Confirmed);
        var acceptedRefereeProposal = confirmedRefereeRequest?.Proposals.FirstOrDefault(p => p.Status == RefereeProposalStatus.Accepted);

        // 3. Tích hợp Điểm danh (RSVP) & Buổi sinh hoạt
        Guid viewerTeamId = (isManagerB || memberB) && match.TeamBId.HasValue ? match.TeamBId.Value : match.TeamAId;

        var attendanceSession = await _db.AttendanceSessions
            .Include(s => s.Records)
                .ThenInclude(r => r.Player)
            .FirstOrDefaultAsync(s => s.MatchId == match.Id && s.TeamId == viewerTeamId);

        if (attendanceSession == null && (isManagerA || memberA || isManagerB || memberB))
        {
            attendanceSession = await _db.AttendanceSessions
                .Include(s => s.Records)
                    .ThenInclude(r => r.Player)
                .FirstOrDefaultAsync(s => s.TeamId == viewerTeamId && s.MatchId == null && s.SessionDateTime.Date == match.MatchDateTime.Date);

            if (attendanceSession != null)
            {
                attendanceSession.MatchId = match.Id;
                await _db.SaveChangesAsync();
            }
            else if (isManagerA || isManagerB)
            {
                var period = match.MatchDateTime.ToString("yyyy-MM");
                if (!await CompetitionRules.IsClosedAsync(_db, viewerTeamId, period))
                {
                    attendanceSession = new AttendanceSession
                    {
                        TeamId = viewerTeamId,
                        MatchId = match.Id,
                        Title = $"Trận đấu - {match.PitchName}",
                        SessionDateTime = match.MatchDateTime,
                        ResponseDeadline = match.MatchDateTime.Date.AddHours(12),
                        StatisticalPeriod = period,
                        PitchName = match.PitchName ?? string.Empty,
                        PitchType = match.PitchType ?? "7 người",
                        Status = SessionStatus.Draft,
                        Notes = match.Notes
                    };
                    _db.AttendanceSessions.Add(attendanceSession);
                    await _db.SaveChangesAsync();

                    var teamPlayers = await _db.Players
                        .Where(p => p.TeamId == viewerTeamId && p.IsActive)
                        .ToListAsync();
                    foreach (var p in teamPlayers)
                    {
                        _db.AttendanceRecords.Add(new AttendanceRecord
                        {
                            SessionId = attendanceSession.Id,
                            PlayerId = p.Id,
                            Response = ResponseStatus.NotRecorded,
                            Actual = ActualStatus.NotChecked
                        });
                    }
                    await _db.SaveChangesAsync();
                }
            }
        }

        AttendanceRecord? myRecord = null;
        if (user != null && attendanceSession != null)
        {
            myRecord = attendanceSession.Records.FirstOrDefault(r => r.Player.LinkedUserId == user.Id);
        }

        // 4. Đánh giá độ sẵn sàng của Đội hình (Lineup Readiness)
        int requiredStartersPerSide = match.PitchType switch
        {
            "5 người" => 5,
            "11 người" => 11,
            _ => 7
        };

        bool isLineupReady = false;
        int startersCountA = 0;
        int startersCountB = 0;
        int subsCount = match.Lineups.Count(l => !l.IsStarting);

        if (match.Category == MatchCategory.Internal)
        {
            startersCountA = match.Lineups.Count(l => l.TeamSide == "A" && l.IsStarting);
            startersCountB = match.Lineups.Count(l => l.TeamSide == "B" && l.IsStarting);
            isLineupReady = startersCountA >= requiredStartersPerSide && startersCountB >= requiredStartersPerSide;
        }
        else
        {
            startersCountA = match.Lineups.Count(l => l.TeamId == match.TeamAId && l.IsStarting);
            startersCountB = match.TeamBId.HasValue ? match.Lineups.Count(l => l.TeamId == match.TeamBId.Value && l.IsStarting) : 0;

            if (isManagerB || memberB)
            {
                isLineupReady = startersCountB >= requiredStartersPerSide;
            }
            else if (isManagerA || memberA)
            {
                isLineupReady = startersCountA >= requiredStartersPerSide;
            }
            else
            {
                isLineupReady = startersCountA >= requiredStartersPerSide && (!match.TeamBId.HasValue || startersCountB >= requiredStartersPerSide);
            }
        }

        // 5. Thống kê phong độ chính thức (Chỉ từ các trận ĐÃ XÁC NHẬN)
        int teamAWins = 0;
        int teamADraws = 0;
        int teamALosses = 0;
        if (match.Status == MatchStatus.Completed)
        {
            var confirmedMatches = await _db.Matches
                .Where(m => m.Status == MatchStatus.Completed && m.ConfirmedByTeamA && (m.ConfirmedByTeamB || !m.TeamBId.HasValue) && (m.TeamAId == match.TeamAId || m.TeamBId == match.TeamAId) && m.ScoreA.HasValue && m.ScoreB.HasValue)
                .ToListAsync();

            foreach (var cm in confirmedMatches)
            {
                bool isHome = cm.TeamAId == match.TeamAId;
                int myScore = isHome ? cm.ScoreA!.Value : cm.ScoreB!.Value;
                int oppScore = isHome ? cm.ScoreB!.Value : cm.ScoreA!.Value;

                if (myScore > oppScore) teamAWins++;
                else if (myScore == oppScore) teamADraws++;
                else teamALosses++;
            }
        }

        ViewBag.PitchRequests = pitchRequests;
        ViewBag.RefereeRequests = refereeRequests;
        ViewBag.ConfirmedPitchRequest = confirmedPitchRequest;
        ViewBag.AcceptedPitchOffer = acceptedPitchOffer;
        ViewBag.HasPitchDiscrepancy = hasPitchDiscrepancy;
        ViewBag.ConfirmedPitchDateTime = confirmedPitchDateTime;
        ViewBag.ConfirmedRefereeRequest = confirmedRefereeRequest;
        ViewBag.AcceptedRefereeProposal = acceptedRefereeProposal;
        ViewBag.AttendanceSession = attendanceSession;
        ViewBag.MyRecord = myRecord;
        ViewBag.ViewerTeamId = viewerTeamId;
        ViewBag.IsLineupReady = isLineupReady;
        ViewBag.RequiredStarters = requiredStartersPerSide;
        ViewBag.StartersCountA = startersCountA;
        ViewBag.StartersCountB = startersCountB;
        ViewBag.SubsCount = subsCount;
        ViewBag.GatheringDateTime = match.MatchDateTime.AddMinutes(-30);
        ViewBag.TeamAWins = teamAWins;
        ViewBag.TeamADraws = teamADraws;
        ViewBag.TeamALosses = teamALosses;

        // Smart Match Finance Overview
        var matchSettlement = await _db.MatchSettlements
            .FirstOrDefaultAsync(s => s.MatchId == id && s.TeamId == viewerTeamId);
        var customEstimates = await _db.MatchExpenseEstimates
            .Where(e => e.MatchId == id && e.TeamId == viewerTeamId)
            .ToListAsync();
        ViewBag.MatchSettlement = matchSettlement;
        ViewBag.CustomEstimates = customEstimates;

        return View(match);
    }

    // 2. Cập nhật tỉ số trận đấu (Đề xuất & Xác nhận & Đồng bộ sổ điểm AB)
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateScore(Guid matchId, int scoreA, int scoreB, string? reason)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var match = await _db.Matches.FindAsync(matchId);
        if (match == null) return NotFound();

        bool isManagerA = await _auth.HasManagerAccessAsync(match.TeamAId, user.Id);
        bool isManagerB = match.TeamBId.HasValue && await _auth.HasManagerAccessAsync(match.TeamBId.Value, user.Id);

        if (!isManagerA && !isManagerB) return Forbid();

        if (await CompetitionRules.IsMatchClosedAsync(_db, match)) return Conflict(new { message = "Kỳ đã chốt. Hãy mở lại kỳ trong Giữ Lửa trước khi sửa." });
        if (scoreA < 0 || scoreB < 0 || scoreA > 99 || scoreB > 99 || match.MatchDateTime > CompetitionRules.LocalNow)
            return BadRequest(new { message = "Tỉ số phải từ 0–99 và chỉ được ghi nhận sau giờ thi đấu." });

        match.ScoreA = scoreA;
        match.ScoreB = scoreB;
        match.Status = MatchStatus.Completed;

        if (isManagerA)
        {
            match.ConfirmedByTeamA = true;
            match.ConfirmedByTeamB = !match.TeamBId.HasValue; // Nếu là đối ngoài thì tự chốt
        }
        else if (isManagerB)
        {
            match.ConfirmedByTeamB = true;
            match.ConfirmedByTeamA = false;
        }

        match.ConfirmedAt = null;

        if (match.ConfirmedByTeamA && match.ConfirmedByTeamB)
        {
            match.ConfirmedAt = DateTime.UtcNow;
        }

        if (!string.IsNullOrEmpty(reason))
        {
            match.ScoreChangeReason = reason;
            match.ScoreChangedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        // Đồng bộ điểm thưởng thắng / bàn thắng vào sổ AB
        await _fireKeeping.SyncMatchPointsAsync(match.Id, user.Id);

        TempData["Success"] = "Đã cập nhật tỉ số và đồng bộ điểm AB thi đua thành công!";
        return RedirectToAction(nameof(Detail), new { id = matchId });
    }

    // 2.0. Xác nhận đối chiếu tỉ số từ đội bạn
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmMatchScore(Guid matchId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var match = await _db.Matches.FindAsync(matchId);
        if (match == null) return NotFound();

        bool isManagerA = await _auth.HasManagerAccessAsync(match.TeamAId, user.Id);
        bool isManagerB = match.TeamBId.HasValue && await _auth.HasManagerAccessAsync(match.TeamBId.Value, user.Id);

        if (!isManagerA && !isManagerB) return Forbid();

        if (await CompetitionRules.IsMatchClosedAsync(_db, match)) return Conflict(new { message = "Kỳ đã chốt. Hãy mở lại kỳ trước khi xác nhận." });
        if (match.Status != MatchStatus.Completed || !match.ScoreA.HasValue || !match.ScoreB.HasValue || match.MatchDateTime > CompetitionRules.LocalNow)
            return BadRequest(new { message = "Trận chưa có kết quả hợp lệ để xác nhận." });

        if (isManagerA) match.ConfirmedByTeamA = true;
        if (isManagerB) match.ConfirmedByTeamB = true;

        if (match.ConfirmedByTeamA && (match.ConfirmedByTeamB || !match.TeamBId.HasValue))
        {
            match.ConfirmedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        await _fireKeeping.SyncMatchPointsAsync(match.Id, user.Id);

        TempData["Success"] = "Đã xác nhận tỉ số trận đấu thành công!";
        return RedirectToAction(nameof(Detail), new { id = matchId });
    }

    // 2.1. Cập nhật đội hình tham gia thi đấu thực tế của 1 đội (trận giao hữu)
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateLineup(Guid matchId, Guid teamId, List<Guid> playerIds)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var match = await _db.Matches.FindAsync(matchId);
        if (match == null) return NotFound();

        if (!await _auth.HasManagerAccessAsync(teamId, user.Id))
            return Forbid();

        if (match.Category == MatchCategory.Internal || (teamId != match.TeamAId && teamId != match.TeamBId)) return BadRequest();
        if (await CompetitionRules.IsMatchClosedAsync(_db, match)) return Conflict(new { message = "Kỳ đã chốt. Hãy mở lại kỳ trước khi sửa đội hình." });
        playerIds ??= new();
        if (await _db.Players.CountAsync(p => p.TeamId == teamId && p.IsActive && playerIds.Contains(p.Id)) != playerIds.Distinct().Count()) return BadRequest(new { message = "Cầu thủ không thuộc đội hoặc đã ngừng hoạt động." });

        // Xóa danh sách cũ của đội này trong trận
        var oldLineups = await _db.MatchPlayerLineups
            .Where(l => l.MatchId == matchId && l.TeamId == teamId)
            .ToListAsync();
        _db.MatchPlayerLineups.RemoveRange(oldLineups);

        // Thêm danh sách mới
        if (playerIds != null)
        {
            foreach (var pId in playerIds.Distinct())
            {
                _db.MatchPlayerLineups.Add(new MatchPlayerLineup
                {
                    MatchId = matchId,
                    TeamId = teamId,
                    PlayerId = pId,
                    TeamSide = (teamId == match.TeamAId ? "A" : "B"),
                    IsStarting = true
                });
            }
        }

        await _db.SaveChangesAsync();

        // Đồng bộ lại điểm thưởng thắng (chỉ người tham gia thực tế mới hưởng)
        await _fireKeeping.SyncMatchPointsAsync(matchId, user.Id);

        TempData["Success"] = "Đã cập nhật danh sách cầu thủ thi đấu thành công!";
        return RedirectToAction(nameof(Detail), new { id = matchId });
    }

    // DTOs cho chia đội tự động
    public class PreviewDivisionRequestDto
    {
        public Guid MatchId { get; set; }
        public string? FormationCode { get; set; }
        public int? Seed { get; set; }
        public List<Guid>? ParticipantPlayerIds { get; set; }
        public List<Guid>? ExplicitSubstitutePlayerIds { get; set; }
        public Dictionary<Guid, string>? LockedTeamSides { get; set; } // PlayerId -> "A"|"B"
        public Dictionary<Guid, string>? LockedSlotCodes { get; set; } // PlayerId -> "GK"|"CB_L"...
        public bool AutoSelectSubstitutes { get; set; } = false;
    }

    public class ConfirmDivisionRequestDto
    {
        public Guid MatchId { get; set; }
        public string? FormationCode { get; set; }
        public int ExpectedRevision { get; set; }
        public string? SubmissionToken { get; set; }
        public List<ConfirmSlotAssignmentDto> Assignments { get; set; } = new();
        public List<ConfirmSubstituteDto> Substitutes { get; set; } = new();
    }

    public class ConfirmSlotAssignmentDto
    {
        public Guid PlayerId { get; set; }
        public string TeamSide { get; set; } = "A"; // "A" hoặc "B"
        public string SlotCode { get; set; } = string.Empty;
    }

    public class ConfirmSubstituteDto
    {
        public Guid PlayerId { get; set; }
        public string? DesignatedSide { get; set; } // "A", "B", hoặc null
    }

    // 2.1b. Xem trước chia đội nội bộ tự động (Stateless preview)
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> PreviewAutoDivision([FromBody] PreviewDivisionRequestDto req)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var match = await _db.Matches.FindAsync(req.MatchId);
        if (match == null) return NotFound(new { success = false, message = "Trận đấu không tồn tại." });

        bool isManagerA = await _auth.HasManagerAccessAsync(match.TeamAId, user.Id);
        bool isManagerB = match.TeamBId.HasValue && await _auth.HasManagerAccessAsync(match.TeamBId.Value, user.Id);
        if (!isManagerA && !isManagerB) return Forbid();
        if (match.Status != MatchStatus.Scheduled) return BadRequest();

        var formation = AloBall.Web.Services.TeamDivision.FormationRegistry.GetByCode(req.FormationCode, match.PitchType);

        if (match.Category != MatchCategory.Internal)
        {
            var resultNonInternal = new AloBall.Web.Services.TeamDivision.TeamDivisionResult
            {
                IsSuccess = true,
                FormationCode = formation.Code,
                FormationName = formation.Name,
                PlayersPerSide = formation.PlayersPerSide
            };

            // Bảo vệ chiến thuật đối thủ: Chỉ phân tích và trả về đội của người quản lý đang gọi
            bool callerIsSideB = isManagerB && match.TeamBId.HasValue && !isManagerA;
            Guid callerTeamId = callerIsSideB ? match.TeamBId!.Value : match.TeamAId;
            string callerSide = callerIsSideB ? "B" : "A";

            var poolQuery = _db.Players.Where(p => p.TeamId == callerTeamId && p.IsActive);
            if (req.ParticipantPlayerIds != null && req.ParticipantPlayerIds.Any())
            {
                poolQuery = poolQuery.Where(p => req.ParticipantPlayerIds.Contains(p.Id));
            }
            var pList = await poolQuery.ToListAsync();

            int GetScore(Player p)
            {
                int score = 70;
                string exp = (p.Experience ?? string.Empty).ToLower();
                if (exp.Contains("chuyên nghiệp") || exp.Contains("bán chuyên") || exp.Contains("ăn tập")) score = 95;
                else if (exp.Contains("5+") || exp.Contains("> 5")) score = 88;
                else if (exp.Contains("3-5")) score = 82;
                else if (exp.Contains("1-2") || exp.Contains("1-3")) score = 76;
                return score;
            }

            var explicitSubs = req.ExplicitSubstitutePlayerIds?.ToHashSet() ?? new HashSet<Guid>();
            var eligiblePool = pList.Where(p => !explicitSubs.Contains(p.Id))
                                    .OrderByDescending(p => GetScore(p))
                                    .ToList();

            var assigned = new HashSet<Guid>();
            var targetSlots = callerIsSideB ? resultNonInternal.TeamBSlots : resultNonInternal.TeamASlots;

            // 1. Điền thủ môn GK
            var gkSlot = formation.Slots.FirstOrDefault(s => s.Category == "GK");
            if (gkSlot != null)
            {
                var gkPlayer = eligiblePool.FirstOrDefault(p => (p.StandardPrimaryPosition ?? p.Position ?? "").ToUpper().Contains("GK"))
                               ?? eligiblePool.FirstOrDefault();
                if (gkPlayer != null)
                {
                    assigned.Add(gkPlayer.Id);
                    targetSlots.Add(new AloBall.Web.Services.TeamDivision.SlotAssignment
                    {
                        SlotCode = gkSlot.SlotCode,
                        SlotName = gkSlot.SlotName,
                        Category = gkSlot.Category,
                        XPercent = gkSlot.XPercent,
                        YPercent = gkSlot.YPercent,
                        PlayerId = gkPlayer.Id,
                        PlayerName = gkPlayer.FullName,
                        KitNumber = gkPlayer.KitNumber,
                        PhotoUrl = gkPlayer.PhotoUrl,
                        PreferredFoot = gkPlayer.PreferredFoot,
                        Experience = gkPlayer.Experience,
                        IsPrimaryPosition = true,
                        AssignmentReason = "Thủ môn sở trường"
                    });
                }
            }

            // 2. Điền các vị trí theo đúng sở trường
            foreach (var slot in formation.Slots.Where(s => s.Category != "GK"))
            {
                var player = eligiblePool.FirstOrDefault(p => !assigned.Contains(p.Id) &&
                    AloBall.Web.Services.TeamDivision.PositionCompatibilityMatrix.NormalizePositionString(p.Position ?? "MF") == slot.Category);
                if (player != null)
                {
                    assigned.Add(player.Id);
                    targetSlots.Add(new AloBall.Web.Services.TeamDivision.SlotAssignment
                    {
                        SlotCode = slot.SlotCode,
                        SlotName = slot.SlotName,
                        Category = slot.Category,
                        XPercent = slot.XPercent,
                        YPercent = slot.YPercent,
                        PlayerId = player.Id,
                        PlayerName = player.FullName,
                        KitNumber = player.KitNumber,
                        PhotoUrl = player.PhotoUrl,
                        PreferredFoot = player.PreferredFoot,
                        Experience = player.Experience,
                        IsPrimaryPosition = true,
                        AssignmentReason = "Đúng vị trí sở trường"
                    });
                }
            }

            // 3. Lấp các ô còn trống bằng cầu thủ điểm cao nhất chưa xếp
            foreach (var slot in formation.Slots.Where(s => s.Category != "GK"))
            {
                if (!targetSlots.Any(ts => ts.SlotCode == slot.SlotCode))
                {
                    var player = eligiblePool.FirstOrDefault(p => !assigned.Contains(p.Id));
                    if (player != null)
                    {
                        assigned.Add(player.Id);
                        targetSlots.Add(new AloBall.Web.Services.TeamDivision.SlotAssignment
                        {
                            SlotCode = slot.SlotCode,
                            SlotName = slot.SlotName,
                            Category = slot.Category,
                            XPercent = slot.XPercent,
                            YPercent = slot.YPercent,
                            PlayerId = player.Id,
                            PlayerName = player.FullName,
                            KitNumber = player.KitNumber,
                            PhotoUrl = player.PhotoUrl,
                            PreferredFoot = player.PreferredFoot,
                            Experience = player.Experience,
                            IsPrimaryPosition = false,
                            AssignmentReason = "Phân bổ chiến thuật"
                        });
                    }
                }
            }

            // 4. Toàn bộ cầu thủ còn lại hoặc điểm thấp hơn tự động vào dự bị
            foreach (var p in pList.Where(p => !assigned.Contains(p.Id)))
            {
                resultNonInternal.Substitutes.Add(new AloBall.Web.Services.TeamDivision.SubstitutePlayerDto
                {
                    PlayerId = p.Id,
                    PlayerName = p.FullName,
                    KitNumber = p.KitNumber,
                    PhotoUrl = p.PhotoUrl,
                    PreferredFoot = p.PreferredFoot,
                    Experience = p.Experience,
                    DesignatedSide = callerSide,
                    SubstituteReason = explicitSubs.Contains(p.Id) ? "Dự bị chỉ định" : "Dự bị chiến thuật"
                });
            }

            return Json(new { success = true, data = resultNonInternal });
        }

        // Lấy danh sách cầu thủ tham gia
        List<Guid> participantIds = req.ParticipantPlayerIds?.Distinct().ToList() ?? new List<Guid>();
        if (req.ParticipantPlayerIds == null)
        {
            // Mặc định lấy từ danh sách đội hình hiện có hoặc toàn bộ active players
            var currentLineups = await _db.MatchPlayerLineups.Where(l => l.MatchId == match.Id).Select(l => l.PlayerId).ToListAsync();
            if (currentLineups.Any()) participantIds = currentLineups;
            else participantIds = await _db.Players.Where(p => p.TeamId == match.TeamAId && p.IsActive).Select(p => p.Id).ToListAsync();
        }

        var players = await _db.Players
            .Where(p => p.TeamId == match.TeamAId && participantIds.Contains(p.Id) && p.IsActive)
            .ToListAsync();

        // Lấy danh sách ngoại lệ vị trí trận đã được cấp
        var exceptions = await _db.MatchPositionExceptions
            .Where(e => e.MatchId == match.Id)
            .ToListAsync();

        var explicitSubSet = req.ExplicitSubstitutePlayerIds?.ToHashSet() ?? new HashSet<Guid>();
        var lockedSides = req.LockedTeamSides ?? new Dictionary<Guid, string>();
        var lockedSlots = req.LockedSlotCodes ?? new Dictionary<Guid, string>();

        var divisionInputs = players.Select(p =>
        {
            var pExceptions = exceptions.Where(e => e.PlayerId == p.Id).Select(e => e.AllowedSlotCode).ToList();
            List<string> secondaries = new();
            if (!string.IsNullOrEmpty(p.AllowedSecondaryPositionsJson))
            {
                try { secondaries = System.Text.Json.JsonSerializer.Deserialize<List<string>>(p.AllowedSecondaryPositionsJson) ?? new(); }
                catch { }
            }

            return new AloBall.Web.Services.TeamDivision.PlayerDivisionInput
            {
                PlayerId = p.Id,
                PlayerName = p.FullName,
                KitNumber = p.KitNumber,
                RawPosition = p.Position ?? string.Empty,
                StandardPrimaryPosition = p.StandardPrimaryPosition ?? AloBall.Web.Services.TeamDivision.PositionCodes.UNASSIGNED,
                SecondaryPositions = secondaries,
                IsExplicitSubstitute = explicitSubSet.Contains(p.Id),
                LockedTeamSide = lockedSides.TryGetValue(p.Id, out var s) ? s : null,
                LockedSlotCode = lockedSlots.TryGetValue(p.Id, out var slot) ? slot : null,
                GrantedExceptionSlots = pExceptions,
                PhotoUrl = p.PhotoUrl,
                PreferredFoot = p.PreferredFoot,
                Experience = p.Experience,
                SkillScore = null
            };
        }).ToList();

        var result = req.AutoSelectSubstitutes
            ? _divisionEngine.SmartDivideTeams(divisionInputs, formation, req.Seed, autoSelectSubstitutes: true)
            : _divisionEngine.DivideTeams(divisionInputs, formation, req.Seed);
        return Json(new { success = result.IsSuccess, data = result });
    }

    // 2.1c. Xác nhận và lưu chính thức đội hình chia 2 bên (Server-side validation, Concurrency & Atomic Transaction)
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmInternalDivision([FromBody] ConfirmDivisionRequestDto req)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        using var tx = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync() : null;
        try
        {
            var match = await _db.Matches
                .Include(m => m.PositionExceptions)
                .FirstOrDefaultAsync(m => m.Id == req.MatchId);

            if (match == null) return NotFound(new { success = false, message = "Trận đấu không tồn tại." });

            bool isManagerA = await _auth.HasManagerAccessAsync(match.TeamAId, user.Id);
            bool isManagerB = match.TeamBId.HasValue && await _auth.HasManagerAccessAsync(match.TeamBId.Value, user.Id);

            if (!isManagerA && !isManagerB)
                return Forbid();
            if (await CompetitionRules.IsMatchClosedAsync(_db, match)) return Conflict(new { message = "Kỳ đã chốt." });

            // Chặn sửa trực tiếp nếu trận đã hoàn tất hoặc bị hủy
            if (match.Status == MatchStatus.Completed || match.Status == MatchStatus.Cancelled)
            {
                return BadRequest(new { success = false, message = "Trận đấu đã hoàn thành hoặc bị hủy. Không thể thay đổi đội hình trực tiếp." });
            }

            // Kiểm tra Concurrency thực sự
            if (match.LineupRevision != req.ExpectedRevision)
            {
                return Conflict(new { success = false, message = "Đội hình đã được cập nhật bởi người khác (xung đột phiên bản). Vui lòng tải lại trang." });
            }

            var formation = AloBall.Web.Services.TeamDivision.FormationRegistry.GetByCode(req.FormationCode, match.PitchType);

            if (match.Category == MatchCategory.Internal)
            {
                if (!isManagerA) return Forbid();
                int neededPerSide = formation.PlayersPerSide;

                var sideA = req.Assignments.Where(a => a.TeamSide == "A").ToList();
                var sideB = req.Assignments.Where(a => a.TeamSide == "B").ToList();

                // Kiểm tra số lượng đá chính
                if (sideA.Count != neededPerSide || sideB.Count != neededPerSide)
                {
                    return BadRequest(new { success = false, message = $"Số lượng đá chính mỗi bên phải đúng {neededPerSide} người theo sơ đồ {formation.Name}." });
                }

                // Kiểm tra không có người trùng lặp giữa các bên và dự bị
                var allAssignedIds = req.Assignments.Select(a => a.PlayerId)
                    .Concat(req.Substitutes.Select(s => s.PlayerId))
                    .ToList();

                if (allAssignedIds.Count != allAssignedIds.Distinct().Count())
                {
                    return BadRequest(new { success = false, message = "Có cầu thủ bị xuất hiện trùng lặp trong đội hình hoặc dự bị." });
                }

                // Kiểm tra tenancy của toàn bộ cầu thủ
                var validPlayers = await _db.Players
                    .Where(p => p.TeamId == match.TeamAId && allAssignedIds.Contains(p.Id) && p.IsActive)
                    .ToDictionaryAsync(p => p.Id);

                if (validPlayers.Count != allAssignedIds.Count)
                {
                    return BadRequest(new { success = false, message = "Một số cầu thủ không hợp lệ hoặc không thuộc đội bóng." });
                }

                // Kiểm tra tính tương thích vị trí từng người theo PositionCompatibilityMatrix
                foreach (var a in req.Assignments)
                {
                    var p = validPlayers[a.PlayerId];
                    var pExceptions = match.PositionExceptions
                        .Where(e => e.PlayerId == p.Id)
                        .Select(e => e.AllowedSlotCode)
                        .ToList();

                    List<string> secondaries = new();
                    if (!string.IsNullOrEmpty(p.AllowedSecondaryPositionsJson))
                    {
                        try { secondaries = System.Text.Json.JsonSerializer.Deserialize<List<string>>(p.AllowedSecondaryPositionsJson) ?? new(); }
                        catch { }
                    }

                    string stdPrimary = p.StandardPrimaryPosition ?? AloBall.Web.Services.TeamDivision.PositionCompatibilityMatrix.NormalizePositionString(p.Position);
                    bool canPlay = AloBall.Web.Services.TeamDivision.PositionCompatibilityMatrix.CanPlaySlot(
                        stdPrimary,
                        secondaries,
                        a.SlotCode,
                        pExceptions);

                    if (!canPlay)
                    {
                        return BadRequest(new { success = false, message = $"Cầu thủ {p.FullName} không tương thích với vị trí {a.SlotCode} và chưa được cấp ngoại lệ vị trí trận." });
                    }
                }

                // Xóa lineups cũ
                var oldLineups = await _db.MatchPlayerLineups.Where(l => l.MatchId == match.Id).ToListAsync();
                _db.MatchPlayerLineups.RemoveRange(oldLineups);

                // Thêm đá chính Bên A và Bên B
                foreach (var a in req.Assignments)
                {
                    var p = validPlayers[a.PlayerId];
                    _db.MatchPlayerLineups.Add(new MatchPlayerLineup
                    {
                        MatchId = match.Id,
                        TeamId = match.TeamAId,
                        PlayerId = a.PlayerId,
                        TeamSide = a.TeamSide,
                        IsStarting = true,
                        AssignedSlotCode = a.SlotCode,
                        AssignedPosition = a.SlotCode,
                        AssignedKitNumber = p.KitNumber,
                        HasActuallyPlayed = false
                    });
                }

                // Thêm dự bị đã xác nhận
                foreach (var s in req.Substitutes)
                {
                    var p = validPlayers[s.PlayerId];
                    _db.MatchPlayerLineups.Add(new MatchPlayerLineup
                    {
                        MatchId = match.Id,
                        TeamId = match.TeamAId,
                        PlayerId = s.PlayerId,
                        TeamSide = s.DesignatedSide, // "A", "B", hoặc null (dự bị chung)
                        IsStarting = false,
                        AssignedSlotCode = null,
                        AssignedPosition = "Dự bị",
                        AssignedKitNumber = p.KitNumber,
                        HasActuallyPlayed = false
                    });
                }

                // Cập nhật thông tin sơ đồ và tăng Revision
                match.FormationCode = formation.Code;
                match.LineupRevision += 1;
            }
            else
            {
                // Trận Giao hữu hoặc Giải đấu
                if (isManagerA)
                {
                    var sideA = req.Assignments.Where(a => a.TeamSide == "A" || a.TeamSide == null).ToList();
                    var subsA = req.Substitutes.Where(s => s.DesignatedSide == "A" || s.DesignatedSide == null).ToList();
                    var allIdsA = sideA.Select(a => a.PlayerId).Concat(subsA.Select(s => s.PlayerId)).Distinct().ToList();

                    var validTeamAPlayers = await _db.Players
                        .Where(p => p.TeamId == match.TeamAId && allIdsA.Contains(p.Id) && p.IsActive)
                        .ToDictionaryAsync(p => p.Id);

                    var oldLineupsA = await _db.MatchPlayerLineups
                        .Where(l => l.MatchId == match.Id && l.TeamId == match.TeamAId)
                        .ToListAsync();
                    _db.MatchPlayerLineups.RemoveRange(oldLineupsA);

                    foreach (var a in sideA)
                    {
                        if (validTeamAPlayers.TryGetValue(a.PlayerId, out var p))
                        {
                            _db.MatchPlayerLineups.Add(new MatchPlayerLineup
                            {
                                MatchId = match.Id,
                                TeamId = match.TeamAId,
                                PlayerId = a.PlayerId,
                                TeamSide = "A",
                                IsStarting = true,
                                AssignedSlotCode = a.SlotCode,
                                AssignedPosition = a.SlotCode,
                                AssignedKitNumber = p.KitNumber,
                                HasActuallyPlayed = false
                            });
                        }
                    }

                    foreach (var s in subsA)
                    {
                        if (validTeamAPlayers.TryGetValue(s.PlayerId, out var p))
                        {
                            _db.MatchPlayerLineups.Add(new MatchPlayerLineup
                            {
                                MatchId = match.Id,
                                TeamId = match.TeamAId,
                                PlayerId = s.PlayerId,
                                TeamSide = "A",
                                IsStarting = false,
                                AssignedSlotCode = null,
                                AssignedPosition = "Dự bị",
                                AssignedKitNumber = p.KitNumber,
                                HasActuallyPlayed = false
                            });
                        }
                    }
                }

                if (isManagerB && match.TeamBId.HasValue)
                {
                    var sideB = req.Assignments.Where(a => a.TeamSide == "B").ToList();
                    var subsB = req.Substitutes.Where(s => s.DesignatedSide == "B").ToList();
                    var allIdsB = sideB.Select(b => b.PlayerId).Concat(subsB.Select(s => s.PlayerId)).Distinct().ToList();

                    var validTeamBPlayers = await _db.Players
                        .Where(p => p.TeamId == match.TeamBId.Value && allIdsB.Contains(p.Id) && p.IsActive)
                        .ToDictionaryAsync(p => p.Id);

                    var oldLineupsB = await _db.MatchPlayerLineups
                        .Where(l => l.MatchId == match.Id && l.TeamId == match.TeamBId.Value)
                        .ToListAsync();
                    _db.MatchPlayerLineups.RemoveRange(oldLineupsB);

                    foreach (var b in sideB)
                    {
                        if (validTeamBPlayers.TryGetValue(b.PlayerId, out var p))
                        {
                            _db.MatchPlayerLineups.Add(new MatchPlayerLineup
                            {
                                MatchId = match.Id,
                                TeamId = match.TeamBId.Value,
                                PlayerId = b.PlayerId,
                                TeamSide = "B",
                                IsStarting = true,
                                AssignedSlotCode = b.SlotCode,
                                AssignedPosition = b.SlotCode,
                                AssignedKitNumber = p.KitNumber,
                                HasActuallyPlayed = false
                            });
                        }
                    }

                    foreach (var s in subsB)
                    {
                        if (validTeamBPlayers.TryGetValue(s.PlayerId, out var p))
                        {
                            _db.MatchPlayerLineups.Add(new MatchPlayerLineup
                            {
                                MatchId = match.Id,
                                TeamId = match.TeamBId.Value,
                                PlayerId = s.PlayerId,
                                TeamSide = "B",
                                IsStarting = false,
                                AssignedSlotCode = null,
                                AssignedPosition = "Dự bị",
                                AssignedKitNumber = p.KitNumber,
                                HasActuallyPlayed = false
                            });
                        }
                    }
                }

                match.FormationCode = req.FormationCode;
                match.LineupRevision += 1;
            }

            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();

            return Json(new { success = true, newRevision = match.LineupRevision, message = "Đã lưu đội hình chia đội thành công!" });
        }
        catch (Exception ex)
        {
            if (tx != null) await tx.RollbackAsync();
            return StatusCode(500, new { success = false, message = "Lỗi khi lưu đội hình: " + ex.Message });
        }
    }

    // 2.1d. Cấp ngoại lệ vị trí trận (MatchPositionException)
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GrantPositionException(Guid matchId, Guid playerId, string slotCode, string? reason)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var match = await _db.Matches.FindAsync(matchId);
        if (match == null) return NotFound(new { success = false, message = "Trận đấu không tồn tại." });

        if (!await _auth.HasManagerAccessAsync(match.TeamAId, user.Id))
            return Forbid();
        if (match.Category != MatchCategory.Internal || match.Status != MatchStatus.Scheduled || await CompetitionRules.IsMatchClosedAsync(_db, match)) return BadRequest();
        if (!await _db.Players.AnyAsync(p => p.Id == playerId && p.TeamId == match.TeamAId && p.IsActive)) return BadRequest();

        var exists = await _db.MatchPositionExceptions
            .AnyAsync(e => e.MatchId == matchId && e.PlayerId == playerId && e.AllowedSlotCode == slotCode);

        if (!exists)
        {
            _db.MatchPositionExceptions.Add(new MatchPositionException
            {
                MatchId = matchId,
                PlayerId = playerId,
                AllowedSlotCode = slotCode,
                GrantedByUserId = user.Id,
                Reason = string.IsNullOrWhiteSpace(reason) ? "Đội trưởng chỉ định đá trái sở trường trong trận" : reason.Trim(),
                GrantedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        return Json(new { success = true, message = "Đã cấp ngoại lệ vị trí thành công!" });
    }

    // 2.1e. Thu hồi ngoại lệ vị trí trận
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokePositionException(Guid matchId, Guid playerId, string slotCode)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var match = await _db.Matches.FindAsync(matchId);
        if (match == null) return NotFound(new { success = false, message = "Trận đấu không tồn tại." });

        if (!await _auth.HasManagerAccessAsync(match.TeamAId, user.Id))
            return Forbid();
        if (match.Category != MatchCategory.Internal || match.Status != MatchStatus.Scheduled || await CompetitionRules.IsMatchClosedAsync(_db, match)) return BadRequest();

        var ex = await _db.MatchPositionExceptions
            .FirstOrDefaultAsync(e => e.MatchId == matchId && e.PlayerId == playerId && e.AllowedSlotCode == slotCode);

        if (ex != null)
        {
            _db.MatchPositionExceptions.Remove(ex);
            await _db.SaveChangesAsync();
        }

        return Json(new { success = true, message = "Đã thu hồi ngoại lệ vị trí." });
    }

    // 2.1f. Nạp đội hình từ trận trước trong cùng buổi sinh hoạt (Tạo bản sao độc lập, không sửa ngược lịch sử)
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CopyLineupFromPreviousMatch(Guid currentMatchId, Guid sourceMatchId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        using var tx = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync() : null;
        try
        {
            var targetMatch = await _db.Matches.FindAsync(currentMatchId);
            var sourceMatch = await _db.Matches.Include(m => m.Lineups).FirstOrDefaultAsync(m => m.Id == sourceMatchId);

            if (targetMatch == null || sourceMatch == null)
                return NotFound(new { success = false, message = "Không tìm thấy trận đấu." });

            if (!await _auth.HasManagerAccessAsync(targetMatch.TeamAId, user.Id))
                return Forbid();

            if (targetMatch.Category != MatchCategory.Internal || sourceMatch.Category != MatchCategory.Internal || sourceMatch.TeamAId != targetMatch.TeamAId || targetMatch.Status != MatchStatus.Scheduled) return BadRequest();
            if (await CompetitionRules.IsMatchClosedAsync(_db, targetMatch)) return Conflict(new { message = "Kỳ đã chốt." });
            // Xóa lineups cũ của targetMatch
            var oldLineups = await _db.MatchPlayerLineups.Where(l => l.MatchId == targetMatch.Id).ToListAsync();
            _db.MatchPlayerLineups.RemoveRange(oldLineups);

            // Sao chép sâu các bản ghi lineup
            foreach (var l in sourceMatch.Lineups)
            {
                _db.MatchPlayerLineups.Add(new MatchPlayerLineup
                {
                    MatchId = targetMatch.Id,
                    TeamId = l.TeamId,
                    PlayerId = l.PlayerId,
                    TeamSide = l.TeamSide,
                    IsStarting = l.IsStarting,
                    AssignedPosition = l.AssignedPosition,
                    AssignedSlotCode = l.AssignedSlotCode,
                    AssignedKitNumber = l.AssignedKitNumber,
                    HasActuallyPlayed = false
                });
            }

            targetMatch.FormationCode = sourceMatch.FormationCode;
            targetMatch.LineupRevision += 1;

            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();

            return Json(new { success = true, message = "Đã nạp đội hình từ trận trước thành công!" });
        }
        catch (Exception ex)
        {
            if (tx != null) await tx.RollbackAsync();
            return StatusCode(500, new { success = false, message = "Lỗi sao chép: " + ex.Message });
        }
    }

    // 2.1g. Cập nhật chia 2 bên A/B (giữ tương thích form cũ nếu có)
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateInternalLineup(Guid matchId, List<Guid> sideAPlayerIds, List<Guid> sideBPlayerIds)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var match = await _db.Matches.FindAsync(matchId);
        if (match == null) return NotFound();

        if (!await _auth.HasManagerAccessAsync(match.TeamAId, user.Id))
            return Forbid();

        if (match.Category != MatchCategory.Internal || match.Status == MatchStatus.Cancelled) return BadRequest();
        if (await CompetitionRules.IsMatchClosedAsync(_db, match)) return Conflict(new { message = "Kỳ đã chốt. Hãy mở lại kỳ trước khi sửa đội hình." });
        sideAPlayerIds ??= new();
        sideBPlayerIds ??= new();
        var selectedIds = sideAPlayerIds.Concat(sideBPlayerIds).ToList();
        if (selectedIds.Distinct().Count() != selectedIds.Count || await _db.Players.CountAsync(p => p.TeamId == match.TeamAId && p.IsActive && selectedIds.Contains(p.Id)) != selectedIds.Count)
            return BadRequest(new { message = "Mỗi cầu thủ của đội chỉ được xếp vào một bên." });

        // Xóa lineups cũ của trận
        var oldLineups = await _db.MatchPlayerLineups
            .Where(l => l.MatchId == matchId)
            .ToListAsync();
        _db.MatchPlayerLineups.RemoveRange(oldLineups);

        sideAPlayerIds ??= new List<Guid>();
        sideBPlayerIds ??= new List<Guid>();

        // Thêm cầu thủ bên A
        foreach (var pId in sideAPlayerIds.Distinct())
        {
            _db.MatchPlayerLineups.Add(new MatchPlayerLineup
            {
                MatchId = matchId,
                TeamId = match.TeamAId,
                PlayerId = pId,
                TeamSide = "A",
                IsStarting = true,
                HasActuallyPlayed = false
            });
        }

        // Thêm cầu thủ bên B (nếu trùng bên A thì bỏ qua)
        foreach (var pId in sideBPlayerIds.Distinct())
        {
            if (sideAPlayerIds.Contains(pId)) continue;

            _db.MatchPlayerLineups.Add(new MatchPlayerLineup
            {
                MatchId = matchId,
                TeamId = match.TeamAId,
                PlayerId = pId,
                TeamSide = "B",
                IsStarting = true,
                HasActuallyPlayed = false
            });
        }

        match.LineupRevision += 1;
        await _db.SaveChangesAsync();

        await _fireKeeping.SyncMatchPointsAsync(match.Id, user.Id);

        TempData["Success"] = "Đã cập nhật chia đội 2 bên A và B thành công!";
        return RedirectToAction(nameof(Detail), new { id = matchId });
    }

    // 2.2. Thêm sự kiện bàn thắng / kiến tạo
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddGoalEvent(Guid matchId, Guid teamId, Guid scorerPlayerId, Guid? assistPlayerId, int? minute, string? description)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var match = await _db.Matches.FindAsync(matchId);
        if (match == null) return NotFound();

        if (!await _auth.HasManagerAccessAsync(teamId, user.Id))
            return Forbid();

        if (teamId != match.TeamAId && teamId != match.TeamBId) return BadRequest();
        if (await CompetitionRules.IsMatchClosedAsync(_db, match)) return Conflict(new { message = "Kỳ đã chốt. Hãy mở lại kỳ trước khi sửa bàn thắng." });
        if (match.Status == MatchStatus.Cancelled || minute is < 0 or > 180 || scorerPlayerId == assistPlayerId) return BadRequest();
        if (!await _db.Players.AnyAsync(p => p.Id == scorerPlayerId && p.TeamId == teamId && p.IsActive) ||
            (assistPlayerId.HasValue && !await _db.Players.AnyAsync(p => p.Id == assistPlayerId && p.TeamId == teamId && p.IsActive)))
            return BadRequest(new { message = "Người ghi bàn và kiến tạo phải thuộc đội thi đấu." });

        var goal = new MatchGoalEvent
        {
            MatchId = matchId,
            TeamId = teamId,
            ScorerPlayerId = scorerPlayerId,
            AssistPlayerId = assistPlayerId,
            Minute = minute,
            Description = description,
            Revision = 1
        };

        _db.MatchGoalEvents.Add(goal);
        await _db.SaveChangesAsync();

        // Đồng bộ điểm bàn thắng/kiến tạo vào sổ AB
        await _fireKeeping.SyncMatchPointsAsync(matchId, user.Id);

        TempData["Success"] = "Đã ghi nhận bàn thắng và cộng điểm AB tương ứng!";
        return RedirectToAction(nameof(Detail), new { id = matchId });
    }

    // 2.3. Xóa sự kiện bàn thắng (Thu hồi điểm an toàn qua bút toán đảo)
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteGoalEvent(Guid goalId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var goal = await _db.MatchGoalEvents.FindAsync(goalId);
        if (goal == null) return NotFound();

        if (!await _auth.HasManagerAccessAsync(goal.TeamId, user.Id))
            return Forbid();

        var match = await _db.Matches.FindAsync(goal.MatchId);
        if (match == null || (goal.TeamId != match.TeamAId && goal.TeamId != match.TeamBId)) return BadRequest();
        if (await CompetitionRules.IsMatchClosedAsync(_db, match)) return Conflict(new { message = "Kỳ đã chốt. Hãy mở lại kỳ trước khi xóa bàn thắng." });

        Guid matchId = goal.MatchId;
        _db.MatchGoalEvents.Remove(goal);
        await _db.SaveChangesAsync();

        // Đồng bộ sổ AB (sẽ đảo điểm của bàn thắng này)
        await _fireKeeping.SyncMatchPointsAsync(matchId, user.Id);

        TempData["Success"] = "Đã xóa bàn thắng và thu hồi điểm AB tương ứng.";
        return RedirectToAction(nameof(Detail), new { id = matchId });
    }

    // 3. Đánh giá đối thủ sau trận (Có kiểm tra 1 sao -> Blacklist)
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitReview(Guid matchId, Guid reviewerTeamId, int stars, string comment)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var result = await _matchmaking.SubmitReviewAsync(matchId, reviewerTeamId, user.Id, stars, comment);

        if (result.Success)
        {
            TempData["Success"] = result.Message;
        }
        else
        {
            TempData["Error"] = result.Message;
        }

        return RedirectToAction(nameof(Detail), new { id = matchId });
    }
}
