using Microsoft.AspNetCore.Mvc;

namespace PolicyManager.Controllers;

public class PoliciesController : Controller
{
    // GET
    public IActionResult Index()
    {
        return View();
    }
}