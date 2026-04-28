using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TwitchTools.Web.Controllers;

[AllowAnonymous]
public sealed class AccountController : Controller
{
    [HttpGet("/account/access-denied")]
    public IActionResult AccessDenied(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }
}
