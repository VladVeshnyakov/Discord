using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordBirge.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerPersonaName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TargetGames",
                table: "Services",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "NameAccount",
                table: "Orders",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetGames",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "NameAccount",
                table: "Orders");
        }
    }
}
