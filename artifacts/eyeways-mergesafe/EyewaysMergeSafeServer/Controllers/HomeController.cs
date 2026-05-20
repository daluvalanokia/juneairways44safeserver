using Microsoft.AspNetCore.Mvc;

namespace EyewaysMergeSafeServer.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => RedirectToAction("Index", "Dashboard");
    public IActionResult Error() => View();
}
