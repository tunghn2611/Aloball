using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
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

        // Äáº£m báº£o Database Ä‘Ã£ Ä‘Æ°á»£c táº¡o
        await db.Database.EnsureCreatedAsync();

        // 1. Tá»± Ä‘á»™ng kiá»ƒm tra vÃ  thÃªm cÃ¡c cá»™t má»›i vÃ o SQLite DB cÅ© náº¿u chÆ°a tá»“n táº¡i
        if (db.Database.IsRelational())
        {
            await EnsureDatabaseSchemaAsync(db);
        }

        try
        {
            // 2. Táº¡o hoáº·c cáº­p nháº­t tÃ i khoáº£n quáº£n lÃ½ / Ä‘á»™i trÆ°á»Ÿng
            var users = await EnsureUsersAsync(userManager);

            // 3. Táº¡o hoáº·c má»Ÿ rá»™ng cÃ¡c Äá»™i bÃ³ng & Äáº§y Ä‘á»§ danh sÃ¡ch Cáº§u thá»§
            var teams = await EnsureTeamsAndRostersAsync(db, users);

            // 4. Táº¡o hoáº·c bá»• sung Quy cháº¿ hoáº¡t Ä‘á»™ng máº«u
            await EnsureRegulationsAsync(db, teams);

            // 5. Táº¡o hoáº·c bá»• sung Buá»•i thi Ä‘áº¥u & Äiá»ƒm danh máº«u Ä‘á»ƒ sáºµn sÃ ng test chia Ä‘á»™i tá»± Ä‘á»™ng & dá»± bá»‹
            await EnsureSessionsAndAttendanceAsync(db, teams);

            // 6. Táº¡o hoáº·c bá»• sung KÃ¨o giao lÆ°u Matchmaking & Tráº­n Ä‘áº¥u thá»±c táº¿
            await EnsureMatchmakingAndMatchesAsync(db, teams);

            // 7. Táº¡o danh báº¡ Trá»ng tÃ i máº«u phá»¥c vá»¥ Ä‘iá»u phá»‘i
            await EnsureRefereesAsync(db);

            // 8. Táº¡o danh báº¡ SÃ¢n bÃ³ng máº«u (Pitch Venues) phá»¥c vá»¥ khÃ¡m phÃ¡ & Ä‘áº·t sÃ¢n
            await EnsurePitchVenuesAsync(db);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error during SeedDemoDataAsync: {ex.Message} -> {ex.StackTrace}", ex);
        }
    }

    private static async Task EnsureDatabaseSchemaAsync(ApplicationDbContext db)
    {
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE AttendanceSessions ADD COLUMN PitchName TEXT DEFAULT '';"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE AttendanceSessions ADD COLUMN PitchType TEXT DEFAULT '7 ngÆ°á»i';"); } catch { }
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

        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Teams ADD COLUMN CoverImageUrl TEXT NULL;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Teams ADD COLUMN CoverOpacity REAL NULL;"); } catch { }

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
            ");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error migrating Finance tables: {ex.Message}");
        }
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

        return new SeededUsers
        {
            Admin = await GetOrCreateUserAsync("admin@aloball.vn", "Platform Administrator", "Admin@123456", "usr-admin-001", "STAMP-ADMIN-001"),
            KlbOwner = await GetOrCreateUserAsync("owner@klbfc.vn", "Nguyễn Văn Chủ Tịch", "Owner@123456", "usr-owner-klb-002", "STAMP-OWNER-KLB-002"),
            KlbCaptain = await GetOrCreateUserAsync("captain@klbfc.vn", "Trần Đội Trưởng", "Captain@123456", "usr-captain-klb-003", "STAMP-CAPTAIN-KLB-003"),
            KlbMember = await GetOrCreateUserAsync("member@klbfc.vn", "Lê Cầu Thủ", "Member@123456", "usr-member-klb-004", "STAMP-MEMBER-KLB-004"),
            SaoVangCaptain = await GetOrCreateUserAsync("captain@saovangfc.vn", "Hoàng Sao Vàng", "SaoVang@123456", "usr-captain-sv-005", "STAMP-CAPTAIN-SV-005"),
            PhoenixCaptain = await GetOrCreateUserAsync("captain@phoenixfc.vn", "Vũ Phượng Hoàng", "Phoenix@123456", "usr-captain-ph-006", "STAMP-CAPTAIN-PH-006"),
            TrangAnCaptain = await GetOrCreateUserAsync("captain@tranganfc.vn", "Đinh Tràng An", "TrangAn@123456", "usr-captain-ta-007", "STAMP-CAPTAIN-TA-007"),
            BachKhoaCaptain = await GetOrCreateUserAsync("captain@bachkhoafc.vn", "Tạ Bách Khoa", "BachKhoa@123456", "usr-captain-bk-008", "STAMP-CAPTAIN-BK-008")
        };
    }

    private class SeededTeams
    {
        public Team Klb { get; set; } = null!;
        public Team SaoVang { get; set; } = null!;
        public Team Phoenix { get; set; } = null!;
        public Team TrangAn { get; set; } = null!;
        public Team BachKhoa { get; set; } = null!;
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
                Description = "CÃ¢u láº¡c bá»™ bÃ³ng Ä‘Ã¡ phong trÃ o KLB - Giao lÆ°u há»¯u nghá»‹, rÃ¨n luyá»‡n sá»©c khá»e, vÄƒn minh sÃ¢n cá».",
                ActivityArea = "Cáº§u Giáº¥y, HÃ  Ná»™i",
                HomePitch = "SÃ¢n bÃ³ng PVV - 72 Tráº§n ThÃ¡i TÃ´ng",
                EstablishedDate = new DateTime(2020, 10, 10),
                IsPublic = true,
                AverageRating = 5.0,
                ValidReviewCount = 3
            };
            db.Teams.Add(klb);
            await db.SaveChangesAsync();
        }

        // 2. Sao VÃ ng FC
        var saoVang = await db.Teams.FirstOrDefaultAsync(t => t.Slug == "sao-vang-fc");
        if (saoVang == null)
        {
            saoVang = new Team
            {
                Name = "Sao VÃ ng FC",
                Slug = "sao-vang-fc",
                Description = "Sao VÃ ng FC - Tinh tháº§n fair-play, nhiá»‡t huyáº¿t, giao lÆ°u tá»‘i thá»© 4 & chá»§ nháº­t.",
                ActivityArea = "Nam Tá»« LiÃªm, HÃ  Ná»™i",
                HomePitch = "SÃ¢n Má»… TrÃ¬ ThÆ°á»£ng",
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
                Description = "Phoenix FC (PhÆ°á»£ng HoÃ ng Lá»­a) - Lá»‘i Ä‘Ã¡ táº¥n cÃ´ng cá»‘ng hiáº¿n, tá»‘c Ä‘á»™ vÃ  ká»¹ thuáº­t.",
                ActivityArea = "Thanh XuÃ¢n, HÃ  Ná»™i",
                HomePitch = "SÃ¢n bÃ³ng Bá»™ CÃ´ng An - Nguyá»…n Xiá»ƒn",
                EstablishedDate = new DateTime(2022, 3, 15),
                IsPublic = true,
                AverageRating = 4.9,
                ValidReviewCount = 5
            };
            db.Teams.Add(phoenix);
            await db.SaveChangesAsync();
        }

        // 4. TrÃ ng An FC
        var trangAn = await db.Teams.FirstOrDefaultAsync(t => t.Slug == "trang-an-fc");
        if (trangAn == null)
        {
            trangAn = new Team
            {
                Name = "TrÃ ng An FC",
                Slug = "trang-an-fc",
                Description = "TrÃ ng An FC - Gáº¯n káº¿t anh em Ä‘á»“ng hÆ°Æ¡ng, ká»· luáº­t chiáº¿n thuáº­t, giao lÆ°u cá»Ÿi má»Ÿ.",
                ActivityArea = "Äá»‘ng Äa, HÃ  Ná»™i",
                HomePitch = "SÃ¢n bÃ³ng Thá»§y Lá»£i",
                EstablishedDate = new DateTime(2021, 11, 20),
                IsPublic = true,
                AverageRating = 4.7,
                ValidReviewCount = 4
            };
            db.Teams.Add(trangAn);
            await db.SaveChangesAsync();
        }

        // 5. FC BÃ¡ch Khoa 2012
        var bachKhoa = await db.Teams.FirstOrDefaultAsync(t => t.Slug == "bach-khoa-fc");
        if (bachKhoa == null)
        {
            bachKhoa = new Team
            {
                Name = "FC BÃ¡ch Khoa 2012",
                Slug = "bach-khoa-fc",
                Description = "Há»™i cá»±u sinh viÃªn BÃ¡ch Khoa K57 - Äam mÃª bÃ³ng Ä‘Ã¡, thá»ƒ lá»±c bá»n bá»‰, chiáº¿n thuáº­t cháº·t cháº½.",
                ActivityArea = "Hai BÃ  TrÆ°ng, HÃ  Ná»™i",
                HomePitch = "SÃ¢n váº­n Ä‘á»™ng BÃ¡ch Khoa",
                EstablishedDate = new DateTime(2019, 8, 15),
                IsPublic = true,
                AverageRating = 4.9,
                ValidReviewCount = 6
            };
            db.Teams.Add(bachKhoa);
            await db.SaveChangesAsync();
        }

        // GÃ¡n ban cÃ¡n sá»± / ThÃ nh viÃªn náº¿u chÆ°a cÃ³
        await EnsureMembershipAsync(db, klb.Id, users.KlbOwner.Id, TeamRole.Owner, true);
        await EnsureMembershipAsync(db, klb.Id, users.KlbCaptain.Id, TeamRole.Manager, true);
        await EnsureMembershipAsync(db, klb.Id, users.KlbMember.Id, TeamRole.Member, false);
        await EnsureMembershipAsync(db, saoVang.Id, users.SaoVangCaptain.Id, TeamRole.Owner, true);
        await EnsureMembershipAsync(db, phoenix.Id, users.PhoenixCaptain.Id, TeamRole.Owner, true);
        await EnsureMembershipAsync(db, trangAn.Id, users.TrangAnCaptain.Id, TeamRole.Owner, true);
        await EnsureMembershipAsync(db, bachKhoa.Id, users.BachKhoaCaptain.Id, TeamRole.Owner, true);

        // Bá»• sung Ä‘áº§y Ä‘á»§ cáº§u thá»§ cho tá»«ng Ä‘á»™i
        await SeedSquadKlbAsync(db, klb.Id, users);
        await SeedSquadSaoVangAsync(db, saoVang.Id, users);
        await SeedSquadPhoenixAsync(db, phoenix.Id, users);
        await SeedSquadTrangAnAsync(db, trangAn.Id, users);
        await SeedSquadBachKhoaAsync(db, bachKhoa.Id, users);

        await db.SaveChangesAsync();

        return new SeededTeams
        {
            Klb = klb,
            SaoVang = saoVang,
            Phoenix = phoenix,
            TrangAn = trangAn,
            BachKhoa = bachKhoa
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
        double? weight = 70)
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
                ShirtName = shirtName ?? fullName.ToUpperInvariant(),
                ShirtSize = "L",
                ShortSize = "L",
                HeightCm = height,
                WeightKg = weight,
                IsPublicProfile = true,
                PublicKitNumber = true,
                PublicPosition = true,
                PublicHeightWeight = true
            });
        }
        else
        {
            existing.KitNumber = kitNumber;
            existing.Position = rawPosition;
            existing.StandardPrimaryPosition = stdPrimary;
            existing.AllowedSecondaryPositionsJson = secJson;
            if (linkedUserId != null && string.IsNullOrEmpty(existing.LinkedUserId))
            {
                existing.LinkedUserId = linkedUserId;
            }
        }
    }

    private static async Task SeedSquadKlbAsync(ApplicationDbContext db, Guid teamId, SeededUsers users)
    {
        // 16 cáº§u thá»§ - Äáº§y Ä‘á»§ 2 GK, 5 DF, 5 MF, 4 FW (LÃ½ tÆ°á»Ÿng kiá»ƒm tra 14 Ä‘Ã¡ chÃ­nh + 2 dá»± bá»‹ cho sÆ¡ Ä‘á»“ 7_231 hoáº·c 7_321)
        await AddOrUpdatePlayerAsync(db, teamId, "Pháº¡m Trung KiÃªn", 1, "Thá»§ mÃ´n", PositionCodes.GK, new[] { PositionCodes.CB }, null, "KIEN GK", 182, 76);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyá»…n Minh HoÃ ng", 25, "Thá»§ mÃ´n", PositionCodes.GK, new string[] { }, null, "HOANG GK", 180, 74);

        await AddOrUpdatePlayerAsync(db, teamId, "VÅ© ÄÃ¬nh Trá»ng", 5, "Trung vá»‡", PositionCodes.CB, new[] { PositionCodes.DF_L, PositionCodes.CDM }, null, "TRONG CB", 178, 73);
        await AddOrUpdatePlayerAsync(db, teamId, "BÃ¹i Tiáº¿n DÅ©ng", 4, "Trung vá»‡", PositionCodes.CB, new[] { PositionCodes.DF_R }, null, "TIEN DUNG", 179, 75);
        await AddOrUpdatePlayerAsync(db, teamId, "LÃª Cáº§u Thá»§", 8, "Háº­u vá»‡ cÃ¡nh trÃ¡i", PositionCodes.LB, new[] { PositionCodes.CB, PositionCodes.LM }, users.KlbMember.Id, "LE THU", 175, 69);
        await AddOrUpdatePlayerAsync(db, teamId, "Tráº§n VÄƒn Háº­u", 2, "Háº­u vá»‡ cÃ¡nh trÃ¡i", PositionCodes.LB, new[] { PositionCodes.LM }, null, "VAN HAU", 174, 68);
        await AddOrUpdatePlayerAsync(db, teamId, "Äá»— Thanh BÃ¬nh", 3, "Háº­u vá»‡ cÃ¡nh pháº£i", PositionCodes.RB, new[] { PositionCodes.RM, PositionCodes.CB }, null, "THANH BINH", 176, 70);

        await AddOrUpdatePlayerAsync(db, teamId, "Nguyá»…n VÄƒn Chá»§ Tá»‹ch", 10, "Tiá»n vá»‡ trung tÃ¢m", PositionCodes.CM, new[] { PositionCodes.CAM, PositionCodes.ST }, users.KlbOwner.Id, "VAN CHU TICH", 172, 68);
        await AddOrUpdatePlayerAsync(db, teamId, "Äá»— HÃ¹ng DÅ©ng", 16, "Tiá»n vá»‡ phÃ²ng ngá»±", PositionCodes.CDM, new[] { PositionCodes.CM, PositionCodes.CB }, null, "HUNG DUNG", 174, 69);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyá»…n Quang Háº£i", 19, "Tiá»n vá»‡ táº¥n cÃ´ng", PositionCodes.CAM, new[] { PositionCodes.CM, PositionCodes.RW }, null, "QUANG HAI", 168, 65);
        await AddOrUpdatePlayerAsync(db, teamId, "LÆ°Æ¡ng XuÃ¢n TrÆ°á»ng", 6, "Tiá»n vá»‡ trung tÃ¢m", PositionCodes.CM, new[] { PositionCodes.CAM, PositionCodes.CDM }, null, "XUAN TRUONG", 177, 71);
        await AddOrUpdatePlayerAsync(db, teamId, "Phan VÄƒn Äá»©c", 20, "Tiá»n vá»‡ cÃ¡nh trÃ¡i", PositionCodes.LM, new[] { PositionCodes.LW, PositionCodes.LB }, null, "VAN DUC", 173, 67);

        await AddOrUpdatePlayerAsync(db, teamId, "Tráº§n Äá»™i TrÆ°á»Ÿng", 7, "Tiá»n Ä‘áº¡o cÃ¡nh pháº£i", PositionCodes.RW, new[] { PositionCodes.RM, PositionCodes.ST }, users.KlbCaptain.Id, "DOI TRUONG", 175, 70);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyá»…n CÃ´ng PhÆ°á»£ng", 11, "Tiá»n Ä‘áº¡o cÃ¡nh trÃ¡i", PositionCodes.LW, new[] { PositionCodes.ST, PositionCodes.CAM }, null, "CONG PHUONG", 170, 66);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyá»…n Tiáº¿n Linh", 22, "Tiá»n Ä‘áº¡o cáº¯m", PositionCodes.ST, new[] { PositionCodes.CF }, null, "TIEN LINH", 183, 77);
        await AddOrUpdatePlayerAsync(db, teamId, "HÃ  Äá»©c Chinh", 9, "Tiá»n Ä‘áº¡o cáº¯m", PositionCodes.ST, new[] { PositionCodes.LW, PositionCodes.RW }, null, "DUC CHINH", 177, 74);
    }

    private static async Task SeedSquadSaoVangAsync(ApplicationDbContext db, Guid teamId, SeededUsers users)
    {
        // 15 cáº§u thá»§ Sao VÃ ng FC
        await AddOrUpdatePlayerAsync(db, teamId, "Äáº·ng VÄƒn LÃ¢m", 1, "Thá»§ mÃ´n", PositionCodes.GK, new string[] { }, null, "VAN LAM", 188, 85);
        await AddOrUpdatePlayerAsync(db, teamId, "Tráº§n ÄÃ¬nh Trá»ng", 21, "Trung vá»‡", PositionCodes.CB, new[] { PositionCodes.CDM }, null, "DINH TRONG", 175, 70);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyá»…n ThÃ nh Chung", 16, "Trung vá»‡", PositionCodes.CB, new[] { PositionCodes.ST }, null, "THANH CHUNG", 182, 76);
        await AddOrUpdatePlayerAsync(db, teamId, "Há»“ Táº¥n TÃ i", 13, "Háº­u vá»‡ cÃ¡nh pháº£i", PositionCodes.RB, new[] { PositionCodes.RM, PositionCodes.CB }, null, "TAN TAI", 180, 74);
        await AddOrUpdatePlayerAsync(db, teamId, "VÅ© VÄƒn Thanh", 17, "Háº­u vá»‡ cÃ¡nh trÃ¡i", PositionCodes.LB, new[] { PositionCodes.RB, PositionCodes.LM, PositionCodes.RM }, null, "VAN THANH", 171, 69);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyá»…n Phong Há»“ng Duy", 7, "Háº­u vá»‡ cÃ¡nh trÃ¡i", PositionCodes.LB, new[] { PositionCodes.LM, PositionCodes.LW }, null, "HONG DUY", 168, 65);

        await AddOrUpdatePlayerAsync(db, teamId, "Nguyá»…n Tuáº¥n Anh", 8, "Tiá»n vá»‡ trung tÃ¢m", PositionCodes.CM, new[] { PositionCodes.CDM, PositionCodes.CAM }, null, "TUAN ANH", 174, 67);
        await AddOrUpdatePlayerAsync(db, teamId, "Pháº¡m Äá»©c Huy", 15, "Tiá»n vá»‡ phÃ²ng ngá»±", PositionCodes.CDM, new[] { PositionCodes.CM, PositionCodes.RB }, null, "DUC HUY", 173, 72);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyá»…n HoÃ ng Äá»©c", 14, "Tiá»n vá»‡ táº¥n cÃ´ng", PositionCodes.CAM, new[] { PositionCodes.CM, PositionCodes.ST }, null, "HOANG DUC", 184, 75);
        await AddOrUpdatePlayerAsync(db, teamId, "Triá»‡u Viá»‡t HÆ°ng", 97, "Tiá»n vá»‡ trung tÃ¢m", PositionCodes.CM, new[] { PositionCodes.LM }, null, "VIET HUNG", 172, 66);
        await AddOrUpdatePlayerAsync(db, teamId, "ChÃ¢u Ngá»c Quang", 18, "Tiá»n vá»‡ cÃ¡nh pháº£i", PositionCodes.RM, new[] { PositionCodes.CM, PositionCodes.RW }, null, "NGOC QUANG", 173, 68);

        await AddOrUpdatePlayerAsync(db, teamId, "HoÃ ng Sao VÃ ng", 9, "Tiá»n Ä‘áº¡o cáº¯m", PositionCodes.ST, new[] { PositionCodes.LW, PositionCodes.RW }, users.SaoVangCaptain.Id, "HOANG 9", 178, 73);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyá»…n VÄƒn ToÃ n", 99, "Tiá»n Ä‘áº¡o cÃ¡nh pháº£i", PositionCodes.RW, new[] { PositionCodes.RM, PositionCodes.ST }, null, "VAN TOAN", 170, 63);
        await AddOrUpdatePlayerAsync(db, teamId, "Pháº¡m Tuáº¥n Háº£i", 10, "Tiá»n Ä‘áº¡o cáº¯m", PositionCodes.ST, new[] { PositionCodes.LW, PositionCodes.CAM }, null, "TUAN HAI", 172, 68);
        await AddOrUpdatePlayerAsync(db, teamId, "BÃ¹i VÄ© HÃ o", 11, "Tiá»n Ä‘áº¡o cÃ¡nh trÃ¡i", PositionCodes.LW, new[] { PositionCodes.ST }, null, "VI HAO", 177, 70);
    }

    private static async Task SeedSquadPhoenixAsync(ApplicationDbContext db, Guid teamId, SeededUsers users)
    {
        // 15 cáº§u thá»§ Phoenix FC
        await AddOrUpdatePlayerAsync(db, teamId, "VÅ© PhÆ°á»£ng HoÃ ng", 1, "Thá»§ mÃ´n", PositionCodes.GK, new string[] { }, users.PhoenixCaptain.Id, "PHUONG HOANG", 184, 78);
        await AddOrUpdatePlayerAsync(db, teamId, "LÃª BÃ¡ Nam", 23, "Thá»§ mÃ´n", PositionCodes.GK, new string[] { }, null, "BA NAM", 181, 75);

        await AddOrUpdatePlayerAsync(db, teamId, "Tráº§n Kim HÃ¹ng", 3, "Trung vá»‡", PositionCodes.CB, new[] { PositionCodes.DF_L }, null, "KIM HUNG", 180, 77);
        await AddOrUpdatePlayerAsync(db, teamId, "Äáº·ng Tháº¿ TÃ i", 4, "Trung vá»‡", PositionCodes.CB, new[] { PositionCodes.CDM }, null, "THE TAI", 179, 74);
        await AddOrUpdatePlayerAsync(db, teamId, "NgÃ´ VÄƒn Long", 12, "Háº­u vá»‡ cÃ¡nh trÃ¡i", PositionCodes.LB, new[] { PositionCodes.LM }, null, "VAN LONG", 172, 66);
        await AddOrUpdatePlayerAsync(db, teamId, "Táº¡ ÄÃ¬nh Phong", 2, "Háº­u vá»‡ cÃ¡nh pháº£i", PositionCodes.RB, new[] { PositionCodes.RM }, null, "DINH PHONG", 174, 69);

        await AddOrUpdatePlayerAsync(db, teamId, "Phan Anh VÅ©", 6, "Tiá»n vá»‡ phÃ²ng ngá»±", PositionCodes.CDM, new[] { PositionCodes.CM }, null, "ANH VU", 176, 71);
        await AddOrUpdatePlayerAsync(db, teamId, "LÃ½ Gia Huy", 8, "Tiá»n vá»‡ trung tÃ¢m", PositionCodes.CM, new[] { PositionCodes.CAM }, null, "GIA HUY", 173, 67);
        await AddOrUpdatePlayerAsync(db, teamId, "Mai XuÃ¢n Quyáº¿t", 10, "Tiá»n vá»‡ táº¥n cÃ´ng", PositionCodes.CAM, new[] { PositionCodes.ST, PositionCodes.LW }, null, "XUAN QUYET", 175, 68);
        await AddOrUpdatePlayerAsync(db, teamId, "DÆ°Æ¡ng Quá»‘c Báº£o", 17, "Tiá»n vá»‡ cÃ¡nh trÃ¡i", PositionCodes.LM, new[] { PositionCodes.LW }, null, "QUOC BAO", 171, 65);
        await AddOrUpdatePlayerAsync(db, teamId, "LÃª Tuáº¥n Kiá»‡t", 19, "Tiá»n vá»‡ cÃ¡nh pháº£i", PositionCodes.RM, new[] { PositionCodes.RW }, null, "TUAN KIET", 173, 68);

        await AddOrUpdatePlayerAsync(db, teamId, "HoÃ ng Nháº­t Minh", 7, "Tiá»n Ä‘áº¡o cÃ¡nh pháº£i", PositionCodes.RW, new[] { PositionCodes.ST }, null, "NHAT MINH", 176, 70);
        await AddOrUpdatePlayerAsync(db, teamId, "TrÆ°Æ¡ng Há»¯u Tháº¯ng", 11, "Tiá»n Ä‘áº¡o cÃ¡nh trÃ¡i", PositionCodes.LW, new[] { PositionCodes.LM }, null, "HUU THANG", 174, 67);
        await AddOrUpdatePlayerAsync(db, teamId, "ÄÃ o Máº¡nh CÆ°á»ng", 9, "Tiá»n Ä‘áº¡o cáº¯m", PositionCodes.ST, new[] { PositionCodes.CF }, null, "MANH CUONG", 182, 78);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyá»…n HÃ¹ng SÆ¡n", 20, "Tiá»n Ä‘áº¡o cáº¯m", PositionCodes.ST, new string[] { }, null, "HUNG SON", 178, 72);
    }

    private static async Task SeedSquadTrangAnAsync(ApplicationDbContext db, Guid teamId, SeededUsers users)
    {
        // 14 cáº§u thá»§ TrÃ ng An FC
        await AddOrUpdatePlayerAsync(db, teamId, "Äinh TrÃ ng An", 1, "Thá»§ mÃ´n", PositionCodes.GK, new string[] { }, users.TrangAnCaptain.Id, "TRANG AN", 182, 76);
        await AddOrUpdatePlayerAsync(db, teamId, "Pháº¡m XuÃ¢n HÆ°ng", 12, "Thá»§ mÃ´n", PositionCodes.GK, new string[] { }, null, "XUAN HUNG", 180, 73);

        await AddOrUpdatePlayerAsync(db, teamId, "VÅ© VÄƒn LÆ°Æ¡ng", 4, "Trung vá»‡", PositionCodes.CB, new[] { PositionCodes.CDM }, null, "VAN LUONG", 180, 75);
        await AddOrUpdatePlayerAsync(db, teamId, "LÃª Há»“ng QuÃ¢n", 5, "Trung vá»‡", PositionCodes.CB, new[] { PositionCodes.RB }, null, "HONG QUAN", 178, 74);
        await AddOrUpdatePlayerAsync(db, teamId, "NgÃ´ Trá»ng Táº¥n", 3, "Háº­u vá»‡ cÃ¡nh trÃ¡i", PositionCodes.LB, new[] { PositionCodes.LM }, null, "TRONG TAN", 171, 66);
        await AddOrUpdatePlayerAsync(db, teamId, "Äá»— Äá»©c Thá»‹nh", 2, "Háº­u vá»‡ cÃ¡nh pháº£i", PositionCodes.RB, new[] { PositionCodes.RM }, null, "DUC THINH", 173, 68);

        await AddOrUpdatePlayerAsync(db, teamId, "Nguyá»…n ThÃ¡i SÆ¡n", 6, "Tiá»n vá»‡ phÃ²ng ngá»±", PositionCodes.CDM, new[] { PositionCodes.CM }, null, "THAI SON", 174, 70);
        await AddOrUpdatePlayerAsync(db, teamId, "Tráº§n VÄƒn CÆ°á»ng", 8, "Tiá»n vá»‡ trung tÃ¢m", PositionCodes.CM, new[] { PositionCodes.CAM }, null, "VAN CUONG", 172, 67);
        await AddOrUpdatePlayerAsync(db, teamId, "LÃª Há»¯u Äáº¡t", 10, "Tiá»n vá»‡ táº¥n cÃ´ng", PositionCodes.CAM, new[] { PositionCodes.ST }, null, "HUU DAT", 175, 69);
        await AddOrUpdatePlayerAsync(db, teamId, "HoÃ ng Kháº¯c KhiÃªm", 14, "Tiá»n vá»‡ cÃ¡nh trÃ¡i", PositionCodes.LM, new[] { PositionCodes.LW }, null, "KHAC KHIEM", 170, 65);
        await AddOrUpdatePlayerAsync(db, teamId, "VÃµ Phi Há»•", 16, "Tiá»n vá»‡ cÃ¡nh pháº£i", PositionCodes.RM, new[] { PositionCodes.RW }, null, "PHI HO", 174, 69);

        await AddOrUpdatePlayerAsync(db, teamId, "Äáº·ng Quá»‘c HÆ°ng", 7, "Tiá»n Ä‘áº¡o cÃ¡nh pháº£i", PositionCodes.RW, new[] { PositionCodes.ST }, null, "QUOC HUNG", 175, 71);
        await AddOrUpdatePlayerAsync(db, teamId, "Táº¡ ÄÃ¬nh Háº­u", 11, "Tiá»n Ä‘áº¡o cÃ¡nh trÃ¡i", PositionCodes.LW, new[] { PositionCodes.LM }, null, "DINH HAU", 173, 68);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyá»…n Sá»¹ Chiáº¿n", 9, "Tiá»n Ä‘áº¡o cáº¯m", PositionCodes.ST, new[] { PositionCodes.CF }, null, "SY CHIEN", 181, 77);
    }

    private static async Task SeedSquadBachKhoaAsync(ApplicationDbContext db, Guid teamId, SeededUsers users)
    {
        // 14 cáº§u thá»§ FC BÃ¡ch Khoa 2012
        await AddOrUpdatePlayerAsync(db, teamId, "Táº¡ BÃ¡ch Khoa", 1, "Thá»§ mÃ´n", PositionCodes.GK, new string[] { }, users.BachKhoaCaptain.Id, "BACH KHOA", 183, 77);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyá»…n ÄÃ¬nh Triá»‡u", 25, "Thá»§ mÃ´n", PositionCodes.GK, new string[] { }, null, "DINH TRIEU", 181, 75);

        await AddOrUpdatePlayerAsync(db, teamId, "LÃª VÄƒn SÆ¡n", 4, "Trung vá»‡", PositionCodes.CB, new[] { PositionCodes.RB }, null, "VAN SON", 176, 72);
        await AddOrUpdatePlayerAsync(db, teamId, "Tráº§n Há»¯u KhÃ´i", 5, "Trung vá»‡", PositionCodes.CB, new[] { PositionCodes.CDM }, null, "HUU KHOI", 179, 75);
        await AddOrUpdatePlayerAsync(db, teamId, "HoÃ ng VÄƒn KhÃ¡nh", 3, "Háº­u vá»‡ cÃ¡nh trÃ¡i", PositionCodes.LB, new[] { PositionCodes.LM }, null, "VAN KHANH", 172, 67);
        await AddOrUpdatePlayerAsync(db, teamId, "BÃ¹i HoÃ ng Viá»‡t Anh", 20, "Háº­u vá»‡ cÃ¡nh pháº£i", PositionCodes.RB, new[] { PositionCodes.CB }, null, "VIET ANH", 184, 78);

        await AddOrUpdatePlayerAsync(db, teamId, "Trá»‹nh VÄƒn Lá»£i", 6, "Tiá»n vá»‡ phÃ²ng ngá»±", PositionCodes.CDM, new[] { PositionCodes.CB }, null, "VAN LOI", 175, 70);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyá»…n Trá»ng HoÃ ng", 8, "Tiá»n vá»‡ trung tÃ¢m", PositionCodes.CM, new[] { PositionCodes.RM, PositionCodes.RB }, null, "TRONG HOANG", 170, 68);
        await AddOrUpdatePlayerAsync(db, teamId, "LÃª Huá»³nh Äá»©c", 10, "Tiá»n vá»‡ táº¥n cÃ´ng", PositionCodes.CAM, new[] { PositionCodes.ST }, null, "HUYNH DUC", 178, 73);
        await AddOrUpdatePlayerAsync(db, teamId, "Äáº·ng Anh Tuáº¥n", 18, "Tiá»n vá»‡ cÃ¡nh trÃ¡i", PositionCodes.LM, new[] { PositionCodes.LW }, null, "ANH TUAN", 172, 66);
        await AddOrUpdatePlayerAsync(db, teamId, "Pháº¡m XuÃ¢n Máº¡nh", 7, "Tiá»n vá»‡ cÃ¡nh pháº£i", PositionCodes.RM, new[] { PositionCodes.RB }, null, "XUAN MANH", 175, 70);

        await AddOrUpdatePlayerAsync(db, teamId, "Nguyá»…n Viá»‡t Phong", 17, "Tiá»n Ä‘áº¡o cÃ¡nh pháº£i", PositionCodes.RW, new[] { PositionCodes.ST }, null, "VIET PHONG", 174, 69);
        await AddOrUpdatePlayerAsync(db, teamId, "LÃ¢m Ti PhÃ´ng", 19, "Tiá»n Ä‘áº¡o cÃ¡nh trÃ¡i", PositionCodes.LW, new[] { PositionCodes.LM }, null, "TI PHONG", 169, 64);
        await AddOrUpdatePlayerAsync(db, teamId, "HÃ  Minh Tuáº¥n", 9, "Tiá»n Ä‘áº¡o cáº¯m", PositionCodes.ST, new[] { PositionCodes.CF }, null, "MINH TUAN", 182, 76);
    }

    private static async Task EnsureRegulationsAsync(ApplicationDbContext db, SeededTeams teams)
    {
        if (!await db.TeamRegulations.AnyAsync(r => r.TeamId == teams.Klb.Id))
        {
            db.TeamRegulations.Add(new TeamRegulation
            {
                TeamId = teams.Klb.Id,
                Version = 1,
                Title = "Quy cháº¿ chuyÃªn cáº§n KLB FC (Máº«u thá»±c táº¿)",
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
        // Kiá»ƒm tra xem Ä‘Ã£ cÃ³ buá»•i thi Ä‘áº¥u cho KLB chÆ°a
        var existingSession = await db.AttendanceSessions.FirstOrDefaultAsync(s => s.TeamId == teams.Klb.Id);
        if (existingSession == null)
        {
            existingSession = new AttendanceSession
            {
                TeamId = teams.Klb.Id,
                Title = "Tráº­n Ä‘áº¥u háº±ng tuáº§n - Thá»© TÆ° (SÃ¢n 7)",
                SessionDateTime = DateTime.Today.AddHours(19).AddMinutes(30),
                ResponseDeadline = DateTime.Today.AddHours(12),
                StatisticalPeriod = DateTime.Today.ToString("yyyy-MM"),
                Status = SessionStatus.Draft,
                PitchName = "SÃ¢n bÃ³ng PVV - 72 Tráº§n ThÃ¡i TÃ´ng",
                PitchType = "7 ngÆ°á»i",
                Notes = "Má»i ngÆ°á»i pháº£n há»“i trÆ°á»›c 12h trÆ°a Ä‘á»ƒ chá»‘t quÃ¢n sá»‘ vÃ  chia Ã¡o chia sÃ¢n."
            };
            db.AttendanceSessions.Add(existingSession);
            await db.SaveChangesAsync();
        }

        // Äáº£m báº£o toÃ n bá»™ 16 cáº§u thá»§ cá»§a KLB Ä‘á»u cÃ³ báº£n ghi Ä‘iá»ƒm danh
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
                    PenaltyReason = "CÃ³ máº·t Ä‘Ãºng cam káº¿t"
                });
            }
        }
        await db.SaveChangesAsync();
    }

    private static async Task EnsureMatchmakingAndMatchesAsync(ApplicationDbContext db, SeededTeams teams)
    {
        // BÃ i Ä‘Äƒng tÃ¬m Ä‘á»‘i cá»§a KLB
        if (!await db.MatchmakingPosts.AnyAsync(m => m.TeamId == teams.Klb.Id))
        {
            db.MatchmakingPosts.Add(new MatchmakingPost
            {
                TeamId = teams.Klb.Id,
                MatchDate = DateTime.Today.AddDays(3),
                StartTime = new TimeSpan(19, 30, 0),
                EndTime = new TimeSpan(21, 0, 0),
                Area = "Cáº§u Giáº¥y, HÃ  Ná»™i",
                PitchArrangement = PitchArrangement.HomePitch,
                PitchName = "SÃ¢n bÃ³ng PVV - Tráº§n ThÃ¡i TÃ´ng",
                IsPitchBooked = true,
                PitchType = "7 ngÆ°á»i",
                DesiredLevel = "Trung bÃ¬nh - KhÃ¡",
                CostSharing = "50-50 (Chia Ä‘Ã´i tiá»n sÃ¢n)",
                Notes = "Cáº§n tÃ¬m Ä‘á»‘i tÃ¡c giao lÆ°u vÄƒn minh, khÃ´ng chÆ¡i xáº¥u, sau tráº­n giao lÆ°u bia cá».",
                Status = MatchmakingStatus.Open
            });
        }

        // BÃ i Ä‘Äƒng tÃ¬m Ä‘á»‘i cá»§a Phoenix FC
        if (!await db.MatchmakingPosts.AnyAsync(m => m.TeamId == teams.Phoenix.Id))
        {
            db.MatchmakingPosts.Add(new MatchmakingPost
            {
                TeamId = teams.Phoenix.Id,
                MatchDate = DateTime.Today.AddDays(5),
                StartTime = new TimeSpan(20, 0, 0),
                EndTime = new TimeSpan(21, 30, 0),
                Area = "Thanh XuÃ¢n, HÃ  Ná»™i",
                PitchArrangement = PitchArrangement.HomePitch,
                PitchName = "SÃ¢n bÃ³ng Bá»™ CÃ´ng An - Nguyá»…n Xiá»ƒn",
                IsPitchBooked = true,
                PitchType = "7 ngÆ°á»i",
                DesiredLevel = "KhÃ¡",
                CostSharing = "50-50",
                Notes = "Phoenix FC tÃ¬m Ä‘á»‘i giao lÆ°u tá»‘i cuá»‘i tuáº§n, Æ°u tiÃªn Ä‘á»™i hÃ¬nh tráº» khá»e, nhiá»‡t huyáº¿t.",
                Status = MatchmakingStatus.Open
            });
        }

        // ThÃ nh tÃ­ch
        if (!await db.CompetitionAchievements.AnyAsync(c => c.TeamId == teams.Klb.Id))
        {
            db.CompetitionAchievements.Add(new CompetitionAchievement
            {
                TeamId = teams.Klb.Id,
                TournamentName = "CÃºp Doanh Nghiá»‡p Má»Ÿ Rá»™ng 2025",
                SeasonOrYear = "2025",
                TitleOrRank = "VÃ´ Äá»‹ch",
                IsFeatured = true,
                IsVerifiedOnPlatform = true
            });
        }

        // Tráº­n Ä‘áº¥u máº«u giá»¯a cÃ¡c Ä‘á»™i
        if (!await db.Matches.AnyAsync(m => m.TeamAId == teams.Klb.Id && m.TeamBId == teams.SaoVang.Id))
        {
            var upcomingMatch = new MatchGame
            {
                TeamAId = teams.Klb.Id,
                TeamBId = teams.SaoVang.Id,
                MatchDateTime = DateTime.Today.AddDays(2).AddHours(19).AddMinutes(30),
                PitchName = "SÃ¢n bÃ³ng PVV - 72 Tráº§n ThÃ¡i TÃ´ng",
                PitchType = "7 ngÆ°á»i",
                Category = MatchCategory.Friendly,
                Status = MatchStatus.Scheduled,
                IsPublic = true,
                Notes = "Tráº­n giao há»¯u lÆ°á»£t Ä‘i giá»¯a KLB FC vÃ  Sao VÃ ng FC"
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
                PitchName = "SÃ¢n bÃ³ng PVV - 72 Tráº§n ThÃ¡i TÃ´ng",
                PitchType = "7 ngÆ°á»i",
                Category = MatchCategory.Friendly,
                Status = MatchStatus.Completed,
                ScoreA = 3,
                ScoreB = 2,
                ConfirmedByTeamA = true,
                ConfirmedByTeamB = true,
                ConfirmedAt = DateTime.Today.AddDays(-5).AddHours(22),
                IsPublic = true,
                Notes = "Tráº­n thÆ° hÃ¹ng ká»‹ch tÃ­nh giá»¯a KLB FC vÃ  Phoenix FC"
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
                PitchName = "SÃ¢n Thá»§y Lá»£i",
                PitchType = "7 ngÆ°á»i",
                Category = MatchCategory.Friendly,
                Status = MatchStatus.Completed,
                ScoreA = 2,
                ScoreB = 2,
                ConfirmedByTeamA = true,
                ConfirmedByTeamB = true,
                ConfirmedAt = DateTime.Today.AddDays(-3).AddHours(22),
                IsPublic = true,
                Notes = "Tráº­n derby giao lÆ°u giá»¯a TrÃ ng An FC vÃ  FC BÃ¡ch Khoa 2012"
            };
            db.Matches.Add(derbyMatch);
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureRefereesAsync(ApplicationDbContext db)
    {
        if (await db.Referees.AnyAsync()) return;

        var referees = new List<Referee>
        {
            new Referee
            {
                Name = "Nguyá»…n VÄƒn Tuáº¥n",
                ContactInfo = "0912.345.678 - tuan.ref@aloball.vn",
                ExperienceSummary = "Trá»ng tÃ i cáº¥p Quá»‘c gia C2, 5 nÄƒm báº¯t giáº£i phong trÃ o HÃ  Ná»™i, Ä‘iá»u hÃ nh hÆ¡n 200 tráº­n.",
                SupportedFormats = "7 ngÆ°á»i, 11 ngÆ°á»i",
                ServiceNotes = "Báº¯t nhiá»‡t tÃ¬nh, nghiÃªm kháº¯c, kiá»ƒm soÃ¡t tá»‘t cÃ¡c tÃ¬nh huá»‘ng tranh cháº¥p.",
                DefaultPriceVND = 350000,
                IsActive = true
            },
            new Referee
            {
                Name = "Tráº§n ÄÃ¬nh Trá»ng",
                ContactInfo = "0987.654.321 - trong.ref@aloball.vn",
                ExperienceSummary = "Trá»ng tÃ i Futsal & sÃ¢n 7 VFF, kinh nghiá»‡m Ä‘iá»u hÃ nh giáº£i cÃ¡c doanh nghiá»‡p.",
                SupportedFormats = "5 ngÆ°á»i, 7 ngÆ°á»i",
                ServiceNotes = "Di chuyá»ƒn linh hoáº¡t, xá»­ lÃ½ chÃ­nh xÃ¡c luáº­t bÃ³ng Ä‘Ã¡ mini 7 ngÆ°á»i.",
                DefaultPriceVND = 300000,
                IsActive = true
            },
            new Referee
            {
                Name = "LÃª Minh Äá»©c",
                ContactInfo = "0903.112.233 - duc.ref@aloball.vn",
                ExperienceSummary = "Cá»±u cáº§u thá»§ sinh viÃªn ÄHQG, chá»©ng chá»‰ trá»ng tÃ i sÆ¡ cáº¥p, chuyÃªn sÃ¢n 7.",
                SupportedFormats = "7 ngÆ°á»i",
                ServiceNotes = "ThÃ¢n thiá»‡n, giáº£i thÃ­ch luáº­t rÃµ rÃ ng, há»— trá»£ ghi biÃªn báº£n tráº­n Ä‘áº¥u.",
                DefaultPriceVND = 250000,
                IsActive = true
            },
            new Referee
            {
                Name = "HoÃ ng Quá»‘c Viá»‡t",
                ContactInfo = "0944.888.999 - viet.ref@aloball.vn",
                ExperienceSummary = "Trá»ng tÃ i sÃ¢n 11 & sÃ¢n 7, 7 nÄƒm kinh nghiá»‡m cÃ¡c giáº£i phá»§i miá»n Báº¯c.",
                SupportedFormats = "7 ngÆ°á»i, 11 ngÆ°á»i",
                ServiceNotes = "ChuyÃªn báº¯t cÃ¡c tráº­n cáº§u cÄƒng tháº³ng, tÃ­nh cháº¥t cáº¡nh tranh cao.",
                DefaultPriceVND = 400000,
                IsActive = true
            }
        };

        db.Referees.AddRange(referees);
        await db.SaveChangesAsync();
    }

    private static async Task EnsurePitchVenuesAsync(ApplicationDbContext db)
    {
        if (await db.PitchVenues.AnyAsync()) return;

        var venues = new List<PitchVenue>
        {
            new PitchVenue
            {
                Name = "SÃ¢n bÃ³ng ThÃ nh PhÃ¡t",
                Description = "Cá»¥m sÃ¢n cá» nhÃ¢n táº¡o hiá»‡n Ä‘áº¡i báº­c nháº¥t khu vá»±c Cáº§u Giáº¥y, há»‡ thá»‘ng chiáº¿u sÃ¡ng chuáº©n vÃ  thoÃ¡t nÆ°á»›c cá»±c tá»‘t.",
                City = "HÃ  Ná»™i",
                District = "Cáº§u Giáº¥y",
                FullAddress = "Sá»‘ 2 HoÃ ng Minh GiÃ¡m, Trung HÃ²a, Cáº§u Giáº¥y, HÃ  Ná»™i",
                ContactPhone = "0912.345.888",
                SupportedFormats = "5 ngÆ°á»i, 7 ngÆ°á»i",
                SurfaceType = "Cá» nhÃ¢n táº¡o tiÃªu chuáº©n FIFA",
                Facilities = "ÄÃ¨n LED cao cáº¥p, BÃ£i Ä‘á»— xe Ã´ tÃ´ rá»™ng, NÆ°á»›c uá»‘ng miá»…n phÃ­, PhÃ²ng thay Ä‘á»“",
                OpeningHours = "06:00 - 23:30",
                IndicativePricePerHour = 450000,
                PriceNotes = "Khung giá» vÃ ng (17:30 - 20:30): 650.000Ä‘/90p; NgÃ y thÆ°á»ng: 450.000Ä‘/90p",
                ImageUrl = "https://images.unsplash.com/photo-1529900241477-94a53e3ba439?auto=format&fit=crop&w=800&q=80",
                MapLink = "https://maps.google.com/?q=Sá»‘+2+HoÃ ng+Minh+GiÃ¡m+Cáº§u+Giáº¥y+HÃ +Ná»™i",
                TotalPitches = 4,
                IsActive = true
            },
            new PitchVenue
            {
                Name = "SÃ¢n bÃ³ng Thá»§y Lá»£i",
                Description = "Cá»¥m sÃ¢n trong khuÃ´n viÃªn Äáº¡i há»c Thá»§y Lá»£i, máº·t cá» má»m, khÃ¡n Ä‘Ã i mini cÃ³ mÃ¡i che, vá»‹ trÃ­ trung tÃ¢m.",
                City = "HÃ  Ná»™i",
                District = "Äá»‘ng Äa",
                FullAddress = "NgÃµ 95 ChÃ¹a Bá»™c, Trung Liá»‡t, Äá»‘ng Äa, HÃ  Ná»™i",
                ContactPhone = "0988.777.666",
                SupportedFormats = "7 ngÆ°á»i, 11 ngÆ°á»i",
                SurfaceType = "Cá» nhÃ¢n táº¡o sá»£i Ä‘ay cao cáº¥p",
                Facilities = "KhÃ¡n Ä‘Ã i mini, CÄƒn tin nÆ°á»›c giáº£i khÃ¡t, Táº¯m nÃ³ng láº¡nh, Gá»­i xe trÆ°á»ng há»c",
                OpeningHours = "06:00 - 23:00",
                IndicativePricePerHour = 400000,
                PriceNotes = "Khung giá» tá»‘i: 600.000Ä‘/tráº­n 90p; Cuá»‘i tuáº§n phá»¥ thu 50.000Ä‘",
                ImageUrl = "https://images.unsplash.com/photo-1574629810360-7efbbe195018?auto=format&fit=crop&w=800&q=80",
                MapLink = "https://maps.google.com/?q=NgÃµ+95+ChÃ¹a+Bá»™c+Äá»‘ng+Äa+HÃ +Ná»™i",
                TotalPitches = 6,
                IsActive = true
            },
            new PitchVenue
            {
                Name = "SÃ¢n bÃ³ng PVV - Vinaconex 39",
                Description = "SÃ¢n bÃ³ng quen thuá»™c cá»§a giá»›i phá»§i HÃ  thÃ nh, khÃ´ng gian thoÃ¡ng Ä‘Ã£ng, máº·t sÃ¢n Ä‘Æ°á»£c báº£o dÆ°á»¡ng Ä‘á»‹nh ká»³.",
                City = "HÃ  Ná»™i",
                District = "Cáº§u Giáº¥y",
                FullAddress = "Sá»‘ 72 Tráº§n ThÃ¡i TÃ´ng, Dá»‹ch Vá»ng Háº­u, Cáº§u Giáº¥y, HÃ  Ná»™i",
                ContactPhone = "0904.123.456",
                SupportedFormats = "7 ngÆ°á»i",
                SurfaceType = "Cá» nhÃ¢n táº¡o monofilament",
                Facilities = "BÃ£i Ä‘á»— Ã´ tÃ´ xe mÃ¡y, ÄÃ¨n chiáº¿u sÃ¡ng chá»‘ng chÃ³i, Cho thuÃª giÃ y & Ã¡o táº­p",
                OpeningHours = "06:00 - 22:30",
                IndicativePricePerHour = 500000,
                PriceNotes = "GiÃ¡ cá»‘ Ä‘á»‹nh 700.000Ä‘/tráº­n 90p giá» cao Ä‘iá»ƒm",
                ImageUrl = "https://images.unsplash.com/photo-1508098682722-e99c43a406b2?auto=format&fit=crop&w=800&q=80",
                MapLink = "https://maps.google.com/?q=72+Tráº§n+ThÃ¡i+TÃ´ng+Cáº§u+Giáº¥y+HÃ +Ná»™i",
                TotalPitches = 3,
                IsActive = true
            },
            new PitchVenue
            {
                Name = "SÃ¢n váº­n Ä‘á»™ng BÃ¡ch Khoa",
                Description = "SÃ¢n váº­n Ä‘á»™ng kÃ­ch thÆ°á»›c tiÃªu chuáº©n thi Ä‘áº¥u 11 ngÆ°á»i hoáº·c chia thÃ nh 8 sÃ¢n 7 ngÆ°á»i, khÃ¡n Ä‘Ã i A-B sá»©c chá»©a lá»›n.",
                City = "HÃ  Ná»™i",
                District = "Hai BÃ  TrÆ°ng",
                FullAddress = "Sá»‘ 1 Äáº¡i Cá»“ Viá»‡t, BÃ¡ch Khoa, Hai BÃ  TrÆ°ng, HÃ  Ná»™i",
                ContactPhone = "0936.888.999",
                SupportedFormats = "7 ngÆ°á»i, 11 ngÆ°á»i",
                SurfaceType = "Cá» tá»± nhiÃªn & Cá» nhÃ¢n táº¡o",
                Facilities = "KhÃ¡n Ä‘Ã i cÃ³ mÃ¡i che, PhÃ²ng thay Ä‘á»“ VIP, Há»‡ thá»‘ng Ã¢m thanh & báº£ng Ä‘iá»‡n tá»­",
                OpeningHours = "06:00 - 22:00",
                IndicativePricePerHour = 600000,
                PriceNotes = "SÃ¢n 7: 650.000Ä‘/90p; SÃ¢n 11 ngÆ°á»i: 1.800.000Ä‘/120p",
                ImageUrl = "https://images.unsplash.com/photo-1489944445301-4475b3d14996?auto=format&fit=crop&w=800&q=80",
                MapLink = "https://maps.google.com/?q=SÃ¢n+váº­n+Ä‘á»™ng+BÃ¡ch+Khoa+HÃ +Ná»™i",
                TotalPitches = 8,
                IsActive = true
            }
        };

        db.PitchVenues.AddRange(venues);
        await db.SaveChangesAsync();
    }
}