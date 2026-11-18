using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AloBall.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddClubPlanAutoPairingAndConcierge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PlanExpiresAt",
                table: "Teams",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlanTier",
                table: "Teams",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "MatchConciergeOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    Region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    KickoffAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MinStars = table.Column<double>(type: "double precision", nullable: false),
                    MaxStars = table.Column<double>(type: "double precision", nullable: false),
                    MinFriendlyRating = table.Column<double>(type: "double precision", nullable: false),
                    IsProWithReferee = table.Column<bool>(type: "boolean", nullable: false),
                    MatchedOpponentTeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    BookedPitchSlotId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedRefereeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResultMatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    EstimatedTotalFee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchConciergeOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchConciergeOrders_Teams_MatchedOpponentTeamId",
                        column: x => x.MatchedOpponentTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchConciergeOrders_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamRecurringSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsAutoPairingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DaysOfWeekMask = table.Column<int>(type: "integer", nullable: false),
                    PreferredKickoffTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    Region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MinAloballStars = table.Column<double>(type: "double precision", nullable: false),
                    MaxAloballStars = table.Column<double>(type: "double precision", nullable: false),
                    MinFriendlyRating = table.Column<double>(type: "double precision", nullable: false),
                    AvoidHardTackle = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamRecurringSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamRecurringSchedules_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchConciergeOrders_MatchedOpponentTeamId",
                table: "MatchConciergeOrders",
                column: "MatchedOpponentTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchConciergeOrders_TeamId_Status",
                table: "MatchConciergeOrders",
                columns: new[] { "TeamId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamRecurringSchedules_TeamId",
                table: "TeamRecurringSchedules",
                column: "TeamId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchConciergeOrders");

            migrationBuilder.DropTable(
                name: "TeamRecurringSchedules");

            migrationBuilder.DropColumn(
                name: "PlanExpiresAt",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "PlanTier",
                table: "Teams");
        }
    }
}
