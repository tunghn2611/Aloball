using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Models;
using AloBall.Web.Services;
using AloBall.Web.Services.TeamDivision;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AloBall.Web.Data;

public static class DbInitializer
{
    public static async Task SeedDemoDataAsync(IServiceProvider serviceProvider, ApplicationDbContext? existingDb = null)
    {
        using var scope = serviceProvider.CreateScope();
        var db = existingDb ?? scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        // Chỉ chạy EnsureCreated và bản vá schema SQLite thô khi đang dùng SQLite
        if (db.Database.IsSqlite())
        {
            await db.Database.EnsureCreatedAsync();
            await EnsureDatabaseSchemaAsync(db);
        }

        try
        {
            // 2. Tạo hoặc cập nhật tài khoản quản lý / đội trưởng
            var users = await EnsureUsersAsync(userManager);

            // 3. Tạo hoặc mở rộng các Đội bóng & Đầy đủ danh sách Cầu thủ
            var teams = await EnsureTeamsAndRostersAsync(db, users);

            // 4. Tạo hoặc bổ sung Quy chế hoạt động mẫu
            await EnsureRegulationsAsync(db, teams);

            // 5. Tạo hoặc bổ sung Buổi thi đấu & Điểm danh mẫu để sẵn sàng test chia đội tự động & dự bị
            await EnsureSessionsAndAttendanceAsync(db, teams);

            // 6. Tạo hoặc bổ sung Kèo giao lưu Matchmaking & Trận đấu thực tế
            await EnsureMatchmakingAndMatchesAsync(db, teams);

            // 7. Tạo danh bạ Trọng tài mẫu phục vụ điều phối
            await EnsureRefereesAsync(db);

            // 8. Tạo danh bạ Sân bóng mẫu (Pitch Venues) phục vụ khám phá & đặt sân
            await EnsurePitchVenuesAsync(db);

            // 9. Chợ Cầu Thủ 2 chiều & Lịch cố định xếp cặp tự động (Phase 3A/3B)
            await EnsureLoanMarketAndRecurringSchedulesAsync(db, teams, users);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error during SeedDemoDataAsync: {ex.Message} -> {ex.StackTrace}", ex);
        }
    }

    public static async Task EnsureDatabaseSchemaAsync(ApplicationDbContext db)
    {
        if (db.Database.IsNpgsql())
        {
            try { await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Players"" ADD COLUMN IF NOT EXISTS ""PlayStyles"" VARCHAR(120) NULL;"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""PlayStyles"" VARCHAR(120) NULL;"); } catch { }
                        try { await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""TeamMatchReviews"" ADD COLUMN IF NOT EXISTS ""PlayStyleVibeTags"" VARCHAR(150) NULL;"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Players"" ADD COLUMN IF NOT EXISTS ""IsClaimed"" boolean NOT NULL DEFAULT FALSE;"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Players"" ADD COLUMN IF NOT EXISTS ""InviteToken"" VARCHAR(50) NULL;"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Matches"" ADD COLUMN IF NOT EXISTS ""RsvpToken"" VARCHAR(50) NULL;"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Matches"" ADD COLUMN IF NOT EXISTS ""KitColor"" VARCHAR(50) NULL;"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Teams"" ADD COLUMN IF NOT EXISTS ""BankCode"" VARCHAR(30) NULL;"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Teams"" ADD COLUMN IF NOT EXISTS ""AccountNumber"" VARCHAR(40) NULL;"); } catch { }
            try { await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Teams"" ADD COLUMN IF NOT EXISTS ""AccountHolderName"" VARCHAR(120) NULL;"); } catch { }
            try
            {
                await db.Database.ExecuteSqlRawAsync(@"
                    CREATE TABLE IF NOT EXISTS ""MatchExpenses"" (
                        ""Id"" uuid NOT NULL PRIMARY KEY,
                        ""MatchId"" uuid NOT NULL REFERENCES ""Matches""(""Id"") ON DELETE CASCADE,
                        ""TeamId"" uuid NOT NULL REFERENCES ""Teams""(""Id"") ON DELETE CASCADE,
                        ""PitchFee"" numeric(18,2) NOT NULL DEFAULT 0,
                        ""WaterFee"" numeric(18,2) NOT NULL DEFAULT 0,
                        ""RefereeFee"" numeric(18,2) NOT NULL DEFAULT 0,
                        ""SplitRatio"" VARCHAR(20) NOT NULL DEFAULT '100',
                        ""Tang2Fee"" numeric(18,2) NOT NULL DEFAULT 0,
                        ""MatchTotalPayable"" numeric(18,2) NOT NULL DEFAULT 0,
                        ""MatchPerPersonShare"" numeric(18,2) NOT NULL DEFAULT 0,
                        ""Tang2PerPersonShare"" numeric(18,2) NOT NULL DEFAULT 0,
                        ""Notes"" VARCHAR(500) NULL,
                        ""CreatedByUserId"" text NULL,
                        ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT NOW(),
                        ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT NOW()
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_MatchExpenses_MatchId_TeamId"" ON ""MatchExpenses"" (""MatchId"", ""TeamId"");
                    CREATE TABLE IF NOT EXISTS ""MatchExpenseShares"" (
                        ""Id"" uuid NOT NULL PRIMARY KEY,
                        ""MatchExpenseId"" uuid NOT NULL REFERENCES ""MatchExpenses""(""Id"") ON DELETE CASCADE,
                        ""PlayerId"" uuid NOT NULL REFERENCES ""Players""(""Id"") ON DELETE RESTRICT,
                        ""LinkedUserId"" text NULL,
                        ""ParticipatesInMatch"" boolean NOT NULL DEFAULT TRUE,
                        ""ParticipatesInTang2"" boolean NOT NULL DEFAULT FALSE,
                        ""MatchAmount"" numeric(18,2) NOT NULL DEFAULT 0,
                        ""Tang2Amount"" numeric(18,2) NOT NULL DEFAULT 0,
                        ""TotalAmount"" numeric(18,2) NOT NULL DEFAULT 0,
                        ""PaymentStatus"" integer NOT NULL DEFAULT 0,
                        ""PaidAt"" timestamp with time zone NULL,
                        ""MarkedByUserId"" text NULL
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_MatchExpenseShares_MatchExpenseId_PlayerId"" ON ""MatchExpenseShares"" (""MatchExpenseId"", ""PlayerId"");
                    ALTER TABLE ""Teams"" ADD COLUMN IF NOT EXISTS ""PlanTier"" integer NOT NULL DEFAULT 0;
                    ALTER TABLE ""Teams"" ADD COLUMN IF NOT EXISTS ""PlanExpiresAt"" timestamp with time zone NULL;
                    CREATE TABLE IF NOT EXISTS ""TeamRecurringSchedules"" (
                        ""Id"" uuid NOT NULL PRIMARY KEY,
                        ""TeamId"" uuid NOT NULL REFERENCES ""Teams""(""Id"") ON DELETE CASCADE,
                        ""IsAutoPairingEnabled"" boolean NOT NULL DEFAULT FALSE,
                        ""DaysOfWeekMask"" integer NOT NULL DEFAULT 0,
                        ""PreferredKickoffTime"" interval NOT NULL DEFAULT '19:00:00',
                        ""Region"" VARCHAR(100) NOT NULL DEFAULT '',
                        ""MinAloballStars"" double precision NOT NULL DEFAULT 0,
                        ""MaxAloballStars"" double precision NOT NULL DEFAULT 100,
                        ""MinFriendlyRating"" double precision NOT NULL DEFAULT 3.5,
                        ""AvoidHardTackle"" boolean NOT NULL DEFAULT TRUE,
                        ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT NOW()
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TeamRecurringSchedules_TeamId"" ON ""TeamRecurringSchedules"" (""TeamId"");
                    CREATE TABLE IF NOT EXISTS ""MatchConciergeOrders"" (
                        ""Id"" uuid NOT NULL PRIMARY KEY,
                        ""Source"" integer NOT NULL DEFAULT 0,
                        ""TeamId"" uuid NOT NULL REFERENCES ""Teams""(""Id"") ON DELETE CASCADE,
                        ""Region"" VARCHAR(100) NOT NULL DEFAULT '',
                        ""KickoffAt"" timestamp with time zone NOT NULL,
                        ""MinStars"" double precision NOT NULL DEFAULT 0,
                        ""MaxStars"" double precision NOT NULL DEFAULT 100,
                        ""MinFriendlyRating"" double precision NOT NULL DEFAULT 3.5,
                        ""IsProWithReferee"" boolean NOT NULL DEFAULT FALSE,
                        ""MatchedOpponentTeamId"" uuid NULL REFERENCES ""Teams""(""Id"") ON DELETE RESTRICT,
                        ""BookedPitchSlotId"" uuid NULL,
                        ""AssignedRefereeId"" uuid NULL,
                        ""ResultMatchId"" uuid NULL,
                        ""EstimatedTotalFee"" numeric(18,2) NOT NULL DEFAULT 0,
                        ""Status"" integer NOT NULL DEFAULT 0,
                        ""FailureReason"" VARCHAR(500) NULL,
                        ""CreatedByUserId"" text NULL,
                        ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT NOW()
                    );
                    CREATE INDEX IF NOT EXISTS ""IX_MatchConciergeOrders_TeamId_Status"" ON ""MatchConciergeOrders"" (""TeamId"", ""Status"");

                    CREATE TABLE IF NOT EXISTS ""Tournaments"" (
                        ""Id"" uuid NOT NULL PRIMARY KEY,
                        ""Name"" VARCHAR(150) NOT NULL,
                        ""OrganizerUserId"" text NOT NULL REFERENCES ""AspNetUsers""(""Id"") ON DELETE RESTRICT,
                        ""Region"" VARCHAR(100) NOT NULL DEFAULT '',
                        ""Format"" integer NOT NULL DEFAULT 0,
                        ""StartDate"" timestamp with time zone NOT NULL DEFAULT NOW(),
                        ""PitchVenue"" VARCHAR(200) NOT NULL DEFAULT '',
                        ""EntryFeeVnd"" numeric(18,2) NOT NULL DEFAULT 0,
                        ""Status"" integer NOT NULL DEFAULT 0,
                        ""InviteCode"" VARCHAR(16) NOT NULL,
                        ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT NOW()
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Tournaments_InviteCode"" ON ""Tournaments"" (""InviteCode"");
                    CREATE INDEX IF NOT EXISTS ""IX_Tournaments_Status"" ON ""Tournaments"" (""Status"");

                    CREATE TABLE IF NOT EXISTS ""TournamentTeams"" (
                        ""Id"" uuid NOT NULL PRIMARY KEY,
                        ""TournamentId"" uuid NOT NULL REFERENCES ""Tournaments""(""Id"") ON DELETE CASCADE,
                        ""TeamId"" uuid NOT NULL REFERENCES ""Teams""(""Id"") ON DELETE RESTRICT,
                        ""GroupName"" VARCHAR(40) NULL,
                        ""SeedOrder"" integer NOT NULL DEFAULT 0,
                        ""JoinedAt"" timestamp with time zone NOT NULL DEFAULT NOW()
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TournamentTeams_TournamentId_TeamId"" ON ""TournamentTeams"" (""TournamentId"", ""TeamId"");

                    CREATE TABLE IF NOT EXISTS ""TournamentStandings"" (
                        ""Id"" uuid NOT NULL PRIMARY KEY,
                        ""TournamentId"" uuid NOT NULL REFERENCES ""Tournaments""(""Id"") ON DELETE CASCADE,
                        ""TeamId"" uuid NOT NULL REFERENCES ""Teams""(""Id"") ON DELETE RESTRICT,
                        ""GroupName"" VARCHAR(40) NULL,
                        ""Rank"" integer NOT NULL DEFAULT 1,
                        ""Played"" integer NOT NULL DEFAULT 0,
                        ""Wins"" integer NOT NULL DEFAULT 0,
                        ""Draws"" integer NOT NULL DEFAULT 0,
                        ""Losses"" integer NOT NULL DEFAULT 0,
                        ""GoalsFor"" integer NOT NULL DEFAULT 0,
                        ""GoalsAgainst"" integer NOT NULL DEFAULT 0,
                        ""GoalDiff"" integer NOT NULL DEFAULT 0,
                        ""Points"" integer NOT NULL DEFAULT 0,
                        ""YellowCards"" integer NOT NULL DEFAULT 0,
                        ""RedCards"" integer NOT NULL DEFAULT 0,
                        ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT NOW()
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TournamentStandings_TournamentId_TeamId"" ON ""TournamentStandings"" (""TournamentId"", ""TeamId"");

                    ALTER TABLE ""Matches"" ADD COLUMN IF NOT EXISTS ""TournamentId"" uuid NULL REFERENCES ""Tournaments""(""Id"") ON DELETE SET NULL;
                    ALTER TABLE ""Matches"" ADD COLUMN IF NOT EXISTS ""RoundLabel"" VARCHAR(60) NULL;
                    CREATE INDEX IF NOT EXISTS ""IX_Matches_TournamentId_RoundLabel"" ON ""Matches"" (""TournamentId"", ""RoundLabel"");

                    CREATE TABLE IF NOT EXISTS ""MatchCardEvents"" (
                        ""Id"" uuid NOT NULL PRIMARY KEY,
                        ""MatchId"" uuid NOT NULL REFERENCES ""Matches""(""Id"") ON DELETE CASCADE,
                        ""TeamId"" uuid NOT NULL REFERENCES ""Teams""(""Id"") ON DELETE RESTRICT,
                        ""PlayerId"" uuid NOT NULL REFERENCES ""Players""(""Id"") ON DELETE RESTRICT,
                        ""CardType"" integer NOT NULL DEFAULT 1,
                        ""Minute"" integer NULL,
                        ""Notes"" VARCHAR(200) NULL,
                        ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT NOW()
                    );
                    CREATE INDEX IF NOT EXISTS ""IX_MatchCardEvents_MatchId"" ON ""MatchCardEvents"" (""MatchId"");
                    CREATE TABLE IF NOT EXISTS ""PlayerLoanListings"" (
                        ""Id"" uuid NOT NULL PRIMARY KEY,
                        ""ListingType"" integer NOT NULL DEFAULT 0,
                        ""UserId"" text NULL REFERENCES ""AspNetUsers""(""Id"") ON DELETE RESTRICT,
                        ""PlayerId"" uuid NULL REFERENCES ""Players""(""Id"") ON DELETE SET NULL,
                        ""TeamId"" uuid NULL REFERENCES ""Teams""(""Id"") ON DELETE SET NULL,
                        ""MatchId"" uuid NULL REFERENCES ""Matches""(""Id"") ON DELETE SET NULL,
                        ""Region"" VARCHAR(100) NOT NULL DEFAULT '',
                        ""PitchName"" VARCHAR(150) NULL,
                        ""PlayDate"" timestamp with time zone NOT NULL DEFAULT NOW(),
                        ""TimeSlot"" VARCHAR(50) NOT NULL DEFAULT '19:00 - 20:30',
                        ""PositionsNeeded"" VARCHAR(50) NOT NULL DEFAULT 'MID',
                        ""FeeType"" integer NOT NULL DEFAULT 0,
                        ""LoanFeeAmount"" numeric(18,2) NOT NULL DEFAULT 0,
                        ""EscrowAbCoin"" integer NOT NULL DEFAULT 0,
                        ""EscrowJournalEntryId"" uuid NULL,
                        ""IsEscrowReleased"" boolean NOT NULL DEFAULT FALSE,
                        ""Notes"" VARCHAR(500) NULL,
                        ""Status"" integer NOT NULL DEFAULT 0,
                        ""AcceptedApplicationId"" uuid NULL,
                        ""LoanedGuestPlayerId"" uuid NULL,
                        ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT NOW(),
                        ""MatchedAt"" timestamp with time zone NULL,
                        ""CompletedAt"" timestamp with time zone NULL
                    );
                    CREATE INDEX IF NOT EXISTS ""IX_PlayerLoanListings_ListingType_Status_PlayDate"" ON ""PlayerLoanListings"" (""ListingType"", ""Status"", ""PlayDate"");
                    CREATE TABLE IF NOT EXISTS ""PlayerLoanApplications"" (
                        ""Id"" uuid NOT NULL PRIMARY KEY,
                        ""ListingId"" uuid NOT NULL REFERENCES ""PlayerLoanListings""(""Id"") ON DELETE CASCADE,
                        ""ApplicantUserId"" text NOT NULL REFERENCES ""AspNetUsers""(""Id"") ON DELETE RESTRICT,
                        ""ApplicantPlayerId"" uuid NULL REFERENCES ""Players""(""Id"") ON DELETE SET NULL,
                        ""ApplicantTeamId"" uuid NULL REFERENCES ""Teams""(""Id"") ON DELETE SET NULL,
                        ""TargetMatchId"" uuid NULL REFERENCES ""Matches""(""Id"") ON DELETE SET NULL,
                        ""Position"" VARCHAR(50) NOT NULL DEFAULT 'MID',
                        ""Message"" VARCHAR(500) NULL,
                        ""Status"" integer NOT NULL DEFAULT 0,
                        ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT NOW(),
                        ""RespondedAt"" timestamp with time zone NULL
                    );
                    CREATE INDEX IF NOT EXISTS ""IX_PlayerLoanApplications_ListingId_Status"" ON ""PlayerLoanApplications"" (""ListingId"", ""Status"");
                ");
            }
            catch { }
            return;
        }

        if (!db.Database.IsSqlite())
        {
            return;
        }

        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE AttendanceSessions ADD COLUMN PitchName TEXT DEFAULT '';"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE AttendanceSessions ADD COLUMN PitchType TEXT DEFAULT '7 người';"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE MatchPlayerLineups ADD COLUMN TeamSide TEXT DEFAULT 'A';"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE MatchPlayerLineups ADD COLUMN AssignedSlotCode TEXT NULL;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE MatchPlayerLineups ADD COLUMN HasActuallyPlayed INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Matches ADD COLUMN LineupRevision INTEGER NOT NULL DEFAULT 1;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Matches ADD COLUMN FormationCode TEXT NULL;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Players ADD COLUMN StandardPrimaryPosition TEXT NULL;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Players ADD COLUMN AllowedSecondaryPositionsJson TEXT NULL;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Players ADD COLUMN PreferredFoot TEXT NULL;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Players ADD COLUMN Experience TEXT NULL;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Players ADD COLUMN Region TEXT NULL;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Players ADD COLUMN PublicPreferredFoot INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Players ADD COLUMN PublicExperience INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Players ADD COLUMN PlayStyles TEXT NULL;"); } catch { }
                try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE TeamMatchReviews ADD COLUMN PlayStyleVibeTags TEXT NULL;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Players ADD COLUMN IsClaimed INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Players ADD COLUMN InviteToken TEXT NULL;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Matches ADD COLUMN RsvpToken TEXT NULL;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Matches ADD COLUMN KitColor TEXT NULL;"); } catch { }

        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Teams ADD COLUMN CoverImageUrl TEXT NULL;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Teams ADD COLUMN CoverOpacity REAL NULL;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Teams ADD COLUMN BankCode TEXT NULL;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Teams ADD COLUMN AccountNumber TEXT NULL;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Teams ADD COLUMN AccountHolderName TEXT NULL;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Teams ADD COLUMN PlanTier INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Teams ADD COLUMN PlanExpiresAt TEXT NULL;"); } catch { }

        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE AspNetUsers ADD COLUMN PreferredFoot TEXT NULL;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE AspNetUsers ADD COLUMN Experience TEXT NULL;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE AspNetUsers ADD COLUMN Region TEXT NULL;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE AspNetUsers ADD COLUMN PrimaryPosition TEXT NULL;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE AspNetUsers ADD COLUMN HasCompletedOnboarding INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE AspNetUsers ADD COLUMN OnboardingCompletedAt TEXT NULL;"); } catch { }

        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""MatchPositionExceptions"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_MatchPositionExceptions"" PRIMARY KEY,
                    ""MatchId"" TEXT NOT NULL,
                    ""PlayerId"" TEXT NOT NULL,
                    ""AllowedSlotCode"" TEXT NOT NULL,
                    ""GrantedByUserId"" TEXT NOT NULL,
                    ""Reason"" TEXT NULL,
                    ""GrantedAt"" TEXT NOT NULL,
                    CONSTRAINT ""FK_MatchPositionExceptions_Matches_MatchId"" FOREIGN KEY (""MatchId"") REFERENCES ""Matches"" (""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_MatchPositionExceptions_Players_PlayerId"" FOREIGN KEY (""PlayerId"") REFERENCES ""Players"" (""Id"") ON DELETE RESTRICT
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_MatchPositionExceptions_MatchId_PlayerId_AllowedSlotCode""
                ON ""MatchPositionExceptions"" (""MatchId"", ""PlayerId"", ""AllowedSlotCode"");
            ");
        }
        catch { }

        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""MatchGoalEvents_New"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_MatchGoalEvents"" PRIMARY KEY,
                    ""MatchId"" TEXT NOT NULL,
                    ""TeamId"" TEXT NOT NULL,
                    ""ScorerPlayerId"" TEXT NOT NULL,
                    ""AssistPlayerId"" TEXT NULL,
                    ""Minute"" INTEGER NULL,
                    ""Description"" TEXT NULL,
                    ""Revision"" INTEGER NOT NULL,
                    CONSTRAINT ""FK_MatchGoalEvents_Matches_MatchId"" FOREIGN KEY (""MatchId"") REFERENCES ""Matches"" (""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_MatchGoalEvents_Players_ScorerPlayerId"" FOREIGN KEY (""ScorerPlayerId"") REFERENCES ""Players"" (""Id"") ON DELETE RESTRICT,
                    CONSTRAINT ""FK_MatchGoalEvents_Players_AssistPlayerId"" FOREIGN KEY (""AssistPlayerId"") REFERENCES ""Players"" (""Id"") ON DELETE RESTRICT
                );
                INSERT OR IGNORE INTO ""MatchGoalEvents_New"" (""Id"", ""MatchId"", ""TeamId"", ""ScorerPlayerId"", ""AssistPlayerId"", ""Minute"", ""Description"", ""Revision"")
                SELECT ""Id"", ""MatchId"", ""TeamId"", ""ScorerPlayerId"", ""AssistPlayerId"", ""Minute"", ""Description"", ""Revision"" FROM ""MatchGoalEvents"";
                DROP TABLE ""MatchGoalEvents"";
                ALTER TABLE ""MatchGoalEvents_New"" RENAME TO ""MatchGoalEvents"";
            ");
        }
        catch { }

        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""TeamMessages"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_TeamMessages"" PRIMARY KEY,
                    ""TeamId"" TEXT NOT NULL,
                    ""SenderUserId"" TEXT NOT NULL,
                    ""RecipientUserId"" TEXT NULL,
                    ""ConversationId"" TEXT NOT NULL,
                    ""MessageType"" INTEGER NOT NULL DEFAULT 0,
                    ""Title"" TEXT NOT NULL,
                    ""Content"" TEXT NOT NULL,
                    ""IsRead"" INTEGER NOT NULL DEFAULT 0,
                    ""ReadAt"" TEXT NULL,
                    ""CreatedAt"" TEXT NOT NULL,
                    CONSTRAINT ""FK_TeamMessages_Teams_TeamId"" FOREIGN KEY (""TeamId"") REFERENCES ""Teams"" (""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_TeamMessages_AspNetUsers_SenderUserId"" FOREIGN KEY (""SenderUserId"") REFERENCES ""AspNetUsers"" (""Id"") ON DELETE RESTRICT,
                    CONSTRAINT ""FK_TeamMessages_AspNetUsers_RecipientUserId"" FOREIGN KEY (""RecipientUserId"") REFERENCES ""AspNetUsers"" (""Id"") ON DELETE RESTRICT
                );
                CREATE INDEX IF NOT EXISTS ""IX_TeamMessages_TeamId_ConversationId"" ON ""TeamMessages"" (""TeamId"", ""ConversationId"");
                CREATE INDEX IF NOT EXISTS ""IX_TeamMessages_RecipientUserId_IsRead"" ON ""TeamMessages"" (""RecipientUserId"", ""IsRead"");
                CREATE INDEX IF NOT EXISTS ""IX_TeamMessages_TeamId_SenderUserId_CreatedAt"" ON ""TeamMessages"" (""TeamId"", ""SenderUserId"", ""CreatedAt"");
            ");
        }
        catch { }

        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""Referees"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Referees"" PRIMARY KEY,
                    ""Name"" TEXT NOT NULL,
                    ""ContactInfo"" TEXT NOT NULL,
                    ""ExperienceSummary"" TEXT NOT NULL,
                    ""SupportedFormats"" TEXT NULL,
                    ""ServiceNotes"" TEXT NULL,
                    ""DefaultPriceVND"" TEXT NULL,
                    ""IsActive"" INTEGER NOT NULL DEFAULT 1
                );

                CREATE TABLE IF NOT EXISTS ""RefereeRequests"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_RefereeRequests"" PRIMARY KEY,
                    ""TeamId"" TEXT NOT NULL,
                    ""MatchId"" TEXT NULL,
                    ""ServiceDate"" TEXT NOT NULL,
                    ""StartTime"" TEXT NOT NULL,
                    ""EndTime"" TEXT NOT NULL,
                    ""VenueName"" TEXT NOT NULL,
                    ""VenueAddress"" TEXT NOT NULL,
                    ""MatchFormat"" TEXT NOT NULL,
                    ""RefereeCount"" INTEGER NOT NULL,
                    ""PreferredExperience"" TEXT NULL,
                    ""BudgetVND"" TEXT NULL,
                    ""Notes"" TEXT NULL,
                    ""Status"" INTEGER NOT NULL DEFAULT 0,
                    ""CreatedAt"" TEXT NOT NULL,
                    ""UpdatedAt"" TEXT NULL,
                    CONSTRAINT ""FK_RefereeRequests_Teams_TeamId"" FOREIGN KEY (""TeamId"") REFERENCES ""Teams"" (""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_RefereeRequests_Matches_MatchId"" FOREIGN KEY (""MatchId"") REFERENCES ""Matches"" (""Id"") ON DELETE SET NULL
                );
                CREATE INDEX IF NOT EXISTS ""IX_RefereeRequests_TeamId"" ON ""RefereeRequests"" (""TeamId"");
                CREATE INDEX IF NOT EXISTS ""IX_RefereeRequests_Status"" ON ""RefereeRequests"" (""Status"");

                CREATE TABLE IF NOT EXISTS ""RefereeProposals"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_RefereeProposals"" PRIMARY KEY,
                    ""RequestId"" TEXT NOT NULL,
                    ""AdminId"" TEXT NOT NULL,
                    ""TotalPriceVND"" TEXT NOT NULL,
                    ""ServiceNote"" TEXT NULL,
                    ""Status"" INTEGER NOT NULL DEFAULT 0,
                    ""SubmittedAt"" TEXT NOT NULL,
                    CONSTRAINT ""FK_RefereeProposals_RefereeRequests_RequestId"" FOREIGN KEY (""RequestId"") REFERENCES ""RefereeRequests"" (""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_RefereeProposals_AspNetUsers_AdminId"" FOREIGN KEY (""AdminId"") REFERENCES ""AspNetUsers"" (""Id"") ON DELETE RESTRICT
                );
                CREATE INDEX IF NOT EXISTS ""IX_RefereeProposals_RequestId"" ON ""RefereeProposals"" (""RequestId"");
                CREATE INDEX IF NOT EXISTS ""IX_RefereeProposals_Status"" ON ""RefereeProposals"" (""Status"");

                CREATE TABLE IF NOT EXISTS ""RefereeAssignments"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_RefereeAssignments"" PRIMARY KEY,
                    ""ProposalId"" TEXT NOT NULL,
                    ""RefereeId"" TEXT NOT NULL,
                    CONSTRAINT ""FK_RefereeAssignments_RefereeProposals_ProposalId"" FOREIGN KEY (""ProposalId"") REFERENCES ""RefereeProposals"" (""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_RefereeAssignments_Referees_RefereeId"" FOREIGN KEY (""RefereeId"") REFERENCES ""Referees"" (""Id"") ON DELETE RESTRICT
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RefereeAssignments_ProposalId_RefereeId"" ON ""RefereeAssignments"" (""ProposalId"", ""RefereeId"");
            ");
        }
        catch { }

        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""PitchVenues"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_PitchVenues"" PRIMARY KEY,
                    ""Name"" TEXT NOT NULL,
                    ""Description"" TEXT NULL,
                    ""City"" TEXT NOT NULL,
                    ""District"" TEXT NOT NULL,
                    ""FullAddress"" TEXT NOT NULL,
                    ""ContactPhone"" TEXT NOT NULL,
                    ""SupportedFormats"" TEXT NOT NULL,
                    ""SurfaceType"" TEXT NOT NULL,
                    ""Facilities"" TEXT NULL,
                    ""OpeningHours"" TEXT NULL,
                    ""IndicativePricePerHour"" TEXT NULL,
                    ""PriceNotes"" TEXT NULL,
                    ""ImageUrl"" TEXT NULL,
                    ""MapLink"" TEXT NULL,
                    ""TotalPitches"" INTEGER NOT NULL DEFAULT 4,
                    ""IsActive"" INTEGER NOT NULL DEFAULT 1,
                    ""CreatedAt"" TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ""IX_PitchVenues_City"" ON ""PitchVenues"" (""City"");
                CREATE INDEX IF NOT EXISTS ""IX_PitchVenues_District"" ON ""PitchVenues"" (""District"");
                CREATE INDEX IF NOT EXISTS ""IX_PitchVenues_IsActive"" ON ""PitchVenues"" (""IsActive"");

                CREATE TABLE IF NOT EXISTS ""PitchBookingRequests"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_PitchBookingRequests"" PRIMARY KEY,
                    ""TeamId"" TEXT NOT NULL,
                    ""PitchVenueId"" TEXT NOT NULL,
                    ""MatchId"" TEXT NULL,
                    ""ServiceDate"" TEXT NOT NULL,
                    ""StartTime"" TEXT NOT NULL,
                    ""EndTime"" TEXT NOT NULL,
                    ""PitchFormat"" TEXT NOT NULL,
                    ""PitchCount"" INTEGER NOT NULL DEFAULT 1,
                    ""BudgetVND"" TEXT NULL,
                    ""Notes"" TEXT NULL,
                    ""Status"" INTEGER NOT NULL DEFAULT 0,
                    ""ConfirmedFieldCode"" TEXT NULL,
                    ""CreatedAt"" TEXT NOT NULL,
                    ""UpdatedAt"" TEXT NULL,
                    CONSTRAINT ""FK_PitchBookingRequests_Teams_TeamId"" FOREIGN KEY (""TeamId"") REFERENCES ""Teams"" (""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_PitchBookingRequests_PitchVenues_PitchVenueId"" FOREIGN KEY (""PitchVenueId"") REFERENCES ""PitchVenues"" (""Id"") ON DELETE RESTRICT,
                    CONSTRAINT ""FK_PitchBookingRequests_Matches_MatchId"" FOREIGN KEY (""MatchId"") REFERENCES ""Matches"" (""Id"") ON DELETE SET NULL
                );
                CREATE INDEX IF NOT EXISTS ""IX_PitchBookingRequests_TeamId"" ON ""PitchBookingRequests"" (""TeamId"");
                CREATE INDEX IF NOT EXISTS ""IX_PitchBookingRequests_PitchVenueId"" ON ""PitchBookingRequests"" (""PitchVenueId"");
                CREATE INDEX IF NOT EXISTS ""IX_PitchBookingRequests_Status"" ON ""PitchBookingRequests"" (""Status"");

                CREATE TABLE IF NOT EXISTS ""PitchBookingOffers"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_PitchBookingOffers"" PRIMARY KEY,
                    ""RequestId"" TEXT NOT NULL,
                    ""AdminId"" TEXT NOT NULL,
                    ""ConfirmedVenueName"" TEXT NOT NULL,
                    ""ConfirmedAddress"" TEXT NOT NULL,
                    ""ConfirmedDate"" TEXT NOT NULL,
                    ""ConfirmedStartTime"" TEXT NOT NULL,
                    ""ConfirmedEndTime"" TEXT NOT NULL,
                    ""ConfirmedPitchFormat"" TEXT NOT NULL,
                    ""ConfirmedPitchCount"" INTEGER NOT NULL DEFAULT 1,
                    ""SpecificFieldCode"" TEXT NULL,
                    ""TotalPriceVND"" TEXT NOT NULL,
                    ""DepositInstructions"" TEXT NULL,
                    ""Notes"" TEXT NULL,
                    ""Status"" INTEGER NOT NULL DEFAULT 0,
                    ""SubmittedAt"" TEXT NOT NULL,
                    CONSTRAINT ""FK_PitchBookingOffers_PitchBookingRequests_RequestId"" FOREIGN KEY (""RequestId"") REFERENCES ""PitchBookingRequests"" (""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_PitchBookingOffers_AspNetUsers_AdminId"" FOREIGN KEY (""AdminId"") REFERENCES ""AspNetUsers"" (""Id"") ON DELETE RESTRICT
                );
                CREATE INDEX IF NOT EXISTS ""IX_PitchBookingOffers_RequestId"" ON ""PitchBookingOffers"" (""RequestId"");
                CREATE INDEX IF NOT EXISTS ""IX_PitchBookingOffers_Status"" ON ""PitchBookingOffers"" (""Status"");
            ");
        }
        catch { }

        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""MatchExpenseEstimates"" (
                    ""Id"" TEXT NOT NULL PRIMARY KEY,
                    ""MatchId"" TEXT NOT NULL,
                    ""TeamId"" TEXT NOT NULL,
                    ""Description"" TEXT NOT NULL,
                    ""EstimatedAmount"" TEXT NOT NULL,
                    ""CreatedAt"" TEXT NOT NULL,
                    ""CreatedByUserId"" TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ""IX_MatchExpenseEstimates_MatchId_TeamId"" ON ""MatchExpenseEstimates"" (""MatchId"", ""TeamId"");

                CREATE TABLE IF NOT EXISTS ""MatchSettlements"" (
                    ""Id"" TEXT NOT NULL PRIMARY KEY,
                    ""MatchId"" TEXT NOT NULL,
                    ""TeamId"" TEXT NOT NULL,
                    ""SettlementDate"" TEXT NOT NULL,
                    ""TotalPitchCost"" TEXT NOT NULL,
                    ""TotalRefereeCost"" TEXT NOT NULL,
                    ""TotalOtherExpenses"" TEXT NOT NULL,
                    ""TotalAmount"" TEXT NOT NULL,
                    ""ParticipantCount"" INTEGER NOT NULL,
                    ""PerPersonShare"" TEXT NOT NULL,
                    ""Status"" INTEGER NOT NULL,
                    ""ConfirmedAt"" TEXT NULL,
                    ""ConfirmedByUserId"" TEXT NULL,
                    ""Notes"" TEXT NULL,
                    ""CreatedAt"" TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_MatchSettlements_MatchId_TeamId"" ON ""MatchSettlements"" (""MatchId"", ""TeamId"");

                CREATE TABLE IF NOT EXISTS ""MatchSettlementItems"" (
                    ""Id"" TEXT NOT NULL PRIMARY KEY,
                    ""SettlementId"" TEXT NOT NULL,
                    ""Category"" INTEGER NOT NULL,
                    ""Description"" TEXT NOT NULL,
                    ""Amount"" TEXT NOT NULL,
                    ""IsFromConfirmedService"" INTEGER NOT NULL,
                    ""SourceReferenceId"" TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS ""IX_MatchSettlementItems_SettlementId"" ON ""MatchSettlementItems"" (""SettlementId"");

                CREATE TABLE IF NOT EXISTS ""MatchSettlementMemberAllocations"" (
                    ""Id"" TEXT NOT NULL PRIMARY KEY,
                    ""SettlementId"" TEXT NOT NULL,
                    ""PlayerId"" TEXT NOT NULL,
                    ""UserId"" TEXT NULL,
                    ""CalculatedAmount"" TEXT NOT NULL,
                    ""AdjustedAmount"" TEXT NOT NULL,
                    ""AdjustmentReason"" TEXT NULL,
                    ""AmountPaid"" TEXT NOT NULL,
                    ""PaymentStatus"" INTEGER NOT NULL,
                    ""LastPaymentDate"" TEXT NULL,
                    ""Notes"" TEXT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_MatchSettlementMemberAllocations_SettlementId_PlayerId"" ON ""MatchSettlementMemberAllocations"" (""SettlementId"", ""PlayerId"");

                CREATE TABLE IF NOT EXISTS ""MatchSettlementPayments"" (
                    ""Id"" TEXT NOT NULL PRIMARY KEY,
                    ""AllocationId"" TEXT NOT NULL,
                    ""Amount"" TEXT NOT NULL,
                    ""PaymentDate"" TEXT NOT NULL,
                    ""PaymentMethod"" INTEGER NOT NULL,
                    ""ReferenceCode"" TEXT NULL,
                    ""Note"" TEXT NULL,
                    ""RecordedByUserId"" TEXT NOT NULL,
                    ""CreatedAt"" TEXT NOT NULL,
                    ""IsReversed"" INTEGER NOT NULL,
                    ""ReversalReason"" TEXT NULL,
                    ""ReversedAt"" TEXT NULL,
                    ""ReversedByUserId"" TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS ""IX_MatchSettlementPayments_AllocationId"" ON ""MatchSettlementPayments"" (""AllocationId"");

                CREATE TABLE IF NOT EXISTS ""MatchSettlementAdjustmentAudits"" (
                    ""Id"" TEXT NOT NULL PRIMARY KEY,
                    ""AllocationId"" TEXT NOT NULL,
                    ""PreviousAmount"" TEXT NOT NULL,
                    ""NewAmount"" TEXT NOT NULL,
                    ""MandatoryReason"" TEXT NOT NULL,
                    ""AdjustedByUserId"" TEXT NOT NULL,
                    ""AdjustedAt"" TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ""IX_MatchSettlementAdjustmentAudits_AllocationId"" ON ""MatchSettlementAdjustmentAudits"" (""AllocationId"");

                CREATE TABLE IF NOT EXISTS ""MatchExpenses"" (
                    ""Id"" TEXT NOT NULL PRIMARY KEY,
                    ""MatchId"" TEXT NOT NULL,
                    ""TeamId"" TEXT NOT NULL,
                    ""PitchFee"" TEXT NOT NULL DEFAULT '0',
                    ""WaterFee"" TEXT NOT NULL DEFAULT '0',
                    ""RefereeFee"" TEXT NOT NULL DEFAULT '0',
                    ""SplitRatio"" TEXT NOT NULL DEFAULT '100',
                    ""Tang2Fee"" TEXT NOT NULL DEFAULT '0',
                    ""MatchTotalPayable"" TEXT NOT NULL DEFAULT '0',
                    ""MatchPerPersonShare"" TEXT NOT NULL DEFAULT '0',
                    ""Tang2PerPersonShare"" TEXT NOT NULL DEFAULT '0',
                    ""Notes"" TEXT NULL,
                    ""CreatedByUserId"" TEXT NULL,
                    ""CreatedAt"" TEXT NOT NULL,
                    ""UpdatedAt"" TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_MatchExpenses_MatchId_TeamId"" ON ""MatchExpenses"" (""MatchId"", ""TeamId"");

                CREATE TABLE IF NOT EXISTS ""MatchExpenseShares"" (
                    ""Id"" TEXT NOT NULL PRIMARY KEY,
                    ""MatchExpenseId"" TEXT NOT NULL,
                    ""PlayerId"" TEXT NOT NULL,
                    ""LinkedUserId"" TEXT NULL,
                    ""ParticipatesInMatch"" INTEGER NOT NULL DEFAULT 1,
                    ""ParticipatesInTang2"" INTEGER NOT NULL DEFAULT 0,
                    ""MatchAmount"" TEXT NOT NULL DEFAULT '0',
                    ""Tang2Amount"" TEXT NOT NULL DEFAULT '0',
                    ""TotalAmount"" TEXT NOT NULL DEFAULT '0',
                    ""PaymentStatus"" INTEGER NOT NULL DEFAULT 0,
                    ""PaidAt"" TEXT NULL,
                    ""MarkedByUserId"" TEXT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_MatchExpenseShares_MatchExpenseId_PlayerId"" ON ""MatchExpenseShares"" (""MatchExpenseId"", ""PlayerId"");

                CREATE TABLE IF NOT EXISTS ""PlayerLoanListings"" (
                    ""Id"" TEXT NOT NULL PRIMARY KEY,
                    ""ListingType"" INTEGER NOT NULL DEFAULT 0,
                    ""UserId"" TEXT NULL,
                    ""PlayerId"" TEXT NULL,
                    ""TeamId"" TEXT NULL,
                    ""MatchId"" TEXT NULL,
                    ""Region"" TEXT NOT NULL DEFAULT '',
                    ""PitchName"" TEXT NULL,
                    ""PlayDate"" TEXT NOT NULL,
                    ""TimeSlot"" TEXT NOT NULL DEFAULT '19:00 - 20:30',
                    ""PositionsNeeded"" TEXT NOT NULL DEFAULT 'MID',
                    ""FeeType"" INTEGER NOT NULL DEFAULT 0,
                    ""LoanFeeAmount"" TEXT NOT NULL DEFAULT '0',
                    ""EscrowAbCoin"" INTEGER NOT NULL DEFAULT 0,
                    ""EscrowJournalEntryId"" TEXT NULL,
                    ""IsEscrowReleased"" INTEGER NOT NULL DEFAULT 0,
                    ""Notes"" TEXT NULL,
                    ""Status"" INTEGER NOT NULL DEFAULT 0,
                    ""AcceptedApplicationId"" TEXT NULL,
                    ""LoanedGuestPlayerId"" TEXT NULL,
                    ""CreatedAt"" TEXT NOT NULL,
                    ""MatchedAt"" TEXT NULL,
                    ""CompletedAt"" TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS ""IX_PlayerLoanListings_ListingType_Status_PlayDate"" ON ""PlayerLoanListings"" (""ListingType"", ""Status"", ""PlayDate"");

                CREATE TABLE IF NOT EXISTS ""PlayerLoanApplications"" (
                    ""Id"" TEXT NOT NULL PRIMARY KEY,
                    ""ListingId"" TEXT NOT NULL,
                    ""ApplicantUserId"" TEXT NOT NULL,
                    ""ApplicantPlayerId"" TEXT NULL,
                    ""ApplicantTeamId"" TEXT NULL,
                    ""TargetMatchId"" TEXT NULL,
                    ""Position"" TEXT NOT NULL DEFAULT 'MID',
                    ""Message"" TEXT NULL,
                    ""Status"" INTEGER NOT NULL DEFAULT 0,
                    ""CreatedAt"" TEXT NOT NULL,
                    ""RespondedAt"" TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS ""IX_PlayerLoanApplications_ListingId_Status"" ON ""PlayerLoanApplications"" (""ListingId"", ""Status"");

                CREATE TABLE IF NOT EXISTS ""TeamRecurringSchedules"" (
                    ""Id"" TEXT NOT NULL PRIMARY KEY,
                    ""TeamId"" TEXT NOT NULL,
                    ""IsAutoPairingEnabled"" INTEGER NOT NULL DEFAULT 0,
                    ""DaysOfWeekMask"" INTEGER NOT NULL DEFAULT 0,
                    ""PreferredKickoffTime"" TEXT NOT NULL DEFAULT '19:00:00',
                    ""Region"" TEXT NOT NULL DEFAULT '',
                    ""MinAloballStars"" REAL NOT NULL DEFAULT 0,
                    ""MaxAloballStars"" REAL NOT NULL DEFAULT 100,
                    ""MinFriendlyRating"" REAL NOT NULL DEFAULT 3.5,
                    ""AvoidHardTackle"" INTEGER NOT NULL DEFAULT 1,
                    ""UpdatedAt"" TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TeamRecurringSchedules_TeamId"" ON ""TeamRecurringSchedules"" (""TeamId"");

                CREATE TABLE IF NOT EXISTS ""MatchConciergeOrders"" (
                    ""Id"" TEXT NOT NULL PRIMARY KEY,
                    ""Source"" INTEGER NOT NULL DEFAULT 0,
                    ""TeamId"" TEXT NOT NULL,
                    ""Region"" TEXT NOT NULL DEFAULT '',
                    ""KickoffAt"" TEXT NOT NULL,
                    ""MinStars"" REAL NOT NULL DEFAULT 0,
                    ""MaxStars"" REAL NOT NULL DEFAULT 100,
                    ""MinFriendlyRating"" REAL NOT NULL DEFAULT 3.5,
                    ""IsProWithReferee"" INTEGER NOT NULL DEFAULT 0,
                    ""MatchedOpponentTeamId"" TEXT NULL,
                    ""BookedPitchSlotId"" TEXT NULL,
                    ""AssignedRefereeId"" TEXT NULL,
                    ""ResultMatchId"" TEXT NULL,
                    ""EstimatedTotalFee"" TEXT NOT NULL DEFAULT '0',
                    ""Status"" INTEGER NOT NULL DEFAULT 0,
                    ""FailureReason"" TEXT NULL,
                    ""CreatedByUserId"" TEXT NULL,
                    ""CreatedAt"" TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ""IX_MatchConciergeOrders_TeamId_Status"" ON ""MatchConciergeOrders"" (""TeamId"", ""Status"");

                CREATE TABLE IF NOT EXISTS ""Tournaments"" (
                    ""Id"" TEXT NOT NULL PRIMARY KEY,
                    ""Name"" TEXT NOT NULL,
                    ""OrganizerUserId"" TEXT NOT NULL,
                    ""Region"" TEXT NOT NULL DEFAULT '',
                    ""Format"" INTEGER NOT NULL DEFAULT 0,
                    ""StartDate"" TEXT NOT NULL,
                    ""PitchVenue"" TEXT NOT NULL DEFAULT '',
                    ""EntryFeeVnd"" TEXT NOT NULL DEFAULT '0',
                    ""Status"" INTEGER NOT NULL DEFAULT 0,
                    ""InviteCode"" TEXT NOT NULL,
                    ""CreatedAt"" TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Tournaments_InviteCode"" ON ""Tournaments"" (""InviteCode"");

                CREATE TABLE IF NOT EXISTS ""TournamentTeams"" (
                    ""Id"" TEXT NOT NULL PRIMARY KEY,
                    ""TournamentId"" TEXT NOT NULL,
                    ""TeamId"" TEXT NOT NULL,
                    ""GroupName"" TEXT NULL,
                    ""SeedOrder"" INTEGER NOT NULL DEFAULT 0,
                    ""JoinedAt"" TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TournamentTeams_TournamentId_TeamId"" ON ""TournamentTeams"" (""TournamentId"", ""TeamId"");

                CREATE TABLE IF NOT EXISTS ""TournamentStandings"" (
                    ""Id"" TEXT NOT NULL PRIMARY KEY,
                    ""TournamentId"" TEXT NOT NULL,
                    ""TeamId"" TEXT NOT NULL,
                    ""GroupName"" TEXT NULL,
                    ""Rank"" INTEGER NOT NULL DEFAULT 1,
                    ""Played"" INTEGER NOT NULL DEFAULT 0,
                    ""Wins"" INTEGER NOT NULL DEFAULT 0,
                    ""Draws"" INTEGER NOT NULL DEFAULT 0,
                    ""Losses"" INTEGER NOT NULL DEFAULT 0,
                    ""GoalsFor"" INTEGER NOT NULL DEFAULT 0,
                    ""GoalsAgainst"" INTEGER NOT NULL DEFAULT 0,
                    ""GoalDiff"" INTEGER NOT NULL DEFAULT 0,
                    ""Points"" INTEGER NOT NULL DEFAULT 0,
                    ""YellowCards"" INTEGER NOT NULL DEFAULT 0,
                    ""RedCards"" INTEGER NOT NULL DEFAULT 0,
                    ""UpdatedAt"" TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TournamentStandings_TournamentId_TeamId"" ON ""TournamentStandings"" (""TournamentId"", ""TeamId"");

                CREATE TABLE IF NOT EXISTS ""MatchCardEvents"" (
                    ""Id"" TEXT NOT NULL PRIMARY KEY,
                    ""MatchId"" TEXT NOT NULL,
                    ""TeamId"" TEXT NOT NULL,
                    ""PlayerId"" TEXT NOT NULL,
                    ""CardType"" INTEGER NOT NULL DEFAULT 1,
                    ""Minute"" INTEGER NULL,
                    ""Notes"" TEXT NULL,
                    ""CreatedAt"" TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ""IX_MatchCardEvents_MatchId"" ON ""MatchCardEvents"" (""MatchId"");
            ");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error migrating Finance tables: {ex.Message}");
        }

        try { await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Matches"" ADD COLUMN ""TournamentId"" TEXT NULL;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Matches"" ADD COLUMN ""RoundLabel"" TEXT NULL;"); } catch { }
    }



    private class SeededUsers
    {
        public ApplicationUser Admin { get; set; } = null!;
        public ApplicationUser KlbOwner { get; set; } = null!;
        public ApplicationUser KlbCaptain { get; set; } = null!;
        public ApplicationUser KlbMember { get; set; } = null!;
        public ApplicationUser SaoVangCaptain { get; set; } = null!;
        public ApplicationUser PhoenixCaptain { get; set; } = null!;
        public ApplicationUser TrangAnCaptain { get; set; } = null!;
        public ApplicationUser BachKhoaCaptain { get; set; } = null!;
        public ApplicationUser SaiGonUnitedCaptain { get; set; } = null!;
        public ApplicationUser ThuDucCaptain { get; set; } = null!;
        public ApplicationUser BinhThanhCaptain { get; set; } = null!;
    }

    private static async Task<SeededUsers> EnsureUsersAsync(UserManager<ApplicationUser> userManager)
    {
        async Task<ApplicationUser> GetOrCreateUserAsync(string email, string fullName, string password, string fixedId, string fixedStamp)
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    Id = fixedId,
                    UserName = email,
                    Email = email,
                    FullName = fullName,
                    EmailConfirmed = true,
                    SecurityStamp = fixedStamp
                };
                var res = await userManager.CreateAsync(user, password);
                if (!res.Succeeded)
                {
                    throw new InvalidOperationException($"Failed to create user {email}: {string.Join(", ", res.Errors.Select(e => e.Description))}");
                }
            }
            return user;
        }

        var klbOwner = await GetOrCreateUserAsync("owner@klbfc.vn", "Nguyễn Văn Chủ Tịch", "Owner@123456", "usr-owner-klb-002", "STAMP-OWNER-KLB-002");
        if (string.IsNullOrWhiteSpace(klbOwner.PlayStyles)) klbOwner.PlayStyles = "MAESTRO+,TIKI_TAKA,INCISIVE,FIRST_TIME";
        if (string.IsNullOrWhiteSpace(klbOwner.PreferredFoot)) klbOwner.PreferredFoot = "Chân phải";
        if (string.IsNullOrWhiteSpace(klbOwner.Experience)) klbOwner.Experience = "Bán chuyên / Phủi cứng";
        if (string.IsNullOrWhiteSpace(klbOwner.Region)) klbOwner.Region = "Cầu Giấy, Hà Nội";

        var klbCaptain = await GetOrCreateUserAsync("captain@klbfc.vn", "Trần Đội Trưởng", "Captain@123456", "usr-captain-klb-003", "STAMP-CAPTAIN-KLB-003");
        if (string.IsNullOrWhiteSpace(klbCaptain.PlayStyles)) klbCaptain.PlayStyles = "CUT_INSIDE+,FINESSE,RAPID,ACCELERATOR";
        if (string.IsNullOrWhiteSpace(klbCaptain.PreferredFoot)) klbCaptain.PreferredFoot = "Chân trái";
        if (string.IsNullOrWhiteSpace(klbCaptain.Region)) klbCaptain.Region = "Cầu Giấy, Hà Nội";

        var klbMember = await GetOrCreateUserAsync("member@klbfc.vn", "Lê Cầu Thủ", "Member@123456", "usr-member-klb-004", "STAMP-MEMBER-KLB-004");
        if (string.IsNullOrWhiteSpace(klbMember.PlayStyles)) klbMember.PlayStyles = "GUARDIAN+,ANTICIPATE,RELENTLESS";
        if (string.IsNullOrWhiteSpace(klbMember.PreferredFoot)) klbMember.PreferredFoot = "Chân trái";
        if (string.IsNullOrWhiteSpace(klbMember.Region)) klbMember.Region = "Cầu Giấy, Hà Nội";

        var saiGonUnitedCaptain = await GetOrCreateUserAsync("captain@saigonunited.vn", "Phạm Sài Gòn", "SaiGon@123456", "usr-captain-sgu-009", "STAMP-CAPTAIN-SGU-009");
        if (string.IsNullOrWhiteSpace(saiGonUnitedCaptain.PlayStyles)) saiGonUnitedCaptain.PlayStyles = "FINESSE+,CLINICAL,TRICKSTER";
        if (string.IsNullOrWhiteSpace(saiGonUnitedCaptain.PreferredFoot)) saiGonUnitedCaptain.PreferredFoot = "Chân phải";
        if (string.IsNullOrWhiteSpace(saiGonUnitedCaptain.Region)) saiGonUnitedCaptain.Region = "Quận 7, TP.HCM";

        var thuDucCaptain = await GetOrCreateUserAsync("captain@thuducfc.vn", "Huỳnh Thủ Đức", "ThuDuc@123456", "usr-captain-td-010", "STAMP-CAPTAIN-TD-010");
        if (string.IsNullOrWhiteSpace(thuDucCaptain.PlayStyles)) thuDucCaptain.PlayStyles = "TIKI_TAKA+,BULLET_PASS,OFF_BALL";
        if (string.IsNullOrWhiteSpace(thuDucCaptain.PreferredFoot)) thuDucCaptain.PreferredFoot = "Hai chân";
        if (string.IsNullOrWhiteSpace(thuDucCaptain.Region)) thuDucCaptain.Region = "TP. Thủ Đức, TP.HCM";

        var binhThanhCaptain = await GetOrCreateUserAsync("captain@binhthanhfc.vn", "Đoàn Bình Thạnh", "BinhThanh@123456", "usr-captain-bt-011", "STAMP-CAPTAIN-BT-011");
        if (string.IsNullOrWhiteSpace(binhThanhCaptain.PlayStyles)) binhThanhCaptain.PlayStyles = "BRUISER+,TACKLE_MASTER,RELENTLESS";
        if (string.IsNullOrWhiteSpace(binhThanhCaptain.PreferredFoot)) binhThanhCaptain.PreferredFoot = "Chân phải";
        if (string.IsNullOrWhiteSpace(binhThanhCaptain.Region)) binhThanhCaptain.Region = "Bình Thạnh, TP.HCM";

        return new SeededUsers
        {
            Admin = await GetOrCreateUserAsync("admin@aloball.vn", "Platform Administrator", "Admin@123456", "usr-admin-001", "STAMP-ADMIN-001"),
            KlbOwner = klbOwner,
            KlbCaptain = klbCaptain,
            KlbMember = klbMember,
            SaoVangCaptain = await GetOrCreateUserAsync("captain@saovangfc.vn", "Hoàng Sao Vàng", "SaoVang@123456", "usr-captain-sv-005", "STAMP-CAPTAIN-SV-005"),
            PhoenixCaptain = await GetOrCreateUserAsync("captain@phoenixfc.vn", "Vũ Phượng Hoàng", "Phoenix@123456", "usr-captain-ph-006", "STAMP-CAPTAIN-PH-006"),
            TrangAnCaptain = await GetOrCreateUserAsync("captain@tranganfc.vn", "Đinh Tràng An", "TrangAn@123456", "usr-captain-ta-007", "STAMP-CAPTAIN-TA-007"),
            BachKhoaCaptain = await GetOrCreateUserAsync("captain@bachkhoafc.vn", "Tạ Bách Khoa", "BachKhoa@123456", "usr-captain-bk-008", "STAMP-CAPTAIN-BK-008"),
            SaiGonUnitedCaptain = saiGonUnitedCaptain,
            ThuDucCaptain = thuDucCaptain,
            BinhThanhCaptain = binhThanhCaptain
        };
    }

    private class SeededTeams
    {
        public Team Klb { get; set; } = null!;
        public Team SaoVang { get; set; } = null!;
        public Team Phoenix { get; set; } = null!;
        public Team TrangAn { get; set; } = null!;
        public Team BachKhoa { get; set; } = null!;
        public Team SaiGonUnited { get; set; } = null!;
        public Team ThuDuc { get; set; } = null!;
        public Team BinhThanh { get; set; } = null!;
    }

    private static async Task<SeededTeams> EnsureTeamsAndRostersAsync(ApplicationDbContext db, SeededUsers users)
    {
        // 1. KLB FC
        var klb = await db.Teams.FirstOrDefaultAsync(t => t.Slug == "klb-fc");
        if (klb == null)
        {
            klb = new Team
            {
                Name = "KLB FC",
                Slug = "klb-fc",
                Description = "Câu lạc bộ bóng đá phong trào KLB - Giao lưu hữu nghị, rèn luyện sức khỏe, văn minh sân cỏ.",
                ActivityArea = "Cầu Giấy, Hà Nội",
                HomePitch = "Sân bóng PVV - 72 Trần Thái Tông",
                EstablishedDate = new DateTime(2020, 10, 10),
                IsPublic = true,
                AverageRating = 5.0,
                ValidReviewCount = 3
            };
            db.Teams.Add(klb);
            await db.SaveChangesAsync();
        }

        // 2. Sao Vàng FC
        var saoVang = await db.Teams.FirstOrDefaultAsync(t => t.Slug == "sao-vang-fc");
        if (saoVang == null)
        {
            saoVang = new Team
            {
                Name = "Sao Vàng FC",
                Slug = "sao-vang-fc",
                Description = "Sao Vàng FC - Tinh thần fair-play, nhiệt huyết, giao lưu tối thứ 4 & chủ nhật.",
                ActivityArea = "Nam Từ Liêm, Hà Nội",
                HomePitch = "Sân Mễ Trì Thượng",
                EstablishedDate = new DateTime(2021, 5, 1),
                IsPublic = true,
                AverageRating = 4.8,
                ValidReviewCount = 2
            };
            db.Teams.Add(saoVang);
            await db.SaveChangesAsync();
        }

        // 3. Phoenix FC
        var phoenix = await db.Teams.FirstOrDefaultAsync(t => t.Slug == "phoenix-fc");
        if (phoenix == null)
        {
            phoenix = new Team
            {
                Name = "Phoenix FC",
                Slug = "phoenix-fc",
                Description = "Phoenix FC (Phượng Hoàng Lửa) - Lối đá tấn công cống hiến, tốc độ và kỹ thuật.",
                ActivityArea = "Thanh Xuân, Hà Nội",
                HomePitch = "Sân bóng Bộ Công An - Nguyễn Xiển",
                EstablishedDate = new DateTime(2022, 3, 15),
                IsPublic = true,
                AverageRating = 4.9,
                ValidReviewCount = 5
            };
            db.Teams.Add(phoenix);
            await db.SaveChangesAsync();
        }

        // 4. Tràng An FC
        var trangAn = await db.Teams.FirstOrDefaultAsync(t => t.Slug == "trang-an-fc");
        if (trangAn == null)
        {
            trangAn = new Team
            {
                Name = "Tràng An FC",
                Slug = "trang-an-fc",
                Description = "Tràng An FC - Gắn kết anh em đồng hương, kỷ luật chiến thuật, giao lưu cởi mở.",
                ActivityArea = "Đống Đa, Hà Nội",
                HomePitch = "Sân bóng Thủy Lợi",
                EstablishedDate = new DateTime(2021, 11, 20),
                IsPublic = true,
                AverageRating = 4.7,
                ValidReviewCount = 4
            };
            db.Teams.Add(trangAn);
            await db.SaveChangesAsync();
        }

        // 5. FC Bách Khoa 2012
        var bachKhoa = await db.Teams.FirstOrDefaultAsync(t => t.Slug == "bach-khoa-fc");
        if (bachKhoa == null)
        {
            bachKhoa = new Team
            {
                Name = "FC Bách Khoa 2012",
                Slug = "bach-khoa-fc",
                Description = "Hội cựu sinh viên Bách Khoa K57 - Đam mê bóng đá, thể lực bền bỉ, chiến thuật chặt chẽ.",
                ActivityArea = "Hai Bà Trưng, Hà Nội",
                HomePitch = "Sân vận động Bách Khoa",
                EstablishedDate = new DateTime(2019, 8, 15),
                IsPublic = true,
                AverageRating = 4.9,
                ValidReviewCount = 6
            };
            db.Teams.Add(bachKhoa);
            await db.SaveChangesAsync();
        }

        if (saoVang.PlanTier == PlanTier.Free && !saoVang.PlanExpiresAt.HasValue)
        {
            saoVang.PlanTier = PlanTier.Plus;
            saoVang.PlanExpiresAt = DateTime.UtcNow.AddDays(30);
        }

        async Task<Team> GetOrCreateClubAsync(string name, string slug, string area, string pitch, string desc, PlanTier tier, double rating)
        {
            var t = await db.Teams.FirstOrDefaultAsync(x => x.Slug == slug);
            if (t == null)
            {
                t = new Team
                {
                    Name = name,
                    Slug = slug,
                    Description = desc,
                    ActivityArea = area,
                    HomePitch = pitch,
                    EstablishedDate = new DateTime(2021, 5, 1),
                    IsPublic = true,
                    PlanTier = tier,
                    PlanExpiresAt = tier != PlanTier.Free ? DateTime.UtcNow.AddDays(30) : null,
                    AverageRating = rating,
                    ValidReviewCount = 3
                };
                db.Teams.Add(t);
                await db.SaveChangesAsync();
            }
            return t;
        }

        var saiGonUnited = await GetOrCreateClubAsync(
            "Sài Gòn United FC", "saigon-united-fc", "Quận 7, TP.HCM", "Sân bóng Phú Mỹ Hưng - Quận 7",
            "CLB bóng đá cộng đồng khu Nam Sài Gòn - Ban bật gắn kết, giao lưu lịch sự.", PlanTier.Plus, 4.8);
        var thuDuc = await GetOrCreateClubAsync(
            "Thủ Đức Warriors FC", "thu-duc-warriors-fc", "TP. Thủ Đức, TP.HCM", "Sân bóng Rạch Chiếc - Thủ Đức",
            "Đội bóng anh em kỹ sư TP. Thủ Đức - Đá nhiệt huyết, ưu tiên trọng tài chuẩn mực.", PlanTier.Pro, 4.7);
        var binhThanh = await GetOrCreateClubAsync(
            "Bình Thạnh Phủi FC", "binh-thanh-phui-fc", "Bình Thạnh, TP.HCM", "Sân bóng Thanh Đa - Bình Thạnh",
            "Đội phủi khu vực Bình Thạnh - Phong cách máu lửa, tranh chấp quyết liệt.", PlanTier.Free, 3.8);

        // Gán ban cán sự / Thành viên nếu chưa có
        await EnsureMembershipAsync(db, klb.Id, users.KlbOwner.Id, TeamRole.Owner, true);
        await EnsureMembershipAsync(db, klb.Id, users.KlbCaptain.Id, TeamRole.Manager, true);
        await EnsureMembershipAsync(db, klb.Id, users.KlbMember.Id, TeamRole.Member, false);
        await EnsureMembershipAsync(db, saoVang.Id, users.SaoVangCaptain.Id, TeamRole.Owner, true);
        await EnsureMembershipAsync(db, phoenix.Id, users.PhoenixCaptain.Id, TeamRole.Owner, true);
        await EnsureMembershipAsync(db, trangAn.Id, users.TrangAnCaptain.Id, TeamRole.Owner, true);
        await EnsureMembershipAsync(db, bachKhoa.Id, users.BachKhoaCaptain.Id, TeamRole.Owner, true);
        await EnsureMembershipAsync(db, saiGonUnited.Id, users.SaiGonUnitedCaptain.Id, TeamRole.Owner, true);
        await EnsureMembershipAsync(db, thuDuc.Id, users.ThuDucCaptain.Id, TeamRole.Owner, true);
        await EnsureMembershipAsync(db, binhThanh.Id, users.BinhThanhCaptain.Id, TeamRole.Owner, true);

        // Bổ sung đầy đủ cầu thủ cho từng đội
        await SeedSquadKlbAsync(db, klb.Id, users);
        await SeedSquadSaoVangAsync(db, saoVang.Id, users);
        await SeedSquadPhoenixAsync(db, phoenix.Id, users);
        await SeedSquadTrangAnAsync(db, trangAn.Id, users);
        await SeedSquadBachKhoaAsync(db, bachKhoa.Id, users);

        await SeedCompactSquadAsync(db, saiGonUnited.Id, new (string Name, int Kit, string Role, string Primary, string[] Sec, string? UserId, string Styles, string Foot)[]
        {
            ("Trần Bảo Long", 1, "Thủ môn", PositionCodes.GK, Array.Empty<string>(), null, "PROACTIVE_GK+,DEFLECTOR", "Chân phải"),
            ("Nguyễn Minh Triết", 4, "Trung vệ", PositionCodes.CB, new[] { PositionCodes.CDM }, null, "ANTICIPATE+,GUARDIAN", "Chân phải"),
            ("Lê Quốc Bảo", 5, "Hậu vệ cánh", PositionCodes.LB, new[] { PositionCodes.LM }, null, "RELENTLESS,WHIPPED_CROSS", "Chân trái"),
            ("Phạm Sài Gòn", 10, "Tiền đạo cắm", PositionCodes.ST, new[] { PositionCodes.CAM }, users.SaiGonUnitedCaptain.Id, "FINESSE+,CLINICAL,TRICKSTER", "Chân phải"),
            ("Võ Hoàng Nam", 8, "Tiền vệ trung tâm", PositionCodes.CM, new[] { PositionCodes.CAM }, null, "TIKI_TAKA+,INCISIVE", "Hai chân"),
            ("Đặng Gia Huy", 7, "Tiền vệ cánh", PositionCodes.RW, new[] { PositionCodes.LW }, null, "RAPID,CUT_INSIDE", "Chân phải")
        });

        await SeedCompactSquadAsync(db, thuDuc.Id, new (string Name, int Kit, string Role, string Primary, string[] Sec, string? UserId, string Styles, string Foot)[]
        {
            ("Lý Thanh Sơn", 1, "Thủ môn", PositionCodes.GK, Array.Empty<string>(), null, "DEFLECTOR+,GK_LONG_SHOT", "Chân phải"),
            ("Bùi Tấn Phát", 6, "Trung vệ", PositionCodes.CB, new[] { PositionCodes.RB }, null, "STOPPER+,INTERCEPTOR", "Chân phải"),
            ("Huỳnh Thủ Đức", 8, "Tiền vệ trung tâm", PositionCodes.CM, new[] { PositionCodes.CDM }, users.ThuDucCaptain.Id, "TIKI_TAKA+,BULLET_PASS,OFF_BALL", "Hai chân"),
            ("Phan Thanh Lộc", 19, "Tiền vệ tấn công", PositionCodes.CAM, new[] { PositionCodes.CM }, null, "MAESTRO,FIRST_TIME", "Chân trái"),
            ("Nguyễn Tấn Dũng", 9, "Tiền đạo cắm", PositionCodes.ST, new[] { PositionCodes.CF }, null, "POWER_SHOT+,CLINICAL", "Chân phải"),
            ("Trương Hoàng Phúc", 11, "Tiền đạo cánh", PositionCodes.LW, new[] { PositionCodes.LM }, null, "ACCELERATOR,TRICKSTER", "Chân phải")
        });

        await SeedCompactSquadAsync(db, binhThanh.Id, new (string Name, int Kit, string Role, string Primary, string[] Sec, string? UserId, string Styles, string Foot)[]
        {
            ("Mai Văn Tùng", 1, "Thủ môn", PositionCodes.GK, Array.Empty<string>(), null, "RUSH_OUT+,DEFLECTOR", "Chân phải"),
            ("Đoàn Bình Thạnh", 5, "Trung vệ", PositionCodes.CB, new[] { PositionCodes.CDM }, users.BinhThanhCaptain.Id, "BRUISER+,TACKLE_MASTER,RELENTLESS", "Chân phải"),
            ("Ngô Phước Thịnh", 2, "Hậu vệ cánh", PositionCodes.RB, new[] { PositionCodes.CB }, null, "SLIDE_TACKLE,MAN_MARKER", "Chân phải"),
            ("Trần Trọng Nghĩa", 14, "Tiền vệ phòng ngự", PositionCodes.CDM, new[] { PositionCodes.CM }, null, "INTERCEPTOR+,BRUISER", "Chân trái"),
            ("Lâm Văn Khải", 17, "Tiền vệ cánh", PositionCodes.RM, new[] { PositionCodes.RW }, null, "SUPER_SPRINT,POWER_SHOT", "Chân phải"),
            ("Hồ Minh Quân", 9, "Tiền đạo", PositionCodes.ST, new[] { PositionCodes.RW }, null, "PRECISION_HEAD+,VOLLEY", "Chân phải")
        });

        await db.SaveChangesAsync();

        return new SeededTeams
        {
            Klb = klb,
            SaoVang = saoVang,
            Phoenix = phoenix,
            TrangAn = trangAn,
            BachKhoa = bachKhoa,
            SaiGonUnited = saiGonUnited,
            ThuDuc = thuDuc,
            BinhThanh = binhThanh
        };
    }

    private static async Task EnsureMembershipAsync(ApplicationDbContext db, Guid teamId, string userId, TeamRole role, bool isBtc)
    {
        if (!await db.TeamMembers.AnyAsync(m => m.TeamId == teamId && m.UserId == userId))
        {
            db.TeamMembers.Add(new TeamMember
            {
                TeamId = teamId,
                UserId = userId,
                Role = role,
                IsBtc = isBtc
            });
        }
    }

    private static async Task AddOrUpdatePlayerAsync(
        ApplicationDbContext db,
        Guid teamId,
        string fullName,
        int kitNumber,
        string rawPosition,
        string stdPrimary,
        string[] stdSecondary,
        string? linkedUserId = null,
        string? shirtName = null,
        double? height = 175,
        double? weight = 70,
        string? playStyles = null,
        string? preferredFoot = null,
        string? experience = null)
    {
        var existing = await db.Players.FirstOrDefaultAsync(p => p.TeamId == teamId && p.FullName == fullName);
        var secJson = JsonSerializer.Serialize(stdSecondary);

        if (existing == null)
        {
            db.Players.Add(new Player
            {
                TeamId = teamId,
                FullName = fullName,
                KitNumber = kitNumber,
                Position = rawPosition,
                StandardPrimaryPosition = stdPrimary,
                AllowedSecondaryPositionsJson = secJson,
                LinkedUserId = linkedUserId,
                IsClaimed = !string.IsNullOrEmpty(linkedUserId),
                ShirtName = shirtName ?? fullName.ToUpperInvariant(),
                ShirtSize = "L",
                ShortSize = "L",
                HeightCm = height,
                WeightKg = weight,
                PlayStyles = playStyles,
                PreferredFoot = preferredFoot,
                Experience = experience,
                IsPublicProfile = true,
                PublicKitNumber = true,
                PublicPosition = true,
                PublicHeightWeight = true
            });
        }
        else
        {
            // Bảo toàn dữ liệu: Tuyệt đối không ghi đè số áo, vị trí hay thông tin người dùng đã chỉnh sửa
            if (linkedUserId != null && string.IsNullOrEmpty(existing.LinkedUserId))
            {
                existing.LinkedUserId = linkedUserId;
                existing.IsClaimed = true;
            }
            if (!string.IsNullOrWhiteSpace(playStyles) && string.IsNullOrWhiteSpace(existing.PlayStyles))
            {
                existing.PlayStyles = playStyles;
            }
            if (!string.IsNullOrWhiteSpace(preferredFoot) && string.IsNullOrWhiteSpace(existing.PreferredFoot))
            {
                existing.PreferredFoot = preferredFoot;
            }
            if (!string.IsNullOrWhiteSpace(experience) && string.IsNullOrWhiteSpace(existing.Experience))
            {
                existing.Experience = experience;
            }
        }
    }

    private static async Task SeedCompactSquadAsync(
        ApplicationDbContext db,
        Guid teamId,
        IEnumerable<(string Name, int Kit, string Role, string Primary, string[] Sec, string? UserId, string Styles, string Foot)> members)
    {
        foreach (var m in members)
        {
            await AddOrUpdatePlayerAsync(db, teamId, m.Name, m.Kit, m.Role, m.Primary, m.Sec, m.UserId, null, 175, 70, m.Styles, m.Foot, "Phủi phong trào");
        }
    }

    private static async Task SeedSquadKlbAsync(ApplicationDbContext db, Guid teamId, SeededUsers users)
    {
        // 16 cầu thủ - Đầy đủ 2 GK, 5 DF, 5 MF, 4 FW (kèm PlayStyle+ và PlayStyle thường chuẩn EA FC)
        await AddOrUpdatePlayerAsync(db, teamId, "Phạm Trung Kiên", 1, "Thủ môn", PositionCodes.GK, new[] { PositionCodes.CB }, null, "KIEN GK", 182, 76, "PROACTIVE_GK+,DEFLECTOR,RUSH_OUT", "Chân phải", "Phủi cứng");
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyễn Minh Hoàng", 25, "Thủ môn", PositionCodes.GK, new string[] { }, null, "HOANG GK", 180, 74, "GK_AERIAL+,GK_LONG_SHOT,DEFLECTOR", "Chân phải", "Phong trào");

        await AddOrUpdatePlayerAsync(db, teamId, "Vũ Đình Trọng", 5, "Trung vệ", PositionCodes.CB, new[] { PositionCodes.DF_L, PositionCodes.CDM }, null, "TRONG CB", 178, 73, "ANTICIPATE+,GUARDIAN,AERIAL_MASTER", "Chân phải", "Phủi cứng");
        await AddOrUpdatePlayerAsync(db, teamId, "Bùi Tiến Dũng", 4, "Trung vệ", PositionCodes.CB, new[] { PositionCodes.DF_R }, null, "TIEN DUNG", 179, 75, "STOPPER+,INTERCEPTOR,PRECISION_HEAD", "Chân trái", "Phủi cứng");
        await AddOrUpdatePlayerAsync(db, teamId, "Lê Cầu Thủ", 8, "Hậu vệ cánh trái", PositionCodes.LB, new[] { PositionCodes.CB, PositionCodes.LM }, users.KlbMember.Id, "LE THU", 175, 69, "GUARDIAN+,ANTICIPATE,RELENTLESS", "Chân trái", "Phong trào");
        await AddOrUpdatePlayerAsync(db, teamId, "Trần Văn Hậu", 2, "Hậu vệ cánh trái", PositionCodes.LB, new[] { PositionCodes.LM }, null, "VAN HAU", 174, 68, "SLIDE_TACKLE+,BRUISER,WHIPPED_CROSS", "Chân trái", "Phủi cứng");
        await AddOrUpdatePlayerAsync(db, teamId, "Đỗ Thanh Bình", 3, "Hậu vệ cánh phải", PositionCodes.RB, new[] { PositionCodes.RM, PositionCodes.CB }, null, "THANH BINH", 176, 70, "RELENTLESS+,TACKLE_MASTER,RAPID", "Chân phải", "Phong trào");

        await AddOrUpdatePlayerAsync(db, teamId, "Nguyễn Văn Chủ Tịch", 10, "Tiền vệ trung tâm", PositionCodes.CM, new[] { PositionCodes.CAM, PositionCodes.ST }, users.KlbOwner.Id, "VAN CHU TICH", 172, 68, "MAESTRO+,TIKI_TAKA,INCISIVE,FIRST_TIME", "Chân phải", "Bán chuyên / Phủi cứng");
        await AddOrUpdatePlayerAsync(db, teamId, "Đỗ Hùng Dũng", 16, "Tiền vệ phòng ngự", PositionCodes.CDM, new[] { PositionCodes.CM, PositionCodes.CB }, null, "HUNG DUNG", 174, 69, "RELENTLESS+,INTERCEPTOR,BULLET_PASS", "Chân phải", "Phủi cứng");
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyễn Quang Hải", 19, "Tiền vệ tấn công", PositionCodes.CAM, new[] { PositionCodes.CM, PositionCodes.RW }, null, "QUANG HAI", 168, 65, "FINESSE+,TIKI_TAKA,DEAD_BALL,ILLUSIONIST", "Chân trái", "Phủi cứng");
        await AddOrUpdatePlayerAsync(db, teamId, "Lương Xuân Trường", 6, "Tiền vệ trung tâm", PositionCodes.CM, new[] { PositionCodes.CAM, PositionCodes.CDM }, null, "XUAN TRUONG", 177, 71, "INCISIVE+,DRIVEN_LOB,BULLET_PASS", "Chân phải", "Phong trào");
        await AddOrUpdatePlayerAsync(db, teamId, "Phan Văn Đức", 20, "Tiền vệ cánh trái", PositionCodes.LM, new[] { PositionCodes.LW, PositionCodes.LB }, null, "VAN DUC", 173, 67, "OFF_BALL+,FIRST_TIME,CHIP_SHOT", "Chân phải", "Phong trào");

        await AddOrUpdatePlayerAsync(db, teamId, "Trần Đội Trưởng", 7, "Tiền đạo cánh phải", PositionCodes.RW, new[] { PositionCodes.RM, PositionCodes.ST }, users.KlbCaptain.Id, "DOI TRUONG", 175, 70, "CUT_INSIDE+,FINESSE,RAPID,ACCELERATOR", "Chân trái", "Phủi cứng");
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyễn Công Phượng", 11, "Tiền đạo cánh trái", PositionCodes.LW, new[] { PositionCodes.ST, PositionCodes.CAM }, null, "CONG PHUONG", 170, 66, "TRICKSTER+,ILLUSIONIST,SUPER_SPRINT", "Chân phải", "Phủi cứng");
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyễn Tiến Linh", 22, "Tiền đạo cắm", PositionCodes.ST, new[] { PositionCodes.CF }, null, "TIEN LINH", 183, 77, "CLINICAL+,PRECISION_HEAD,FIRST_TIME", "Chân phải", "Phủi cứng");
        await AddOrUpdatePlayerAsync(db, teamId, "Hà Đức Chinh", 9, "Tiền đạo cắm", PositionCodes.ST, new[] { PositionCodes.LW, PositionCodes.RW }, null, "DUC CHINH", 177, 74, "POWER_SHOT+,VOLLEY,RELENTLESS", "Chân phải", "Phong trào");
    }

    private static async Task SeedSquadSaoVangAsync(ApplicationDbContext db, Guid teamId, SeededUsers users)
    {
        // 15 cầu thủ Sao Vàng FC
        await AddOrUpdatePlayerAsync(db, teamId, "Đặng Văn Lâm", 1, "Thủ môn", PositionCodes.GK, new string[] { }, null, "VAN LAM", 188, 85);
        await AddOrUpdatePlayerAsync(db, teamId, "Trần Đình Trọng", 21, "Trung vệ", PositionCodes.CB, new[] { PositionCodes.CDM }, null, "DINH TRONG", 175, 70);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyễn Thành Chung", 16, "Trung vệ", PositionCodes.CB, new[] { PositionCodes.ST }, null, "THANH CHUNG", 182, 76);
        await AddOrUpdatePlayerAsync(db, teamId, "Hồ Tấn Tài", 13, "Hậu vệ cánh phải", PositionCodes.RB, new[] { PositionCodes.RM, PositionCodes.CB }, null, "TAN TAI", 180, 74);
        await AddOrUpdatePlayerAsync(db, teamId, "Vũ Văn Thanh", 17, "Hậu vệ cánh trái", PositionCodes.LB, new[] { PositionCodes.RB, PositionCodes.LM, PositionCodes.RM }, null, "VAN THANH", 171, 69);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyễn Phong Hồng Duy", 7, "Hậu vệ cánh trái", PositionCodes.LB, new[] { PositionCodes.LM, PositionCodes.LW }, null, "HONG DUY", 168, 65);

        await AddOrUpdatePlayerAsync(db, teamId, "Nguyễn Tuấn Anh", 8, "Tiền vệ trung tâm", PositionCodes.CM, new[] { PositionCodes.CDM, PositionCodes.CAM }, null, "TUAN ANH", 174, 67);
        await AddOrUpdatePlayerAsync(db, teamId, "Phạm Đức Huy", 15, "Tiền vệ phòng ngự", PositionCodes.CDM, new[] { PositionCodes.CM, PositionCodes.RB }, null, "DUC HUY", 173, 72);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyễn Hoàng Đức", 14, "Tiền vệ tấn công", PositionCodes.CAM, new[] { PositionCodes.CM, PositionCodes.ST }, null, "HOANG DUC", 184, 75);
        await AddOrUpdatePlayerAsync(db, teamId, "Triệu Việt Hưng", 97, "Tiền vệ trung tâm", PositionCodes.CM, new[] { PositionCodes.LM }, null, "VIET HUNG", 172, 66);
        await AddOrUpdatePlayerAsync(db, teamId, "Châu Ngọc Quang", 18, "Tiền vệ cánh phải", PositionCodes.RM, new[] { PositionCodes.CM, PositionCodes.RW }, null, "NGOC QUANG", 173, 68);

        await AddOrUpdatePlayerAsync(db, teamId, "Hoàng Sao Vàng", 9, "Tiền đạo cắm", PositionCodes.ST, new[] { PositionCodes.LW, PositionCodes.RW }, users.SaoVangCaptain.Id, "HOANG 9", 178, 73);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyễn Văn Toàn", 99, "Tiền đạo cánh phải", PositionCodes.RW, new[] { PositionCodes.RM, PositionCodes.ST }, null, "VAN TOAN", 170, 63);
        await AddOrUpdatePlayerAsync(db, teamId, "Phạm Tuấn Hải", 10, "Tiền đạo cắm", PositionCodes.ST, new[] { PositionCodes.LW, PositionCodes.CAM }, null, "TUAN HAI", 172, 68);
        await AddOrUpdatePlayerAsync(db, teamId, "Bùi Vĩ Hào", 11, "Tiền đạo cánh trái", PositionCodes.LW, new[] { PositionCodes.ST }, null, "VI HAO", 177, 70);
    }

    private static async Task SeedSquadPhoenixAsync(ApplicationDbContext db, Guid teamId, SeededUsers users)
    {
        // 15 cầu thủ Phoenix FC
        await AddOrUpdatePlayerAsync(db, teamId, "Vũ Phượng Hoàng", 1, "Thủ môn", PositionCodes.GK, new string[] { }, users.PhoenixCaptain.Id, "PHUONG HOANG", 184, 78);
        await AddOrUpdatePlayerAsync(db, teamId, "Lê Bá Nam", 23, "Thủ môn", PositionCodes.GK, new string[] { }, null, "BA NAM", 181, 75);

        await AddOrUpdatePlayerAsync(db, teamId, "Trần Kim Hùng", 3, "Trung vệ", PositionCodes.CB, new[] { PositionCodes.DF_L }, null, "KIM HUNG", 180, 77);
        await AddOrUpdatePlayerAsync(db, teamId, "Đặng Thế Tài", 4, "Trung vệ", PositionCodes.CB, new[] { PositionCodes.CDM }, null, "THE TAI", 179, 74);
        await AddOrUpdatePlayerAsync(db, teamId, "Ngô Văn Long", 12, "Hậu vệ cánh trái", PositionCodes.LB, new[] { PositionCodes.LM }, null, "VAN LONG", 172, 66);
        await AddOrUpdatePlayerAsync(db, teamId, "Tạ Đình Phong", 2, "Hậu vệ cánh phải", PositionCodes.RB, new[] { PositionCodes.RM }, null, "DINH PHONG", 174, 69);

        await AddOrUpdatePlayerAsync(db, teamId, "Phan Anh Vũ", 6, "Tiền vệ phòng ngự", PositionCodes.CDM, new[] { PositionCodes.CM }, null, "ANH VU", 176, 71);
        await AddOrUpdatePlayerAsync(db, teamId, "Lý Gia Huy", 8, "Tiền vệ trung tâm", PositionCodes.CM, new[] { PositionCodes.CAM }, null, "GIA HUY", 173, 67);
        await AddOrUpdatePlayerAsync(db, teamId, "Mai Xuân Quyết", 10, "Tiền vệ tấn công", PositionCodes.CAM, new[] { PositionCodes.ST, PositionCodes.LW }, null, "XUAN QUYET", 175, 68);
        await AddOrUpdatePlayerAsync(db, teamId, "Dương Quốc Bảo", 17, "Tiền vệ cánh trái", PositionCodes.LM, new[] { PositionCodes.LW }, null, "QUOC BAO", 171, 65);
        await AddOrUpdatePlayerAsync(db, teamId, "Lê Tuấn Kiệt", 19, "Tiền vệ cánh phải", PositionCodes.RM, new[] { PositionCodes.RW }, null, "TUAN KIET", 173, 68);

        await AddOrUpdatePlayerAsync(db, teamId, "Hoàng Nhật Minh", 7, "Tiền đạo cánh phải", PositionCodes.RW, new[] { PositionCodes.ST }, null, "NHAT MINH", 176, 70);
        await AddOrUpdatePlayerAsync(db, teamId, "Trương Hữu Thắng", 11, "Tiền đạo cánh trái", PositionCodes.LW, new[] { PositionCodes.LM }, null, "HUU THANG", 174, 67);
        await AddOrUpdatePlayerAsync(db, teamId, "Đào Mạnh Cường", 9, "Tiền đạo cắm", PositionCodes.ST, new[] { PositionCodes.CF }, null, "MANH CUONG", 182, 78);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyễn Hùng Sơn", 20, "Tiền đạo cắm", PositionCodes.ST, new string[] { }, null, "HUNG SON", 178, 72);
    }

    private static async Task SeedSquadTrangAnAsync(ApplicationDbContext db, Guid teamId, SeededUsers users)
    {
        // 14 cầu thủ Tràng An FC
        await AddOrUpdatePlayerAsync(db, teamId, "Đinh Tràng An", 1, "Thủ môn", PositionCodes.GK, new string[] { }, users.TrangAnCaptain.Id, "TRANG AN", 182, 76);
        await AddOrUpdatePlayerAsync(db, teamId, "Phạm Xuân Hưng", 12, "Thủ môn", PositionCodes.GK, new string[] { }, null, "XUAN HUNG", 180, 73);

        await AddOrUpdatePlayerAsync(db, teamId, "Vũ Văn Lương", 4, "Trung vệ", PositionCodes.CB, new[] { PositionCodes.CDM }, null, "VAN LUONG", 180, 75);
        await AddOrUpdatePlayerAsync(db, teamId, "Lê Hồng Quân", 5, "Trung vệ", PositionCodes.CB, new[] { PositionCodes.RB }, null, "HONG QUAN", 178, 74);
        await AddOrUpdatePlayerAsync(db, teamId, "Ngô Trọng Tấn", 3, "Hậu vệ cánh trái", PositionCodes.LB, new[] { PositionCodes.LM }, null, "TRONG TAN", 171, 66);
        await AddOrUpdatePlayerAsync(db, teamId, "Đỗ Đức Thịnh", 2, "Hậu vệ cánh phải", PositionCodes.RB, new[] { PositionCodes.RM }, null, "DUC THINH", 173, 68);

        await AddOrUpdatePlayerAsync(db, teamId, "Nguyễn Thái Sơn", 6, "Tiền vệ phòng ngự", PositionCodes.CDM, new[] { PositionCodes.CM }, null, "THAI SON", 174, 70);
        await AddOrUpdatePlayerAsync(db, teamId, "Trần Văn Cường", 8, "Tiền vệ trung tâm", PositionCodes.CM, new[] { PositionCodes.CAM }, null, "VAN CUONG", 172, 67);
        await AddOrUpdatePlayerAsync(db, teamId, "Lê Hữu Đạt", 10, "Tiền vệ tấn công", PositionCodes.CAM, new[] { PositionCodes.ST }, null, "HUU DAT", 175, 69);
        await AddOrUpdatePlayerAsync(db, teamId, "Hoàng Khắc Khiêm", 14, "Tiền vệ cánh trái", PositionCodes.LM, new[] { PositionCodes.LW }, null, "KHAC KHIEM", 170, 65);
        await AddOrUpdatePlayerAsync(db, teamId, "Võ Phi Hổ", 16, "Tiền vệ cánh phải", PositionCodes.RM, new[] { PositionCodes.RW }, null, "PHI HO", 174, 69);

        await AddOrUpdatePlayerAsync(db, teamId, "Đặng Quốc Hưng", 7, "Tiền đạo cánh phải", PositionCodes.RW, new[] { PositionCodes.ST }, null, "QUOC HUNG", 175, 71);
        await AddOrUpdatePlayerAsync(db, teamId, "Tạ Đình Hậu", 11, "Tiền đạo cánh trái", PositionCodes.LW, new[] { PositionCodes.LM }, null, "DINH HAU", 173, 68);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyễn Sỹ Chiến", 9, "Tiền đạo cắm", PositionCodes.ST, new[] { PositionCodes.CF }, null, "SY CHIEN", 181, 77);
    }

    private static async Task SeedSquadBachKhoaAsync(ApplicationDbContext db, Guid teamId, SeededUsers users)
    {
        // 14 cầu thủ FC Bách Khoa 2012
        await AddOrUpdatePlayerAsync(db, teamId, "Tạ Bách Khoa", 1, "Thủ môn", PositionCodes.GK, new string[] { }, users.BachKhoaCaptain.Id, "BACH KHOA", 183, 77);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyễn Đình Triệu", 25, "Thủ môn", PositionCodes.GK, new string[] { }, null, "DINH TRIEU", 181, 75);

        await AddOrUpdatePlayerAsync(db, teamId, "Lê Văn Sơn", 4, "Trung vệ", PositionCodes.CB, new[] { PositionCodes.RB }, null, "VAN SON", 176, 72);
        await AddOrUpdatePlayerAsync(db, teamId, "Trần Hữu Khôi", 5, "Trung vệ", PositionCodes.CB, new[] { PositionCodes.CDM }, null, "HUU KHOI", 179, 75);
        await AddOrUpdatePlayerAsync(db, teamId, "Hoàng Văn Khánh", 3, "Hậu vệ cánh trái", PositionCodes.LB, new[] { PositionCodes.LM }, null, "VAN KHANH", 172, 67);
        await AddOrUpdatePlayerAsync(db, teamId, "Bùi Hoàng Việt Anh", 20, "Hậu vệ cánh phải", PositionCodes.RB, new[] { PositionCodes.CB }, null, "VIET ANH", 184, 78);

        await AddOrUpdatePlayerAsync(db, teamId, "Trịnh Văn Lợi", 6, "Tiền vệ phòng ngự", PositionCodes.CDM, new[] { PositionCodes.CB }, null, "VAN LOI", 175, 70);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyễn Trọng Hoàng", 8, "Tiền vệ trung tâm", PositionCodes.CM, new[] { PositionCodes.RM, PositionCodes.RB }, null, "TRONG HOANG", 170, 68);
        await AddOrUpdatePlayerAsync(db, teamId, "Lê Huỳnh Đức", 10, "Tiền vệ tấn công", PositionCodes.CAM, new[] { PositionCodes.ST }, null, "HUYNH DUC", 178, 73);
        await AddOrUpdatePlayerAsync(db, teamId, "Đặng Anh Tuấn", 18, "Tiền vệ cánh trái", PositionCodes.LM, new[] { PositionCodes.LW }, null, "ANH TUAN", 172, 66);
        await AddOrUpdatePlayerAsync(db, teamId, "Phạm Xuân Mạnh", 7, "Tiền vệ cánh phải", PositionCodes.RM, new[] { PositionCodes.RB }, null, "XUAN MANH", 175, 70);

        await AddOrUpdatePlayerAsync(db, teamId, "Nguyễn Việt Phong", 17, "Tiền đạo cánh phải", PositionCodes.RW, new[] { PositionCodes.ST }, null, "VIET PHONG", 174, 69);
        await AddOrUpdatePlayerAsync(db, teamId, "Lâm Ti Phông", 19, "Tiền đạo cánh trái", PositionCodes.LW, new[] { PositionCodes.LM }, null, "TI PHONG", 169, 64);
        await AddOrUpdatePlayerAsync(db, teamId, "Hà Minh Tuấn", 9, "Tiền đạo cắm", PositionCodes.ST, new[] { PositionCodes.CF }, null, "MINH TUAN", 182, 76);
    }

    private static async Task EnsureRegulationsAsync(ApplicationDbContext db, SeededTeams teams)
    {
        if (!await db.TeamRegulations.AnyAsync(r => r.TeamId == teams.Klb.Id))
        {
            db.TeamRegulations.Add(new TeamRegulation
            {
                TeamId = teams.Klb.Id,
                Version = 1,
                Title = "Quy chế chuyên cần KLB FC (Mẫu thực tế)",
                FineNoResponse = 30000m,
                FineAttendingButAbsent = 50000m,
                MinMatchesPerMonth = 2,
                FineUnderMinAttendance = 200000m,
                QuarterlyFundDue = 300000m,
                IsExcusedExemptFromFine = true,
                DefaultDeadlineTimeOfDay = new TimeSpan(12, 0, 0)
            });
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureSessionsAndAttendanceAsync(ApplicationDbContext db, SeededTeams teams)
    {
        // Kiểm tra xem đã có buổi thi đấu cho KLB chưa
        var existingSession = await db.AttendanceSessions.FirstOrDefaultAsync(s => s.TeamId == teams.Klb.Id);
        if (existingSession == null)
        {
            existingSession = new AttendanceSession
            {
                TeamId = teams.Klb.Id,
                Title = "Trận đấu hằng tuần - Thứ Tư (Sân 7)",
                SessionDateTime = DateTime.Today.AddHours(19).AddMinutes(30),
                ResponseDeadline = DateTime.Today.AddHours(12),
                StatisticalPeriod = DateTime.Today.ToString("yyyy-MM"),
                Status = SessionStatus.Draft,
                PitchName = "Sân bóng PVV - 72 Trần Thái Tông",
                PitchType = "7 người",
                Notes = "Mọi người phản hồi trước 12h trưa để chốt quân số và chia áo chia sân."
            };
            db.AttendanceSessions.Add(existingSession);
            await db.SaveChangesAsync();
        }

        // Đảm bảo toàn bộ 16 cầu thủ của KLB đều có bản ghi điểm danh
        var klbPlayers = await db.Players.Where(p => p.TeamId == teams.Klb.Id).ToListAsync();
        var existingRecords = await db.AttendanceRecords.Where(r => r.SessionId == existingSession.Id).ToListAsync();
        var existingPlayerIds = existingRecords.Select(r => r.PlayerId).ToHashSet();

        foreach (var player in klbPlayers)
        {
            if (!existingPlayerIds.Contains(player.Id))
            {
                db.AttendanceRecords.Add(new AttendanceRecord
                {
                    SessionId = existingSession.Id,
                    PlayerId = player.Id,
                    Response = ResponseStatus.Attending,
                    Actual = ActualStatus.Present,
                    FineAmount = 0m,
                    PenaltyReason = "Có mặt đúng cam kết"
                });
            }
        }
        await db.SaveChangesAsync();
    }

    private static async Task EnsureMatchmakingAndMatchesAsync(ApplicationDbContext db, SeededTeams teams)
    {
        // Bài đăng tìm đối của KLB
        if (!await db.MatchmakingPosts.AnyAsync(m => m.TeamId == teams.Klb.Id))
        {
            db.MatchmakingPosts.Add(new MatchmakingPost
            {
                TeamId = teams.Klb.Id,
                MatchDate = DateTime.Today.AddDays(3),
                StartTime = new TimeSpan(19, 30, 0),
                EndTime = new TimeSpan(21, 0, 0),
                Area = "Cầu Giấy, Hà Nội",
                PitchArrangement = PitchArrangement.HomePitch,
                PitchName = "Sân bóng PVV - Trần Thái Tông",
                IsPitchBooked = true,
                PitchType = "7 người",
                DesiredLevel = "Trung bình - Khá",
                CostSharing = "50-50 (Chia đôi tiền sân)",
                Notes = "Cần tìm đối tác giao lưu văn minh, không chơi xấu, sau trận giao lưu bia cỏ.",
                Status = MatchmakingStatus.Open
            });
        }

        // Bài đăng tìm đối của Phoenix FC
        if (!await db.MatchmakingPosts.AnyAsync(m => m.TeamId == teams.Phoenix.Id))
        {
            db.MatchmakingPosts.Add(new MatchmakingPost
            {
                TeamId = teams.Phoenix.Id,
                MatchDate = DateTime.Today.AddDays(5),
                StartTime = new TimeSpan(20, 0, 0),
                EndTime = new TimeSpan(21, 30, 0),
                Area = "Thanh Xuân, Hà Nội",
                PitchArrangement = PitchArrangement.HomePitch,
                PitchName = "Sân bóng Bộ Công An - Nguyễn Xiển",
                IsPitchBooked = true,
                PitchType = "7 người",
                DesiredLevel = "Khá",
                CostSharing = "50-50",
                Notes = "Phoenix FC tìm đối giao lưu tối cuối tuần, ưu tiên đội hình trẻ khỏe, nhiệt huyết.",
                Status = MatchmakingStatus.Open
            });
        }

        // Thành tích
        if (!await db.CompetitionAchievements.AnyAsync(c => c.TeamId == teams.Klb.Id))
        {
            db.CompetitionAchievements.Add(new CompetitionAchievement
            {
                TeamId = teams.Klb.Id,
                TournamentName = "Cúp Doanh Nghiệp Mở Rộng 2025",
                SeasonOrYear = "2025",
                TitleOrRank = "Vô Địch",
                IsFeatured = true,
                IsVerifiedOnPlatform = true
            });
        }

        // Trận đấu mẫu giữa các đội
        if (!await db.Matches.AnyAsync(m => m.TeamAId == teams.Klb.Id && m.TeamBId == teams.SaoVang.Id))
        {
            var upcomingMatch = new MatchGame
            {
                TeamAId = teams.Klb.Id,
                TeamBId = teams.SaoVang.Id,
                MatchDateTime = DateTime.Today.AddDays(2).AddHours(19).AddMinutes(30),
                PitchName = "Sân bóng PVV - 72 Trần Thái Tông",
                PitchType = "7 người",
                Category = MatchCategory.Friendly,
                Status = MatchStatus.Scheduled,
                IsPublic = true,
                Notes = "Trận giao hữu lượt đi giữa KLB FC và Sao Vàng FC"
            };
            db.Matches.Add(upcomingMatch);
        }

        if (!await db.Matches.AnyAsync(m => m.TeamAId == teams.Klb.Id && m.TeamBId == teams.Phoenix.Id))
        {
            var completedMatch = new MatchGame
            {
                TeamAId = teams.Klb.Id,
                TeamBId = teams.Phoenix.Id,
                MatchDateTime = DateTime.Today.AddDays(-5).AddHours(19).AddMinutes(30),
                PitchName = "Sân bóng PVV - 72 Trần Thái Tông",
                PitchType = "7 người",
                Category = MatchCategory.Friendly,
                Status = MatchStatus.Completed,
                ScoreA = 3,
                ScoreB = 2,
                ConfirmedByTeamA = true,
                ConfirmedByTeamB = true,
                ConfirmedAt = DateTime.Today.AddDays(-5).AddHours(22),
                IsPublic = true,
                Notes = "Trận thư hùng kịch tính giữa KLB FC và Phoenix FC"
            };
            db.Matches.Add(completedMatch);
        }

        if (!await db.Matches.AnyAsync(m => m.TeamAId == teams.TrangAn.Id && m.TeamBId == teams.BachKhoa.Id))
        {
            var derbyMatch = new MatchGame
            {
                TeamAId = teams.TrangAn.Id,
                TeamBId = teams.BachKhoa.Id,
                MatchDateTime = DateTime.Today.AddDays(-3).AddHours(20),
                PitchName = "Sân Thủy Lợi",
                PitchType = "7 người",
                Category = MatchCategory.Friendly,
                Status = MatchStatus.Completed,
                ScoreA = 2,
                ScoreB = 2,
                ConfirmedByTeamA = true,
                ConfirmedByTeamB = true,
                ConfirmedAt = DateTime.Today.AddDays(-3).AddHours(22),
                IsPublic = true,
                Notes = "Trận derby giao lưu giữa Tràng An FC và FC Bách Khoa 2012"
            };
            db.Matches.Add(derbyMatch);
        }

        if (!await db.MatchmakingPosts.AnyAsync(m => m.TeamId == teams.SaiGonUnited.Id))
        {
            db.MatchmakingPosts.Add(new MatchmakingPost
            {
                TeamId = teams.SaiGonUnited.Id,
                MatchDate = CompetitionRules.LocalNow.Date.AddDays(4),
                StartTime = new TimeSpan(19, 30, 0),
                EndTime = new TimeSpan(21, 0, 0),
                Area = "Quận 7, TP.HCM",
                PitchArrangement = PitchArrangement.HomePitch,
                PitchName = "Sân bóng Phú Mỹ Hưng - Quận 7",
                IsPitchBooked = true,
                PitchType = "7 người",
                DesiredLevel = "Trung bình - Khá",
                CostSharing = "50-50 (Chia đôi tiền sân)",
                Notes = "Sài Gòn United tìm đối ban bật kỹ thuật tối thứ 5 tại Quận 7.",
                Status = MatchmakingStatus.Open
            });
        }

        // Bổ sung chuỗi trận giao hữu xác nhận 2 chiều (>= 10 trận để mở khóa BXH Sao Aloball & Vibe cộng đồng)
        var todayLocal = CompetitionRules.LocalNow.Date;
        var extraRankedSpecs = new (Team TeamA, Team TeamB, int DaysAgo, string Pitch, int ScoreA, int ScoreB, string Note)[]
        {
            (teams.Klb, teams.BachKhoa, 7, "Sân bóng PVV - 72 Trần Thái Tông", 2, 1, "[SEED] KLB FC vs FC Bách Khoa 2012"),
            (teams.SaoVang, teams.Phoenix, 9, "Sân bóng Thành Phát", 4, 1, "[SEED] Sao Vàng FC vs Phoenix FC"),
            (teams.Klb, teams.TrangAn, 11, "Sân bóng PVV - 72 Trần Thái Tông", 1, 1, "[SEED] KLB FC vs Tràng An FC"),
            (teams.SaoVang, teams.BachKhoa, 13, "Sân vận động Bách Khoa", 2, 0, "[SEED] Sao Vàng FC vs FC Bách Khoa 2012"),
            (teams.Phoenix, teams.TrangAn, 15, "Sân Thủy Lợi", 3, 1, "[SEED] Phoenix FC vs Tràng An FC"),
            (teams.SaoVang, teams.Klb, 17, "Sân bóng Thành Phát", 1, 2, "[SEED] Sao Vàng FC vs KLB FC"),
            (teams.SaiGonUnited, teams.ThuDuc, 6, "Sân bóng Phú Mỹ Hưng - Quận 7", 2, 2, "[SEED] Sài Gòn United vs Thủ Đức Warriors L1"),
            (teams.SaiGonUnited, teams.BinhThanh, 8, "Sân bóng Phú Mỹ Hưng - Quận 7", 3, 1, "[SEED] Sài Gòn United vs Bình Thạnh Phủi"),
            (teams.ThuDuc, teams.BinhThanh, 10, "Sân bóng Rạch Chiếc - Thủ Đức", 4, 2, "[SEED] Thủ Đức Warriors vs Bình Thạnh Phủi"),
            (teams.ThuDuc, teams.SaiGonUnited, 14, "Sân bóng Rạch Chiếc - Thủ Đức", 3, 2, "[SEED] Thủ Đức Warriors vs Sài Gòn United L2")
        };

        foreach (var spec in extraRankedSpecs)
        {
            if (!await db.Matches.AnyAsync(m => m.Notes == spec.Note))
            {
                db.Matches.Add(new MatchGame
                {
                    TeamAId = spec.TeamA.Id,
                    TeamBId = spec.TeamB.Id,
                    MatchDateTime = todayLocal.AddDays(-spec.DaysAgo).AddHours(19).AddMinutes(30),
                    PitchName = spec.Pitch,
                    PitchType = "7 người",
                    Category = MatchCategory.Friendly,
                    Status = MatchStatus.Completed,
                    ScoreA = spec.ScoreA,
                    ScoreB = spec.ScoreB,
                    ConfirmedByTeamA = true,
                    ConfirmedByTeamB = true,
                    ConfirmedAt = todayLocal.AddDays(-spec.DaysAgo).AddHours(21).AddMinutes(30),
                    IsPublic = true,
                    Notes = spec.Note
                });
            }
        }

        await db.SaveChangesAsync();

        // Đánh giá Playstyle Vibe sau trận (an toàn unique index MatchId + ReviewerTeamId)
        var completedRanked = await db.Matches
            .Where(m => m.Category == MatchCategory.Friendly && m.Status == MatchStatus.Completed && m.TeamBId.HasValue)
            .ToListAsync();

        string ResolveVibesForTarget(Guid targetId)
        {
            if (targetId == teams.Klb.Id) return $"{PlayStyleCatalog.VibeDaoDuongSinh},{PlayStyleCatalog.VibeVuiVe}";
            if (targetId == teams.SaoVang.Id || targetId == teams.SaiGonUnited.Id) return $"{PlayStyleCatalog.VibeBanBat},{PlayStyleCatalog.VibeVuiVe}";
            if (targetId == teams.ThuDuc.Id) return $"{PlayStyleCatalog.VibeBanBat},{PlayStyleCatalog.VibeTheLuc}";
            if (targetId == teams.TrangAn.Id || targetId == teams.BachKhoa.Id) return $"{PlayStyleCatalog.VibeDaoDuongSinh},{PlayStyleCatalog.VibeBanBat}";
            return $"{PlayStyleCatalog.VibeDaRat},{PlayStyleCatalog.VibeTheLuc}";
        }

        int ResolveStarsForTarget(Guid targetId) =>
            targetId == teams.BinhThanh.Id ? 4 : 5;

        var existingReviewKeys = (await db.TeamMatchReviews
            .Select(r => new { r.MatchId, r.ReviewerTeamId })
            .ToListAsync())
            .Select(x => (x.MatchId, x.ReviewerTeamId))
            .ToHashSet();

        var reviewedTargetIds = new HashSet<Guid>();
        foreach (var cm in completedRanked)
        {
            var tb = cm.TeamBId!.Value;
            if (!existingReviewKeys.Contains((cm.Id, cm.TeamAId)))
            {
                db.TeamMatchReviews.Add(new TeamMatchReview
                {
                    MatchId = cm.Id,
                    ReviewerTeamId = cm.TeamAId,
                    TargetTeamId = tb,
                    SubmittedByUserId = "system-seed",
                    RatingStars = ResolveStarsForTarget(tb),
                    Comment = "Giao lưu đúng giờ, tinh thần thể thao tốt.",
                    PlayStyleVibeTags = ResolveVibesForTarget(tb),
                    IsValid = true
                });
                existingReviewKeys.Add((cm.Id, cm.TeamAId));
                reviewedTargetIds.Add(tb);
            }

            if (!existingReviewKeys.Contains((cm.Id, tb)))
            {
                db.TeamMatchReviews.Add(new TeamMatchReview
                {
                    MatchId = cm.Id,
                    ReviewerTeamId = tb,
                    TargetTeamId = cm.TeamAId,
                    SubmittedByUserId = "system-seed",
                    RatingStars = ResolveStarsForTarget(cm.TeamAId),
                    Comment = "Đối thủ lịch sự, phối hợp fair-play.",
                    PlayStyleVibeTags = ResolveVibesForTarget(cm.TeamAId),
                    IsValid = true
                });
                existingReviewKeys.Add((cm.Id, tb));
                reviewedTargetIds.Add(cm.TeamAId);
            }
        }

        if (reviewedTargetIds.Count > 0)
        {
            await db.SaveChangesAsync();
            foreach (var tid in reviewedTargetIds)
            {
                var teamEntity = await db.Teams.FindAsync(tid);
                if (teamEntity == null) continue;
                var stats = await db.TeamMatchReviews
                    .Where(r => r.TargetTeamId == tid && r.IsValid)
                    .Select(r => r.RatingStars)
                    .ToListAsync();
                if (stats.Count > 0)
                {
                    teamEntity.ValidReviewCount = stats.Count;
                    teamEntity.AverageRating = Math.Round(stats.Average(), 2);
                }
            }
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureRefereesAsync(ApplicationDbContext db)
    {
        if (await db.Referees.AnyAsync()) return;

        var referees = new List<Referee>
        {
            new Referee
            {
                Name = "Nguyễn Văn Tuấn",
                ContactInfo = "0912.345.678 - tuan.ref@aloball.vn",
                ExperienceSummary = "Trọng tài cấp Quốc gia C2, 5 năm bắt giải phong trào Hà Nội, điều hành hơn 200 trận.",
                SupportedFormats = "7 người, 11 người",
                ServiceNotes = "Bắt nhiệt tình, nghiêm khắc, kiểm soát tốt các tình huống tranh chấp.",
                DefaultPriceVND = 350000,
                IsActive = true
            },
            new Referee
            {
                Name = "Trần Đình Trọng",
                ContactInfo = "0987.654.321 - trong.ref@aloball.vn",
                ExperienceSummary = "Trọng tài Futsal & sân 7 VFF, kinh nghiệm điều hành giải các doanh nghiệp.",
                SupportedFormats = "5 người, 7 người",
                ServiceNotes = "Di chuyển linh hoạt, xử lý chính xác luật bóng đá mini 7 người.",
                DefaultPriceVND = 300000,
                IsActive = true
            },
            new Referee
            {
                Name = "Lê Minh Đức",
                ContactInfo = "0903.112.233 - duc.ref@aloball.vn",
                ExperienceSummary = "Cựu cầu thủ sinh viên ĐHQG, chứng chỉ trọng tài sơ cấp, chuyên sân 7.",
                SupportedFormats = "7 người",
                ServiceNotes = "Thân thiện, giải thích luật rõ ràng, hỗ trợ ghi biên bản trận đấu.",
                DefaultPriceVND = 250000,
                IsActive = true
            },
            new Referee
            {
                Name = "Hoàng Quốc Việt",
                ContactInfo = "0944.888.999 - viet.ref@aloball.vn",
                ExperienceSummary = "Trọng tài sân 11 & sân 7, 7 năm kinh nghiệm các giải phủi miền Bắc.",
                SupportedFormats = "7 người, 11 người",
                ServiceNotes = "Chuyên bắt các trận cầu căng thẳng, tính chất cạnh tranh cao.",
                DefaultPriceVND = 400000,
                IsActive = true
            }
        };

        db.Referees.AddRange(referees);
        await db.SaveChangesAsync();
    }

    private static async Task EnsurePitchVenuesAsync(ApplicationDbContext db)
    {
        var existingNames = (await db.PitchVenues.Select(v => v.Name).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var venues = new List<PitchVenue>
        {
            new PitchVenue
            {
                Name = "Sân bóng Thành Phát",
                Description = "Cụm sân cỏ nhân tạo hiện đại bậc nhất khu vực Cầu Giấy, hệ thống chiếu sáng chuẩn và thoát nước cực tốt.",
                City = "Hà Nội",
                District = "Cầu Giấy",
                FullAddress = "Số 2 Hoàng Minh Giám, Trung Hòa, Cầu Giấy, Hà Nội",
                ContactPhone = "0912.345.888",
                SupportedFormats = "5 người, 7 người",
                SurfaceType = "Cỏ nhân tạo tiêu chuẩn FIFA",
                Facilities = "Đèn LED cao cấp, Bãi đỗ xe ô tô rộng, Nước uống miễn phí, Phòng thay đồ",
                OpeningHours = "06:00 - 23:30",
                IndicativePricePerHour = 450000,
                PriceNotes = "Khung giờ vàng (17:30 - 20:30): 650.000đ/90p; Ngày thường: 450.000đ/90p",
                ImageUrl = "https://images.unsplash.com/photo-1529900241477-94a53e3ba439?auto=format&fit=crop&w=800&q=80",
                MapLink = "https://maps.google.com/?q=Số+2+Hoàng+Minh+Giám+Cầu+Giấy+Hà+Nội",
                TotalPitches = 4,
                IsActive = true
            },
            new PitchVenue
            {
                Name = "Sân bóng Thủy Lợi",
                Description = "Cụm sân trong khuôn viên Đại học Thủy Lợi, mặt cỏ mềm, khán đài mini có mái che, vị trí trung tâm.",
                City = "Hà Nội",
                District = "Đống Đa",
                FullAddress = "Ngõ 95 Chùa Bộc, Trung Liệt, Đống Đa, Hà Nội",
                ContactPhone = "0988.777.666",
                SupportedFormats = "7 người, 11 người",
                SurfaceType = "Cỏ nhân tạo sợi đay cao cấp",
                Facilities = "Khán đài mini, Căn tin nước giải khát, Tắm nóng lạnh, Gửi xe trường học",
                OpeningHours = "06:00 - 23:00",
                IndicativePricePerHour = 400000,
                PriceNotes = "Khung giờ tối: 600.000đ/trận 90p; Cuối tuần phụ thu 50.000đ",
                ImageUrl = "https://images.unsplash.com/photo-1574629810360-7efbbe195018?auto=format&fit=crop&w=800&q=80",
                MapLink = "https://maps.google.com/?q=Ngõ+95+Chùa+Bộc+Đống+Đa+Hà+Nội",
                TotalPitches = 6,
                IsActive = true
            },
            new PitchVenue
            {
                Name = "Sân bóng PVV - Vinaconex 39",
                Description = "Sân bóng quen thuộc của giới phủi Hà thành, không gian thoáng đãng, mặt sân được bảo dưỡng định kỳ.",
                City = "Hà Nội",
                District = "Cầu Giấy",
                FullAddress = "Số 72 Trần Thái Tông, Dịch Vọng Hậu, Cầu Giấy, Hà Nội",
                ContactPhone = "0904.123.456",
                SupportedFormats = "7 người",
                SurfaceType = "Cỏ nhân tạo monofilament",
                Facilities = "Bãi đỗ ô tô xe máy, Đèn chiếu sáng chống chói, Cho thuê giày & áo tập",
                OpeningHours = "06:00 - 22:30",
                IndicativePricePerHour = 500000,
                PriceNotes = "Giá cố định 700.000đ/trận 90p giờ cao điểm",
                ImageUrl = "https://images.unsplash.com/photo-1508098682722-e99c43a406b2?auto=format&fit=crop&w=800&q=80",
                MapLink = "https://maps.google.com/?q=72+Trần+Thái+Tông+Cầu+Giấy+Hà+Nội",
                TotalPitches = 3,
                IsActive = true
            },
            new PitchVenue
            {
                Name = "Sân vận động Bách Khoa",
                Description = "Sân vận động kích thước tiêu chuẩn thi đấu 11 người hoặc chia thành 8 sân 7 người, khán đài A-B sức chứa lớn.",
                City = "Hà Nội",
                District = "Hai Bà Trưng",
                FullAddress = "Số 1 Đại Cồ Việt, Bách Khoa, Hai Bà Trưng, Hà Nội",
                ContactPhone = "0936.888.999",
                SupportedFormats = "7 người, 11 người",
                SurfaceType = "Cỏ tự nhiên & Cỏ nhân tạo",
                Facilities = "Khán đài có mái che, Phòng thay đồ VIP, Hệ thống âm thanh & bảng điện tử",
                OpeningHours = "06:00 - 22:00",
                IndicativePricePerHour = 600000,
                PriceNotes = "Sân 7: 650.000đ/90p; Sân 11 người: 1.800.000đ/120p",
                ImageUrl = "https://images.unsplash.com/photo-1489944445301-4475b3d14996?auto=format&fit=crop&w=800&q=80",
                MapLink = "https://maps.google.com/?q=Sân+vận+động+Bách+Khoa+Hà+Nội",
                TotalPitches = 8,
                IsActive = true
            },
            new PitchVenue
            {
                Name = "Sân bóng Phú Mỹ Hưng - Quận 7",
                Description = "Cụm sân 7 người cao cấp khu Nam Sài Gòn, mặt cỏ FIFA Quality thoáng mát và bãi đỗ xe rộng.",
                City = "TP.HCM",
                District = "Quận 7",
                FullAddress = "Đường Tân Phú, Tân Phú, Quận 7, TP.HCM",
                ContactPhone = "0909.333.777",
                SupportedFormats = "5 người, 7 người",
                SurfaceType = "Cỏ nhân tạo FIFA Quality",
                Facilities = "Đèn LED chuẩn, Căng tin nước lạnh, Tủ đồ bảo vệ",
                OpeningHours = "06:00 - 23:00",
                IndicativePricePerHour = 550000,
                PriceNotes = "Giờ vàng buổi tối: 650.000đ/90p",
                ImageUrl = "https://images.unsplash.com/photo-1529900241477-94a53e3ba439?auto=format&fit=crop&w=800&q=80",
                MapLink = "https://maps.google.com/?q=Sân+bóng+Phú+Mỹ+Hưng+Quận+7+TP.HCM",
                TotalPitches = 6,
                IsActive = true
            },
            new PitchVenue
            {
                Name = "Sân bóng Thanh Đa - Bình Thạnh",
                Description = "Điểm hẹn bóng đá phủi quen thuộc khu Bình Thạnh - Thủ Đức, sân rộng và dễ di chuyển.",
                City = "TP.HCM",
                District = "Bình Thạnh",
                FullAddress = "1017 Bình Quới, Phường 28, Bình Thạnh, TP.HCM",
                ContactPhone = "0918.222.555",
                SupportedFormats = "7 người",
                SurfaceType = "Cỏ nhân tạo sợi tổng hợp",
                Facilities = "Gửi xe máy ô tô, Nước uống, Thuê áo bib tập",
                OpeningHours = "06:00 - 22:30",
                IndicativePricePerHour = 500000,
                PriceNotes = "500.000đ - 600.000đ/90p",
                ImageUrl = "https://images.unsplash.com/photo-1574629810360-7efbbe195018?auto=format&fit=crop&w=800&q=80",
                MapLink = "https://maps.google.com/?q=1017+Bình+Quới+Bình+Thạnh+TP.HCM",
                TotalPitches = 4,
                IsActive = true
            }
        };

        var missingVenues = venues.Where(v => !existingNames.Contains(v.Name)).ToList();
        if (missingVenues.Count > 0)
        {
            db.PitchVenues.AddRange(missingVenues);
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureLoanMarketAndRecurringSchedulesAsync(ApplicationDbContext db, SeededTeams teams, SeededUsers users)
    {
        var scheduleSpecs = new (Guid TeamId, int DaysMask, string Region, bool AvoidHardTackle)[]
        {
            (teams.SaoVang.Id, (1 << 2) | (1 << 4), "Cầu Giấy, Hà Nội", true),
            (teams.SaiGonUnited.Id, (1 << 2) | (1 << 4) | (1 << 6), "TP.HCM", true),
            (teams.ThuDuc.Id, (1 << 2) | (1 << 4) | (1 << 6), "TP.HCM", true)
        };

        bool scheduleAdded = false;
        foreach (var s in scheduleSpecs)
        {
            if (!await db.TeamRecurringSchedules.AnyAsync(x => x.TeamId == s.TeamId))
            {
                db.TeamRecurringSchedules.Add(new TeamRecurringSchedule
                {
                    TeamId = s.TeamId,
                    IsAutoPairingEnabled = true,
                    DaysOfWeekMask = s.DaysMask,
                    PreferredKickoffTime = new TimeSpan(19, 30, 0),
                    Region = s.Region,
                    MinAloballStars = 0,
                    MaxAloballStars = 40,
                    MinFriendlyRating = 3.5,
                    AvoidHardTackle = s.AvoidHardTackle,
                    UpdatedAt = DateTime.UtcNow
                });
                scheduleAdded = true;
            }
        }
        if (scheduleAdded) await db.SaveChangesAsync();

        if (!await db.PlayerLoanListings.AnyAsync())
        {
            var localToday = CompetitionRules.LocalNow.Date;
            var klbMemberPlayer = await db.Players.FirstOrDefaultAsync(p => p.LinkedUserId == users.KlbMember.Id);
            var sguCaptainPlayer = await db.Players.FirstOrDefaultAsync(p => p.LinkedUserId == users.SaiGonUnitedCaptain.Id);

            db.PlayerLoanListings.AddRange(
                new PlayerLoanListing
                {
                    ListingType = LoanListingType.TeamRecruiting,
                    UserId = users.KlbOwner.Id,
                    TeamId = teams.Klb.Id,
                    Region = "Cầu Giấy, Hà Nội",
                    PitchName = "Sân bóng PVV - 72 Trần Thái Tông",
                    PlayDate = localToday.AddDays(2),
                    TimeSlot = "19:30 - 21:00",
                    PositionsNeeded = "GK",
                    FeeType = LoanFeeType.SplitPitch,
                    LoanFeeAmount = 0m,
                    EscrowAbCoin = 20,
                    Notes = "KLB FC cần mượn 1 thủ môn bắt giao hữu tối thứ 5, share tiền sân 0đ, anh em đá vui vẻ.",
                    Status = LoanListingStatus.Open
                },
                new PlayerLoanListing
                {
                    ListingType = LoanListingType.TeamRecruiting,
                    UserId = users.PhoenixCaptain.Id,
                    TeamId = teams.Phoenix.Id,
                    Region = "Thanh Xuân, Hà Nội",
                    PitchName = "Sân bóng Bộ Công An - Nguyễn Xiển",
                    PlayDate = localToday.AddDays(3),
                    TimeSlot = "20:00 - 21:30",
                    PositionsNeeded = "DEF",
                    FeeType = LoanFeeType.PaidMercenary,
                    LoanFeeAmount = 200_000m,
                    EscrowAbCoin = 30,
                    Notes = "Phoenix FC thiếu 1 trung vệ cứng đá trận giao hữu căng, hỗ trợ 200k + miễn tiền sân.",
                    Status = LoanListingStatus.Open
                },
                new PlayerLoanListing
                {
                    ListingType = LoanListingType.TeamRecruiting,
                    UserId = users.ThuDucCaptain.Id,
                    TeamId = teams.ThuDuc.Id,
                    Region = "TP. Thủ Đức, TP.HCM",
                    PitchName = "Sân bóng Rạch Chiếc - Thủ Đức",
                    PlayDate = localToday.AddDays(4),
                    TimeSlot = "19:30 - 21:00",
                    PositionsNeeded = "FWD",
                    FeeType = LoanFeeType.PaidMercenary,
                    LoanFeeAmount = 250_000m,
                    EscrowAbCoin = 20,
                    Notes = "Thủ Đức Warriors tuyển gấp 1 tiền đạo cắm săn bàn cuối tuần, bồi dưỡng 250k.",
                    Status = LoanListingStatus.Open
                },
                new PlayerLoanListing
                {
                    ListingType = LoanListingType.PlayerAvailable,
                    UserId = users.KlbMember.Id,
                    PlayerId = klbMemberPlayer?.Id,
                    TeamId = teams.Klb.Id,
                    Region = "Cầu Giấy, Hà Nội",
                    PitchName = "Khu vực Cầu Giấy / Mỹ Đình",
                    PlayDate = localToday.AddDays(3),
                    TimeSlot = "19:00 - 21:00",
                    PositionsNeeded = "DEF",
                    FeeType = LoanFeeType.SplitPitch,
                    LoanFeeAmount = 0m,
                    EscrowAbCoin = 20,
                    Notes = "Rảnh lịch tối thứ 6, đá được hậu vệ trái & tiền vệ cánh, cam kết đúng giờ.",
                    Status = LoanListingStatus.Open
                },
                new PlayerLoanListing
                {
                    ListingType = LoanListingType.PlayerAvailable,
                    UserId = users.SaiGonUnitedCaptain.Id,
                    PlayerId = sguCaptainPlayer?.Id,
                    TeamId = teams.SaiGonUnited.Id,
                    Region = "Quận 7, TP.HCM",
                    PitchName = "Quận 7 / Bình Thạnh",
                    PlayDate = localToday.AddDays(5),
                    TimeSlot = "18:30 - 20:30",
                    PositionsNeeded = "MID",
                    FeeType = LoanFeeType.PaidMercenary,
                    LoanFeeAmount = 150_000m,
                    EscrowAbCoin = 10,
                    Notes = "Tiền đạo/hộ công chân phải, từng đá giải doanh nghiệp Quận 7, nhận kèo 150k tăng cường.",
                    Status = LoanListingStatus.Open
                }
            );
            await db.SaveChangesAsync();
        }
    }
}
