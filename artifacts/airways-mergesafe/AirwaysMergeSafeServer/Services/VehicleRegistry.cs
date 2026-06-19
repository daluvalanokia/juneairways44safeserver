using AirwaysMergeSafeServer.Models;

namespace AirwaysMergeSafeServer.Services;

public sealed record VehicleSpec(
    string   Type,
    string   Make,
    string   Model,
    string   Size,
    string   Icon,
    string[] Colors,
    float    LengthM,
    float    WidthM,
    float    HeightM
);

/// <summary>
/// D5 FIX: VehicleRegistry now implements IVehicleRegistry so it can be
///         registered as a singleton in DI and injected into VehiclesController,
///         decoupling the controller from static state and enabling testing.
/// </summary>
public interface IVehicleRegistry
{
    IReadOnlyList<VehicleSpec> All { get; }
}

public class VehicleRegistry : IVehicleRegistry
{
    // ── Singleton data — all known vehicle specs ───────────────────────────
    public IReadOnlyList<VehicleSpec> All { get; } = new[]
    {
        // ── Sedans ──────────────────────────────────────────────────────────
        new VehicleSpec("sedan","Toyota","Camry","medium","🚗",
            new[]{"#c0392b","#2c3e50","#bdc3c7","#e8d5b7","#16a085"},4.2f,1.80f,0.85f),
        new VehicleSpec("sedan","Honda","Civic","small","🚗",
            new[]{"#3498db","#e74c3c","#2ecc71","#ecf0f1","#9b59b6"},3.9f,1.70f,0.78f),
        new VehicleSpec("sedan","Ford","Fusion","medium","🚗",
            new[]{"#2980b9","#7f8c8d","#c0392b","#f39c12","#1a1a2e"},4.3f,1.85f,0.87f),
        new VehicleSpec("sedan","Chevrolet","Malibu","medium","🚗",
            new[]{"#d35400","#8e44ad","#16a085","#bdc3c7","#2c2c54"},4.2f,1.82f,0.86f),
        new VehicleSpec("sedan","BMW","3 Series","medium","🚗",
            new[]{"#2c2c2c","#f5f5f5","#a0522d","#4169e1","#708090"},4.1f,1.80f,0.82f),
        new VehicleSpec("sedan","Mercedes","C-Class","medium","🚗",
            new[]{"#1a1a2e","#c0c0c0","#000080","#8b0000","#f5f5f5"},4.2f,1.82f,0.83f),
        // ── SUVs ────────────────────────────────────────────────────────────
        new VehicleSpec("suv","Ford","Explorer","large","🚙",
            new[]{"#1a1a2e","#4682b4","#8b4513","#696969","#006400"},4.9f,2.00f,1.25f),
        new VehicleSpec("suv","Chevrolet","Tahoe","large","🚙",
            new[]{"#1c1c1c","#f5f5dc","#556b2f","#8b0000","#4169e1"},5.1f,2.05f,1.35f),
        new VehicleSpec("suv","Toyota","RAV4","medium","🚙",
            new[]{"#cc0000","#1a1a2e","#808080","#f0f0f0","#2e8b57"},4.4f,1.86f,1.22f),
        new VehicleSpec("suv","Honda","CR-V","medium","🚙",
            new[]{"#b22222","#708090","#2f4f4f","#ffd700","#4682b4"},4.3f,1.84f,1.20f),
        new VehicleSpec("suv","Jeep","Wrangler","medium","🚙",
            new[]{"#ff4500","#2f4f4f","#f5f5f5","#ffd700","#1a1a2e"},4.0f,1.88f,1.40f),
        new VehicleSpec("suv","Tesla","Model X","large","🚙",
            new[]{"#f5f5f5","#cc0000","#1a1a2e","#808080","#000000"},5.0f,2.00f,1.28f),
        // ── Trucks ──────────────────────────────────────────────────────────
        new VehicleSpec("truck","Ford","F-150","large","🛻",
            new[]{"#1a1a2e","#cc0000","#696969","#f5f5dc","#2e8b57"},5.9f,2.04f,1.90f),
        new VehicleSpec("truck","Chevrolet","Silverado","large","🛻",
            new[]{"#c0392b","#1a1a2e","#808080","#f5f5dc","#4682b4"},5.9f,2.03f,1.88f),
        new VehicleSpec("truck","Ram","1500","large","🛻",
            new[]{"#1a1a2e","#cc0000","#808080","#556b2f","#4682b4"},5.8f,2.02f,1.87f),
        // ── Motorcycles ─────────────────────────────────────────────────────
        new VehicleSpec("motorcycle","Harley-Davidson","Sportster","small","🏍",
            new[]{"#1a1a2e","#cc0000","#808080","#f5f5dc","#ff8c00"},2.4f,0.90f,1.10f),
        new VehicleSpec("motorcycle","Honda","CBR600","small","🏍",
            new[]{"#cc0000","#1a1a2e","#0000cd","#f5f5f5","#ff8c00"},2.1f,0.72f,1.05f),
        // ── Vans ────────────────────────────────────────────────────────────
        new VehicleSpec("van","Ford","Transit","large","🚐",
            new[]{"#f5f5f5","#1a1a2e","#808080","#ffd700","#cc0000"},5.5f,2.47f,2.77f),
        new VehicleSpec("van","Mercedes","Sprinter","large","🚐",
            new[]{"#f5f5f5","#1a1a2e","#808080","#4682b4","#2e8b57"},5.9f,1.99f,2.59f),
        // ── Air Vehicles (Phase 4) ───────────────────────────────────────────
        new VehicleSpec("air","Joby","S4","small","✈",
            new[]{"#00bcd4","#1a1a2e","#f5f5f5","#3b82f6","#22c55e"},6.4f,10.70f,1.50f),
        new VehicleSpec("air","Archer","Midnight","small","✈",
            new[]{"#1e90ff","#f5f5f5","#1a1a2e","#22c55e","#f59e0b"},6.0f, 9.14f,1.45f),
        new VehicleSpec("air","Wisk","Cora","small","✈",
            new[]{"#22c55e","#f5f5f5","#1a1a2e","#00bcd4","#3b82f6"},6.1f, 8.50f,1.60f),
    };
}
