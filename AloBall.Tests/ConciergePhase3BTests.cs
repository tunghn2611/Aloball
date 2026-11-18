using System.Reflection;
using AloBall.Web.Controllers;
using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AloBall.Tests;

public class ConciergePhase3BTests
{
    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"ConciergeTest_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<(ApplicationUser capA, ApplicationUser capB, ApplicationUser outsider, Team teamA, Team teamB, Team teamHardTackle, PitchVenue venue, Referee ref1)>
        SeedScenarioAsync(ApplicationDbContext db, PlanTier teamATier = PlanTier.Free)
    {
        var capA = new ApplicationUser { Id = "capA_" + Guid.NewGuid(), UserName = "capA@aloball.vn", Email = "capA@aloball.vn", FullName = "Cap Team A" };
        var capB = new ApplicationUser { Id = "capB_" + Guid.NewGuid(), UserName = "capB@aloball.vn", Email = "capB@aloball.vn", FullName = "Cap Team B" };
        var outsider = new ApplicationUser { Id = "out_" + Guid.NewGuid(), UserName = "out@aloball.vn", Email = "out@aloball.vn", FullName = "Outsider" };
        db.Users.AddRange(capA, capB, outsider);

        var teamA = new Team
        {
            Id = Guid.NewGuid(),
            Name = "FC Aurora",
            Slug = "fc-aurora-" + Guid.NewGuid().ToString("N")[..6],
            ActivityArea = "Cầu Giấy",
            HomePitch = "Sân Thành Phát",
            AverageRating = 4.6,
            PlanTier = teamATier,
            PlanExpiresAt = teamATier == PlanTier.Free ? null : DateTime.UtcNow.AddDays(14),
            IsPublic = true
        };
        var teamB = new Team
        {
            Id = Guid.NewGuid(),
            Name = "FC Hòa Bình",
            Slug = "fc-hoa-binh-" + Guid.NewGuid().ToString("N")[..6],
            ActivityArea = "Cầu Giấy",
            AverageRating = 4.5,
            PlanTier = PlanTier.Plus,
            PlanExpiresAt = DateTime.UtcNow.AddDays(14),
            IsPublic = true
        };
        var teamHardTackle = new Team
        {
            Id = Guid.NewGuid(),
            Name = "FC Quyết Liệt",
            Slug = "fc-quyet-liet-" + Guid.NewGuid().ToString("N")[..6],
            ActivityArea = "Cầu Giấy",
            AverageRating = 4.5,
            PlanTier = PlanTier.Plus,
            PlanExpiresAt = DateTime.UtcNow.AddDays(14),
            IsPublic = true
        };
        var extraTeam = new Team
        {
            Id = Guid.NewGuid(),
            Name = "FC Khách",
            Slug = "fc-khach-" + Guid.NewGuid().ToString("N")[..6],
            ActivityArea = "Đống Đa",
            AverageRating = 2.5,
            PlanTier = PlanTier.Free,
            IsPublic = true
        };
        db.Teams.AddRange(teamA, teamB, teamHardTackle, extraTeam);

        db.TeamMembers.Add(new TeamMember { TeamId = teamA.Id, UserId = capA.Id, Role = TeamRole.Owner });
        db.TeamMembers.Add(new TeamMember { TeamId = teamB.Id, UserId = capB.Id, Role = TeamRole.Owner });
        db.Players.Add(new Player { Id = Guid.NewGuid(), TeamId = teamA.Id, LinkedUserId = capA.Id, FullName = capA.FullName, IsActive = true, IsClaimed = true });

        // Give teamB 2 confirmed wins + 1 draw (prior month to satisfy anti-farming cap) = 7 Aloball Stars
        var pastDay = CompetitionRules.LocalNow.AddDays(-10);
        db.Matches.AddRange(
            new MatchGame { TeamAId = teamB.Id, TeamBId = extraTeam.Id, Category = MatchCategory.Friendly, Status = MatchStatus.Completed, ConfirmedByTeamA = true, ConfirmedByTeamB = true, ScoreA = 3, ScoreB = 1, MatchDateTime = pastDay },
            new MatchGame { TeamAId = extraTeam.Id, TeamBId = teamB.Id, Category = MatchCategory.Friendly, Status = MatchStatus.Completed, ConfirmedByTeamA = true, ConfirmedByTeamB = true, ScoreA = 0, ScoreB = 2, MatchDateTime = pastDay.AddDays(1) },
            new MatchGame { TeamAId = teamB.Id, TeamBId = extraTeam.Id, Category = MatchCategory.Friendly, Status = MatchStatus.Completed, ConfirmedByTeamA = true, ConfirmedByTeamB = true, ScoreA = 1, ScoreB = 1, MatchDateTime = pastDay.AddMonths(-1) }
        );

        // Mark teamHardTackle with >= 35% "Đá rát – quyết liệt" reviews (1 of 2 = 50%) and give it 6 stars
        var pastMatchHard = new MatchGame { Id = Guid.NewGuid(), TeamAId = teamHardTackle.Id, TeamBId = extraTeam.Id, Category = MatchCategory.Friendly, Status = MatchStatus.Completed, ConfirmedByTeamA = true, ConfirmedByTeamB = true, ScoreA = 2, ScoreB = 0, MatchDateTime = pastDay };
        var pastMatchHard2 = new MatchGame { Id = Guid.NewGuid(), TeamAId = teamHardTackle.Id, TeamBId = teamB.Id, Category = MatchCategory.Friendly, Status = MatchStatus.Completed, ConfirmedByTeamA = true, ConfirmedByTeamB = true, ScoreA = 3, ScoreB = 0, MatchDateTime = pastDay.AddDays(1) };
        db.Matches.AddRange(pastMatchHard, pastMatchHard2);

        db.TeamMatchReviews.AddRange(
            new TeamMatchReview { MatchId = pastMatchHard.Id, ReviewerTeamId = extraTeam.Id, TargetTeamId = teamHardTackle.Id, RatingStars = 4, Comment = "Đá quá rát", PlayStyleVibeTags = PlayStyleCatalog.VibeDaRat, IsValid = true },
            new TeamMatchReview { MatchId = pastMatchHard2.Id, ReviewerTeamId = teamB.Id, TargetTeamId = teamHardTackle.Id, RatingStars = 5, Comment = "Vui vẻ", PlayStyleVibeTags = PlayStyleCatalog.VibeVuiVe, IsValid = true }
        );

        var venue = new PitchVenue
        {
            Id = Guid.NewGuid(),
            Name = "Sân bóng Thành Phát",
            City = "Hà Nội",
            District = "Cầu Giấy",
            FullAddress = "Số 2 Hoàng Minh Giám, Cầu Giấy, Hà Nội",
            ContactPhone = "0912345678",
            SupportedFormats = "7 người",
            SurfaceType = "Cỏ nhân tạo",
            IndicativePricePerHour = 600_000m,
            TotalPitches = 3,
            IsActive = true
        };
        db.PitchVenues.Add(venue);

        var ref1 = new Referee
        {
            Id = Guid.NewGuid(),
            Name = "Trọng tài Nguyễn Văn Tuấn",
            ContactInfo = "0909000111",
            ExperienceSummary = "Cấp Quốc gia C2",
            SupportedFormats = "7 người",
            DefaultPriceVND = 350_000m,
            IsActive = true
        };
        db.Referees.Add(ref1);

        await db.SaveChangesAsync();
        return (capA, capB, outsider, teamA, teamB, teamHardTackle, venue, ref1);
    }

    [Fact]
    public async Task PlusProTierGating_BlocksFreeAndExpiredTeams_Allows14DayTrialActivation()
    {
        using var db = CreateDb();
        var (capA, _, outsider, teamA, _, _, _, _) = await SeedScenarioAsync(db, PlanTier.Free);
        var svc = new MatchConciergeService(db, new TeamAuthorizationService(db));
        var futureDay = CompetitionRules.LocalNow.Date.AddDays(2);

        // 1. Free team is gated on Concierge and AutoPairing
        var conciergeRes = await svc.CreateAndFulfillConciergeOrderAsync(new ConciergeOrderInput
        {
            TeamId = teamA.Id, PlayDate = futureDay, KickoffTime = new(19, 0, 0),
            Region = "Cầu Giấy", MinStars = 0, MaxStars = 20, MinFriendlyRating = 3.5
        }, capA.Id);
        Assert.False(conciergeRes.Success);
        Assert.Contains("Plus", conciergeRes.Message);

        var schedRes = await svc.SaveRecurringScheduleAsync(new RecurringScheduleInput
        {
            TeamId = teamA.Id, IsAutoPairingEnabled = true, SelectedDays = [2, 4],
            Region = "Cầu Giấy", MinAloballStars = 0, MaxAloballStars = 20
        }, capA.Id);
        Assert.False(schedRes.Success);

        // 2. Non-manager cannot activate trial
        var unauthTrial = await svc.ActivatePlusTrialAsync(teamA.Id, outsider.Id);
        Assert.False(unauthTrial.Success);

        // 3. Captain activates 14-day Plus trial -> unlocked
        var trialRes = await svc.ActivatePlusTrialAsync(teamA.Id, capA.Id);
        Assert.True(trialRes.Success);

        var refreshed = await db.Teams.FindAsync(teamA.Id);
        Assert.Equal(PlanTier.Plus, refreshed!.PlanTier);
        Assert.True(refreshed.IsPaidPlanActive);
        Assert.True(refreshed.PlanExpiresAt > DateTime.UtcNow.AddDays(13));

        // 4. Expired plan is rejected again
        refreshed.PlanExpiresAt = DateTime.UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync();
        Assert.False(refreshed.IsPaidPlanActive);
        var expiredRes = await svc.RunAutoPairingForTeamAsync(teamA.Id, capA.Id);
        Assert.False(expiredRes.Success);
    }

    [Fact]
    public async Task AutoPairing_FiltersByDayOverlap_Region_Stars_Friendliness_AndHardTackle_ThenCreatesMatchRoom()
    {
        using var db = CreateDb();
        var (capA, capB, _, teamA, teamB, teamHardTackle, _, _) = await SeedScenarioAsync(db, PlanTier.Plus);
        var svc = new MatchConciergeService(db, new TeamAuthorizationService(db));

        // Verify stars calculation: teamB has 2 wins (6) + 1 draw (1) = 7 stars
        Assert.Equal(7, await svc.GetTeamAloballStarsAsync(teamB.Id));
        // teamA has 0 stars
        Assert.Equal(0, await svc.GetTeamAloballStarsAsync(teamA.Id));

        int tueThuMask = MatchConciergeService.ComputeMaskFromDays([(int)DayOfWeek.Tuesday, (int)DayOfWeek.Thursday]);

        // Opponent B (7 stars, 4.5 friendly, Tue/Thu) & Opponent HardTackle (50% Đá rát, Tue/Thu)
        db.TeamRecurringSchedules.AddRange(
            new TeamRecurringSchedule
            {
                TeamId = teamB.Id, IsAutoPairingEnabled = true, DaysOfWeekMask = tueThuMask,
                PreferredKickoffTime = new(19, 30, 0), Region = "Cầu Giấy, Hà Nội",
                MinAloballStars = 0, MaxAloballStars = 15, MinFriendlyRating = 4.0, AvoidHardTackle = true
            },
            new TeamRecurringSchedule
            {
                TeamId = teamHardTackle.Id, IsAutoPairingEnabled = true, DaysOfWeekMask = tueThuMask,
                PreferredKickoffTime = new(19, 0, 0), Region = "Cầu Giấy",
                MinAloballStars = 0, MaxAloballStars = 20, MinFriendlyRating = 3.5, AvoidHardTackle = false
            }
        );
        await db.SaveChangesAsync();

        // Save Team A schedule expecting 5..10 stars, >= 4.0 rating, Thu/Sat, AvoidHardTackle = true
        var saveRes = await svc.SaveRecurringScheduleAsync(new RecurringScheduleInput
        {
            TeamId = teamA.Id, IsAutoPairingEnabled = true,
            SelectedDays = [(int)DayOfWeek.Thursday, (int)DayOfWeek.Saturday],
            PreferredKickoffTime = new(19, 0, 0), Region = "Cầu Giấy",
            MinAloballStars = 5, MaxAloballStars = 10, MinFriendlyRating = 4.0, AvoidHardTackle = true
        }, capA.Id);
        Assert.True(saveRes.Success);

        // Run auto-pairing: should match teamB ONLY (excluding teamHardTackle due to 50% >= 35% Đá rát!)
        var pairRes = await svc.RunAutoPairingForTeamAsync(teamA.Id, capA.Id);
        Assert.True(pairRes.Success);
        var proposal = Assert.Single(pairRes.Proposals);
        Assert.Equal(teamB.Id, proposal.MatchedOpponentTeamId);
        Assert.Equal(ConciergeOrderStatus.Proposed, proposal.Status);
        Assert.Equal(DayOfWeek.Thursday, proposal.KickoffAt.DayOfWeek);

        // Confirm proposal -> creates MatchGame room
        var confirmRes = await svc.ConfirmAutoPairingOrderAsync(proposal.Id, capA.Id);
        Assert.True(confirmRes.Success);
        Assert.Equal(ConciergeOrderStatus.Fulfilled, confirmRes.Order!.Status);
        Assert.NotNull(confirmRes.Order.ResultMatchId);

        var createdMatch = await db.Matches.FindAsync(confirmRes.Order.ResultMatchId);
        Assert.NotNull(createdMatch);
        Assert.Equal(teamA.Id, createdMatch!.TeamAId);
        Assert.Equal(teamB.Id, createdMatch.TeamBId);

        // Unauthenticated / unauthorized confirmation rejected
        var unauthConfirm = await svc.ConfirmAutoPairingOrderAsync(proposal.Id, capB.Id);
        Assert.False(unauthConfirm.Success);
    }

    [Fact]
    public async Task Concierge_AmateurVsProOrchestration_ValidatesAndOrchestratesEndToEnd()
    {
        using var db = CreateDb();
        var (capA, _, _, teamA, teamB, _, venue, ref1) = await SeedScenarioAsync(db, PlanTier.Pro);
        var svc = new MatchConciergeService(db, new TeamAuthorizationService(db));
        var futureDay = CompetitionRules.LocalNow.Date.AddDays(3);

        // Validation: past KickoffAt fails
        var pastRes = await svc.CreateAndFulfillConciergeOrderAsync(new ConciergeOrderInput
        {
            TeamId = teamA.Id, PlayDate = CompetitionRules.LocalNow.Date.AddDays(-1), KickoffTime = new(19, 0, 0),
            Region = "Cầu Giấy", MinStars = 0, MaxStars = 15, MinFriendlyRating = 3.5
        }, capA.Id);
        Assert.False(pastRes.Success);

        // Validation: MinStars > MaxStars fails
        var badRangeRes = await svc.CreateAndFulfillConciergeOrderAsync(new ConciergeOrderInput
        {
            TeamId = teamA.Id, PlayDate = futureDay, KickoffTime = new(19, 0, 0),
            Region = "Cầu Giấy", MinStars = 10, MaxStars = 5, MinFriendlyRating = 3.5
        }, capA.Id);
        Assert.False(badRangeRes.Success);

        // 1. AMATEUR mode (IsProWithReferee = false): skips referee
        var amateurRes = await svc.CreateAndFulfillConciergeOrderAsync(new ConciergeOrderInput
        {
            TeamId = teamA.Id, PlayDate = futureDay, KickoffTime = new(18, 0, 0),
            Region = "Cầu Giấy", MinStars = 5, MaxStars = 10, MinFriendlyRating = 4.0,
            IsProWithReferee = false, AvoidHardTackle = true
        }, capA.Id);

        Assert.True(amateurRes.Success);
        Assert.NotNull(amateurRes.Order);
        Assert.Equal(teamB.Id, amateurRes.Order!.MatchedOpponentTeamId);
        Assert.NotNull(amateurRes.Order.BookedPitchSlotId);
        Assert.Null(amateurRes.Order.AssignedRefereeId); // Skipped referee
        Assert.Equal(600_000m, amateurRes.Order.EstimatedTotalFee);
        Assert.Contains("Nghiệp dư", amateurRes.Summary!.ZaloSummaryMessage);

        var amateurExpense = await db.MatchExpenses.FirstOrDefaultAsync(e => e.MatchId == amateurRes.Order.ResultMatchId);
        Assert.NotNull(amateurExpense);
        Assert.Equal(600_000m, amateurExpense!.PitchFee);
        Assert.Equal(0m, amateurExpense.RefereeFee);

        // 2. PRO mode (IsProWithReferee = true): assigns referee & includes referee fee
        var proDay = futureDay.AddDays(1);
        var proRes = await svc.CreateAndFulfillConciergeOrderAsync(new ConciergeOrderInput
        {
            TeamId = teamA.Id, PlayDate = proDay, KickoffTime = new(20, 0, 0),
            Region = "Cầu Giấy", MinStars = 5, MaxStars = 10, MinFriendlyRating = 4.0,
            IsProWithReferee = true, AvoidHardTackle = true
        }, capA.Id);

        Assert.True(proRes.Success);
        Assert.Equal(ref1.Id, proRes.Order!.AssignedRefereeId);
        Assert.Equal(950_000m, proRes.Order.EstimatedTotalFee); // 600k pitch + 350k referee
        Assert.Contains(ref1.Name, proRes.Summary!.ZaloSummaryMessage);

        var proPitchReq = await db.PitchBookingRequests.FindAsync(proRes.Order.BookedPitchSlotId);
        Assert.Equal(PitchBookingStatus.Confirmed, proPitchReq!.Status);
        Assert.Equal(venue.Id, proPitchReq.PitchVenueId);

        var proRefAssign = await db.RefereeAssignments.Include(a => a.Proposal).ThenInclude(p => p.Request)
            .FirstOrDefaultAsync(a => a.RefereeId == ref1.Id && a.Proposal.Request.MatchId == proRes.Order.ResultMatchId);
        Assert.NotNull(proRefAssign);
        Assert.Equal(RefereeRequestStatus.Confirmed, proRefAssign!.Proposal.Request.Status);

        var proExpense = await db.MatchExpenses.FirstOrDefaultAsync(e => e.MatchId == proRes.Order.ResultMatchId);
        Assert.NotNull(proExpense);
        Assert.Equal(600_000m, proExpense!.PitchFee);
        Assert.Equal(350_000m, proExpense.RefereeFee);
    }

    [Fact]
    public void ConciergeController_EnforcesValidateAntiForgeryTokenOnAllPostActions()
    {
        var postMethods = typeof(ConciergeController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<HttpPostAttribute>() != null)
            .ToList();

        Assert.NotEmpty(postMethods);
        foreach (var method in postMethods)
        {
            Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        }
    }
}
