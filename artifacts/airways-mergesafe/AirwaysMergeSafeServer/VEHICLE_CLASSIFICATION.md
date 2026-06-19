# Vehicle Classification & 3D Scene Rendering Algorithm
## juneairways44safeserver — Phase 6

---

## 1. Classification Decision Tree

Every incoming data record (simulated or real) is routed through a deterministic
classification pipeline **before** being written to VehicleEvents or rendered in the
3D scene. The output is a `VehicleClass` with a `Domain` (ground / air) and a
`Category` (sedan, suv, truck, motorcycle, van, air_urban, air_express).

```
INPUT RECORD (JSON payload)
        │
        ▼
┌───────────────────────────────────────┐
│  STEP 1: Altitude Classification      │
│                                       │
│  altitude_m present?                  │
│  ├─ NO  → Domain = GROUND             │
│  └─ YES → altitude_m ≤ 10 m          │
│           ├─ YES → Domain = GROUND    │
│           └─ NO  → Domain = AIR       │
│              altitude_m ≤ 150 m       │
│              ├─ YES → air_urban       │
│              └─ NO  → air_express     │
└───────────────────────────────────────┘
        │ Domain = GROUND
        ▼
┌───────────────────────────────────────┐
│  STEP 2: vehicle_type field           │
│  (if present in payload)              │
│                                       │
│  sedan → Category = sedan             │
│  suv   → Category = suv               │
│  truck → Category = truck             │
│  motorcycle → Category = motorcycle   │
│  van   → Category = van               │
│  * (missing / unknown) → STEP 3       │
└───────────────────────────────────────┘
        │ vehicle_type missing
        ▼
┌───────────────────────────────────────┐
│  STEP 3: Speed-based heuristic        │
│  (ground vehicles only)               │
│                                       │
│  speed_mph ≤ 25  → motorcycle         │
│  speed_mph ≤ 55  → sedan              │
│  speed_mph ≤ 75  → suv                │
│  speed_mph ≤ 90  → truck              │
│  speed_mph > 90  → van (anomaly)      │
│  speed = null    → sedan (default)    │
└───────────────────────────────────────┘
        │
        ▼
┌───────────────────────────────────────┐
│  STEP 4: Source-type weight modifier  │
│                                       │
│  physical  → trust vehicle_type 100%  │
│  satellite → trust vehicle_type 90%   │
│              use altitude_ft fallback │
│  telecom   → use speed heuristic 70%  │
│  tracker   → use speed heuristic 80%  │
└───────────────────────────────────────┘
        │
        ▼
   VehicleClass { Domain, Category, ConfidenceScore, Color, Shape3D }
```

---

## 2. 3D Scene Rendering Rules

### Geometry per category

| Category     | Domain | Shape (Three.js)   | Scale (L×W×H units)  | Y-position           |
|-------------|--------|--------------------|----------------------|----------------------|
| sedan        | ground | BoxGeometry        | 1.6 × 0.8 × 0.4      | 0 (ground plane)     |
| suv          | ground | BoxGeometry        | 1.9 × 0.9 × 0.55     | 0                    |
| truck        | ground | BoxGeometry+Cab    | 2.4 × 0.95 × 0.75    | 0                    |
| motorcycle   | ground | CylinderGeometry   | 0.6 × 0.3 × 0.5      | 0                    |
| van          | ground | BoxGeometry        | 2.1 × 1.0 × 1.0      | 0                    |
| air_urban    | air    | ConeGeometry (inv) | 0.5 × 0.5 × 0.3      | altitude_m × ALT_SCALE|
| air_express  | air    | OctahedronGeometry | 0.6 × 0.6 × 0.6      | altitude_m × ALT_SCALE|

### Color coding

| Category    | Hex       | Meaning                          |
|-------------|-----------|----------------------------------|
| sedan       | #3b82f6   | Blue — standard ground           |
| suv         | #22c55e   | Green — medium ground            |
| truck       | #f59e0b   | Amber — heavy ground             |
| motorcycle  | #a855f7   | Purple — light ground            |
| van         | #64748b   | Slate — commercial ground        |
| air_urban   | #00bcd4   | Cyan — low-altitude air corridor |
| air_express | #ff6b35   | Orange-red — high-altitude air   |

### View-specific rendering

**Perspective (Panel A)**
- Full 3D geometry per category above
- Ambient + directional lighting
- Vehicles bob on Y with sin(tick) for alive-feel

**Side View / Elevation (Panel B)**
- Orthographic camera looking along +X
- Renders altitude stacking clearly — ground vehicles at Y=0, air at altitude Y
- Ground vehicles shown as flat rectangles coloured by category
- Air vehicles shown as diamond markers at altitude Y

**Top / Plan View (Panel C)**
- Orthographic camera looking straight down
- All vehicles shown as category-coloured circles sized by vehicle footprint
- Lane separation visible in XZ plane
- Air vehicles rendered with dashed ring to distinguish from ground

---

## 3. Classification Confidence Score

```
confidence = base_score
           + (vehicle_type_present ? 0.30 : 0.0)
           + (altitude_m_present   ? 0.25 : 0.0)
           + (source_weight)          // physical=0.20, satellite=0.18, telecom=0.14, tracker=0.16
           + (speed_present         ? 0.10 : 0.0)
           + (lat_lon_present       ? 0.15 : 0.0)

base_score = 0.0
Max possible = 1.00
```

Records with confidence < 0.40 are flagged as `low_confidence` in the inspector panel.

---

## 4. Data Flow Architecture

```
DataInputFormats (POST SimulationPost)
        │
        ▼
InputPayloadService.Generate(sourceType, fields)
        │  produces JSON with vehicle_type, altitude_m, speed_mph, lat, lon
        ▼
VehicleClassifier.Classify(payload, sourceType)
        │  → VehicleClass { Domain, Category, Color, Shape3D, Confidence }
        ▼
VehicleEvent (DB write)
        │  VehicleClass fields stored in VehicleClass column (JSON)
        ▼
GET /api/events/live  →  Feed JSON includes vehicleClass
        │
        ▼
3D Scene JS (AirScene.cshtml / Traffic3D/Index.cshtml)
        │  reads vehicleClass.domain → chooses Y-position (ground vs air)
        │  reads vehicleClass.category → chooses geometry + color
        │  reads vehicleClass.confidence → shows inspector badge
        ▼
Panel A: Perspective   Panel B: Side/Elevation   Panel C: Top/Plan
```
