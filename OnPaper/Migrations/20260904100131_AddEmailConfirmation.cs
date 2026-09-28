using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnPaper.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "email_confirmation_token",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "email_confirmation_token_expires_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "email_confirmed",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "email_confirmation_token",
                table: "users");

            migrationBuilder.DropColumn(
                name: "email_confirmation_token_expires_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "email_confirmed",
                table: "users");
        }
    }
}
