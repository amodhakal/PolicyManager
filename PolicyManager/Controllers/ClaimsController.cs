using Microsoft.AspNetCore.Mvc;

namespace PolicyManager.Controllers;

public class ClaimsController : Controller
{
    // GET
    public IActionResult Index()
    {
        return View();
    }
}