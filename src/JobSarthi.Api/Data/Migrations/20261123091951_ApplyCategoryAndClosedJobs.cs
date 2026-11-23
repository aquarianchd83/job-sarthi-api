using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSarthi.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ApplyCategoryAndClosedJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApplyCategory",
                table: "Jobs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplyEmail",
                table: "Jobs",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplyUrl",
                table: "Jobs",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AtsKey",
                table: "Jobs",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategoryReason",
                table: "Jobs",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClassifiedAtUtc",
                table: "Jobs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosedReason",
                table: "Jobs",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsClosed",
                table: "Jobs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PageCheckedAtUtc",
                table: "Jobs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_IsClosed_ApplyCategory",
                table: "Jobs",
                columns: new[] { "IsClosed", "ApplyCategory" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Jobs_IsClosed_ApplyCategory",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ApplyCategory",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ApplyEmail",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ApplyUrl",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "AtsKey",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "CategoryReason",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ClassifiedAtUtc",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ClosedReason",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "IsClosed",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "PageCheckedAtUtc",
                table: "Jobs");
        }
    }
}
