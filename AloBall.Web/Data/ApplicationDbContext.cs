using AloBall.Web.Data.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, IDataProtectionKeyContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<TeamRegulation> TeamRegulations => Set<TeamRegulation>();
    public DbSet<AttendanceSession> AttendanceSessions => Set<AttendanceSession>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<MonthlyAttendanceClosing> MonthlyAttendanceClosings => Set<MonthlyAttendanceClosing>();
    public DbSet<MonthlyPlayerSummary> MonthlyPlayerSummaries => Set<MonthlyPlayerSummary>();
    public DbSet<MatchGame> Matches => Set<MatchGame>();
    public DbSet<MatchPlayerLineup> MatchPlayerLineups => Set<MatchPlayerLineup>();
    public DbSet<MatchPositionException> MatchPositionExceptions => Set<MatchPositionException>();
    public DbSet<MatchGoalEvent> MatchGoalEvents => Set<MatchGoalEvent>();
    public DbSet<CompetitionAchievement> CompetitionAchievements => Set<CompetitionAchievement>();
    public DbSet<MatchmakingPost> MatchmakingPosts => Set<MatchmakingPost>();
    public DbSet<MatchmakingProposal> MatchmakingProposals => Set<MatchmakingProposal>();
    public DbSet<TeamMatchReview> TeamMatchReviews => Set<TeamMatchReview>();
    public DbSet<ReviewDisputeAppeal> ReviewDisputeAppeals => Set<ReviewDisputeAppeal>();
    public DbSet<KitOrderBatch> KitOrderBatches => Set<KitOrderBatch>();
    public DbSet<KitOrderItem> KitOrderItems => Set<KitOrderItem>();
    public DbSet<TeamMessage> TeamMessages => Set<TeamMessage>();
    public DbSet<Referee> Referees => Set<Referee>();
    public DbSet<RefereeRequest> RefereeRequests => Set<RefereeRequest>();
    public DbSet<RefereeProposal> RefereeProposals => Set<RefereeProposal>();
    public DbSet<RefereeAssignment> RefereeAssignments => Set<RefereeAssignment>();
    public DbSet<PitchVenue> PitchVenues => Set<PitchVenue>();
    public DbSet<PitchBookingRequest> PitchBookingRequests => Set<PitchBookingRequest>();
    public DbSet<PitchBookingOffer> PitchBookingOffers => Set<PitchBookingOffer>();

    // Module Smart Match Finance
    public DbSet<MatchExpenseEstimate> MatchExpenseEstimates => Set<MatchExpenseEstimate>();
    public DbSet<MatchSettlement> MatchSettlements => Set<MatchSettlement>();
    public DbSet<MatchSettlementItem> MatchSettlementItems => Set<MatchSettlementItem>();
    public DbSet<MatchSettlementMemberAllocation> MatchSettlementMemberAllocations => Set<MatchSettlementMemberAllocation>();
    public DbSet<MatchSettlementPayment> MatchSettlementPayments => Set<MatchSettlementPayment>();
    public DbSet<MatchSettlementAdjustmentAudit> MatchSettlementAdjustmentAudits => Set<MatchSettlementAdjustmentAudit>();

    // Module Giữ Lửa
    public DbSet<TeamFireRegulation> TeamFireRegulations => Set<TeamFireRegulation>();
    public DbSet<FireCompetitionPeriod> FireCompetitionPeriods => Set<FireCompetitionPeriod>();
    public DbSet<PlayerAbJournalEntry> PlayerAbJournalEntries => Set<PlayerAbJournalEntry>();
    public DbSet<MonthlyFireAward> MonthlyFireAwards => Set<MonthlyFireAward>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Unique constraint: Team slug
        builder.Entity<Team>()
            .HasIndex(t => t.Slug)
            .IsUnique();

        // Unique constraint: TeamMember (1 user chỉ thuộc 1 team 1 lần)
        builder.Entity<TeamMember>()
            .HasIndex(tm => new { tm.TeamId, tm.UserId })
            .IsUnique();

        // Unique constraint: 1 buổi + 1 cầu thủ chỉ có duy nhất 1 bản ghi điểm danh
        builder.Entity<AttendanceRecord>()
            .HasIndex(ar => new { ar.SessionId, ar.PlayerId })
            .IsUnique();

        // Unique constraint: 1 cầu thủ trong 1 trận chỉ có 1 vị trí trong danh sách
        builder.Entity<MatchPlayerLineup>()
            .HasIndex(l => new { l.MatchId, l.PlayerId })
            .IsUnique();

        // Unique constraint: Mỗi ngoại lệ vị trí trận là duy nhất theo MatchId, PlayerId và AllowedSlotCode
        builder.Entity<MatchPositionException>()
            .HasIndex(e => new { e.MatchId, e.PlayerId, e.AllowedSlotCode })
            .IsUnique();

        // Unique constraint: 1 đội chỉ đánh giá 1 lần cho mỗi trận
        builder.Entity<TeamMatchReview>()
            .HasIndex(r => new { r.MatchId, r.ReviewerTeamId })
            .IsUnique();

        // Unique constraint: 1 trận chỉ có 1 quyết toán cho mỗi đội (chống quyết toán trùng)
        builder.Entity<MatchSettlement>()
            .HasIndex(s => new { s.MatchId, s.TeamId })
            .IsUnique();

        // Unique constraint: 1 cầu thủ chỉ có 1 bản phân bổ trong 1 quyết toán
        builder.Entity<MatchSettlementMemberAllocation>()
            .HasIndex(a => new { a.SettlementId, a.PlayerId })
            .IsUnique();

        // Giữ Lửa Constraints:
        // 1. Quy chế giữ lửa: duy nhất theo TeamId và Version
        builder.Entity<TeamFireRegulation>()
            .HasIndex(r => new { r.TeamId, r.Version })
            .IsUnique();

        // 2. Kỳ thi đua: duy nhất theo TeamId và Kỳ "yyyy-MM"
        builder.Entity<FireCompetitionPeriod>()
            .HasIndex(p => new { p.TeamId, p.StatisticalPeriod })
            .IsUnique();

        // 3. Sổ AB: Chống trùng xử lý (Idempotency) theo OperationKey
        builder.Entity<PlayerAbJournalEntry>()
            .HasIndex(j => j.OperationKey)
            .IsUnique();

        // 4. Bút toán đảo: 1 phát sinh gốc chỉ được đảo duy nhất 1 lần (filtered unique index)
        builder.Entity<PlayerAbJournalEntry>()
            .HasIndex(j => j.ReversedEntryId)
            .IsUnique()
            .HasFilter("\"ReversedEntryId\" IS NOT NULL");

        // 5. Cúp tháng: duy nhất theo PeriodId, PlayerId và PeriodRevision
        builder.Entity<MonthlyFireAward>()
            .HasIndex(a => new { a.PeriodId, a.PlayerId, a.PeriodRevision })
            .IsUnique();

        // Relationships configuration
        builder.Entity<AttendanceRecord>()
            .HasOne(ar => ar.Session)
            .WithMany(s => s.Records)
            .HasForeignKey(ar => ar.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<AttendanceRecord>()
            .HasOne(ar => ar.Player)
            .WithMany(p => p.AttendanceRecords)
            .HasForeignKey(ar => ar.PlayerId)
            .OnDelete(DeleteBehavior.Restrict); // Không xóa dây chuyền lịch sử thi đấu khi xóa/ngừng hoạt động cầu thủ

        builder.Entity<MatchGame>()
            .HasOne(m => m.TeamA)
            .WithMany()
            .HasForeignKey(m => m.TeamAId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MatchGame>()
            .HasOne(m => m.TeamB)
            .WithMany()
            .HasForeignKey(m => m.TeamBId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MatchPlayerLineup>()
            .HasOne(l => l.Match)
            .WithMany(m => m.Lineups)
            .HasForeignKey(l => l.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<MatchPlayerLineup>()
            .HasOne(l => l.Player)
            .WithMany(p => p.MatchLineups)
            .HasForeignKey(l => l.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MatchmakingPost>()
            .HasOne(p => p.Team)
            .WithMany(t => t.MatchmakingPosts)
            .HasForeignKey(p => p.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<MatchmakingPost>()
            .HasOne(p => p.TargetTeam)
            .WithMany()
            .HasForeignKey(p => p.TargetTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MatchmakingProposal>()
            .HasOne(p => p.Post)
            .WithMany(post => post.Proposals)
            .HasForeignKey(p => p.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<MatchmakingProposal>()
            .HasOne(p => p.ChallengingTeam)
            .WithMany()
            .HasForeignKey(p => p.ChallengingTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<TeamMatchReview>()
            .HasOne(r => r.ReviewerTeam)
            .WithMany()
            .HasForeignKey(r => r.ReviewerTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<TeamMatchReview>()
            .HasOne(r => r.TargetTeam)
            .WithMany()
            .HasForeignKey(r => r.TargetTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MatchGoalEvent>()
            .HasOne(g => g.Match)
            .WithMany(m => m.GoalEvents)
            .HasForeignKey(g => g.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<MatchGoalEvent>()
            .HasOne(g => g.Team)
            .WithMany()
            .HasForeignKey(g => g.TeamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MatchGoalEvent>()
            .HasOne(g => g.Scorer)
            .WithMany()
            .HasForeignKey(g => g.ScorerPlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MatchGoalEvent>()
            .HasOne(g => g.Assist)
            .WithMany()
            .HasForeignKey(g => g.AssistPlayerId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Internal Messaging: TeamMessage
        builder.Entity<TeamMessage>(entity =>
        {
            entity.HasOne(m => m.Team)
                .WithMany(t => t.Messages)
                .HasForeignKey(m => m.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(m => m.SenderUser)
                .WithMany()
                .HasForeignKey(m => m.SenderUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(m => m.RecipientUser)
                .WithMany()
                .HasForeignKey(m => m.RecipientUserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(m => new { m.TeamId, m.ConversationId });
            entity.HasIndex(m => new { m.RecipientUserId, m.IsRead });
            entity.HasIndex(m => new { m.TeamId, m.SenderUserId, m.CreatedAt });
        });

        // Referee Request & Coordination
        builder.Entity<RefereeRequest>(entity =>
        {
            entity.HasOne(r => r.Team)
                .WithMany()
                .HasForeignKey(r => r.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.Match)
                .WithMany()
                .HasForeignKey(r => r.MatchId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(r => r.TeamId);
            entity.HasIndex(r => r.Status);
        });

        builder.Entity<RefereeProposal>(entity =>
        {
            entity.HasOne(p => p.Request)
                .WithMany(r => r.Proposals)
                .HasForeignKey(p => p.RequestId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(p => p.RequestId);
            entity.HasIndex(p => p.Status);
        });

        builder.Entity<RefereeAssignment>(entity =>
        {
            entity.HasOne(a => a.Proposal)
                .WithMany(p => p.Assignments)
                .HasForeignKey(a => a.ProposalId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.Referee)
                .WithMany()
                .HasForeignKey(a => a.RefereeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(a => new { a.ProposalId, a.RefereeId }).IsUnique();
        });

        // Pitch Discovery & Booking MVP
        builder.Entity<PitchBookingRequest>(entity =>
        {
            entity.HasOne(r => r.Team)
                .WithMany()
                .HasForeignKey(r => r.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.PitchVenue)
                .WithMany(v => v.BookingRequests)
                .HasForeignKey(r => r.PitchVenueId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.Match)
                .WithMany()
                .HasForeignKey(r => r.MatchId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(r => r.TeamId);
            entity.HasIndex(r => r.PitchVenueId);
            entity.HasIndex(r => r.Status);
        });

        builder.Entity<PitchBookingOffer>(entity =>
        {
            entity.HasOne(o => o.Request)
                .WithMany(r => r.Offers)
                .HasForeignKey(o => o.RequestId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(o => o.RequestId);
            entity.HasIndex(o => o.Status);
        });

        builder.Entity<PitchVenue>(entity =>
        {
            entity.HasIndex(v => v.City);
            entity.HasIndex(v => v.District);
            entity.HasIndex(v => v.IsActive);
        });
    }
}


