using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AloBall.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayStylesAndTeamVibes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlayStyles",
                table: "Players",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlayStyles",
                table: "AspNetUsers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlayStyleVibeTags",
                table: "TeamMatchReviews",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlayStyles",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "PlayStyles",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PlayStyleVibeTags",
                table: "TeamMatchReviews");
        }
    }
}
