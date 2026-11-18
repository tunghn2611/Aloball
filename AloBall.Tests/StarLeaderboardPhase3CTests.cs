using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AloBall.Tests;

public class StarLeaderboardPhase3CTests
{
    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"StarsLeaderboardTest_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Team MakeTeam(string name, string region = "Cầu Giấy", double rating = 4.8) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Slug = name.ToLowerInvariant().Replace(' ', '-') + "-" + Guid.NewGuid().ToString("N")[..5],
        ActivityArea = region,
        AverageRating = rating,
        IsPublic = true,
        IsDraft = false
    };

    [Fact]
    public async Task Standings_Computes310Stars_GoalDiff_AndLast5RecentForm()
    {
        using var db = CreateDb();
        var t1 = MakeTeam("FC Thăng Long");
        var t2 = MakeTeam("FC Tây Hồ");
        var t3 = MakeTeam("FC Đống Đa");
        var t4 = MakeTeam("FC Hoàn Kiếm");
        db.Teams.AddRange(t1, t2, t3, t4);

        var start = new DateTime(2026, 6, 1, 19, 0, 0);
        // 6 confirmed inter-club matches across multiple months/opponents for t1:
        // M1 (Jun 1): t1 vs t2 -> 1-0 (W)
        // M2 (Jun 8): t1 vs t3 -> 2-2 (D)
        // M3 (Jul 1): t1 vs t2 -> 0-3 (L)
        // M4 (Aug 1): t1 vs t3 -> 4-1 (W)
        // M5 (Sep 1): t1 vs t4 -> 3-0 (W)
        // M6 (Oct 1): t1 vs t4 -> 2-1 (W)
        db.Matches.AddRange(
            new MatchGame { TeamAId = t1.Id, TeamBId = t2.Id, Category = MatchCategory.Friendly, Status = MatchStatus.Completed, ConfirmedByTeamA = true, ConfirmedByTeamB = true, ScoreA = 1, ScoreB = 0, MatchDateTime = start },
            new MatchGame { TeamAId = t1.Id, TeamBId = t3.Id, Category = MatchCategory.Friendly, Status = MatchStatus.Completed, ConfirmedByTeamA = true, ConfirmedByTeamB = true, ScoreA = 2, ScoreB = 2, MatchDateTime = start.AddDays(7) },
            new MatchGame { TeamAId = t1.Id, TeamBId = t2.Id, Category = MatchCategory.Friendly, Status = MatchStatus.Completed, ConfirmedByTeamA = true, ConfirmedByTeamB = true, ScoreA = 0, ScoreB = 3, MatchDateTime = start.AddMonths(1) },
            new MatchGame { TeamAId = t1.Id, TeamBId = t3.Id, Category = MatchCategory.Friendly, Status = MatchStatus.Completed, ConfirmedByTeamA = true, ConfirmedByTeamB = true, ScoreA = 4, ScoreB = 1, MatchDateTime = start.AddMonths(2) },
            new MatchGame { TeamAId = t1.Id, TeamBId = t4.Id, Category = MatchCategory.Friendly, Status = MatchStatus.Completed, ConfirmedByTeamA = true, ConfirmedByTeamB = true, ScoreA = 3, ScoreB = 0, MatchDateTime = start.AddMonths(3) },
            new MatchGame { TeamAId = t1.Id, TeamBId = t4.Id, Category = MatchCategory.Friendly, Status = MatchStatus.Completed, ConfirmedByTeamA = true, ConfirmedByTeamB = true, ScoreA = 2, ScoreB = 1, MatchDateTime = start.AddMonths(4) }
        );
        await db.SaveChangesAsync();

        var service = new AloballStarsService(db);
        var lb = await service.BuildLeaderboardAsync("Cầu Giấy", onlyRankedMatches: true);

        var row1 = lb.Standings.Single(r => r.TeamId == t1.Id);
        Assert.Equal(1, row1.Rank);
        Assert.Equal(13, row1.AloballStars); // 4W (12) + 1D (1) + 1L (0) = 13
        Assert.Equal(6, row1.MatchesPlayed);
        Assert.Equal(4, row1.Wins);
        Assert.Equal(1, row1.Draws);
        Assert.Equal(1, row1.Losses);
        Assert.Equal(5, row1.GoalDiff); // GF 12 - GA 7 = +5
        Assert.Equal(["D", "L", "W", "W", "W"], row1.RecentForm); // Last 5 chronological
    }

    [Fact]
    public async Task AntiFarming_RejectsInternalAndSingleSidedMatches_AcceptsRefereeVerifiedMatch()
    {
        using var db = CreateDb();
        var t1 = MakeTeam("FC A");
        var t2 = MakeTeam("FC B");
        db.Teams.AddRange(t1, t2);

        // 1. Internal A/B split -> MUST be excluded
        var mInternal = new MatchGame
        {
            TeamAId = t1.Id, TeamBId = null, Category = MatchCategory.Internal,
            Status = MatchStatus.Completed, ConfirmedByTeamA = true, ConfirmedByTeamB = true,
            ScoreA = 5, ScoreB = 2, MatchDateTime = new DateTime(2026, 11, 1, 19, 0, 0)
        };

        // 2. Single-sided unverified match (no referee) -> MUST be excluded in ranked mode
        var mSingleSide = new MatchGame
        {
            TeamAId = t1.Id, TeamBId = t2.Id, Category = MatchCategory.Friendly,
            Status = MatchStatus.Completed, ConfirmedByTeamA = true, ConfirmedByTeamB = false,
            ScoreA = 3, ScoreB = 0, MatchDateTime = new DateTime(2026, 11, 2, 19, 0, 0)
        };

        // 3. Single-sided confirmed BUT verified by assigned system referee -> MUST count!
        var mRefMatch = new MatchGame
        {
            Id = Guid.NewGuid(),
            TeamAId = t1.Id, TeamBId = t2.Id, Category = MatchCategory.Friendly,
            Status = MatchStatus.Completed, ConfirmedByTeamA = true, ConfirmedByTeamB = false,
            ScoreA = 2, ScoreB = 1, MatchDateTime = new DateTime(2026, 11, 3, 19, 0, 0)
        };

        var referee = new Referee { Id = Guid.NewGuid(), Name = "Trọng tài Chính", IsActive = true };
        var refReq = new RefereeRequest
        {
            Id = Guid.NewGuid(), TeamId = t1.Id, MatchId = mRefMatch.Id,
            VenueName = "Sân A", VenueAddress = "Hà Nội", MatchFormat = "7 người", RefereeCount = 1,
            Status = RefereeRequestStatus.Confirmed
        };
        var refProp = new RefereeProposal
        {
            Id = Guid.NewGuid(), RequestId = refReq.Id, AdminId = "admin",
            TotalPriceVND = 300_000m, Status = RefereeProposalStatus.Accepted
        };
        var refAssign = new RefereeAssignment
        {
            Id = Guid.NewGuid(), ProposalId = refProp.Id, RefereeId = referee.Id
        };

        db.Matches.AddRange(mInternal, mSingleSide, mRefMatch);
        db.Referees.Add(referee);
        db.RefereeRequests.Add(refReq);
        db.RefereeProposals.Add(refProp);
        db.RefereeAssignments.Add(refAssign);
        await db.SaveChangesAsync();

        var service = new AloballStarsService(db);
        Assert.Equal(3, await service.GetTeamStarsAsync(t1.Id)); // Only mRefMatch counts (+3 stars)
        Assert.Equal(0, await service.GetTeamStarsAsync(t2.Id));

        // Verify MatchConciergeService uses the exact same AloballStarsService engine
        var concierge = new MatchConciergeService(db, new TeamAuthorizationService(db), service);
        Assert.Equal(3, await concierge.GetTeamAloballStarsAsync(t1.Id));
    }

    [Fact]
    public async Task AntiFarming_CapsSamePairAt2RankedMatchesPerMonth_WhileCountingAllInHeadToHead()
    {
        using var db = CreateDb();
        var t1 = MakeTeam("FC Hổ");
        var t2 = MakeTeam("FC Báo");
        db.Teams.AddRange(t1, t2);

        // 4 confirmed wins by t1 over t2 in Nov 2026 (Vietnam time): only first 2 count for stars (6★), 3rd & 4th award 0★!
        // Plus 1 confirmed win in Dec 2026 -> new month, awards +3★ (total 9★)!
        db.Matches.AddRange(
            new MatchGame { TeamAId = t1.Id, TeamBId = t2.Id, Category = MatchCategory.Friendly, Status = MatchStatus.Completed, ConfirmedByTeamA = true, ConfirmedByTeamB = true, ScoreA = 3, ScoreB = 1, MatchDateTime = new DateTime(2026, 11, 2, 19, 0, 0) },
            new MatchGame { TeamAId = t1.Id, TeamBId = t2.Id, Category = MatchCategory.Friendly, Status = MatchStatus.Completed, ConfirmedByTeamA = true, ConfirmedByTeamB = true, ScoreA = 2, ScoreB = 0, MatchDateTime = new DateTime(2026, 11, 9, 19, 0, 0) },
            new MatchGame { TeamAId = t1.Id, TeamBId = t2.Id, Category = MatchCategory.Friendly, Status = MatchStatus.Completed, ConfirmedByTeamA = true, ConfirmedByTeamB = true, ScoreA = 4, ScoreB = 1, MatchDateTime = new DateTime(2026, 11, 16, 19, 0, 0) },
            new MatchGame { TeamAId = t1.Id, TeamBId = t2.Id, Category = MatchCategory.Friendly, Status = MatchStatus.Completed, ConfirmedByTeamA = true, ConfirmedByTeamB = true, ScoreA = 1, ScoreB = 0, MatchDateTime = new DateTime(2026, 11, 23, 19, 0, 0) },
            new MatchGame { TeamAId = t1.Id, TeamBId = t2.Id, Category = MatchCategory.Friendly, Status = MatchStatus.Completed, ConfirmedByTeamA = true, ConfirmedByTeamB = true, ScoreA = 2, ScoreB = 1, MatchDateTime = new DateTime(2026, 12, 5, 19, 0, 0) }
        );
        await db.SaveChangesAsync();

        var service = new AloballStarsService(db);
        var lb = await service.BuildLeaderboardAsync(null, onlyRankedMatches: true);

        var t1Row = lb.Standings.Single(r => r.TeamId == t1.Id);
        Assert.Equal(9, t1Row.AloballStars);   // 2 in Nov (6★) + 1 in Dec (3★) = 9★
        Assert.Equal(3, t1Row.MatchesPlayed);  // 3 ranked matches total
        Assert.Equal(3, lb.RegionProgress.CurrentRankedMatches);

        // Head-to-Head STILL counts all 5 verified matches (12 goals for, 3 against)
        var h2h = await service.GetHeadToHeadAsync(t1.Id, t2.Id);
        Assert.NotNull(h2h);
        Assert.Equal(5, h2h!.TotalMatches);
        Assert.Equal(5, h2h.Wins);
        Assert.Equal(0, h2h.Draws);
        Assert.Equal(0, h2h.Losses);
        Assert.Equal(12, h2h.GoalsFor);
        Assert.Equal(3, h2h.GoalsAgainst);
        Assert.Equal(2, h2h.Matches.Count(m => m.WasCappedByMonthlyLimit && m.AwardedStars == 0));
    }

    [Fact]
    public async Task RegionUnlockThreshold_TracksProgressUpTo10RankedMatches()
    {
        using var db = CreateDb();
        var teams = Enumerable.Range(1, 6).Select(i => MakeTeam($"FC Cầu Giấy {i}", "Cầu Giấy")).ToList();
        var otherTeam = MakeTeam("FC Hà Đông", "Hà Đông");
        db.Teams.AddRange(teams);
        db.Teams.Add(otherTeam);

        var service = new AloballStarsService(db);

        // Seed 3 distinct-pair ranked matches in Cầu Giấy (plus 1 outside in Hà Đông using non-Cầu Giấy teams)
        var baseDate = new DateTime(2026, 11, 1, 19, 0, 0);
        for (int i = 0; i < 3; i++)
        {
            db.Matches.Add(new MatchGame
            {
                TeamAId = teams[0].Id, TeamBId = teams[i + 1].Id, Category = MatchCategory.Friendly,
                Status = MatchStatus.Completed, ConfirmedByTeamA = true, ConfirmedByTeamB = true,
                ScoreA = 2, ScoreB = 1, MatchDateTime = baseDate.AddDays(i)
            });
        }
        await db.SaveChangesAsync();

        var lockedLb = await service.BuildLeaderboardAsync("Cầu Giấy", onlyRankedMatches: true);
        Assert.Equal(3, lockedLb.RegionProgress.CurrentRankedMatches);
        Assert.Equal(AloballStarsService.MinRankedMatchesToUnlockRegion, lockedLb.RegionProgress.TargetMatches);
        Assert.Equal(30, lockedLb.RegionProgress.ProgressPercent);
        Assert.False(lockedLb.RegionProgress.IsUnlocked);

        // Add 7 more distinct-pair ranked matches -> reaches 10 -> region unlocked!
        int added = 0;
        for (int i = 1; i < teams.Count && added < 7; i++)
        {
            for (int j = i + 1; j < teams.Count && added < 7; j++)
            {
                db.Matches.Add(new MatchGame
                {
                    TeamAId = teams[i].Id, TeamBId = teams[j].Id, Category = MatchCategory.Friendly,
                    Status = MatchStatus.Completed, ConfirmedByTeamA = true, ConfirmedByTeamB = true,
                    ScoreA = 1, ScoreB = 0, MatchDateTime = baseDate.AddDays(5 + added)
                });
                added++;
            }
        }
        await db.SaveChangesAsync();

        var unlockedLb = await service.BuildLeaderboardAsync("Cầu Giấy", onlyRankedMatches: true);
        Assert.Equal(10, unlockedLb.RegionProgress.CurrentRankedMatches);
        Assert.True(unlockedLb.RegionProgress.IsUnlocked);
        Assert.Equal(100, unlockedLb.RegionProgress.ProgressPercent);
    }
}
