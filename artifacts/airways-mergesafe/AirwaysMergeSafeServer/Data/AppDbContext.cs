using AirwaysMergeSafeServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AirwaysMergeSafeServer.Data;

/// <summary>
/// Phase 5 / E2: Added AuditLogs DbSet.
/// All existing indexes, converters and configuration from Phase 3 preserved.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Highway>             Highways             => Set<Highway>();
    public DbSet<MergeZone>           MergeZones           => Set<MergeZone>();
    public DbSet<SwitchServer>        SwitchServers        => Set<SwitchServer>();
    public DbSet<SensorDevice>        SensorDevices        => Set<SensorDevice>();
    public DbSet<TriangulationConfig> TriangulationConfigs => Set<TriangulationConfig>();
    public DbSet<VehicleEvent>        VehicleEvents        => Set<VehicleEvent>();
    public DbSet<InputFormatConfig>   InputFormatConfigs   => Set<InputFormatConfig>();
    public DbSet<SamplePayload>       SamplePayloads       => Set<SamplePayload>();
    public DbSet<UserProfile>         UserProfiles         => Set<UserProfile>();

    // E2: Audit log — append-only, never mutated after insert
    public DbSet<AuditLog>            AuditLogs            => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── D1: String converters — enum ↔ lowercase string ───────────────
        modelBuilder.Entity<MergeZone>()
            .Property(z => z.Status)
            .HasConversion(v => v.ToLower(), v => v);

        modelBuilder.Entity<SwitchServer>()
            .Property(s => s.Status)
            .HasConversion(v => v.ToLower(), v => v);

        modelBuilder.Entity<SensorDevice>()
            .Property(d => d.Status)
            .HasConversion(v => v.ToLower(), v => v);

        modelBuilder.Entity<VehicleEvent>()
            .Property(e => e.EventType)
            .HasConversion(v => v.ToLower(), v => v);

        modelBuilder.Entity<InputFormatConfig>()
            .Property(c => c.SourceType)
            .HasConversion(v => v.ToLower(), v => v);

        modelBuilder.Entity<UserProfile>()
            .Property(u => u.UserType)
            .HasConversion(v => v.ToLower(), v => v);

        // ── Single-column indexes ──────────────────────────────────────────
        modelBuilder.Entity<MergeZone>()
            .HasIndex(z => z.HighwayId).HasDatabaseName("IX_MergeZones_HighwayId");
        modelBuilder.Entity<SwitchServer>()
            .HasIndex(s => s.HighwayId).HasDatabaseName("IX_SwitchServers_HighwayId");
        modelBuilder.Entity<SwitchServer>()
            .HasIndex(s => s.ZoneId).HasDatabaseName("IX_SwitchServers_ZoneId");
        modelBuilder.Entity<SensorDevice>()
            .HasIndex(d => d.HighwayId).HasDatabaseName("IX_SensorDevices_HighwayId");
        modelBuilder.Entity<SensorDevice>()
            .HasIndex(d => d.ZoneId).HasDatabaseName("IX_SensorDevices_ZoneId");
        modelBuilder.Entity<VehicleEvent>()
            .HasIndex(e => e.HighwayId).HasDatabaseName("IX_VehicleEvents_HighwayId");
        modelBuilder.Entity<VehicleEvent>()
            .HasIndex(e => e.ZoneId).HasDatabaseName("IX_VehicleEvents_ZoneId");
        modelBuilder.Entity<VehicleEvent>()
            .HasIndex(e => e.CreatedDate).HasDatabaseName("IX_VehicleEvents_CreatedDate");
        modelBuilder.Entity<InputFormatConfig>()
            .HasIndex(c => c.SourceType).HasDatabaseName("IX_InputFormatConfigs_SourceType");
        modelBuilder.Entity<UserProfile>()
            .HasIndex(u => u.HighwayId).HasDatabaseName("IX_UserProfiles_HighwayId");

        // ── Composite indexes ──────────────────────────────────────────────
        modelBuilder.Entity<SwitchServer>()
            .HasIndex(s => new { s.HighwayId, s.ZoneId })
            .HasDatabaseName("IX_SwitchServers_HighwayId_ZoneId");
        modelBuilder.Entity<SensorDevice>()
            .HasIndex(d => new { d.HighwayId, d.ZoneId })
            .HasDatabaseName("IX_SensorDevices_HighwayId_ZoneId");
        modelBuilder.Entity<VehicleEvent>()
            .HasIndex(e => new { e.HighwayId, e.ZoneId })
            .HasDatabaseName("IX_VehicleEvents_HighwayId_ZoneId");

        // ── E2: AuditLog indexes ───────────────────────────────────────────
        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => a.UserId).HasDatabaseName("IX_AuditLogs_UserId");
        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => a.CreatedDate).HasDatabaseName("IX_AuditLogs_CreatedDate");
        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => new { a.HighwayId, a.CreatedDate })
            .HasDatabaseName("IX_AuditLogs_HighwayId_CreatedDate");
    }
}
