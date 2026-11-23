using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSarthi.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SkillMatching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MinSkillMatches",
                table: "Rules",
                type: "int",
                nullable: false,
                defaultValue: 0);
        
            // Existing rules get the intended default (2), not the column default (0).
            migrationBuilder.Sql("UPDATE Rules SET MinSkillMatches = 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinSkillMatches",
                table: "Rules");
        }
    }
}
