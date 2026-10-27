using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TwitchTools.Web.Options;

namespace TwitchTools.Web.Controllers;

/// <summary>
/// Forwards browser spans (wwwroot/js/browser-telemetry.js) to the OTel collector. Same-origin so
/// the page needs no CORS, and signed-in only so anonymous visitors cannot post. Locked to OTLP/JSON
/// traces so it cannot be used to inject metrics or logs.
/// </summary>
[Authorize]
public sealed class TelemetryController(IHttpClientFactory httpClientFactory, IOptions<TelemetryOptions> options) : Controller
{
    public const string HttpClientName = "browser-telemetry";

    [HttpPost("/otlp/v1/traces")]
    [Consumes("application/json")]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Traces(CancellationToken cancellationToken)
    {
        var endpoint = options.Value.BrowserTracesEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return NotFound();
        }

        using var content = new StreamContent(Request.Body);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        try
        {
            using var response = await httpClientFactory.CreateClient(HttpClientName).PostAsync(new Uri(endpoint), content, cancellationToken);
            return StatusCode((int)response.StatusCode);
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway);
        }
    }
}
