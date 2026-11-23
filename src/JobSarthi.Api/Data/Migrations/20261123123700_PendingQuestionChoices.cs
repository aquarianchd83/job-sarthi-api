using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSarthi.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PendingQuestionChoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InputType",
                table: "PendingQuestions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "text");

            migrationBuilder.AddColumn<string>(
                name: "OptionsJson",
                table: "PendingQuestions",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InputType",
                table: "PendingQuestions");

            migrationBuilder.DropColumn(
                name: "OptionsJson",
                table: "PendingQuestions");
        }
    }
}
