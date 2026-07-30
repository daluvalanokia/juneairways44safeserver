using AirwaysMergeSafeServer.Data;
using AirwaysMergeSafeServer.Models;
using AirwaysMergeSafeServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AirwaysMergeSafeServer.Controllers;

/// <summary>
/// AirCar Vehicle Registry controller — full CRUD for air vehicles.
/// Completely independent from VehiclesController (ground vehicle registry).
/// Merges static AirCarRegistry seed data with DB-persisted entries.
/// Route: /AirCars
/// </summary>
public class AirCarsController : Controller
{
    private readonly AppDbContext     _db;
    private readonly IAirCarRegistry  _registry;

    public AirCarsController(AppDbContext db, IAirCarRegistry registry)
    { _db = db; _registry = registry; }

    /// <summary>
    /// Index — list all air cars (seed data + DB entries).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        TraceLogger.Enter("AirCars", nameof(Index));

        // Start with seed data from the static registry
        var allEntries = new List<AirCarEntry>();

        // Check if DB has been seeded
        var dbEntries = await _db.AirCarRegistry.AsNoTracking()
            .Where(e => e.IsActive)
            .OrderBy(e => e.Make).ThenBy(e => e.Model)
            .ToListAsync();

        if (dbEntries.Count == 0)
        {
            // Seed the DB from the static registry on first access
            foreach (var spec in _registry.All)
            {
                var entry = new AirCarEntry
                {
                    Type = spec.Type, Make = spec.Make, Model = spec.Model,
                    Size = spec.Size, Icon = spec.Icon,
                    BrandLogo = spec.BrandLogo, SideViewLogo = spec.SideViewLogo,
                    ColorsJson = System.Text.Json.JsonSerializer.Serialize(spec.Colors),
                    LengthM = spec.LengthM, WidthM = spec.WidthM, HeightM = spec.HeightM,
                    MaxAltitudeM = spec.MaxAltitudeM, CruiseSpeedMph = spec.CruiseSpeedMph,
                    IsActive = true, CreatedDate = DateTime.UtcNow
                };
                _db.AirCarRegistry.Add(entry);
                allEntries.Add(entry);
            }
            await _db.SaveChangesAsync();
            TraceLogger.Info("AirCars", nameof(Index), $"Seeded {allEntries.Count} air cars from static registry");
        }
        else
        {
            allEntries = dbEntries;
            TraceLogger.Info("AirCars", nameof(Index), $"Loaded {allEntries.Count} air cars from DB");
        }

        return View(allEntries);
    }

    /// <summary>
    /// Details — show one air car's full specification.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        TraceLogger.Enter("AirCars", nameof(Details), $"id={id}");
        var entry = await _db.AirCarRegistry.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
        if (entry == null) return NotFound();
        return View(entry);
    }

    /// <summary>
    /// Create (GET) — show the create form.
    /// </summary>
    [HttpGet]
    public IActionResult Create()
    {
        TraceLogger.Enter("AirCars", nameof(Create));
        return View(new AirCarEntry { MaxAltitudeM = 3000f, CruiseSpeedMph = 150f, WidthM = 11f, LengthM = 6f, HeightM = 2.5f });
    }

    /// <summary>
    /// Create (POST) — save a new air car to the database.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AirCarEntry entry)
    {
        TraceLogger.Enter("AirCars", nameof(Create), $"make={entry.Make} model={entry.Model}");
        if (!ModelState.IsValid) return View(entry);

        entry.CreatedDate = DateTime.UtcNow;
        entry.UpdatedDate = DateTime.UtcNow;
        entry.IsActive = true;
        _db.AirCarRegistry.Add(entry);
        await _db.SaveChangesAsync();

        TraceLogger.Info("AirCars", nameof(Create), $"Created air car id={entry.Id} {entry.Make} {entry.Model}");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Edit (GET) — show the edit form for an existing air car.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        TraceLogger.Enter("AirCars", nameof(Edit), $"id={id}");
        var entry = await _db.AirCarRegistry.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
        if (entry == null) return NotFound();
        return View(entry);
    }

    /// <summary>
    /// Edit (POST) — save changes to an existing air car.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(AirCarEntry entry)
    {
        TraceLogger.Enter("AirCars", nameof(Edit), $"id={entry.Id}");
        if (!ModelState.IsValid) return View(entry);

        var existing = await _db.AirCarRegistry.FirstOrDefaultAsync(e => e.Id == entry.Id);
        if (existing == null) return NotFound();

        existing.Type = entry.Type;
        existing.Make = entry.Make;
        existing.Model = entry.Model;
        existing.Size = entry.Size;
        existing.Icon = entry.Icon;
        existing.BrandLogo = entry.BrandLogo;
        existing.SideViewLogo = entry.SideViewLogo;
        existing.ColorsJson = entry.ColorsJson;
        existing.LengthM = entry.LengthM;
        existing.WidthM = entry.WidthM;
        existing.HeightM = entry.HeightM;
        existing.MaxAltitudeM = entry.MaxAltitudeM;
        existing.CruiseSpeedMph = entry.CruiseSpeedMph;
        existing.IsActive = entry.IsActive;
        existing.UpdatedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        TraceLogger.Info("AirCars", nameof(Edit), $"Updated air car id={entry.Id}");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Delete (GET) — show delete confirmation.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        TraceLogger.Enter("AirCars", nameof(Delete), $"id={id}");
        var entry = await _db.AirCarRegistry.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
        if (entry == null) return NotFound();
        return View(entry);
    }

    /// <summary>
    /// DeleteConfirmed (POST) — permanently remove an air car from the registry.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        TraceLogger.Enter("AirCars", nameof(DeleteConfirmed), $"id={id}");
        var entry = await _db.AirCarRegistry.FirstOrDefaultAsync(e => e.Id == id);
        if (entry == null) return NotFound();

        _db.AirCarRegistry.Remove(entry);
        await _db.SaveChangesAsync();
        TraceLogger.Info("AirCars", nameof(DeleteConfirmed), $"Deleted air car id={id}");
        return RedirectToAction(nameof(Index));
    }
}
