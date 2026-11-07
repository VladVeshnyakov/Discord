using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordBirge.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchesJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MatchesJson",
                table: "Orders",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MatchesJson",
                table: "Orders");
        }
    }
}
