using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AirwaysMergeSafeServer.Migrations
{
    /// <summary>
    /// Phase 4: Adds 7 altitude columns.
    ///   SwitchServers  → AltitudeMinMeters, AltitudeMaxMeters, AltitudeWidthMeters
    ///   VehicleEvents  → AltitudeMeters
    ///   SensorDevices  → AltitudeMeters
    ///   MergeZones     → AltitudeMeters
    /// C5 FIX: All schema changes now flow through proper EF migrations (not startup DDL).
    /// </summary>
    public partial class AddAltitudeFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── SwitchServers ──────────────────────────────────────────────
            migrationBuilder.AddColumn<double>(
                name: "AltitudeMinMeters",
                table: "SwitchServers",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "AltitudeMaxMeters",
                table: "SwitchServers",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "AltitudeWidthMeters",
                table: "SwitchServers",
                type: "REAL",
                nullable: true);

            // ── VehicleEvents ──────────────────────────────────────────────
            migrationBuilder.AddColumn<double>(
                name: "AltitudeMeters",
                table: "VehicleEvents",
                type: "REAL",
                nullable: true,
                defaultValue: 0.0);

            // ── SensorDevices ──────────────────────────────────────────────
            migrationBuilder.AddColumn<double>(
                name: "AltitudeMeters",
                table: "SensorDevices",
                type: "REAL",
                nullable: true,
                defaultValue: 0.0);

            // ── MergeZones ─────────────────────────────────────────────────
            migrationBuilder.AddColumn<double>(
                name: "AltitudeMeters",
                table: "MergeZones",
                type: "REAL",
                nullable: true,
                defaultValue: 0.0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AltitudeMinMeters",   table: "SwitchServers");
            migrationBuilder.DropColumn(name: "AltitudeMaxMeters",   table: "SwitchServers");
            migrationBuilder.DropColumn(name: "AltitudeWidthMeters", table: "SwitchServers");
            migrationBuilder.DropColumn(name: "AltitudeMeters",      table: "VehicleEvents");
            migrationBuilder.DropColumn(name: "AltitudeMeters",      table: "SensorDevices");
            migrationBuilder.DropColumn(name: "AltitudeMeters",      table: "MergeZones");
        }
    }
}
