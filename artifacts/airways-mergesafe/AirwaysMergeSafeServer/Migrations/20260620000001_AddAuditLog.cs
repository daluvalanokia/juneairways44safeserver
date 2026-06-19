using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AirwaysMergeSafeServer.Migrations
{
    /// <summary>
    /// Phase 5 / E2: Creates the AuditLogs table.
    /// Append-only; never updated. Indexed by UserId, CreatedDate,
    /// and (HighwayId + CreatedDate) for dashboard audit queries.
    /// </summary>
    public partial class AddAuditLog : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id          = table.Column<long>(type: "INTEGER", nullable: false)
                                       .Annotation("Sqlite:Autoincrement", true),
                    UserId      = table.Column<string>(type: "TEXT", maxLength: 50,  nullable: false, defaultValue: ""),
                    FullName    = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, defaultValue: ""),
                    HighwayId   = table.Column<string>(type: "TEXT", maxLength: 50,  nullable: true),
                    Controller  = table.Column<string>(type: "TEXT", maxLength: 60,  nullable: false, defaultValue: ""),
                    Action      = table.Column<string>(type: "TEXT", maxLength: 30,  nullable: false, defaultValue: ""),
                    EntityType  = table.Column<string>(type: "TEXT", maxLength: 60,  nullable: true),
                    EntityId    = table.Column<string>(type: "TEXT", maxLength: 80,  nullable: true),
                    Summary     = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IpAddress   = table.Column<string>(type: "TEXT", maxLength: 45,  nullable: true),
                    CreatedDate = table.Column<DateTime>(nullable: false,
                                      defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table => table.PrimaryKey("PK_AuditLogs", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CreatedDate",
                table: "AuditLogs",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_HighwayId_CreatedDate",
                table: "AuditLogs",
                columns: new[] { "HighwayId", "CreatedDate" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AuditLogs");
        }
    }
}
