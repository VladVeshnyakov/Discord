using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordBirge.Migrations
{
    /// <inheritdoc />
    public partial class addneworderfield : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BoosterLose",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BoosterWin",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ClientLose",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ClientWin",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CurrentGames",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoosterLose",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BoosterWin",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ClientLose",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ClientWin",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CurrentGames",
                table: "Orders");
        }
    }
}
