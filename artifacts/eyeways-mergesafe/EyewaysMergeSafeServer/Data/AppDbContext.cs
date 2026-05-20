using EyewaysMergeSafeServer.Models;
using Microsoft.EntityFrameworkCore;

namespace EyewaysMergeSafeServer.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Highway> Highways => Set<Highway>();
    public DbSet<MergeZone> MergeZones => Set<MergeZone>();
    public DbSet<SwitchServer> SwitchServers => Set<SwitchServer>();
    public DbSet<SensorDevice> SensorDevices => Set<SensorDevice>();
    public DbSet<TriangulationConfig> TriangulationConfigs => Set<TriangulationConfig>();
    public DbSet<VehicleEvent> VehicleEvents => Set<VehicleEvent>();
    public DbSet<InputFormatConfig> InputFormatConfigs => Set<InputFormatConfig>();
    public DbSet<SamplePayload> SamplePayloads => Set<SamplePayload>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MergeZone>()
            .HasIndex(z => z.HighwayId);

        modelBuilder.Entity<SwitchServer>()
            .HasIndex(s => s.HighwayId);
        modelBuilder.Entity<SwitchServer>()
            .HasIndex(s => s.ZoneId);

        modelBuilder.Entity<SensorDevice>()
            .HasIndex(d => d.HighwayId);
        modelBuilder.Entity<SensorDevice>()
            .HasIndex(d => d.ZoneId);

        modelBuilder.Entity<VehicleEvent>()
            .HasIndex(e => e.HighwayId);
        modelBuilder.Entity<VehicleEvent>()
            .HasIndex(e => e.ZoneId);
        modelBuilder.Entity<VehicleEvent>()
            .HasIndex(e => e.CreatedDate);

        modelBuilder.Entity<InputFormatConfig>()
            .HasIndex(c => c.SourceType);

        modelBuilder.Entity<UserProfile>()
            .HasIndex(u => u.HighwayId);
    }
}
