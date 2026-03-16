using Microsoft.AspNetCore.Mvc;

namespace PolicyManager.Controllers;

public class PolicyHoldersController : Controller
{
    // GET
    public IActionResult Index()
    {
        return View();
    }
}