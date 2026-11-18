using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AloBall.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddUnclaimedRosterAndZaloRsvp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsClaimed",
                table: "Players",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "InviteToken",
                table: "Players",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KitColor",
                table: "Matches",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RsvpToken",
                table: "Matches",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsClaimed",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "InviteToken",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "KitColor",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "RsvpToken",
                table: "Matches");
        }
    }
}
