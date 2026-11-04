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

        // Đảm bảo Database đã được tạo
        await db.Database.EnsureCreatedAsync();

        // 1. Tự động kiểm tra và thêm các cột mới vào SQLite DB cũ nếu chưa tồn tại
        if (db.Database.IsRelational())
        {
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
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error during SeedDemoDataAsync: {ex.Message} -> {ex.StackTrace}", ex);
        }
    }

    private static async Task EnsureDatabaseSchemaAsync(ApplicationDbContext db)
    {
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
        async Task<ApplicationUser> GetOrCreateUserAsync(string email, string fullName, string password)
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    FullName = fullName,
                    EmailConfirmed = true
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
            Admin = await GetOrCreateUserAsync("admin@aloball.vn", "Platform Administrator", "Admin@123456"),
            KlbOwner = await GetOrCreateUserAsync("owner@klbfc.vn", "Nguyễn Văn Chủ Tịch", "Owner@123456"),
            KlbCaptain = await GetOrCreateUserAsync("captain@klbfc.vn", "Trần Đội Trưởng", "Captain@123456"),
            KlbMember = await GetOrCreateUserAsync("member@klbfc.vn", "Lê Cầu Thủ", "Member@123456"),
            SaoVangCaptain = await GetOrCreateUserAsync("captain@saovangfc.vn", "Hoàng Sao Vàng", "SaoVang@123456"),
            PhoenixCaptain = await GetOrCreateUserAsync("captain@phoenixfc.vn", "Vũ Phượng Hoàng", "Phoenix@123456"),
            TrangAnCaptain = await GetOrCreateUserAsync("captain@tranganfc.vn", "Đinh Tràng An", "TrangAn@123456"),
            BachKhoaCaptain = await GetOrCreateUserAsync("captain@bachkhoafc.vn", "Tạ Bách Khoa", "BachKhoa@123456")
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

        // Gán ban cán sự / Thành viên nếu chưa có
        await EnsureMembershipAsync(db, klb.Id, users.KlbOwner.Id, TeamRole.Owner, true);
        await EnsureMembershipAsync(db, klb.Id, users.KlbCaptain.Id, TeamRole.Manager, true);
        await EnsureMembershipAsync(db, klb.Id, users.KlbMember.Id, TeamRole.Member, false);
        await EnsureMembershipAsync(db, saoVang.Id, users.SaoVangCaptain.Id, TeamRole.Owner, true);
        await EnsureMembershipAsync(db, phoenix.Id, users.PhoenixCaptain.Id, TeamRole.Owner, true);
        await EnsureMembershipAsync(db, trangAn.Id, users.TrangAnCaptain.Id, TeamRole.Owner, true);
        await EnsureMembershipAsync(db, bachKhoa.Id, users.BachKhoaCaptain.Id, TeamRole.Owner, true);

        // Bổ sung đầy đủ cầu thủ cho từng đội
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
        // 16 cầu thủ - Đầy đủ 2 GK, 5 DF, 5 MF, 4 FW (Lý tưởng kiểm tra 14 đá chính + 2 dự bị cho sơ đồ 7_231 hoặc 7_321)
        await AddOrUpdatePlayerAsync(db, teamId, "Phạm Trung Kiên", 1, "Thủ môn", PositionCodes.GK, new[] { PositionCodes.CB }, null, "KIEN GK", 182, 76);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyễn Minh Hoàng", 25, "Thủ môn", PositionCodes.GK, new string[] { }, null, "HOANG GK", 180, 74);

        await AddOrUpdatePlayerAsync(db, teamId, "Vũ Đình Trọng", 5, "Trung vệ", PositionCodes.CB, new[] { PositionCodes.DF_L, PositionCodes.CDM }, null, "TRONG CB", 178, 73);
        await AddOrUpdatePlayerAsync(db, teamId, "Bùi Tiến Dũng", 4, "Trung vệ", PositionCodes.CB, new[] { PositionCodes.DF_R }, null, "TIEN DUNG", 179, 75);
        await AddOrUpdatePlayerAsync(db, teamId, "Lê Cầu Thủ", 8, "Hậu vệ cánh trái", PositionCodes.LB, new[] { PositionCodes.CB, PositionCodes.LM }, users.KlbMember.Id, "LE THU", 175, 69);
        await AddOrUpdatePlayerAsync(db, teamId, "Trần Văn Hậu", 2, "Hậu vệ cánh trái", PositionCodes.LB, new[] { PositionCodes.LM }, null, "VAN HAU", 174, 68);
        await AddOrUpdatePlayerAsync(db, teamId, "Đỗ Thanh Bình", 3, "Hậu vệ cánh phải", PositionCodes.RB, new[] { PositionCodes.RM, PositionCodes.CB }, null, "THANH BINH", 176, 70);

        await AddOrUpdatePlayerAsync(db, teamId, "Nguyễn Văn Chủ Tịch", 10, "Tiền vệ trung tâm", PositionCodes.CM, new[] { PositionCodes.CAM, PositionCodes.ST }, users.KlbOwner.Id, "VAN CHU TICH", 172, 68);
        await AddOrUpdatePlayerAsync(db, teamId, "Đỗ Hùng Dũng", 16, "Tiền vệ phòng ngự", PositionCodes.CDM, new[] { PositionCodes.CM, PositionCodes.CB }, null, "HUNG DUNG", 174, 69);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyễn Quang Hải", 19, "Tiền vệ tấn công", PositionCodes.CAM, new[] { PositionCodes.CM, PositionCodes.RW }, null, "QUANG HAI", 168, 65);
        await AddOrUpdatePlayerAsync(db, teamId, "Lương Xuân Trường", 6, "Tiền vệ trung tâm", PositionCodes.CM, new[] { PositionCodes.CAM, PositionCodes.CDM }, null, "XUAN TRUONG", 177, 71);
        await AddOrUpdatePlayerAsync(db, teamId, "Phan Văn Đức", 20, "Tiền vệ cánh trái", PositionCodes.LM, new[] { PositionCodes.LW, PositionCodes.LB }, null, "VAN DUC", 173, 67);

        await AddOrUpdatePlayerAsync(db, teamId, "Trần Đội Trưởng", 7, "Tiền đạo cánh phải", PositionCodes.RW, new[] { PositionCodes.RM, PositionCodes.ST }, users.KlbCaptain.Id, "DOI TRUONG", 175, 70);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyễn Công Phượng", 11, "Tiền đạo cánh trái", PositionCodes.LW, new[] { PositionCodes.ST, PositionCodes.CAM }, null, "CONG PHUONG", 170, 66);
        await AddOrUpdatePlayerAsync(db, teamId, "Nguyễn Tiến Linh", 22, "Tiền đạo cắm", PositionCodes.ST, new[] { PositionCodes.CF }, null, "TIEN LINH", 183, 77);
        await AddOrUpdatePlayerAsync(db, teamId, "Hà Đức Chinh", 9, "Tiền đạo cắm", PositionCodes.ST, new[] { PositionCodes.LW, PositionCodes.RW }, null, "DUC CHINH", 177, 74);
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

        await db.SaveChangesAsync();
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
        if (await db.PitchVenues.AnyAsync()) return;

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
            }
        };

        db.PitchVenues.AddRange(venues);
        await db.SaveChangesAsync();
    }
}
