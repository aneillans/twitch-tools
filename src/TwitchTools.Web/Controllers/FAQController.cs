using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TwitchTools.Web.Models;

namespace TwitchTools.Web.Controllers;

public class FAQController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }
}
