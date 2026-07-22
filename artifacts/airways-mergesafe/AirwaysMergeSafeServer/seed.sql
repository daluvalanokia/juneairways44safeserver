-- ============================================================
-- AirwaysMergeSafeServer — Standalone SQLite Seed Script
-- Run this if dotnet run seeding is not working:
--   sqlite3 mergesafe.db < seed.sql
-- All statements use INSERT OR IGNORE — fully idempotent.
-- ============================================================

PRAGMA journal_mode = WAL;
PRAGMA foreign_keys = OFF;

-- ── Highways ────────────────────────────────────────────────
INSERT OR IGNORE INTO Highways (Name, HighwayId, State, Description, IsActive, CreatedDate) VALUES
  ('Interstate 20 — Texas', 'I20-TX', 'Texas', 'East-West corridor through Dallas/Fort Worth',    1, datetime('now')),
  ('Interstate 35 — Texas', 'I35-TX', 'Texas', 'North-South corridor through Austin/San Antonio', 1, datetime('now')),
  ('Interstate 10 — Texas', 'I10-TX', 'Texas', 'Gulf Coast corridor through Houston to El Paso',  1, datetime('now')),
  ('Interstate 45 — Texas', 'I45-TX', 'Texas', 'Houston to Dallas North-South freeway',           1, datetime('now'));

-- ── MergeZones ──────────────────────────────────────────────
INSERT OR IGNORE INTO MergeZones (ZoneName, ZoneId, HighwayId, MileMarker, Latitude, Longitude, GeofenceRadius, Status, CreatedDate) VALUES
  ('I20 Dallas West Merge',      'I20-Z001', 'I20-TX', 458.2, 32.7767, -96.9870, 600, 'active',      datetime('now')),
  ('I20 Grand Prairie Exchange', 'I20-Z002', 'I20-TX', 444.5, 32.7462, -97.0207, 500, 'active',      datetime('now')),
  ('I20 Arlington Merge',        'I20-Z003', 'I20-TX', 436.1, 32.7357, -97.1081, 450, 'fault',       datetime('now')),
  ('I35 Waco North Merge',       'I35-Z001', 'I35-TX', 330.8, 31.5493, -97.1467, 550, 'active',      datetime('now')),
  ('I35 Temple Bypass Zone',     'I35-Z002', 'I35-TX', 304.2, 31.0982, -97.3428, 500, 'maintenance', datetime('now')),
  ('I35 Georgetown Diverge',     'I35-Z003', 'I35-TX', 261.5, 30.6328, -97.6775, 480, 'active',      datetime('now')),
  ('I10 Houston West Merge',     'I10-Z001', 'I10-TX', 758.1, 29.7604, -95.5144, 600, 'active',      datetime('now')),
  ('I10 Katy Freeway Merge',     'I10-Z002', 'I10-TX', 741.3, 29.7855, -95.7560, 520, 'active',      datetime('now')),
  ('I10 Beaumont Approach',      'I10-Z003', 'I10-TX', 859.2, 30.0860, -94.1018, 470, 'inactive',    datetime('now')),
  ('I45 Houston North Merge',    'I45-Z001', 'I45-TX',  52.5, 29.9511, -95.3677, 550, 'active',      datetime('now')),
  ('I45 Conroe Junction',        'I45-Z002', 'I45-TX',  85.1, 30.3119, -95.4561, 500, 'active',      datetime('now')),
  ('I45 Huntsville Interchange', 'I45-Z003', 'I45-TX', 116.8, 30.7235, -95.5507, 490, 'fault',       datetime('now'));

-- ── SwitchServers (3 per zone, 36 total) ────────────────────
INSERT OR IGNORE INTO SwitchServers (ServerName, ServerId, ZoneId, HighwayId, IpAddress, Port, Status, FirmwareVersion, UptimeSeconds, CpuPercent, MemoryPercent, LastHeartbeat, AltitudeMinMeters, AltitudeMaxMeters, AltitudeWidthMeters, CreatedDate) VALUES
  ('I20-Z001 Switch A','SRV-0001','I20-Z001','I20-TX','10.1.12.45', 8081,'online',   'v3.1.7', 432000, 42.3, 58.1, datetime('now','-5 minutes'),  53.0, 153.0, 29.0, datetime('now')),
  ('I20-Z001 Switch B','SRV-0002','I20-Z001','I20-TX','10.2.8.112', 8082,'online',   'v3.0.14',288000, 61.7, 72.4, datetime('now','-2 minutes'),  53.0, 153.0, 31.0, datetime('now')),
  ('I20-Z001 Switch C','SRV-0003','I20-Z001','I20-TX','10.3.19.77', 8083,'degraded', 'v3.2.3', 720000, 78.9, 45.2, datetime('now','-8 minutes'),  53.0, 153.0, 30.0, datetime('now')),
  ('I20-Z002 Switch A','SRV-0004','I20-Z002','I20-TX','10.1.33.88', 8081,'online',   'v3.1.9', 576000, 35.1, 61.8, datetime('now','-1 minutes'),  47.0, 147.0, 28.0, datetime('now')),
  ('I20-Z002 Switch B','SRV-0005','I20-Z002','I20-TX','10.4.7.201', 8082,'online',   'v3.0.11',864000, 52.4, 38.9, datetime('now','-3 minutes'),  47.0, 147.0, 32.0, datetime('now')),
  ('I20-Z002 Switch C','SRV-0006','I20-Z002','I20-TX','10.2.41.55', 8083,'online',   'v3.3.1', 144000, 29.8, 55.7, datetime('now','-7 minutes'),  47.0, 147.0, 30.0, datetime('now')),
  ('I20-Z003 Switch A','SRV-0007','I20-Z003','I20-TX','10.3.22.190',8081,'fault',    'v3.1.5', 360000, 91.2, 88.3, datetime('now','-14 minutes'), 42.0, 142.0, 27.0, datetime('now')),
  ('I20-Z003 Switch B','SRV-0008','I20-Z003','I20-TX','10.1.15.63', 8082,'online',   'v3.0.8', 720000, 44.6, 67.2, datetime('now','-4 minutes'),  42.0, 142.0, 33.0, datetime('now')),
  ('I20-Z003 Switch C','SRV-0009','I20-Z003','I20-TX','10.4.29.147',8083,'online',   'v3.2.12',432000, 38.3, 49.1, datetime('now','-6 minutes'),  42.0, 142.0, 29.0, datetime('now')),
  ('I35-Z001 Switch A','SRV-0010','I35-Z001','I35-TX','10.2.11.98', 8081,'online',   'v3.1.2', 576000, 55.7, 63.4, datetime('now','-2 minutes'), 195.0, 395.0, 58.0, datetime('now')),
  ('I35-Z001 Switch B','SRV-0011','I35-Z001','I35-TX','10.3.44.22', 8082,'online',   'v3.3.7', 288000, 47.9, 71.8, datetime('now','-9 minutes'), 195.0, 395.0, 61.0, datetime('now')),
  ('I35-Z001 Switch C','SRV-0012','I35-Z001','I35-TX','10.1.6.175', 8083,'degraded', 'v3.0.19',720000, 82.1, 44.6, datetime('now','-11 minutes'),195.0, 395.0, 59.0, datetime('now')),
  ('I35-Z002 Switch A','SRV-0013','I35-Z002','I35-TX','10.4.18.54', 8081,'online',   'v3.2.4', 432000, 31.4, 57.3, datetime('now','-3 minutes'), 208.0, 408.0, 62.0, datetime('now')),
  ('I35-Z002 Switch B','SRV-0014','I35-Z002','I35-TX','10.2.37.119',8082,'offline',  'v3.1.11',  3600, 0.0,  0.0,  datetime('now','-60 minutes'),208.0, 408.0, 60.0, datetime('now')),
  ('I35-Z002 Switch C','SRV-0015','I35-Z002','I35-TX','10.3.9.86',  8083,'online',   'v3.0.6', 864000, 43.2, 68.9, datetime('now','-5 minutes'), 208.0, 408.0, 57.0, datetime('now')),
  ('I35-Z003 Switch A','SRV-0016','I35-Z003','I35-TX','10.1.48.31', 8081,'online',   'v3.3.2', 576000, 59.8, 42.7, datetime('now','-1 minutes'), 202.0, 402.0, 63.0, datetime('now')),
  ('I35-Z003 Switch B','SRV-0017','I35-Z003','I35-TX','10.4.23.168',8082,'online',   'v3.1.8', 288000, 36.5, 55.1, datetime('now','-7 minutes'), 202.0, 402.0, 59.0, datetime('now')),
  ('I35-Z003 Switch C','SRV-0018','I35-Z003','I35-TX','10.2.14.93', 8083,'online',   'v3.2.15',720000, 48.1, 74.3, datetime('now','-4 minutes'), 202.0, 402.0, 61.0, datetime('now')),
  ('I10-Z001 Switch A','SRV-0019','I10-Z001','I10-TX','10.3.31.77', 8081,'online',   'v3.0.9', 432000, 41.7, 60.2, datetime('now','-6 minutes'), 103.0, 253.0, 44.0, datetime('now')),
  ('I10-Z001 Switch B','SRV-0020','I10-Z001','I10-TX','10.1.27.142',8082,'online',   'v3.3.4', 864000, 67.3, 52.8, datetime('now','-2 minutes'), 103.0, 253.0, 46.0, datetime('now')),
  ('I10-Z001 Switch C','SRV-0021','I10-Z001','I10-TX','10.4.5.209', 8083,'degraded', 'v3.1.13',576000, 85.4, 79.6, datetime('now','-12 minutes'),103.0, 253.0, 45.0, datetime('now')),
  ('I10-Z002 Switch A','SRV-0022','I10-Z002','I10-TX','10.2.42.61', 8081,'online',   'v3.2.7', 288000, 33.9, 48.4, datetime('now','-3 minutes'),  97.0, 247.0, 43.0, datetime('now')),
  ('I10-Z002 Switch B','SRV-0023','I10-Z002','I10-TX','10.3.16.184',8082,'online',   'v3.0.17',720000, 54.6, 66.1, datetime('now','-8 minutes'),  97.0, 247.0, 47.0, datetime('now')),
  ('I10-Z002 Switch C','SRV-0024','I10-Z002','I10-TX','10.1.39.28', 8083,'online',   'v3.1.3', 432000, 28.7, 41.9, datetime('now','-5 minutes'),  97.0, 247.0, 44.0, datetime('now')),
  ('I10-Z003 Switch A','SRV-0025','I10-Z003','I10-TX','10.4.11.115',8081,'offline',  'v3.3.9',   3600, 0.0,  0.0,  datetime('now','-45 minutes'),106.0, 256.0, 46.0, datetime('now')),
  ('I10-Z003 Switch B','SRV-0026','I10-Z003','I10-TX','10.2.26.52', 8082,'online',   'v3.2.1', 576000, 46.8, 59.7, datetime('now','-4 minutes'), 106.0, 256.0, 45.0, datetime('now')),
  ('I10-Z003 Switch C','SRV-0027','I10-Z003','I10-TX','10.3.47.197',8083,'online',   'v3.0.5', 864000, 38.2, 53.4, datetime('now','-9 minutes'), 106.0, 256.0, 43.0, datetime('now')),
  ('I45-Z001 Switch A','SRV-0028','I45-Z001','I45-TX','10.1.21.74', 8081,'online',   'v3.1.6', 432000, 50.3, 64.7, datetime('now','-1 minutes'),  78.0, 178.0, 36.0, datetime('now')),
  ('I45-Z001 Switch B','SRV-0029','I45-Z001','I45-TX','10.4.34.131',8082,'online',   'v3.3.11',288000, 42.1, 47.8, datetime('now','-6 minutes'),  78.0, 178.0, 34.0, datetime('now')),
  ('I45-Z001 Switch C','SRV-0030','I45-Z001','I45-TX','10.2.3.89',  8083,'fault',    'v3.2.9', 720000, 95.7, 91.2, datetime('now','-13 minutes'), 78.0, 178.0, 35.0, datetime('now')),
  ('I45-Z002 Switch A','SRV-0031','I45-Z002','I45-TX','10.3.28.166',8081,'online',   'v3.0.12',576000, 37.6, 56.3, datetime('now','-4 minutes'),  72.0, 172.0, 33.0, datetime('now')),
  ('I45-Z002 Switch B','SRV-0032','I45-Z002','I45-TX','10.1.45.43', 8082,'online',   'v3.1.18',864000, 61.4, 70.5, datetime('now','-2 minutes'),  72.0, 172.0, 37.0, datetime('now')),
  ('I45-Z002 Switch C','SRV-0033','I45-Z002','I45-TX','10.4.12.108',8083,'degraded', 'v3.3.6', 432000, 76.8, 82.9, datetime('now','-10 minutes'), 72.0, 172.0, 35.0, datetime('now')),
  ('I45-Z003 Switch A','SRV-0034','I45-Z003','I45-TX','10.2.19.25', 8081,'fault',    'v3.2.2', 360000, 88.3, 86.1, datetime('now','-15 minutes'), 80.0, 180.0, 36.0, datetime('now')),
  ('I45-Z003 Switch B','SRV-0035','I45-Z003','I45-TX','10.3.6.152', 8082,'online',   'v3.0.4', 720000, 44.9, 58.6, datetime('now','-3 minutes'),  80.0, 180.0, 34.0, datetime('now')),
  ('I45-Z003 Switch C','SRV-0036','I45-Z003','I45-TX','10.1.38.71', 8083,'online',   'v3.1.10',576000, 39.7, 51.4, datetime('now','-7 minutes'),  80.0, 180.0, 35.0, datetime('now'));

-- ── UserProfiles (passwords are BCrypt of the plaintext shown) ───────────
-- NOTE: Passwords here are stored as plain text for the seed script.
-- Program.cs HashExistingPasswordsAsync() will BCrypt them on first startup.
INSERT OR IGNORE INTO UserProfiles (UserId, FullName, UserType, Phone, HighwayId, HighwayName, Password, IsActive, FailedLoginAttempts, CreatedDate, Notes) VALUES
  ('admin001','System Administrator','admin',      '214-555-0100','I20-TX','Interstate 20 — Texas','admin',    1, 0, datetime('now'), 'Primary system admin'),
  ('op001',   'Maria Gonzalez',       'operator',  '817-555-0210','I20-TX','Interstate 20 — Texas','password', 1, 0, datetime('now'), 'Day shift operator'),
  ('op002',   'James Thompson',       'operator',  '214-555-0312','I35-TX','Interstate 35 — Texas','password', 1, 0, datetime('now'), 'Night shift operator'),
  ('tech001', 'Carlos Rivera',        'technician','512-555-0401','I35-TX','Interstate 35 — Texas','tech123',  1, 0, datetime('now'), 'Field technician'),
  ('sup001',  'Angela Kim',           'supervisor','713-555-0550','I10-TX','Interstate 10 — Texas','super',    1, 0, datetime('now'), 'Regional supervisor'),
  ('view001', 'Robert Davis',         'viewer',    '832-555-0611','I45-TX','Interstate 45 — Texas','viewer',   1, 0, datetime('now'), 'Read-only viewer'),
  ('op003',   'Sarah Mitchell',       'operator',  '214-555-0712','I20-TX','Interstate 20 — Texas','password', 0, 0, datetime('now'), 'Inactive on leave'),
  ('tech002', 'Wei Zhang',            'technician','713-555-0888','I10-TX','Interstate 10 — Texas','tech123',  1, 0, datetime('now'), 'LiDAR specialist'),
  ('view002', 'Diana Flores',         'viewer',    '512-555-0999','I35-TX','Interstate 35 — Texas','viewer',   1, 0, datetime('now'), 'Observer account'),
  ('admin002','Kevin Okafor',         'admin',     '214-555-1010','I45-TX','Interstate 45 — Texas','admin',    1, 0, datetime('now'), 'Backup administrator'),
  ('op004',   'Patricia Nguyen',      'operator',  '713-555-1111','I10-TX','Interstate 10 — Texas','password', 1, 0, datetime('now'), 'Certified V2X operator');

-- ── InputFormatConfigs ───────────────────────────────────────
INSERT OR IGNORE INTO InputFormatConfigs (FormatName, SourceId, SourceType, InputSource, Description, EnabledFieldsRaw, CreatedDate) VALUES
  ('Standard Loop Detector Feed', 'SRC-PHY-001','physical', 'https://feeds.airways.net/physical/stream/1', 'Standard Loop Detector Feed — standard physical sensor telemetry format',    'vehicle_id,timestamp,speed_mph,latitude',                          datetime('now')),
  ('Piezoelectric Sensor Array',  'SRC-PHY-002','physical', 'https://feeds.airways.net/physical/stream/2', 'Piezoelectric Sensor Array — standard physical sensor telemetry format',   'vehicle_id,timestamp,speed_mph,latitude,longitude',                datetime('now')),
  ('GPS Satellite Feed v2',       'SRC-SAT-003','satellite','https://feeds.airways.net/satellite/stream/3','GPS Satellite Feed v2 — standard satellite sensor telemetry format',        'vehicle_id,timestamp,speed_mph,latitude,longitude,altitude_m',     datetime('now')),
  ('Differential GPS Stream',     'SRC-SAT-004','satellite','https://feeds.airways.net/satellite/stream/4','Differential GPS Stream — standard satellite sensor telemetry format',     'vehicle_id,timestamp,speed_mph,latitude,longitude,altitude_m,direction', datetime('now')),
  ('Cellular V2X Data Feed',      'SRC-TEL-005','telecom',  'https://feeds.airways.net/telecom/stream/5',  'Cellular V2X Data Feed — standard telecom sensor telemetry format',        'vehicle_id,timestamp,speed_mph,latitude,longitude',                datetime('now')),
  ('DSRC 5.9GHz Protocol',        'SRC-TEL-006','telecom',  'https://feeds.airways.net/telecom/stream/6',  'DSRC 5.9GHz Protocol — standard telecom sensor telemetry format',          'vehicle_id,timestamp,speed_mph,altitude_m,direction,lane',         datetime('now')),
  ('RFID Tag Reader Stream',      'SRC-TRA-007','tracker',  'https://feeds.airways.net/tracker/stream/7',  'RFID Tag Reader Stream — standard tracker sensor telemetry format',        'vehicle_id,timestamp,speed_mph,latitude,longitude,altitude_m,lane',datetime('now')),
  ('Bluetooth Proximity Feed',    'SRC-TRA-008','tracker',  'https://feeds.airways.net/tracker/stream/8',  'Bluetooth Proximity Feed — standard tracker sensor telemetry format',      'vehicle_id,timestamp,speed_mph,latitude,longitude,altitude_m',     datetime('now'));

-- ── SamplePayloads ───────────────────────────────────────────
INSERT OR IGNORE INTO SamplePayloads (ConfigId, SourceType, Label, Payload, IsValid, CreatedDate) VALUES
  (1,'physical', 'Loop Detector Sample A', '{"vehicle_id":"VEH-4821","timestamp":"2026-05-20T09:32:11Z","speed_mph":67,"latitude":32.7767,"longitude":-96.9870,"altitude_m":0,"lane":2}',                                                                1, datetime('now')),
  (3,'satellite','GPS Feed Sample A',       '{"vehicle_id":"VEH-7742","timestamp":"2026-05-20T10:11:05Z","speed_mph":72,"latitude":31.5493,"longitude":-97.1467,"altitude_m":118,"direction":180,"vehicle_type":"air_urban"}',                          1, datetime('now')),
  (5,'telecom',  'V2X Cellular Sample',    '{"vehicle_id":"VEH-3391","timestamp":"2026-05-20T11:44:22Z","speed_mph":55,"altitude_m":0,"event_type":"detection","lane":1,"direction":270}',                                                              1, datetime('now')),
  (7,'tracker',  'RFID Tag Sample (Air)',  '{"vehicle_id":"AFC-8812","timestamp":"2026-05-20T14:00:01Z","latitude":29.7604,"longitude":-95.5144,"altitude_m":285,"speed_mph":130,"vehicle_type":"air_express"}',                                        1, datetime('now')),
  (2,'physical', 'Piezo Array Sample',     '{"vehicle_id":"VEH-1155","timestamp":"2026-05-20T08:15:00Z","speed_mph":89,"altitude_m":0,"vehicle_type":"truck","lane":3}',                                                                               0, datetime('now')),
  (4,'satellite','DGPS Stream Sample',     '{"vehicle_id":"VEH-6604","timestamp":"2026-05-20T12:22:44Z","speed_mph":65,"latitude":30.0860,"longitude":-94.1018,"altitude_m":0,"direction":90}',                                                        1, datetime('now'));

-- ── Verify ───────────────────────────────────────────────────
SELECT 'Highways'           || ': ' || COUNT(*) FROM Highways;
SELECT 'MergeZones'         || ': ' || COUNT(*) FROM MergeZones;
SELECT 'SwitchServers'      || ': ' || COUNT(*) FROM SwitchServers;
SELECT 'UserProfiles'       || ': ' || COUNT(*) FROM UserProfiles;
SELECT 'InputFormatConfigs' || ': ' || COUNT(*) FROM InputFormatConfigs;
SELECT 'SamplePayloads'     || ': ' || COUNT(*) FROM SamplePayloads;
