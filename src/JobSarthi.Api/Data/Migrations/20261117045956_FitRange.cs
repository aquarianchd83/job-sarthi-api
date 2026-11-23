using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSarthi.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FitRange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxFitScore",
                table: "Rules",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinFitScore",
                table: "Rules",
                type: "int",
                nullable: false,
                defaultValue: 0);
        
            // Existing rules keep everything: range 0 to 100, not the column default 0 to 0.
            migrationBuilder.Sql("UPDATE Rules SET MinFitScore = 0, MaxFitScore = 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxFitScore",
                table: "Rules");

            migrationBuilder.DropColumn(
                name: "MinFitScore",
                table: "Rules");
        }
    }
}
