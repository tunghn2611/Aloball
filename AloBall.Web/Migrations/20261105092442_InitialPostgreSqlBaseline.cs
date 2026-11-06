using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AloBall.Web.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgreSqlBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    FullName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AvatarUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PreferredFoot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Experience = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PrimaryPosition = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    HasCompletedOnboarding = table.Column<bool>(type: "boolean", nullable: false),
                    OnboardingCompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PitchVenues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    District = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FullAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContactPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SupportedFormats = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SurfaceType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Facilities = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OpeningHours = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IndicativePricePerHour = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    PriceNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    MapLink = table.Column<string>(type: "text", nullable: true),
                    TotalPitches = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PitchVenues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Referees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ContactInfo = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    ExperienceSummary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SupportedFormats = table.Column<string>(type: "text", nullable: true),
                    ServiceNotes = table.Column<string>(type: "text", nullable: true),
                    DefaultPriceVND = table.Column<decimal>(type: "numeric", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Referees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LogoUrl = table.Column<string>(type: "text", nullable: true),
                    CoverImageUrl = table.Column<string>(type: "text", nullable: true),
                    CoverOpacity = table.Column<double>(type: "double precision", nullable: true),
                    EstablishedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ActivityArea = table.Column<string>(type: "text", nullable: true),
                    HomePitch = table.Column<string>(type: "text", nullable: true),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    IsDraft = table.Column<bool>(type: "boolean", nullable: false),
                    IsRestrictedFromMatchmaking = table.Column<bool>(type: "boolean", nullable: false),
                    MatchmakingRestrictionReason = table.Column<string>(type: "text", nullable: true),
                    AverageRating = table.Column<double>(type: "double precision", nullable: false),
                    ValidReviewCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionAchievements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    TournamentName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    SeasonOrYear = table.Column<string>(type: "text", nullable: false),
                    TitleOrRank = table.Column<string>(type: "text", nullable: false),
                    ProofUrl = table.Column<string>(type: "text", nullable: true),
                    IsFeatured = table.Column<bool>(type: "boolean", nullable: false),
                    IsVerifiedOnPlatform = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionAchievements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionAchievements_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FireCompetitionPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    StatisticalPeriod = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedByUserId = table.Column<string>(type: "text", nullable: true),
                    ReopenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReopenedByUserId = table.Column<string>(type: "text", nullable: true),
                    ReopenReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FireCompetitionPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FireCompetitionPeriods_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KitOrderBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KitOrderBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KitOrderBatches_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Matches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MatchDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PitchName = table.Column<string>(type: "text", nullable: false),
                    PitchType = table.Column<string>(type: "text", nullable: false),
                    TeamAId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamBId = table.Column<Guid>(type: "uuid", nullable: true),
                    TeamBExternalName = table.Column<string>(type: "text", nullable: true),
                    ScoreA = table.Column<int>(type: "integer", nullable: true),
                    ScoreB = table.Column<int>(type: "integer", nullable: true),
                    IsScoreIncomplete = table.Column<bool>(type: "boolean", nullable: false),
                    ConfirmedByTeamA = table.Column<bool>(type: "boolean", nullable: false),
                    ConfirmedByTeamB = table.Column<bool>(type: "boolean", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    ScoreChangeReason = table.Column<string>(type: "text", nullable: true),
                    ScoreChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    LineupRevision = table.Column<int>(type: "integer", nullable: false),
                    FormationCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Matches_Teams_TeamAId",
                        column: x => x.TeamAId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Matches_Teams_TeamBId",
                        column: x => x.TeamBId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MonthlyAttendanceClosings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    StatisticalPeriod = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedByUserId = table.Column<string>(type: "text", nullable: true),
                    RegulationVersion = table.Column<int>(type: "integer", nullable: false),
                    RegulationMinMatches = table.Column<decimal>(type: "numeric", nullable: false),
                    RegulationUnderAttendanceFine = table.Column<decimal>(type: "numeric", nullable: false),
                    ReopenReason = table.Column<string>(type: "text", nullable: true),
                    ReopenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReopenedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyAttendanceClosings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonthlyAttendanceClosings_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedUserId = table.Column<string>(type: "text", nullable: true),
                    FullName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    KitNumber = table.Column<int>(type: "integer", nullable: true),
                    Position = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    StandardPrimaryPosition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AllowedSecondaryPositionsJson = table.Column<string>(type: "text", nullable: true),
                    PhotoUrl = table.Column<string>(type: "text", nullable: true),
                    HeightCm = table.Column<double>(type: "double precision", nullable: true),
                    WeightKg = table.Column<double>(type: "double precision", nullable: true),
                    ShirtName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ShirtSize = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ShortSize = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    JoinedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InactiveDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InternalNotes = table.Column<string>(type: "text", nullable: true),
                    PreferredFoot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Experience = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsPublicProfile = table.Column<bool>(type: "boolean", nullable: false),
                    PublicKitNumber = table.Column<bool>(type: "boolean", nullable: false),
                    PublicPosition = table.Column<bool>(type: "boolean", nullable: false),
                    PublicHeightWeight = table.Column<bool>(type: "boolean", nullable: false),
                    PublicPhoto = table.Column<bool>(type: "boolean", nullable: false),
                    PublicPreferredFoot = table.Column<bool>(type: "boolean", nullable: false),
                    PublicExperience = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Players_AspNetUsers_LinkedUserId",
                        column: x => x.LinkedUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Players_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamFireRegulations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PointsPerAttendance = table.Column<int>(type: "integer", nullable: false),
                    PointsPerGoal = table.Column<int>(type: "integer", nullable: false),
                    PointsPerAssist = table.Column<int>(type: "integer", nullable: false),
                    PointsPerWin = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamFireRegulations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamFireRegulations_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    IsBtc = table.Column<bool>(type: "boolean", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamMembers_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderUserId = table.Column<string>(type: "text", nullable: false),
                    RecipientUserId = table.Column<string>(type: "text", nullable: true),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageType = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamMessages_AspNetUsers_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamMessages_AspNetUsers_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamMessages_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamRegulations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    FineNoResponse = table.Column<decimal>(type: "numeric", nullable: false),
                    FineAttendingButAbsent = table.Column<decimal>(type: "numeric", nullable: false),
                    MinMatchesPerMonth = table.Column<int>(type: "integer", nullable: false),
                    FineUnderMinAttendance = table.Column<decimal>(type: "numeric", nullable: false),
                    DefaultDeadlineTimeOfDay = table.Column<TimeSpan>(type: "interval", nullable: false),
                    QuarterlyFundDue = table.Column<decimal>(type: "numeric", nullable: false),
                    IsExcusedExemptFromFine = table.Column<bool>(type: "boolean", nullable: false),
                    CombineMonthlyAndSessionFines = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamRegulations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamRegulations_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SessionDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResponseDeadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StatisticalPeriod = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PitchName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    PitchType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceSessions_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AttendanceSessions_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchExpenseEstimates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EstimatedAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchExpenseEstimates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchExpenseEstimates_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchExpenseEstimates_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchmakingPosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostType = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetTeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    MatchDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    Area = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PitchArrangement = table.Column<int>(type: "integer", nullable: false),
                    PitchName = table.Column<string>(type: "text", nullable: true),
                    IsPitchBooked = table.Column<bool>(type: "boolean", nullable: false),
                    PitchType = table.Column<string>(type: "text", nullable: false),
                    DesiredLevel = table.Column<string>(type: "text", nullable: false),
                    CostSharing = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AcceptedProposalId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedMatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchmakingPosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchmakingPosts_Matches_CreatedMatchId",
                        column: x => x.CreatedMatchId,
                        principalTable: "Matches",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MatchmakingPosts_Teams_TargetTeamId",
                        column: x => x.TargetTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchmakingPosts_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchSettlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    SettlementDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalPitchCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TotalRefereeCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TotalOtherExpenses = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ParticipantCount = table.Column<int>(type: "integer", nullable: false),
                    PerPersonShare = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmedByUserId = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchSettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchSettlements_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchSettlements_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PitchBookingRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    PitchVenueId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    ServiceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    PitchFormat = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PitchCount = table.Column<int>(type: "integer", nullable: false),
                    BudgetVND = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ConfirmedFieldCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PitchBookingRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PitchBookingRequests_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PitchBookingRequests_PitchVenues_PitchVenueId",
                        column: x => x.PitchVenueId,
                        principalTable: "PitchVenues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PitchBookingRequests_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RefereeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    ServiceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    VenueName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VenueAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    MatchFormat = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RefereeCount = table.Column<int>(type: "integer", nullable: false),
                    PreferredExperience = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BudgetVND = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefereeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefereeRequests_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RefereeRequests_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamMatchReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedByUserId = table.Column<string>(type: "text", nullable: false),
                    RatingStars = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: false),
                    IsValid = table.Column<bool>(type: "boolean", nullable: false),
                    InvalidationReason = table.Column<string>(type: "text", nullable: true),
                    ModeratedByAdminId = table.Column<string>(type: "text", nullable: true),
                    ModeratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMatchReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamMatchReviews_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamMatchReviews_Teams_ReviewerTeamId",
                        column: x => x.ReviewerTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamMatchReviews_Teams_TargetTeamId",
                        column: x => x.TargetTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KitOrderItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlayerName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ShirtName = table.Column<string>(type: "text", nullable: true),
                    KitNumber = table.Column<int>(type: "integer", nullable: true),
                    ShirtSize = table.Column<string>(type: "text", nullable: true),
                    ShortSize = table.Column<string>(type: "text", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    IsPaid = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KitOrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KitOrderItems_KitOrderBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "KitOrderBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KitOrderItems_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MatchGoalEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScorerPlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssistPlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Minute = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchGoalEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchGoalEvents_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchGoalEvents_Players_AssistPlayerId",
                        column: x => x.AssistPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchGoalEvents_Players_ScorerPlayerId",
                        column: x => x.ScorerPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchGoalEvents_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatchPlayerLineups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamSide = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    IsStarting = table.Column<bool>(type: "boolean", nullable: false),
                    AssignedPosition = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AssignedSlotCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AssignedKitNumber = table.Column<int>(type: "integer", nullable: true),
                    HasActuallyPlayed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchPlayerLineups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchPlayerLineups_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchPlayerLineups_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatchPositionExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllowedSlotCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    GrantedByUserId = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchPositionExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchPositionExceptions_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchPositionExceptions_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MonthlyFireAwards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    StatisticalPeriod = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    PeriodRevision = table.Column<int>(type: "integer", nullable: false),
                    TotalAbPoints = table.Column<int>(type: "integer", nullable: false),
                    AttendedSessions = table.Column<int>(type: "integer", nullable: false),
                    Goals = table.Column<int>(type: "integer", nullable: false),
                    Assists = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    AwardTitle = table.Column<string>(type: "text", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    DecidedByUserId = table.Column<string>(type: "text", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyFireAwards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonthlyFireAwards_FireCompetitionPeriods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "FireCompetitionPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MonthlyFireAwards_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MonthlyFireAwards_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MonthlyPlayerSummaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MonthlyClosingId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalSessionsInMonth = table.Column<int>(type: "integer", nullable: false),
                    AttendedSessionsCount = table.Column<int>(type: "integer", nullable: false),
                    IsBtc = table.Column<bool>(type: "boolean", nullable: false),
                    TotalSessionFines = table.Column<decimal>(type: "numeric", nullable: false),
                    UnderAttendanceFine = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalDue = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalPaid = table.Column<decimal>(type: "numeric", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyPlayerSummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonthlyPlayerSummaries_MonthlyAttendanceClosings_MonthlyClo~",
                        column: x => x.MonthlyClosingId,
                        principalTable: "MonthlyAttendanceClosings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MonthlyPlayerSummaries_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerAbJournalEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    StatisticalPeriod = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    SourceDomain = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceRole = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceRevision = table.Column<int>(type: "integer", nullable: false),
                    OperationKey = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    RegulationVersion = table.Column<int>(type: "integer", nullable: false),
                    ReversedEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdjustmentReason = table.Column<string>(type: "text", nullable: true),
                    EventDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerAbJournalEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerAbJournalEntries_PlayerAbJournalEntries_ReversedEntry~",
                        column: x => x.ReversedEntryId,
                        principalTable: "PlayerAbJournalEntries",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlayerAbJournalEntries_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerAbJournalEntries_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Response = table.Column<int>(type: "integer", nullable: false),
                    ResponseTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsResponseLate = table.Column<bool>(type: "boolean", nullable: false),
                    Actual = table.Column<int>(type: "integer", nullable: false),
                    FineAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    NeedsConfirmation = table.Column<bool>(type: "boolean", nullable: false),
                    PenaltyReason = table.Column<string>(type: "text", nullable: true),
                    IsExempt = table.Column<bool>(type: "boolean", nullable: false),
                    ExemptionReason = table.Column<string>(type: "text", nullable: true),
                    AdjustedByUserId = table.Column<string>(type: "text", nullable: true),
                    AdjustedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsPaid = table.Column<bool>(type: "boolean", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_AttendanceSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AttendanceSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatchmakingProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengingTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposedByUserId = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchmakingProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchmakingProposals_MatchmakingPosts_PostId",
                        column: x => x.PostId,
                        principalTable: "MatchmakingPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchmakingProposals_Teams_ChallengingTeamId",
                        column: x => x.ChallengingTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatchSettlementItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SettlementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    IsFromConfirmedService = table.Column<bool>(type: "boolean", nullable: false),
                    SourceReferenceId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchSettlementItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchSettlementItems_MatchSettlements_SettlementId",
                        column: x => x.SettlementId,
                        principalTable: "MatchSettlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchSettlementMemberAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SettlementId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    CalculatedAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AdjustedAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AdjustmentReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AmountPaid = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PaymentStatus = table.Column<int>(type: "integer", nullable: false),
                    LastPaymentDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchSettlementMemberAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchSettlementMemberAllocations_MatchSettlements_Settlemen~",
                        column: x => x.SettlementId,
                        principalTable: "MatchSettlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchSettlementMemberAllocations_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PitchBookingOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminId = table.Column<string>(type: "text", nullable: false),
                    ConfirmedVenueName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ConfirmedAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ConfirmedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConfirmedStartTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    ConfirmedEndTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    ConfirmedPitchFormat = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ConfirmedPitchCount = table.Column<int>(type: "integer", nullable: false),
                    SpecificFieldCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TotalPriceVND = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DepositInstructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PitchBookingOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PitchBookingOffers_PitchBookingRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "PitchBookingRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RefereeProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminId = table.Column<string>(type: "text", nullable: false),
                    TotalPriceVND = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ServiceNote = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefereeProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefereeProposals_RefereeRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "RefereeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReviewDisputeAppeals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppealReason = table.Column<string>(type: "text", nullable: false),
                    EvidenceUrl = table.Column<string>(type: "text", nullable: true),
                    IsResolved = table.Column<bool>(type: "boolean", nullable: false),
                    AdminResolutionNote = table.Column<string>(type: "text", nullable: true),
                    ResolvedByAdminId = table.Column<string>(type: "text", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewDisputeAppeals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewDisputeAppeals_TeamMatchReviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "TeamMatchReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReviewDisputeAppeals_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchSettlementAdjustmentAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    NewAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    MandatoryReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AdjustedByUserId = table.Column<string>(type: "text", nullable: false),
                    AdjustedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchSettlementAdjustmentAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchSettlementAdjustmentAudits_MatchSettlementMemberAlloca~",
                        column: x => x.AllocationId,
                        principalTable: "MatchSettlementMemberAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchSettlementPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PaymentDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaymentMethod = table.Column<int>(type: "integer", nullable: false),
                    ReferenceCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecordedByUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsReversed = table.Column<bool>(type: "boolean", nullable: false),
                    ReversalReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReversedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReversedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchSettlementPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchSettlementPayments_MatchSettlementMemberAllocations_Al~",
                        column: x => x.AllocationId,
                        principalTable: "MatchSettlementMemberAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RefereeAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefereeId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefereeAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefereeAssignments_RefereeProposals_ProposalId",
                        column: x => x.ProposalId,
                        principalTable: "RefereeProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RefereeAssignments_Referees_RefereeId",
                        column: x => x.RefereeId,
                        principalTable: "Referees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_PlayerId",
                table: "AttendanceRecords",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_SessionId_PlayerId",
                table: "AttendanceRecords",
                columns: new[] { "SessionId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSessions_MatchId",
                table: "AttendanceSessions",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSessions_TeamId",
                table: "AttendanceSessions",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionAchievements_TeamId",
                table: "CompetitionAchievements",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_FireCompetitionPeriods_TeamId_StatisticalPeriod",
                table: "FireCompetitionPeriods",
                columns: new[] { "TeamId", "StatisticalPeriod" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KitOrderBatches_TeamId",
                table: "KitOrderBatches",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_KitOrderItems_BatchId",
                table: "KitOrderItems",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_KitOrderItems_PlayerId",
                table: "KitOrderItems",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_TeamAId",
                table: "Matches",
                column: "TeamAId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_TeamBId",
                table: "Matches",
                column: "TeamBId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchExpenseEstimates_MatchId",
                table: "MatchExpenseEstimates",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchExpenseEstimates_TeamId",
                table: "MatchExpenseEstimates",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchGoalEvents_AssistPlayerId",
                table: "MatchGoalEvents",
                column: "AssistPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchGoalEvents_MatchId",
                table: "MatchGoalEvents",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchGoalEvents_ScorerPlayerId",
                table: "MatchGoalEvents",
                column: "ScorerPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchGoalEvents_TeamId",
                table: "MatchGoalEvents",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchmakingPosts_CreatedMatchId",
                table: "MatchmakingPosts",
                column: "CreatedMatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchmakingPosts_TargetTeamId",
                table: "MatchmakingPosts",
                column: "TargetTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchmakingPosts_TeamId",
                table: "MatchmakingPosts",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchmakingProposals_ChallengingTeamId",
                table: "MatchmakingProposals",
                column: "ChallengingTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchmakingProposals_PostId",
                table: "MatchmakingProposals",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPlayerLineups_MatchId_PlayerId",
                table: "MatchPlayerLineups",
                columns: new[] { "MatchId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchPlayerLineups_PlayerId",
                table: "MatchPlayerLineups",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPositionExceptions_MatchId_PlayerId_AllowedSlotCode",
                table: "MatchPositionExceptions",
                columns: new[] { "MatchId", "PlayerId", "AllowedSlotCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchPositionExceptions_PlayerId",
                table: "MatchPositionExceptions",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchSettlementAdjustmentAudits_AllocationId",
                table: "MatchSettlementAdjustmentAudits",
                column: "AllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchSettlementItems_SettlementId",
                table: "MatchSettlementItems",
                column: "SettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchSettlementMemberAllocations_PlayerId",
                table: "MatchSettlementMemberAllocations",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchSettlementMemberAllocations_SettlementId_PlayerId",
                table: "MatchSettlementMemberAllocations",
                columns: new[] { "SettlementId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchSettlementPayments_AllocationId",
                table: "MatchSettlementPayments",
                column: "AllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchSettlements_MatchId_TeamId",
                table: "MatchSettlements",
                columns: new[] { "MatchId", "TeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchSettlements_TeamId",
                table: "MatchSettlements",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyAttendanceClosings_TeamId",
                table: "MonthlyAttendanceClosings",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyFireAwards_PeriodId_PlayerId_PeriodRevision",
                table: "MonthlyFireAwards",
                columns: new[] { "PeriodId", "PlayerId", "PeriodRevision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyFireAwards_PlayerId",
                table: "MonthlyFireAwards",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyFireAwards_TeamId",
                table: "MonthlyFireAwards",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyPlayerSummaries_MonthlyClosingId",
                table: "MonthlyPlayerSummaries",
                column: "MonthlyClosingId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyPlayerSummaries_PlayerId",
                table: "MonthlyPlayerSummaries",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PitchBookingOffers_RequestId",
                table: "PitchBookingOffers",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_PitchBookingOffers_Status",
                table: "PitchBookingOffers",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PitchBookingRequests_MatchId",
                table: "PitchBookingRequests",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PitchBookingRequests_PitchVenueId",
                table: "PitchBookingRequests",
                column: "PitchVenueId");

            migrationBuilder.CreateIndex(
                name: "IX_PitchBookingRequests_Status",
                table: "PitchBookingRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PitchBookingRequests_TeamId",
                table: "PitchBookingRequests",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_PitchVenues_City",
                table: "PitchVenues",
                column: "City");

            migrationBuilder.CreateIndex(
                name: "IX_PitchVenues_District",
                table: "PitchVenues",
                column: "District");

            migrationBuilder.CreateIndex(
                name: "IX_PitchVenues_IsActive",
                table: "PitchVenues",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAbJournalEntries_OperationKey",
                table: "PlayerAbJournalEntries",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAbJournalEntries_PlayerId",
                table: "PlayerAbJournalEntries",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAbJournalEntries_ReversedEntryId",
                table: "PlayerAbJournalEntries",
                column: "ReversedEntryId",
                unique: true,
                filter: "\"ReversedEntryId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAbJournalEntries_TeamId",
                table: "PlayerAbJournalEntries",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_LinkedUserId",
                table: "Players",
                column: "LinkedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_TeamId",
                table: "Players",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_RefereeAssignments_ProposalId_RefereeId",
                table: "RefereeAssignments",
                columns: new[] { "ProposalId", "RefereeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefereeAssignments_RefereeId",
                table: "RefereeAssignments",
                column: "RefereeId");

            migrationBuilder.CreateIndex(
                name: "IX_RefereeProposals_RequestId",
                table: "RefereeProposals",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_RefereeProposals_Status",
                table: "RefereeProposals",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RefereeRequests_MatchId",
                table: "RefereeRequests",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_RefereeRequests_Status",
                table: "RefereeRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RefereeRequests_TeamId",
                table: "RefereeRequests",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewDisputeAppeals_ReviewId",
                table: "ReviewDisputeAppeals",
                column: "ReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewDisputeAppeals_TeamId",
                table: "ReviewDisputeAppeals",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamFireRegulations_TeamId_Version",
                table: "TeamFireRegulations",
                columns: new[] { "TeamId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamMatchReviews_MatchId_ReviewerTeamId",
                table: "TeamMatchReviews",
                columns: new[] { "MatchId", "ReviewerTeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamMatchReviews_ReviewerTeamId",
                table: "TeamMatchReviews",
                column: "ReviewerTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMatchReviews_TargetTeamId",
                table: "TeamMatchReviews",
                column: "TargetTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TeamId_UserId",
                table: "TeamMembers",
                columns: new[] { "TeamId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_UserId",
                table: "TeamMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMessages_RecipientUserId_IsRead",
                table: "TeamMessages",
                columns: new[] { "RecipientUserId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamMessages_SenderUserId",
                table: "TeamMessages",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMessages_TeamId_ConversationId",
                table: "TeamMessages",
                columns: new[] { "TeamId", "ConversationId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamMessages_TeamId_SenderUserId_CreatedAt",
                table: "TeamMessages",
                columns: new[] { "TeamId", "SenderUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamRegulations_TeamId",
                table: "TeamRegulations",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_Slug",
                table: "Teams",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AttendanceRecords");

            migrationBuilder.DropTable(
                name: "CompetitionAchievements");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "KitOrderItems");

            migrationBuilder.DropTable(
                name: "MatchExpenseEstimates");

            migrationBuilder.DropTable(
                name: "MatchGoalEvents");

            migrationBuilder.DropTable(
                name: "MatchmakingProposals");

            migrationBuilder.DropTable(
                name: "MatchPlayerLineups");

            migrationBuilder.DropTable(
                name: "MatchPositionExceptions");

            migrationBuilder.DropTable(
                name: "MatchSettlementAdjustmentAudits");

            migrationBuilder.DropTable(
                name: "MatchSettlementItems");

            migrationBuilder.DropTable(
                name: "MatchSettlementPayments");

            migrationBuilder.DropTable(
                name: "MonthlyFireAwards");

            migrationBuilder.DropTable(
                name: "MonthlyPlayerSummaries");

            migrationBuilder.DropTable(
                name: "PitchBookingOffers");

            migrationBuilder.DropTable(
                name: "PlayerAbJournalEntries");

            migrationBuilder.DropTable(
                name: "RefereeAssignments");

            migrationBuilder.DropTable(
                name: "ReviewDisputeAppeals");

            migrationBuilder.DropTable(
                name: "TeamFireRegulations");

            migrationBuilder.DropTable(
                name: "TeamMembers");

            migrationBuilder.DropTable(
                name: "TeamMessages");

            migrationBuilder.DropTable(
                name: "TeamRegulations");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AttendanceSessions");

            migrationBuilder.DropTable(
                name: "KitOrderBatches");

            migrationBuilder.DropTable(
                name: "MatchmakingPosts");

            migrationBuilder.DropTable(
                name: "MatchSettlementMemberAllocations");

            migrationBuilder.DropTable(
                name: "FireCompetitionPeriods");

            migrationBuilder.DropTable(
                name: "MonthlyAttendanceClosings");

            migrationBuilder.DropTable(
                name: "PitchBookingRequests");

            migrationBuilder.DropTable(
                name: "RefereeProposals");

            migrationBuilder.DropTable(
                name: "Referees");

            migrationBuilder.DropTable(
                name: "TeamMatchReviews");

            migrationBuilder.DropTable(
                name: "MatchSettlements");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "PitchVenues");

            migrationBuilder.DropTable(
                name: "RefereeRequests");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Matches");

            migrationBuilder.DropTable(
                name: "Teams");
        }
    }
}
