using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AloBall.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddClubBankAndSplitBillVietQr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountHolderName",
                table: "Teams",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountNumber",
                table: "Teams",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankCode",
                table: "Teams",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MatchExpenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    PitchFee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    WaterFee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    RefereeFee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    SplitRatio = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Tang2Fee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    MatchTotalPayable = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    MatchPerPersonShare = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Tang2PerPersonShare = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchExpenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchExpenses_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchExpenses_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchExpenseShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchExpenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedUserId = table.Column<string>(type: "text", nullable: true),
                    ParticipatesInMatch = table.Column<bool>(type: "boolean", nullable: false),
                    ParticipatesInTang2 = table.Column<bool>(type: "boolean", nullable: false),
                    MatchAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Tang2Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PaymentStatus = table.Column<int>(type: "integer", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MarkedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchExpenseShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchExpenseShares_MatchExpenses_MatchExpenseId",
                        column: x => x.MatchExpenseId,
                        principalTable: "MatchExpenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchExpenseShares_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchExpenses_MatchId_TeamId",
                table: "MatchExpenses",
                columns: new[] { "MatchId", "TeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchExpenses_TeamId",
                table: "MatchExpenses",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchExpenseShares_MatchExpenseId_PlayerId",
                table: "MatchExpenseShares",
                columns: new[] { "MatchExpenseId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchExpenseShares_PlayerId",
                table: "MatchExpenseShares",
                column: "PlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchExpenseShares");

            migrationBuilder.DropTable(
                name: "MatchExpenses");

            migrationBuilder.DropColumn(
                name: "AccountHolderName",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "AccountNumber",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "BankCode",
                table: "Teams");
        }
    }
}
