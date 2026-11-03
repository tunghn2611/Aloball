using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AloBall.Tests;

public class DbInitializerSeedTests
{
    [Fact]
    public async Task SeedDemoDataAsync_PopulatesMultipleTeamsAndFullRosters()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: $"AloBall_SeedTest_{Guid.NewGuid()}"));

        services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();

        using (var scope = serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // 1. Chạy SeedDemoDataAsync lần đầu
            await DbInitializer.SeedDemoDataAsync(serviceProvider, db);

            // Kiểm tra có đủ 5 đội bóng
            var teams = await db.Teams.ToListAsync();
            Assert.True(teams.Count >= 5, $"Expected at least 5 teams, but got {teams.Count}");

            var klb = teams.First(t => t.Slug == "klb-fc");
            var saoVang = teams.First(t => t.Slug == "sao-vang-fc");
            var phoenix = teams.First(t => t.Slug == "phoenix-fc");
            var trangAn = teams.First(t => t.Slug == "trang-an-fc");
            var bachKhoa = teams.First(t => t.Slug == "bach-khoa-fc");

            // Kiểm tra quân số từng đội (tối thiểu 14 cầu thủ để test chia đội và đá giải)
            var klbPlayers = await db.Players.Where(p => p.TeamId == klb.Id).ToListAsync();
            var saoVangPlayers = await db.Players.Where(p => p.TeamId == saoVang.Id).ToListAsync();
            var phoenixPlayers = await db.Players.Where(p => p.TeamId == phoenix.Id).ToListAsync();
            var trangAnPlayers = await db.Players.Where(p => p.TeamId == trangAn.Id).ToListAsync();
            var bachKhoaPlayers = await db.Players.Where(p => p.TeamId == bachKhoa.Id).ToListAsync();

            Assert.True(klbPlayers.Count >= 16, $"KLB should have at least 16 players, got {klbPlayers.Count}");
            Assert.True(saoVangPlayers.Count >= 15, $"Sao Vang should have at least 15 players, got {saoVangPlayers.Count}");
            Assert.True(phoenixPlayers.Count >= 15, $"Phoenix should have at least 15 players, got {phoenixPlayers.Count}");
            Assert.True(trangAnPlayers.Count >= 14, $"Trang An should have at least 14 players, got {trangAnPlayers.Count}");
            Assert.True(bachKhoaPlayers.Count >= 14, $"Bach Khoa should have at least 14 players, got {bachKhoaPlayers.Count}");

            // Kiểm tra các cầu thủ đều có StandardPrimaryPosition
            Assert.All(klbPlayers, p => Assert.False(string.IsNullOrWhiteSpace(p.StandardPrimaryPosition)));
            Assert.All(saoVangPlayers, p => Assert.False(string.IsNullOrWhiteSpace(p.StandardPrimaryPosition)));
            Assert.All(phoenixPlayers, p => Assert.False(string.IsNullOrWhiteSpace(p.StandardPrimaryPosition)));

            // Kiểm tra có buổi thi đấu và các bản ghi điểm danh
            var sessions = await db.AttendanceSessions.Where(s => s.TeamId == klb.Id).ToListAsync();
            Assert.NotEmpty(sessions);

            var records = await db.AttendanceRecords.Where(r => r.SessionId == sessions.First().Id).ToListAsync();
            Assert.True(records.Count >= 16, "All 16 players in KLB should have attendance records");

            // Kiểm tra có các trận đấu mẫu
            var matches = await db.Matches.ToListAsync();
            Assert.True(matches.Count >= 3, $"Expected at least 3 matches, got {matches.Count}");
        }

        // 2. Chạy lại lần 2 để kiểm tra tính Idempotent (không tạo trùng lặp dữ liệu)
        using (var scope = serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await DbInitializer.SeedDemoDataAsync(serviceProvider, db);

            var teams = await db.Teams.ToListAsync();
            Assert.Equal(5, teams.Count);

            var klb = teams.First(t => t.Slug == "klb-fc");
            var klbPlayers = await db.Players.Where(p => p.TeamId == klb.Id).ToListAsync();
            Assert.Equal(16, klbPlayers.Count);
        }
    }
}
