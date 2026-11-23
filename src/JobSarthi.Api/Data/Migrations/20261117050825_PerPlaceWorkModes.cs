using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSarthi.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PerPlaceWorkModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LocationPrefs",
                table: "Rules",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "OtherModes",
                table: "Rules",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");
        
            // Existing rules accept every work mode everywhere until the app converts any old location setting (on next start).
            migrationBuilder.Sql("UPDATE Rules SET OtherModes = N'[\"Remote\",\"Hybrid\",\"Onsite\"]'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LocationPrefs",
                table: "Rules");

            migrationBuilder.DropColumn(
                name: "OtherModes",
                table: "Rules");
        }
    }
}
