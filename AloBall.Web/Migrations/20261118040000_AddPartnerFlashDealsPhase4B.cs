using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AloBall.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPartnerFlashDealsPhase4B : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerUserId",
                table: "PitchVenues",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAvailable",
                table: "Referees",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "Referees",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PitchFlashDeals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PitchId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    KickoffAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    OriginalPriceVnd = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DealPriceVnd = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DiscountPercent = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ClaimedByTeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClaimedMatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClaimedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PitchFlashDeals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PitchFlashDeals_Matches_ClaimedMatchId",
                        column: x => x.ClaimedMatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PitchFlashDeals_PitchVenues_PitchId",
                        column: x => x.PitchId,
                        principalTable: "PitchVenues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PitchFlashDeals_Teams_ClaimedByTeamId",
                        column: x => x.ClaimedByTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PitchVenues_OwnerUserId",
                table: "PitchVenues",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Referees_UserId",
                table: "Referees",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PitchFlashDeals_ClaimedByTeamId",
                table: "PitchFlashDeals",
                column: "ClaimedByTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_PitchFlashDeals_ClaimedMatchId",
                table: "PitchFlashDeals",
                column: "ClaimedMatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PitchFlashDeals_PitchId",
                table: "PitchFlashDeals",
                column: "PitchId");

            migrationBuilder.CreateIndex(
                name: "IX_PitchFlashDeals_Status_Region_KickoffAt",
                table: "PitchFlashDeals",
                columns: new[] { "Status", "Region", "KickoffAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PitchFlashDeals");

            migrationBuilder.DropIndex(
                name: "IX_PitchVenues_OwnerUserId",
                table: "PitchVenues");

            migrationBuilder.DropIndex(
                name: "IX_Referees_UserId",
                table: "Referees");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "PitchVenues");

            migrationBuilder.DropColumn(
                name: "IsAvailable",
                table: "Referees");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Referees");
        }
    }
}
