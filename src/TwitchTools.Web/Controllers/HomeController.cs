using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TwitchTools.Web.Models;
using TwitchTools.Web.Options;

namespace TwitchTools.Web.Controllers;

public class HomeController(IOptions<FeatureFlagsOptions> featureFlags, IOptions<SiteOptions> siteOptions) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    [HttpGet]
    public IActionResult Privacy()
    {
        ViewData["EventSubPayloadRetentionDays"] = featureFlags.Value.EventSubPayloadRetentionDays;
        ViewData["SupportContactEmail"] = siteOptions.Value.SupportContactEmail;
        return View();
    }

    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
