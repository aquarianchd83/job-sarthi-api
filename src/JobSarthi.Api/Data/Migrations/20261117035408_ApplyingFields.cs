using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSarthi.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ApplyingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ScreenshotPath",
                table: "Applications",
                newName: "ScreenshotFile");

            migrationBuilder.AlterColumn<string>(
                name: "Question",
                table: "SavedAnswers",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Prompt",
                table: "PendingQuestions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Kind",
                table: "PendingQuestions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "PendingQuestions",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "Resolved",
                table: "PendingQuestions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "Applications",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_SavedAnswers_Question",
                table: "SavedAnswers",
                column: "Question",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingQuestions_ApplicationId",
                table: "PendingQuestions",
                column: "ApplicationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SavedAnswers_Question",
                table: "SavedAnswers");

            migrationBuilder.DropIndex(
                name: "IX_PendingQuestions_ApplicationId",
                table: "PendingQuestions");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "PendingQuestions");

            migrationBuilder.DropColumn(
                name: "Resolved",
                table: "PendingQuestions");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "Applications");

            migrationBuilder.RenameColumn(
                name: "ScreenshotFile",
                table: "Applications",
                newName: "ScreenshotPath");

            migrationBuilder.AlterColumn<string>(
                name: "Question",
                table: "SavedAnswers",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(300)",
                oldMaxLength: 300);

            migrationBuilder.AlterColumn<string>(
                name: "Prompt",
                table: "PendingQuestions",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "Kind",
                table: "PendingQuestions",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);
        }
    }
}
