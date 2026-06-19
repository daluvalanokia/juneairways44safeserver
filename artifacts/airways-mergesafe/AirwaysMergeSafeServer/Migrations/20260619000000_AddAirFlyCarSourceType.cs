using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AirwaysMergeSafeServer.Migrations
{
    /// <summary>
    /// Phase 8: Adds "airflycar" as a valid SourceType in InputFormatConfigs.
    /// Seeds 4 representative AirFlyCar input format configs:
    ///   1. UAM Telemetry Stream    — real-time telemetry from Joby / Archer class craft
    ///   2. Vertiport ADS-B Feed    — ADS-B transponder data from vertiport receivers
    ///   3. UTM Corridor Position   — FAA UTM position reports (4D trajectory)
    ///   4. eVTOL Fleet Tracker     — fleet management + battery/propulsion telemetry
    /// </summary>
    public partial class AddAirFlyCarSourceType : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Insert 4 AirFlyCar input format configs
            migrationBuilder.InsertData(
                table: "InputFormatConfigs",
                columns: new[] { "FormatName","SourceId","SourceType","InputSource","Description","EnabledFieldsRaw","CreatedDate" },
                values: new object[,]
                {
                    {
                        "UAM Telemetry Stream",
                        "AFC-UAM-001",
                        "airflycar",
                        "wss://telemetry.uam-hub.local/stream",
                        "Real-time WebSocket telemetry from Joby S4 / Archer Midnight class eVTOL craft. 10 Hz update rate. Includes 4D position, rotor RPM, battery SOC, flight phase.",
                        "vehicle_id,timestamp,latitude,longitude,altitude_m,speed_mph,heading,vehicle_type,flight_phase,rotor_rpm,battery_soc,battery_temp_c,range_remaining_km,pilot_id,corridor_id,zone_id,highway_id,event_type",
                        DateTime.UtcNow
                    },
                    {
                        "Vertiport ADS-B Feed",
                        "AFC-ADSB-002",
                        "airflycar",
                        "udp://vertiport-receiver-01:30003",
                        "ADS-B transponder messages decoded from vertiport ground stations. Includes ICAO address, squawk code, and NIC/NAC integrity fields.",
                        "vehicle_id,timestamp,icao_address,squawk,latitude,longitude,altitude_m,speed_mph,heading,vertical_rate_fpm,nic,nac_p,vehicle_type,zone_id,highway_id,event_type",
                        DateTime.UtcNow
                    },
                    {
                        "UTM Corridor Position",
                        "AFC-UTM-003",
                        "airflycar",
                        "https://utm-gateway.local/api/v2/positions",
                        "FAA UAS Traffic Management 4D position reports. Includes corridor ID, planned vs actual deviation, and conflict detection flags.",
                        "vehicle_id,timestamp,latitude,longitude,altitude_m,speed_mph,heading,vertical_rate_fpm,corridor_id,corridor_deviation_m,conflict_flag,separation_m,vehicle_type,flight_phase,zone_id,highway_id,event_type",
                        DateTime.UtcNow
                    },
                    {
                        "eVTOL Fleet Tracker",
                        "AFC-FLT-004",
                        "airflycar",
                        "https://fleet.evtol-ops.local/api/telemetry",
                        "Fleet management system position + health telemetry. Covers all craft in the operational area with maintenance flags and propulsion health.",
                        "vehicle_id,timestamp,latitude,longitude,altitude_m,speed_mph,heading,vehicle_type,flight_phase,battery_soc,rotor_health,motor_temp_c,noise_db,passenger_count,destination_pad,zone_id,highway_id,event_type",
                        DateTime.UtcNow
                    }
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "InputFormatConfigs",
                keyColumn: "SourceType",
                keyValue: "airflycar");
        }
    }
}
