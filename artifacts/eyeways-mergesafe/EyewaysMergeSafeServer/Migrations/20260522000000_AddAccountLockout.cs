using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EyewaysMergeSafeServer.Migrations
{
    public partial class AddAccountLockout : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedLoginAttempts",
                table: "UserProfiles",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedUntil",
                table: "UserProfiles",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailedLoginAttempts",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                table: "UserProfiles");
        }
    }
}
