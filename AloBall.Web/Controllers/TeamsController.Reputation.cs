using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Controllers;

public partial class TeamsController
{
    // Cập nhật thống kê uy tín và hồ sơ cầu thủ công khai
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> PlayerPublicProfile(Guid id)
    {
        var player = await _db.Players
            .Include(p => p.Team)
            .Include(p => p.LinkedUser)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (player == null) return NotFound("Không tìm thấy thông tin cầu thủ.");

        var currentUser = User != null ? await _userManager.GetUserAsync(User) : null;
        string? currentUserId = currentUser?.Id;
        bool isSelf = currentUserId != null && player.LinkedUserId == currentUserId;
        bool isManager = currentUserId != null && await _auth.HasManagerAccessAsync(player.TeamId, currentUserId);
        bool isPlatformAdmin = currentUserId != null && await _auth.IsPlatformAdminAsync(currentUserId);

        // Bảo vệ quyền riêng tư: nếu hồ sơ không công khai và không phải chính chủ/manager/admin -> 404
        if (!player.IsPublicProfile && !isSelf && !isManager && !isPlatformAdmin)
        {
            return NotFound("Hồ sơ cầu thủ này đang được cài đặt ở chế độ riêng tư.");
        }

        // Lấy danh sách các trận đấu chính thức đã hoàn tất và được xác nhận
        var officialMatches = await _db.Matches
            .Include(m => m.TeamA)
            .Include(m => m.TeamB)
            .Include(m => m.Lineups)
            .Include(m => m.GoalEvents)
            .Where(m => (m.TeamAId == player.TeamId || m.TeamBId == player.TeamId)
                && m.Status == MatchStatus.Completed
                && m.ConfirmedByTeamA
                && (m.ConfirmedByTeamB || !m.TeamBId.HasValue)
                && m.ScoreA.HasValue && m.ScoreB.HasValue)
            .OrderByDescending(m => m.MatchDateTime)
            .ToListAsync();

        var officialMatchIds = officialMatches.Select(m => m.Id).ToHashSet();

        var playerLineups = await _db.MatchPlayerLineups
            .Where(l => l.PlayerId == player.Id && officialMatchIds.Contains(l.MatchId))
            .ToListAsync();

        var matchSessions = await _db.AttendanceSessions
            .Include(s => s.Records)
            .Where(s => s.TeamId == player.TeamId && s.MatchId.HasValue && officialMatchIds.Contains(s.MatchId.Value))
            .ToListAsync();

        var goalEvents = await _db.MatchGoalEvents
            .Where(g => officialMatchIds.Contains(g.MatchId) && g.TeamId == player.TeamId
                && (g.ScorerPlayerId == player.Id || g.AssistPlayerId == player.Id))
            .ToListAsync();

        int officialGoals = goalEvents.Count(g => g.ScorerPlayerId == player.Id);
        int officialAssists = goalEvents.Count(g => g.AssistPlayerId == player.Id);

        // Số trận ra sân chính thức (distinct theo MatchId để tránh đếm trùng)
        var appearanceMatchIds = new HashSet<Guid>();
        foreach (var lineup in playerLineups)
        {
            if (lineup.HasActuallyPlayed || lineup.IsStarting)
            {
                appearanceMatchIds.Add(lineup.MatchId);
            }
        }
        foreach (var session in matchSessions)
        {
            if (session.MatchId.HasValue && !appearanceMatchIds.Contains(session.MatchId.Value))
            {
                var rec = session.Records.FirstOrDefault(r => r.PlayerId == player.Id);
                if (rec != null && (rec.Actual == ActualStatus.Present || rec.IsExempt))
                {
                    appearanceMatchIds.Add(session.MatchId.Value);
                }
            }
        }
        int officialAppearances = appearanceMatchIds.Count;

        // Tỉ lệ chuyên cần từ AttendanceRecords
        var attendanceRecords = await _db.AttendanceRecords
            .Include(r => r.Session)
            .Where(r => r.PlayerId == player.Id && r.Session.TeamId == player.TeamId && r.Actual != ActualStatus.NotChecked)
            .ToListAsync();

        int totalSessions = attendanceRecords.Count;
        int attendedCount = attendanceRecords.Count(r => r.Actual == ActualStatus.Present || r.IsExempt);
        double attendanceRate = totalSessions > 0 ? Math.Round((double)attendedCount / totalSessions * 100, 1) : 0;

        // Đóng góp trong các trận gần nhất
        var recentContributions = new List<PlayerMatchContributionItem>();
        foreach (var m in officialMatches.Take(10))
        {
            var lineup = playerLineups.FirstOrDefault(l => l.MatchId == m.Id);
            bool hasPlayed = appearanceMatchIds.Contains(m.Id);
            int goals = goalEvents.Count(g => g.MatchId == m.Id && g.ScorerPlayerId == player.Id);
            int assists = goalEvents.Count(g => g.MatchId == m.Id && g.AssistPlayerId == player.Id);

            var opponentName = m.TeamAId == player.TeamId ? (m.TeamB?.Name ?? m.TeamBExternalName ?? "Đối thủ") : m.TeamA.Name;
            int myScore = m.TeamAId == player.TeamId ? (m.ScoreA ?? 0) : (m.ScoreB ?? 0);
            int oppScore = m.TeamAId == player.TeamId ? (m.ScoreB ?? 0) : (m.ScoreA ?? 0);

            if (hasPlayed || goals > 0 || assists > 0)
            {
                recentContributions.Add(new PlayerMatchContributionItem
                {
                    MatchId = m.Id,
                    MatchDateTime = m.MatchDateTime,
                    OpponentName = opponentName,
                    PitchName = m.PitchName ?? "Chưa rõ sân",
                    TeamScore = myScore,
                    OpponentScore = oppScore,
                    HasActuallyPlayed = lineup?.HasActuallyPlayed ?? hasPlayed,
                    IsStarting = lineup?.IsStarting ?? false,
                    GoalsInMatch = goals,
                    AssistsInMatch = assists
                });
            }
        }

        var model = new PlayerPublicProfileViewModel
        {
            PlayerId = player.Id,
            TeamId = player.TeamId,
            TeamName = player.Team?.Name ?? "Đội bóng",
            TeamSlug = player.Team?.Slug ?? "",
            TeamLogoUrl = player.Team?.LogoUrl,
            CanShowTeam = player.Team != null && player.Team.IsPublic && !player.Team.IsDraft,

            DisplayName = player.FullName,
            PhotoUrl = player.PhotoUrl,
            ShowPhoto = player.PublicPhoto,
            KitNumber = player.KitNumber,
            ShowKitNumber = player.PublicKitNumber,
            Position = player.Position,
            ShowPosition = player.PublicPosition,
            PreferredFoot = player.PreferredFoot,
            ShowPreferredFoot = player.PublicPreferredFoot,
            Experience = player.Experience,
            ShowExperience = player.PublicExperience,
            Region = player.Region,
            HeightCm = player.HeightCm,
            WeightKg = player.WeightKg,
            ShowHeightWeight = player.PublicHeightWeight,
            IsLinkedUser = !string.IsNullOrEmpty(player.LinkedUserId),

            IsPublicProfile = player.IsPublicProfile,
            PublicKitNumber = player.PublicKitNumber,
            PublicPosition = player.PublicPosition,
            PublicHeightWeight = player.PublicHeightWeight,
            PublicPhoto = player.PublicPhoto,
            PublicPreferredFoot = player.PublicPreferredFoot,
            PublicExperience = player.PublicExperience,

            OfficialAppearances = officialAppearances,
            OfficialGoals = officialGoals,
            OfficialAssists = officialAssists,
            AttendanceRatePercentage = attendanceRate,
            TotalAttendanceSessions = totalSessions,
            AttendedSessions = attendedCount,

            RecentMatches = recentContributions,
            IsSelf = isSelf,
            IsManager = isManager
        };

        return View(model);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePlayerPrivacy(PlayerPrivacyUpdateInput input)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser == null) return Challenge();

        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == input.PlayerId);
        if (player == null) return NotFound("Không tìm thấy thông tin cầu thủ.");

        // IDOR Protection: Chỉ chính chủ cầu thủ (linked user) hoặc Manager của đội (hoặc Admin) mới có quyền cập nhật quyền riêng tư
        bool isOwner = player.LinkedUserId == currentUser.Id;
        bool isManager = await _auth.HasManagerAccessAsync(player.TeamId, currentUser.Id);
        bool isPlatformAdmin = await _auth.IsPlatformAdminAsync(currentUser.Id);

        if (!isOwner && !isManager && !isPlatformAdmin)
        {
            return Forbid();
        }

        player.IsPublicProfile = input.IsPublicProfile;
        player.PublicKitNumber = input.PublicKitNumber;
        player.PublicPosition = input.PublicPosition;
        player.PublicHeightWeight = input.PublicHeightWeight;
        player.PublicPhoto = input.PublicPhoto;
        player.PublicPreferredFoot = input.PublicPreferredFoot;
        player.PublicExperience = input.PublicExperience;

        await _db.SaveChangesAsync();

        TempData["Success"] = "Đã cập nhật cài đặt quyền riêng tư hồ sơ cầu thủ thành công!";
        return RedirectToAction(nameof(PlayerPublicProfile), new { id = player.Id });
    }
}
