using System.Net;
using System.Security.Claims;
using AloBall.Web.Controllers;
using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using AloBall.Web.Services.TeamDivision;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AloBall.Tests;

public class SmartTacticalBoardTests
{
    private readonly ITeamDivisionEngine _engine = new TeamDivisionEngine();

    [Fact]
    public void AllTenFormations_HaveCorrectSlotCountsAndOutfieldGKCalculations()
    {
        // Kiểm tra đúng 10 presets theo yêu cầu nghiệp vụ
        var presets = FormationRegistry.AllFormations;
        Assert.Equal(10, presets.Count);

        // 1. Sân 5 (3 presets: 1-2-1, 2-1-1, 2-2)
        var s5_121 = FormationRegistry.GetByCode("5_121");
        Assert.Equal(5, s5_121.PlayersPerSide);
        Assert.Equal(5, s5_121.Slots.Count);
        Assert.Equal(1, s5_121.Slots.Count(s => s.Category == "GK"));
        Assert.Equal(4, s5_121.Slots.Count(s => s.Category != "GK")); // 1 DF, 2 MF, 1 FW

        var s5_211 = FormationRegistry.GetByCode("5_211");
        Assert.Equal(5, s5_211.PlayersPerSide);
        Assert.Equal(1, s5_211.Slots.Count(s => s.Category == "GK"));
        Assert.Equal(4, s5_211.Slots.Count(s => s.Category != "GK")); // 2 DF, 1 MF, 1 FW

        var s5_22 = FormationRegistry.GetByCode("5_22");
        Assert.Equal(5, s5_22.PlayersPerSide);
        Assert.Equal(1, s5_22.Slots.Count(s => s.Category == "GK"));
        Assert.Equal(4, s5_22.Slots.Count(s => s.Category != "GK")); // 2 DF, 2 FW

        // 2. Sân 7 (3 presets: 2-3-1, 3-2-1, 2-1-2-1)
        var s7_231 = FormationRegistry.GetByCode("7_231");
        Assert.Equal(7, s7_231.PlayersPerSide);
        Assert.Equal(1, s7_231.Slots.Count(s => s.Category == "GK"));
        Assert.Equal(6, s7_231.Slots.Count(s => s.Category != "GK")); // 2 DF, 3 MF, 1 FW

        var s7_321 = FormationRegistry.GetByCode("7_321");
        Assert.Equal(7, s7_321.PlayersPerSide);
        Assert.Equal(1, s7_321.Slots.Count(s => s.Category == "GK"));
        Assert.Equal(6, s7_321.Slots.Count(s => s.Category != "GK")); // 3 DF, 2 MF, 1 FW

        var s7_2121 = FormationRegistry.GetByCode("7_2121");
        Assert.Equal(7, s7_2121.PlayersPerSide);
        Assert.Equal(1, s7_2121.Slots.Count(s => s.Category == "GK"));
        Assert.Equal(6, s7_2121.Slots.Count(s => s.Category != "GK")); // 2 DF, 1 DM, 2 AM, 1 FW

        // 3. Sân 11 (4 presets: 4-3-3, 4-4-2, 4-2-3-1, 3-5-2)
        var s11_433 = FormationRegistry.GetByCode("11_433");
        Assert.Equal(11, s11_433.PlayersPerSide);
        Assert.Equal(1, s11_433.Slots.Count(s => s.Category == "GK"));
        Assert.Equal(10, s11_433.Slots.Count(s => s.Category != "GK")); // 4 DF, 3 MF, 3 FW

        var s11_442 = FormationRegistry.GetByCode("11_442");
        Assert.Equal(11, s11_442.PlayersPerSide);
        Assert.Equal(1, s11_442.Slots.Count(s => s.Category == "GK"));
        Assert.Equal(10, s11_442.Slots.Count(s => s.Category != "GK")); // 4 DF, 4 MF, 2 FW

        var s11_4231 = FormationRegistry.GetByCode("11_4231");
        Assert.Equal(11, s11_4231.PlayersPerSide);
        Assert.Equal(1, s11_4231.Slots.Count(s => s.Category == "GK"));
        Assert.Equal(10, s11_4231.Slots.Count(s => s.Category != "GK")); // 4 DF, 5 MF, 1 ST

        var s11_352 = FormationRegistry.GetByCode("11_352");
        Assert.Equal(11, s11_352.PlayersPerSide);
        Assert.Equal(1, s11_352.Slots.Count(s => s.Category == "GK"));
        Assert.Equal(10, s11_352.Slots.Count(s => s.Category != "GK")); // 3 DF, 5 MF, 2 FW

        // Toàn bộ các slot phải có tọa độ XPercent, YPercent hợp lệ (0..100)
        foreach (var formation in presets)
        {
            foreach (var slot in formation.Slots)
            {
                Assert.InRange(slot.XPercent, 0, 100);
                Assert.InRange(slot.YPercent, 0, 100);
                Assert.Contains(slot.Category, new[] { "GK", "DF", "MF", "FW" });
            }
        }
    }

    [Fact]
    public void PositionCompatibilityMatrix_SupportsAllRequiredPositionCodes()
    {
        // FW codes: LW, RW, ST, CF
        Assert.True(PositionCompatibilityMatrix.IsSlotCompatible(PositionCodes.LW, "LW"));
        Assert.True(PositionCompatibilityMatrix.IsSlotCompatible(PositionCodes.RW, "RW"));
        Assert.True(PositionCompatibilityMatrix.IsSlotCompatible(PositionCodes.ST, "ST"));
        Assert.True(PositionCompatibilityMatrix.IsSlotCompatible(PositionCodes.CF, "ST"));

        // MF codes: LM, RM, DM, AM, CDM, RDM, LDM, RAM, LAM, CM
        Assert.True(PositionCompatibilityMatrix.IsSlotCompatible(PositionCodes.LM, "LM"));
        Assert.True(PositionCompatibilityMatrix.IsSlotCompatible(PositionCodes.RM, "RM"));
        Assert.True(PositionCompatibilityMatrix.IsSlotCompatible(PositionCodes.DM, "CDM"));
        Assert.True(PositionCompatibilityMatrix.IsSlotCompatible(PositionCodes.AM, "CAM"));
        Assert.True(PositionCompatibilityMatrix.IsSlotCompatible(PositionCodes.CDM, "CDM"));
        Assert.True(PositionCompatibilityMatrix.IsSlotCompatible(PositionCodes.RDM, "RDM"));
        Assert.True(PositionCompatibilityMatrix.IsSlotCompatible(PositionCodes.LDM, "LDM"));
        Assert.True(PositionCompatibilityMatrix.IsSlotCompatible(PositionCodes.RAM, "RAM"));
        Assert.True(PositionCompatibilityMatrix.IsSlotCompatible(PositionCodes.LAM, "LAM"));
        Assert.True(PositionCompatibilityMatrix.IsSlotCompatible(PositionCodes.CM, "CM"));

        // DF codes: CB, LB, RB, LWB, RWB
        Assert.True(PositionCompatibilityMatrix.IsSlotCompatible(PositionCodes.CB, "CB"));
        Assert.True(PositionCompatibilityMatrix.IsSlotCompatible(PositionCodes.LB, "LB"));
        Assert.True(PositionCompatibilityMatrix.IsSlotCompatible(PositionCodes.RB, "RB"));
        Assert.True(PositionCompatibilityMatrix.IsSlotCompatible(PositionCodes.LWB, "LWB"));
        Assert.True(PositionCompatibilityMatrix.IsSlotCompatible(PositionCodes.RWB, "RWB"));

        // GK code: GK
        Assert.True(PositionCompatibilityMatrix.IsSlotCompatible(PositionCodes.GK, "GK"));
        Assert.False(PositionCompatibilityMatrix.IsSlotCompatible(PositionCodes.ST, "GK"));
        Assert.False(PositionCompatibilityMatrix.IsSlotCompatible(PositionCodes.GK, "ST"));
    }

    [Fact]
    public void SmartAutoAssignment_HandlesExcessPlayers_PlacesThemInSubstitutesWithReasons()
    {
        // 18 cầu thủ cho trận sân 7 (cần đúng 14 đá chính cho 2 bên, dư 4 người)
        var players = new List<PlayerDivisionInput>
        {
            new() { PlayerId = Guid.NewGuid(), PlayerName = "GK 1", StandardPrimaryPosition = PositionCodes.GK, RawPosition = "Thủ môn", Experience = "Trên 5 năm" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "GK 2", StandardPrimaryPosition = PositionCodes.GK, RawPosition = "Thủ môn", Experience = "3 - 5 năm" },

            new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 1", StandardPrimaryPosition = PositionCodes.CB, RawPosition = "Trung vệ", Experience = "Trên 5 năm" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 2", StandardPrimaryPosition = PositionCodes.CB, RawPosition = "Trung vệ", Experience = "3 - 5 năm" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 3", StandardPrimaryPosition = PositionCodes.LB, RawPosition = "Hậu vệ trái", Experience = "3 - 5 năm" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 4", StandardPrimaryPosition = PositionCodes.RB, RawPosition = "Hậu vệ phải", Experience = "1 - 3 năm" },

            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 1", StandardPrimaryPosition = PositionCodes.CM, RawPosition = "Tiền vệ giữa", Experience = "Trên 5 năm" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 2", StandardPrimaryPosition = PositionCodes.CM, RawPosition = "Tiền vệ giữa", Experience = "3 - 5 năm" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 3", StandardPrimaryPosition = PositionCodes.LM, RawPosition = "Tiền vệ trái", Experience = "3 - 5 năm" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 4", StandardPrimaryPosition = PositionCodes.RM, RawPosition = "Tiền vệ phải", Experience = "3 - 5 năm" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 5", StandardPrimaryPosition = PositionCodes.CDM, RawPosition = "Tiền vệ phòng ngự", Experience = "1 - 3 năm" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 6", StandardPrimaryPosition = PositionCodes.CAM, RawPosition = "Tiền vệ tấn công", Experience = "1 - 3 năm" },

            new() { PlayerId = Guid.NewGuid(), PlayerName = "FW 1", StandardPrimaryPosition = PositionCodes.ST, RawPosition = "Tiền đạo cắm", Experience = "Trên 5 năm" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "FW 2", StandardPrimaryPosition = PositionCodes.ST, RawPosition = "Tiền đạo cắm", Experience = "3 - 5 năm" },

            // 4 cầu thủ thừa
            new() { PlayerId = Guid.NewGuid(), PlayerName = "Sub MF 1", StandardPrimaryPosition = PositionCodes.CM, RawPosition = "Tiền vệ", Experience = "Dưới 1 năm" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "Sub MF 2", StandardPrimaryPosition = PositionCodes.CM, RawPosition = "Tiền vệ", Experience = "Dưới 1 năm" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "Sub DF 1", StandardPrimaryPosition = PositionCodes.CB, RawPosition = "Trung vệ", Experience = "Dưới 1 năm" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "Sub FW 1", StandardPrimaryPosition = PositionCodes.ST, RawPosition = "Tiền đạo", Experience = "Dưới 1 năm" }
        };

        var result = _engine.SmartDivideTeams(players, FormationRegistry.S7_231, seed: 42, autoSelectSubstitutes: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.TeamASlots.Count);
        Assert.Equal(7, result.TeamBSlots.Count);
        Assert.Equal(4, result.Substitutes.Count);

        // Kiểm tra không có cầu thủ trùng lặp giữa đá chính và dự bị
        var allIds = result.TeamASlots.Select(s => s.PlayerId)
            .Concat(result.TeamBSlots.Select(s => s.PlayerId))
            .Concat(result.Substitutes.Select(b => b.PlayerId))
            .ToList();
        Assert.Equal(18, allIds.Distinct().Count());

        // Kiểm tra mọi slot đá chính đều có giải thích vị trí
        foreach (var slot in result.TeamASlots.Concat(result.TeamBSlots))
        {
            Assert.False(string.IsNullOrWhiteSpace(slot.AssignmentReason));
        }

        // Kiểm tra dự bị đều có lý do
        foreach (var sub in result.Substitutes)
        {
            Assert.False(string.IsNullOrWhiteSpace(sub.SubstituteReason));
        }
    }

    [Fact]
    public void SmartAutoAssignment_BalancesGKCoverageBetweenTeamAAndTeamB()
    {
        // 14 người với đúng 2 GK
        var players = new List<PlayerDivisionInput>
        {
            new() { PlayerId = Guid.NewGuid(), PlayerName = "GK 1", StandardPrimaryPosition = PositionCodes.GK, RawPosition = "Thủ môn" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "GK 2", StandardPrimaryPosition = PositionCodes.GK, RawPosition = "Thủ môn" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 1", StandardPrimaryPosition = PositionCodes.CB, RawPosition = "Trung vệ" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 2", StandardPrimaryPosition = PositionCodes.CB, RawPosition = "Trung vệ" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 3", StandardPrimaryPosition = PositionCodes.LB, RawPosition = "Hậu vệ trái" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 4", StandardPrimaryPosition = PositionCodes.RB, RawPosition = "Hậu vệ phải" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 1", StandardPrimaryPosition = PositionCodes.CM, RawPosition = "Tiền vệ" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 2", StandardPrimaryPosition = PositionCodes.CM, RawPosition = "Tiền vệ" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 3", StandardPrimaryPosition = PositionCodes.LM, RawPosition = "Tiền vệ trái" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 4", StandardPrimaryPosition = PositionCodes.RM, RawPosition = "Tiền vệ phải" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 5", StandardPrimaryPosition = PositionCodes.CDM, RawPosition = "Tiền vệ phòng ngự" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 6", StandardPrimaryPosition = PositionCodes.CAM, RawPosition = "Tiền vệ tấn công" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "FW 1", StandardPrimaryPosition = PositionCodes.ST, RawPosition = "Tiền đạo" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "FW 2", StandardPrimaryPosition = PositionCodes.ST, RawPosition = "Tiền đạo" }
        };

        var result = _engine.SmartDivideTeams(players, FormationRegistry.S7_231, seed: 100);

        Assert.True(result.IsSuccess);
        // Mỗi bên phải có đúng 1 thủ môn
        Assert.Single(result.TeamASlots, s => s.SlotCode == "GK");
        Assert.Single(result.TeamBSlots, s => s.SlotCode == "GK");
    }

    [Fact]
    public async Task ConfirmInternalDivision_RejectsDuplicatePlayersAcrossStartersAndSubstitutes()
    {
        using var app = new RequirementsWebFactory();
        await app.Seed();

        var match = Guid.NewGuid();
        var teamId = app.TeamA;
        var p1 = app.PlayerA;
        var p2 = app.PlayerB;

        await app.WithDb(async db =>
        {
            db.Matches.Add(new MatchGame
            {
                Id = match,
                TeamAId = teamId,
                Category = MatchCategory.Internal,
                Status = MatchStatus.Scheduled,
                LineupRevision = 1,
                MatchDateTime = DateTime.Today.AddDays(1)
            });
            await db.SaveChangesAsync();
        });

        using var client = app.Client(); // Owner của TeamA
        var payload = new MatchesController.ConfirmDivisionRequestDto
        {
            MatchId = match,
            FormationCode = "7_231",
            ExpectedRevision = 1,
            Assignments = new List<MatchesController.ConfirmSlotAssignmentDto>
            {
                // Cố tình đưa cùng 1 cầu thủ p1 vào 2 vị trí khác nhau
                new() { PlayerId = p1, TeamSide = "A", SlotCode = "GK" },
                new() { PlayerId = p1, TeamSide = "A", SlotCode = "CB_L" }
            },
            Substitutes = new List<MatchesController.ConfirmSubstituteDto>()
        };

        var response = await PostJsonAsync(app, client, "/Matches/ConfirmInternalDivision", payload);
        // Bị từ chối do trùng lặp cầu thủ
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmInternalDivision_IDORProtection_RejectsPlayerFromAnotherTeam()
    {
        using var app = new RequirementsWebFactory();
        await app.Seed();

        var match = Guid.NewGuid();
        var teamA = app.TeamA;
        var teamB = app.TeamB;
        var foreignPlayer = Guid.NewGuid();

        await app.WithDb(async db =>
        {
            // Cầu thủ thuộc team B
            db.Players.Add(new Player { Id = foreignPlayer, TeamId = teamB, FullName = "Cầu thủ đội khác", IsActive = true });
            db.Matches.Add(new MatchGame
            {
                Id = match,
                TeamAId = teamA,
                Category = MatchCategory.Internal,
                Status = MatchStatus.Scheduled,
                LineupRevision = 1,
                MatchDateTime = DateTime.Today.AddDays(1)
            });
            await db.SaveChangesAsync();
        });

        using var client = app.Client();
        var payload = new MatchesController.ConfirmDivisionRequestDto
        {
            MatchId = match,
            FormationCode = "7_231",
            ExpectedRevision = 1,
            Assignments = new List<MatchesController.ConfirmSlotAssignmentDto>
            {
                new() { PlayerId = foreignPlayer, TeamSide = "A", SlotCode = "GK" }
            },
            Substitutes = new List<MatchesController.ConfirmSubstituteDto>()
        };

        var response = await PostJsonAsync(app, client, "/Matches/ConfirmInternalDivision", payload);
        // IDOR: Không được xếp cầu thủ của đội khác vào trận nội bộ
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmInternalDivision_UnauthorizedUser_ReturnsForbidden()
    {
        using var app = new RequirementsWebFactory();
        await app.Seed();

        var match = Guid.NewGuid();
        await app.WithDb(async db =>
        {
            db.Matches.Add(new MatchGame
            {
                Id = match,
                TeamAId = app.TeamA,
                Category = MatchCategory.Internal,
                Status = MatchStatus.Scheduled,
                LineupRevision = 1,
                MatchDateTime = DateTime.Today.AddDays(1)
            });
            await db.SaveChangesAsync();
        });

        // Người dùng outsider (không có quyền quản lý Team A)
        using var outsider = app.Client("outsider");
        var payload = new MatchesController.ConfirmDivisionRequestDto
        {
            MatchId = match,
            FormationCode = "7_231",
            ExpectedRevision = 1,
            Assignments = new List<MatchesController.ConfirmSlotAssignmentDto>(),
            Substitutes = new List<MatchesController.ConfirmSubstituteDto>()
        };

        var response = await PostJsonAsync(app, outsider, "/Matches/ConfirmInternalDivision", payload);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TacticalBoard_AlwaysVisible_OnFriendlyMatches_WithProminentButton()
    {
        using var app = new RequirementsWebFactory();
        await app.Seed();

        var friendlyMatch = Guid.NewGuid();
        var session = Guid.NewGuid();
        var tomorrow = DateTime.Today.AddDays(1);

        await app.WithDb(async db =>
        {
            db.Matches.Add(new MatchGame
            {
                Id = friendlyMatch,
                TeamAId = app.TeamA,
                TeamBId = app.TeamB,
                Category = MatchCategory.Friendly,
                Status = MatchStatus.Scheduled,
                PitchType = "7 người",
                PitchName = "Sân bóng PVV",
                MatchDateTime = tomorrow
            });
            db.AttendanceSessions.Add(new AttendanceSession
            {
                Id = session,
                TeamId = app.TeamA,
                MatchId = friendlyMatch,
                Title = "Trận giao hữu",
                SessionDateTime = tomorrow,
                StatisticalPeriod = tomorrow.ToString("yyyy-MM")
            });
            await db.SaveChangesAsync();
        });

        using var client = app.Client("owner");
        var html = await client.GetStringAsync($"/Matches/Detail/{friendlyMatch}");

        // Sa bàn FIFA luôn hiển thị trên trang chi tiết trận đấu giao hữu
        Assert.Contains("id=\"smartTacticalBoard\"", html);
        // Có nút sắp xếp đội hình nổi bật cạnh nhắc nhở
        Assert.Contains("Sắp xếp đội hình (Sa bàn FIFA)", html);
        Assert.Contains("href=\"#smartTacticalBoard\"", html);
    }

    [Fact]
    public async Task ConfirmDivision_FriendlyMatch_AllowsManagerToSaveLineup()
    {
        using var app = new RequirementsWebFactory();
        await app.Seed();

        var friendlyMatch = Guid.NewGuid();
        var tomorrow = DateTime.Today.AddDays(1);

        await app.WithDb(async db =>
        {
            db.Matches.Add(new MatchGame
            {
                Id = friendlyMatch,
                TeamAId = app.TeamA,
                TeamBId = app.TeamB,
                Category = MatchCategory.Friendly,
                Status = MatchStatus.Scheduled,
                PitchType = "7 người",
                PitchName = "Sân bóng PVV",
                MatchDateTime = tomorrow,
                LineupRevision = 0
            });
            await db.SaveChangesAsync();
        });

        using var client = app.Client("owner"); // Manager của TeamA
        var payload = new MatchesController.ConfirmDivisionRequestDto
        {
            MatchId = friendlyMatch,
            FormationCode = "7_231",
            ExpectedRevision = 0,
            Assignments = new List<MatchesController.ConfirmSlotAssignmentDto>
            {
                new() { PlayerId = app.PlayerA, TeamSide = "A", SlotCode = "GK" }
            },
            Substitutes = new List<MatchesController.ConfirmSubstituteDto>()
        };

        var response = await PostJsonAsync(app, client, "/Matches/ConfirmInternalDivision", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Kiểm tra database đã lưu lineup cho TeamA
        await app.WithDb(async db =>
        {
            var lineup = await db.MatchPlayerLineups.FirstOrDefaultAsync(l => l.MatchId == friendlyMatch && l.PlayerId == app.PlayerA);
            Assert.NotNull(lineup);
            Assert.True(lineup.IsStarting);
            Assert.Equal("GK", lineup.AssignedSlotCode);
        });
    }

    private static async Task<HttpResponseMessage> PostJsonAsync<T>(RequirementsWebFactory app, HttpClient client, string path, T payload)
    {
        var html = await client.GetStringAsync("/Teams");
        var tokenMatch = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        var token = tokenMatch.Success ? System.Net.WebUtility.HtmlDecode(tokenMatch.Groups[1].Value) : "";

        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Add("RequestVerificationToken", token);
        }
        request.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }
}
