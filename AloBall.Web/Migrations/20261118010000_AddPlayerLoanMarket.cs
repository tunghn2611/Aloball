using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AloBall.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerLoanMarket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerLoanListings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ListingType = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    Region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PitchName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    PlayDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TimeSlot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PositionsNeeded = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FeeType = table.Column<int>(type: "integer", nullable: false),
                    LoanFeeAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    EscrowAbCoin = table.Column<int>(type: "integer", nullable: false),
                    EscrowJournalEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsEscrowReleased = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AcceptedApplicationId = table.Column<Guid>(type: "uuid", nullable: true),
                    LoanedGuestPlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerLoanListings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerLoanListings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerLoanListings_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlayerLoanListings_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlayerLoanListings_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PlayerLoanApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicantUserId = table.Column<string>(type: "text", nullable: false),
                    ApplicantPlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApplicantTeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetMatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    Position = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerLoanApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerLoanApplications_AspNetUsers_ApplicantUserId",
                        column: x => x.ApplicantUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlayerLoanApplications_Matches_TargetMatchId",
                        column: x => x.TargetMatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlayerLoanApplications_PlayerLoanListings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "PlayerLoanListings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerLoanApplications_Players_ApplicantPlayerId",
                        column: x => x.ApplicantPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlayerLoanApplications_Teams_ApplicantTeamId",
                        column: x => x.ApplicantTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLoanApplications_ApplicantPlayerId",
                table: "PlayerLoanApplications",
                column: "ApplicantPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLoanApplications_ApplicantTeamId",
                table: "PlayerLoanApplications",
                column: "ApplicantTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLoanApplications_ApplicantUserId",
                table: "PlayerLoanApplications",
                column: "ApplicantUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLoanApplications_ListingId_ApplicantUserId",
                table: "PlayerLoanApplications",
                columns: new[] { "ListingId", "ApplicantUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLoanApplications_ListingId_Status",
                table: "PlayerLoanApplications",
                columns: new[] { "ListingId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLoanApplications_TargetMatchId",
                table: "PlayerLoanApplications",
                column: "TargetMatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLoanListings_ListingType_Status_PlayDate",
                table: "PlayerLoanListings",
                columns: new[] { "ListingType", "Status", "PlayDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLoanListings_MatchId",
                table: "PlayerLoanListings",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLoanListings_PlayerId",
                table: "PlayerLoanListings",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLoanListings_Region",
                table: "PlayerLoanListings",
                column: "Region");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLoanListings_TeamId",
                table: "PlayerLoanListings",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerLoanListings_UserId",
                table: "PlayerLoanListings",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerLoanApplications");

            migrationBuilder.DropTable(
                name: "PlayerLoanListings");
        }
    }
}
