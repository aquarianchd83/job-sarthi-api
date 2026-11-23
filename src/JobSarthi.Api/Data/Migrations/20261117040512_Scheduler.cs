using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSarthi.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Scheduler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Applied",
                table: "DailyRuns",
                newName: "Queued");

            migrationBuilder.AddColumn<int>(
                name: "AutoApplyMinScore",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "SchedulerEnabled",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "Error",
                table: "DailyRuns",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NewJobs",
                table: "DailyRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "DailyRuns",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Trigger",
                table: "DailyRuns",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Alerts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Acknowledged = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alerts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyRuns_StartedAtUtc",
                table: "DailyRuns",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_Acknowledged_CreatedAtUtc",
                table: "Alerts",
                columns: new[] { "Acknowledged", "CreatedAtUtc" });
        
            // Existing installs get the intended defaults, not the column defaults (off / 0).
            migrationBuilder.Sql("UPDATE Settings SET SchedulerEnabled = 1, AutoApplyMinScore = 60");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Alerts");

            migrationBuilder.DropIndex(
                name: "IX_DailyRuns_StartedAtUtc",
                table: "DailyRuns");

            migrationBuilder.DropColumn(
                name: "AutoApplyMinScore",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SchedulerEnabled",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "NewJobs",
                table: "DailyRuns");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "DailyRuns");

            migrationBuilder.DropColumn(
                name: "Trigger",
                table: "DailyRuns");

            migrationBuilder.RenameColumn(
                name: "Queued",
                table: "DailyRuns",
                newName: "Applied");

            migrationBuilder.AlterColumn<string>(
                name: "Error",
                table: "DailyRuns",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true);
        }
    }
}
