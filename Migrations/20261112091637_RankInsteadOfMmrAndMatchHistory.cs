using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordBirge.Migrations
{
    /// <inheritdoc />
    public partial class RankInsteadOfMmrAndMatchHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentRankTier",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StartRankTier",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TargetRankTier",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentRankTier",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "StartRankTier",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TargetRankTier",
                table: "Orders");
        }
    }
}
