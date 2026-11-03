using AloBall.Web.Services.TeamDivision;
using Xunit;

namespace AloBall.Tests;

public class TeamDivisionEngineTests
{
    private readonly ITeamDivisionEngine _engine = new TeamDivisionEngine();

    [Fact]
    public void Standard7Players_CreatesTwoBalancedTeams_AllStartersAssigned()
    {
        // 14 cầu thủ với các vị trí chuẩn sân 7: 2 GK, 4 DF, 6 MF, 2 FW
        var players = new List<PlayerDivisionInput>
        {
            new() { PlayerId = Guid.NewGuid(), PlayerName = "GK 1", RawPosition = "Thủ môn" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "GK 2", RawPosition = "Thủ môn" },

            new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 1", RawPosition = "Hậu vệ cánh trái" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 2", RawPosition = "Hậu vệ cánh phải" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 3", RawPosition = "Trung vệ" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 4", RawPosition = "Trung vệ" },

            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 1", RawPosition = "Tiền vệ trung tâm" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 2", RawPosition = "Tiền vệ trung tâm" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 3", RawPosition = "Tiền vệ cánh trái" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 4", RawPosition = "Tiền vệ cánh phải" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 5", RawPosition = "Tiền vệ phòng ngự" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 6", RawPosition = "Tiền vệ tấn công" },

            new() { PlayerId = Guid.NewGuid(), PlayerName = "FW 1", RawPosition = "Tiền đạo cắm" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "FW 2", RawPosition = "Tiền đạo cắm" },
        };

        var result = _engine.DivideTeams(players, FormationRegistry.S7_231, seed: 12345);

        Assert.True(result.IsSuccess);
        Assert.Equal(DivisionEngineStatus.OptimalProven, result.Status);
        Assert.Equal(7, result.TeamASlots.Count);
        Assert.Equal(7, result.TeamBSlots.Count);
        Assert.Empty(result.Substitutes);

        // Mỗi đội phải có đúng 1 Thủ môn ở ô GK
        Assert.Equal("GK", result.TeamASlots.First(s => s.SlotCode == "GK").SlotCode);
        Assert.Equal("GK", result.TeamBSlots.First(s => s.SlotCode == "GK").SlotCode);

        // Tất cả 14 người phải khác nhau
        var allIds = result.TeamASlots.Select(s => s.PlayerId).Concat(result.TeamBSlots.Select(s => s.PlayerId)).ToList();
        Assert.Equal(14, allIds.Distinct().Count());
    }

    [Fact]
    public void MissingGoalkeeper_FailsWithExactDiagnostic()
    {
        // 14 người nhưng chỉ có 1 GK
        var players = new List<PlayerDivisionInput>
        {
            new() { PlayerId = Guid.NewGuid(), PlayerName = "GK 1", RawPosition = "Thủ môn" },
            // Thiếu GK 2, thay bằng tiền vệ
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF Extra", RawPosition = "Tiền vệ" },

            new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 1", RawPosition = "Hậu vệ" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 2", RawPosition = "Hậu vệ" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 3", RawPosition = "Trung vệ" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 4", RawPosition = "Trung vệ" },

            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 1", RawPosition = "Tiền vệ" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 2", RawPosition = "Tiền vệ" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 3", RawPosition = "Tiền vệ" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 4", RawPosition = "Tiền vệ" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 5", RawPosition = "Tiền vệ" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 6", RawPosition = "Tiền vệ" },

            new() { PlayerId = Guid.NewGuid(), PlayerName = "FW 1", RawPosition = "Tiền đạo" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "FW 2", RawPosition = "Tiền đạo" },
        };

        var result = _engine.DivideTeams(players, FormationRegistry.S7_231);

        Assert.False(result.IsSuccess);
        Assert.Equal("MissingGoalkeeper", result.DiagnosticErrorCode);
        Assert.Contains("Thiếu thủ môn", result.DiagnosticMessage);
        Assert.NotEmpty(result.RemediationSuggestions);
    }

    [Fact]
    public void ExtraPlayers_WithoutConfirmedSubstitutes_FailsWithUnconfirmedSubstitutes()
    {
        // 15 người tham gia (thừa 1 người so với 14) nhưng không ai được đánh dấu IsExplicitSubstitute
        var players = Enumerable.Range(1, 15).Select(i => new PlayerDivisionInput
        {
            PlayerId = Guid.NewGuid(),
            PlayerName = $"Cầu thủ {i}",
            RawPosition = "Tiền vệ"
        }).ToList();

        var result = _engine.DivideTeams(players, FormationRegistry.S7_231);

        Assert.False(result.IsSuccess);
        Assert.Equal("UnconfirmedSubstitutes", result.DiagnosticErrorCode);
        Assert.Contains("vượt quá sức chứa sơ đồ", result.DiagnosticMessage);
    }

    [Fact]
    public void ExtraPlayers_WithConfirmedSubstitutes_PlacesExtraInSubstitutesList()
    {
        // 16 người tham gia: 2 người được chỉ định dự bị, 14 người đá chính
        var sub1 = new PlayerDivisionInput { PlayerId = Guid.NewGuid(), PlayerName = "Dự bị 1", RawPosition = "Tiền đạo", IsExplicitSubstitute = true };
        var sub2 = new PlayerDivisionInput { PlayerId = Guid.NewGuid(), PlayerName = "Dự bị 2", RawPosition = "Hậu vệ", IsExplicitSubstitute = true, LockedTeamSide = "A" };

        var starters = new List<PlayerDivisionInput>
        {
            new() { PlayerId = Guid.NewGuid(), PlayerName = "GK 1", RawPosition = "Thủ môn" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "GK 2", RawPosition = "Thủ môn" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 1", RawPosition = "Trung vệ" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 2", RawPosition = "Trung vệ" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 3", RawPosition = "Hậu vệ cánh trái" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 4", RawPosition = "Hậu vệ cánh phải" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 1", RawPosition = "Tiền vệ" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 2", RawPosition = "Tiền vệ" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 3", RawPosition = "Tiền vệ" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 4", RawPosition = "Tiền vệ" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 5", RawPosition = "Tiền vệ" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 6", RawPosition = "Tiền vệ" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "FW 1", RawPosition = "Tiền đạo" },
            new() { PlayerId = Guid.NewGuid(), PlayerName = "FW 2", RawPosition = "Tiền đạo" },
        };

        var allPlayers = starters.Concat(new[] { sub1, sub2 }).ToList();

        var result = _engine.DivideTeams(allPlayers, FormationRegistry.S7_231, seed: 100);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.TeamASlots.Count);
        Assert.Equal(7, result.TeamBSlots.Count);
        Assert.Equal(2, result.Substitutes.Count);
        Assert.Contains(result.Substitutes, s => s.PlayerId == sub1.PlayerId);
        Assert.Contains(result.Substitutes, s => s.PlayerId == sub2.PlayerId && s.DesignatedSide == "A");
    }

    [Fact]
    public void LockedPosition_DoesNotBypassEligibility_FailsWhenIncompatible()
    {
        // Cố tình khóa 1 tiền đạo vào ô GK mà không có ngoại lệ
        var striker = new PlayerDivisionInput
        {
            PlayerId = Guid.NewGuid(),
            PlayerName = "Tiền đạo A",
            RawPosition = "Tiền đạo cắm",
            LockedSlotCode = "GK"
        };

        var players = new List<PlayerDivisionInput> { striker };
        // Bổ sung thêm cho đủ 14
        for (int i = 2; i <= 14; i++)
        {
            players.Add(new PlayerDivisionInput { PlayerId = Guid.NewGuid(), PlayerName = $"P {i}", RawPosition = "Tiền vệ" });
        }

        var result = _engine.DivideTeams(players, FormationRegistry.S7_231);

        Assert.False(result.IsSuccess);
        Assert.Equal("LockPositionMismatch", result.DiagnosticErrorCode);
        Assert.Contains("không thuộc sở trường", result.DiagnosticMessage);
    }

    [Fact]
    public void LockedPosition_WithGrantedException_Succeeds()
    {
        // Tiền đạo A được cấp ngoại lệ vị trí GK cho trận này
        var striker = new PlayerDivisionInput
        {
            PlayerId = Guid.NewGuid(),
            PlayerName = "Tiền đạo A",
            RawPosition = "Tiền đạo cắm",
            LockedSlotCode = "GK",
            GrantedExceptionSlots = new List<string> { "GK" }
        };

        var gk2 = new PlayerDivisionInput { PlayerId = Guid.NewGuid(), PlayerName = "GK Thật", RawPosition = "Thủ môn" };

        var players = new List<PlayerDivisionInput> { striker, gk2 };
        // Thêm 4 DF, 6 MF, 2 FW
        players.Add(new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 1", RawPosition = "Trung vệ" });
        players.Add(new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 2", RawPosition = "Trung vệ" });
        players.Add(new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 3", RawPosition = "Hậu vệ trái" });
        players.Add(new() { PlayerId = Guid.NewGuid(), PlayerName = "DF 4", RawPosition = "Hậu vệ phải" });
        players.Add(new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 1", RawPosition = "Tiền vệ" });
        players.Add(new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 2", RawPosition = "Tiền vệ" });
        players.Add(new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 3", RawPosition = "Tiền vệ" });
        players.Add(new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 4", RawPosition = "Tiền vệ" });
        players.Add(new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 5", RawPosition = "Tiền vệ" });
        players.Add(new() { PlayerId = Guid.NewGuid(), PlayerName = "MF 6", RawPosition = "Tiền vệ" });
        players.Add(new() { PlayerId = Guid.NewGuid(), PlayerName = "FW 1", RawPosition = "Tiền đạo" });
        players.Add(new() { PlayerId = Guid.NewGuid(), PlayerName = "FW 2", RawPosition = "Tiền đạo" });

        var result = _engine.DivideTeams(players, FormationRegistry.S7_231);

        Assert.True(result.IsSuccess);
        // Striker đã được xếp vào ô GK thành công nhờ exception
        var allSlots = result.TeamASlots.Concat(result.TeamBSlots);
        var strikerAssignment = allSlots.First(s => s.PlayerId == striker.PlayerId);
        Assert.Equal("GK", strikerAssignment.SlotCode);
        Assert.True(strikerAssignment.IsPositionException);
    }

    [Fact]
    public void DeterministicOutput_IdenticalInputsAndSeed_ProduceSameResultRegardlessOfInputOrder()
    {
        var p1 = new PlayerDivisionInput { PlayerId = Guid.Parse("11111111-1111-1111-1111-111111111111"), PlayerName = "GK A", RawPosition = "Thủ môn" };
        var p2 = new PlayerDivisionInput { PlayerId = Guid.Parse("22222222-2222-2222-2222-222222222222"), PlayerName = "GK B", RawPosition = "Thủ môn" };
        var p3 = new PlayerDivisionInput { PlayerId = Guid.Parse("33333333-3333-3333-3333-333333333333"), PlayerName = "DF 1", RawPosition = "Trung vệ" };
        var p4 = new PlayerDivisionInput { PlayerId = Guid.Parse("44444444-4444-4444-4444-444444444444"), PlayerName = "DF 2", RawPosition = "Trung vệ" };
        var p5 = new PlayerDivisionInput { PlayerId = Guid.Parse("55555555-5555-5555-5555-555555555555"), PlayerName = "DF 3", RawPosition = "Hậu vệ trái" };
        var p6 = new PlayerDivisionInput { PlayerId = Guid.Parse("66666666-6666-6666-6666-666666666666"), PlayerName = "DF 4", RawPosition = "Hậu vệ phải" };
        var p7 = new PlayerDivisionInput { PlayerId = Guid.Parse("77777777-7777-7777-7777-777777777777"), PlayerName = "MF 1", RawPosition = "Tiền vệ" };
        var p8 = new PlayerDivisionInput { PlayerId = Guid.Parse("88888888-8888-8888-8888-888888888888"), PlayerName = "MF 2", RawPosition = "Tiền vệ" };
        var p9 = new PlayerDivisionInput { PlayerId = Guid.Parse("99999999-9999-9999-9999-999999999999"), PlayerName = "MF 3", RawPosition = "Tiền vệ" };
        var p10 = new PlayerDivisionInput { PlayerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), PlayerName = "MF 4", RawPosition = "Tiền vệ" };
        var p11 = new PlayerDivisionInput { PlayerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), PlayerName = "MF 5", RawPosition = "Tiền vệ" };
        var p12 = new PlayerDivisionInput { PlayerId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), PlayerName = "MF 6", RawPosition = "Tiền vệ" };
        var p13 = new PlayerDivisionInput { PlayerId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), PlayerName = "FW 1", RawPosition = "Tiền đạo" };
        var p14 = new PlayerDivisionInput { PlayerId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), PlayerName = "FW 2", RawPosition = "Tiền đạo" };

        var listOrder1 = new List<PlayerDivisionInput> { p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p13, p14 };
        var listOrder2 = new List<PlayerDivisionInput> { p14, p10, p2, p5, p1, p12, p8, p3, p7, p11, p4, p13, p6, p9 };

        int testSeed = 999;
        var res1 = _engine.DivideTeams(listOrder1, FormationRegistry.S7_231, seed: testSeed);
        var res2 = _engine.DivideTeams(listOrder2, FormationRegistry.S7_231, seed: testSeed);

        Assert.True(res1.IsSuccess);
        Assert.True(res2.IsSuccess);

        // Kiểm tra kết quả 2 lần chạy phải giống nhau tuyệt đối
        for (int i = 0; i < 7; i++)
        {
            Assert.Equal(res1.TeamASlots[i].PlayerId, res2.TeamASlots[i].PlayerId);
            Assert.Equal(res1.TeamASlots[i].SlotCode, res2.TeamASlots[i].SlotCode);

            Assert.Equal(res1.TeamBSlots[i].PlayerId, res2.TeamBSlots[i].PlayerId);
            Assert.Equal(res1.TeamBSlots[i].SlotCode, res2.TeamBSlots[i].SlotCode);
        }
    }
}
