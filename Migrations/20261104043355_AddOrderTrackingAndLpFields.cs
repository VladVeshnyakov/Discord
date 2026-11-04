using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordBirge.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderTrackingAndLpFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BasePricePerGame",
                table: "Orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "BoosterGamesPlayed",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BoosterMmrChange",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CurrentMmr",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "DotaAccountId",
                table: "Orders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "IsShiftActive",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "LastProcessedMatchId",
                table: "Orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerGamesPlayed",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OwnerMmrChange",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ShiftStartedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StartMmr",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TargetGames",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TargetMmr",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TargetMmrDelta",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BasePricePerGame",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BoosterGamesPlayed",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BoosterMmrChange",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CurrentMmr",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DotaAccountId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsShiftActive",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "LastProcessedMatchId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "OwnerGamesPlayed",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "OwnerMmrChange",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShiftStartedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "StartMmr",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TargetGames",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TargetMmr",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TargetMmrDelta",
                table: "Orders");
        }
    }
}
