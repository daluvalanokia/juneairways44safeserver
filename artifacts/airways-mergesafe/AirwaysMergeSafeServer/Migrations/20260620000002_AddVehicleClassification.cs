using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AirwaysMergeSafeServer.Migrations
{
    /// <summary>
    /// Phase 6: Adds VehicleMode, VehicleCategory, VehicleClassJson to VehicleEvents.
    /// All nullable/default-safe — existing rows get default "ground" / "sedan".
    /// </summary>
    public partial class AddVehicleClassification : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VehicleMode",
                table: "VehicleEvents",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "ground");

            migrationBuilder.AddColumn<string>(
                name: "VehicleCategory",
                table: "VehicleEvents",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "sedan");

            migrationBuilder.AddColumn<string>(
                name: "VehicleClassJson",
                table: "VehicleEvents",
                type: "TEXT",
                maxLength: 800,
                nullable: true);

            // Index for fast ground/air split queries
            migrationBuilder.CreateIndex(
                name: "IX_VehicleEvents_VehicleMode",
                table: "VehicleEvents",
                column: "VehicleMode");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleEvents_VehicleCategory",
                table: "VehicleEvents",
                column: "VehicleCategory");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex("IX_VehicleEvents_VehicleMode",     "VehicleEvents");
            migrationBuilder.DropIndex("IX_VehicleEvents_VehicleCategory", "VehicleEvents");
            migrationBuilder.DropColumn("VehicleMode",     "VehicleEvents");
            migrationBuilder.DropColumn("VehicleCategory", "VehicleEvents");
            migrationBuilder.DropColumn("VehicleClassJson","VehicleEvents");
        }
    }
}
