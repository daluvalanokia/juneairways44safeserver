using System;
using EyewaysMergeSafeServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace EyewaysMergeSafeServer.Migrations
{
    [DbContext(typeof(AppDbContext))]
    partial class AppDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "8.0.0");

            modelBuilder.Entity("EyewaysMergeSafeServer.Models.Highway", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
                b.Property<DateTime>("CreatedDate").HasColumnType("TEXT");
                b.Property<string>("Description").HasMaxLength(300).HasColumnType("TEXT");
                b.Property<string>("HighwayId").IsRequired().HasMaxLength(50).HasColumnType("TEXT");
                b.Property<bool>("IsActive").HasColumnType("INTEGER");
                b.Property<string>("Name").IsRequired().HasMaxLength(100).HasColumnType("TEXT");
                b.Property<string>("State").HasMaxLength(50).HasColumnType("TEXT");
                b.HasKey("Id");
                b.ToTable("Highways");
            });

            modelBuilder.Entity("EyewaysMergeSafeServer.Models.MergeZone", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
                b.Property<DateTime>("CreatedDate").HasColumnType("TEXT");
                b.Property<int>("GeofenceRadius").HasColumnType("INTEGER");
                b.Property<string>("HighwayId").IsRequired().HasMaxLength(50).HasColumnType("TEXT");
                b.Property<double?>("Latitude").HasColumnType("REAL");
                b.Property<double?>("Longitude").HasColumnType("REAL");
                b.Property<double?>("MileMarker").HasColumnType("REAL");
                b.Property<string>("Status").IsRequired().HasMaxLength(30).HasColumnType("TEXT");
                b.Property<string>("ZoneId").IsRequired().HasMaxLength(50).HasColumnType("TEXT");
                b.Property<string>("ZoneName").IsRequired().HasMaxLength(100).HasColumnType("TEXT");
                b.HasKey("Id");
                b.HasIndex("HighwayId");
                b.ToTable("MergeZones");
            });

            modelBuilder.Entity("EyewaysMergeSafeServer.Models.SwitchServer", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
                b.Property<double>("CpuPercent").HasColumnType("REAL");
                b.Property<DateTime>("CreatedDate").HasColumnType("TEXT");
                b.Property<string>("FirmwareVersion").HasMaxLength(20).HasColumnType("TEXT");
                b.Property<string>("HighwayId").IsRequired().HasMaxLength(50).HasColumnType("TEXT");
                b.Property<string>("IpAddress").HasMaxLength(45).HasColumnType("TEXT");
                b.Property<DateTime>("LastHeartbeat").HasColumnType("TEXT");
                b.Property<double>("MemoryPercent").HasColumnType("REAL");
                b.Property<int>("Port").HasColumnType("INTEGER");
                b.Property<string>("ServerId").IsRequired().HasMaxLength(50).HasColumnType("TEXT");
                b.Property<string>("ServerName").IsRequired().HasMaxLength(100).HasColumnType("TEXT");
                b.Property<string>("Status").IsRequired().HasMaxLength(30).HasColumnType("TEXT");
                b.Property<long>("UptimeSeconds").HasColumnType("INTEGER");
                b.Property<string>("ZoneId").HasMaxLength(50).HasColumnType("TEXT");
                b.HasKey("Id");
                b.HasIndex("HighwayId");
                b.HasIndex("ZoneId");
                b.ToTable("SwitchServers");
            });

            modelBuilder.Entity("EyewaysMergeSafeServer.Models.SensorDevice", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
                b.Property<DateTime>("CreatedDate").HasColumnType("TEXT");
                b.Property<string>("DeviceId").IsRequired().HasMaxLength(50).HasColumnType("TEXT");
                b.Property<string>("DeviceName").IsRequired().HasMaxLength(100).HasColumnType("TEXT");
                b.Property<string>("DeviceType").IsRequired().HasMaxLength(50).HasColumnType("TEXT");
                b.Property<string>("FirmwareVersion").HasMaxLength(20).HasColumnType("TEXT");
                b.Property<string>("HighwayId").IsRequired().HasMaxLength(50).HasColumnType("TEXT");
                b.Property<DateTime>("LastHeartbeat").HasColumnType("TEXT");
                b.Property<double?>("Latitude").HasColumnType("REAL");
                b.Property<double?>("Longitude").HasColumnType("REAL");
                b.Property<double?>("MileMarker").HasColumnType("REAL");
                b.Property<string>("Status").IsRequired().HasMaxLength(30).HasColumnType("TEXT");
                b.Property<string>("ZoneId").HasMaxLength(50).HasColumnType("TEXT");
                b.HasKey("Id");
                b.HasIndex("HighwayId");
                b.HasIndex("ZoneId");
                b.ToTable("SensorDevices");
            });

            modelBuilder.Entity("EyewaysMergeSafeServer.Models.TriangulationConfig", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
                b.Property<DateTime>("CreatedDate").HasColumnType("TEXT");
                b.Property<int>("GeofenceRadius").HasColumnType("INTEGER");
                b.Property<string>("HighwayId").IsRequired().HasMaxLength(50).HasColumnType("TEXT");
                b.Property<bool>("IsActive").HasColumnType("INTEGER");
                b.Property<double?>("Switch1Lat").HasColumnType("REAL");
                b.Property<string>("Switch1Label").HasMaxLength(50).HasColumnType("TEXT");
                b.Property<double?>("Switch1Lon").HasColumnType("REAL");
                b.Property<string>("Switch1ServerId").HasMaxLength(50).HasColumnType("TEXT");
                b.Property<double?>("Switch2Lat").HasColumnType("REAL");
                b.Property<string>("Switch2Label").HasMaxLength(50).HasColumnType("TEXT");
                b.Property<double?>("Switch2Lon").HasColumnType("REAL");
                b.Property<string>("Switch2ServerId").HasMaxLength(50).HasColumnType("TEXT");
                b.Property<double?>("Switch3Lat").HasColumnType("REAL");
                b.Property<string>("Switch3Label").HasMaxLength(50).HasColumnType("TEXT");
                b.Property<double?>("Switch3Lon").HasColumnType("REAL");
                b.Property<string>("Switch3ServerId").HasMaxLength(50).HasColumnType("TEXT");
                b.Property<string>("ZoneId").IsRequired().HasMaxLength(50).HasColumnType("TEXT");
                b.HasKey("Id");
                b.ToTable("TriangulationConfigs");
            });

            modelBuilder.Entity("EyewaysMergeSafeServer.Models.VehicleEvent", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
                b.Property<DateTime>("CreatedDate").HasColumnType("TEXT");
                b.Property<string>("DeviceId").HasMaxLength(50).HasColumnType("TEXT");
                b.Property<string>("EventType").IsRequired().HasMaxLength(30).HasColumnType("TEXT");
                b.Property<string>("HighwayId").IsRequired().HasMaxLength(50).HasColumnType("TEXT");
                b.Property<double?>("Latitude").HasColumnType("REAL");
                b.Property<double?>("Longitude").HasColumnType("REAL");
                b.Property<string>("Payload").HasMaxLength(500).HasColumnType("TEXT");
                b.Property<double?>("SpeedMph").HasColumnType("REAL");
                b.Property<string>("VehicleId").HasMaxLength(50).HasColumnType("TEXT");
                b.Property<string>("ZoneId").HasMaxLength(50).HasColumnType("TEXT");
                b.HasKey("Id");
                b.HasIndex("CreatedDate");
                b.HasIndex("HighwayId");
                b.HasIndex("ZoneId");
                b.ToTable("VehicleEvents");
            });

            modelBuilder.Entity("EyewaysMergeSafeServer.Models.InputFormatConfig", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
                b.Property<DateTime>("CreatedDate").HasColumnType("TEXT");
                b.Property<string>("Description").HasMaxLength(300).HasColumnType("TEXT");
                b.Property<string>("EnabledFieldsRaw").HasMaxLength(1000).HasColumnType("TEXT");
                b.Property<string>("FormatName").IsRequired().HasMaxLength(100).HasColumnType("TEXT");
                b.Property<string>("InputSource").HasMaxLength(300).HasColumnType("TEXT");
                b.Property<string>("SourceId").HasMaxLength(50).HasColumnType("TEXT");
                b.Property<string>("SourceType").IsRequired().HasMaxLength(30).HasColumnType("TEXT");
                b.HasKey("Id");
                b.HasIndex("SourceType");
                b.ToTable("InputFormatConfigs");
            });

            modelBuilder.Entity("EyewaysMergeSafeServer.Models.SamplePayload", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
                b.Property<int?>("ConfigId").HasColumnType("INTEGER");
                b.Property<DateTime>("CreatedDate").HasColumnType("TEXT");
                b.Property<bool>("IsValid").HasColumnType("INTEGER");
                b.Property<string>("Label").HasMaxLength(100).HasColumnType("TEXT");
                b.Property<string>("Payload").HasColumnType("TEXT");
                b.Property<string>("SourceType").IsRequired().HasMaxLength(30).HasColumnType("TEXT");
                b.HasKey("Id");
                b.ToTable("SamplePayloads");
            });

            modelBuilder.Entity("EyewaysMergeSafeServer.Models.UserProfile", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
                b.Property<string>("Address").HasMaxLength(200).HasColumnType("TEXT");
                b.Property<DateTime>("CreatedDate").HasColumnType("TEXT");
                b.Property<string>("DeviceIdsRaw").HasMaxLength(500).HasColumnType("TEXT");
                b.Property<int>("FailedLoginAttempts").HasColumnType("INTEGER");
                b.Property<string>("FullName").IsRequired().HasMaxLength(100).HasColumnType("TEXT");
                b.Property<string>("HighwayId").HasMaxLength(50).HasColumnType("TEXT");
                b.Property<string>("HighwayName").HasMaxLength(100).HasColumnType("TEXT");
                b.Property<bool>("IsActive").HasColumnType("INTEGER");
                b.Property<DateTime?>("LockedUntil").HasColumnType("TEXT");
                b.Property<string>("Notes").HasMaxLength(300).HasColumnType("TEXT");
                b.Property<string>("Password").HasMaxLength(200).HasColumnType("TEXT");
                b.Property<string>("Phone").HasMaxLength(20).HasColumnType("TEXT");
                b.Property<string>("UserId").HasMaxLength(50).HasColumnType("TEXT");
                b.Property<string>("UserType").IsRequired().HasMaxLength(30).HasColumnType("TEXT");
                b.HasKey("Id");
                b.HasIndex("HighwayId");
                b.ToTable("UserProfiles");
            });
#pragma warning restore 612, 618
        }
    }
}
