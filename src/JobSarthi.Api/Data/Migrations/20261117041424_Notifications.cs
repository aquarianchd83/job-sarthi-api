using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSarthi.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Notifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NotifyAlerts",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NotifySummary",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);
        
            // Existing installs get the intended defaults (on), not the column default (off).
            migrationBuilder.Sql("UPDATE Settings SET NotifySummary = 1, NotifyAlerts = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotifyAlerts",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "NotifySummary",
                table: "Settings");
        }
    }
}
