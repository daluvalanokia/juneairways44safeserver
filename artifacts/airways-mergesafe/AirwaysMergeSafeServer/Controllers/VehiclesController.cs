using AirwaysMergeSafeServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace AirwaysMergeSafeServer.Controllers;

/// <summary>
/// D5 FIX: IVehicleRegistry injected via DI instead of direct static access.
///         Controller is now fully testable; no coupling to static state.
/// </summary>
public class VehiclesController : Controller
{
    private readonly IVehicleRegistry _registry;

    public VehiclesController(IVehicleRegistry registry) { _registry = registry; }

    public IActionResult Index() => View(_registry.All);
}
