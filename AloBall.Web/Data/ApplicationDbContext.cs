using AloBall.Web.Data.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AloBall.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

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
    }
}
