using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TwitchTools.Web.Controllers;
using TwitchTools.Web.Options;

namespace TwitchTools.Web.Tests;

public sealed class TelemetryControllerTests
{
    private const string Body = """{"resourceSpans":[]}""";

    [Fact]
    public async Task Returns_404_when_browser_telemetry_is_not_configured()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var controller = CreateController(handler, endpoint: "");

        var result = await controller.Traces(CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Forwards_the_body_to_the_collector_and_returns_its_status()
    {
        var handler = new RecordingHandler(HttpStatusCode.Accepted);
        var controller = CreateController(handler, endpoint: "http://collector:4318/v1/traces");

        var result = await controller.Traces(CancellationToken.None);

        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(202);
        handler.Requests.Should().ContainSingle();
        var (uri, contentType, body) = handler.Requests[0];
        uri.Should().Be(new Uri("http://collector:4318/v1/traces"));
        contentType.Should().Be("application/json");
        body.Should().Be(Body);
    }

    [Fact]
    public async Task Returns_502_when_the_collector_is_unreachable()
    {
        var handler = new RecordingHandler(new HttpRequestException("connection refused"));
        var controller = CreateController(handler, endpoint: "http://collector:4318/v1/traces");

        var result = await controller.Traces(CancellationToken.None);

        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(502);
    }

    private static TelemetryController CreateController(RecordingHandler handler, string endpoint)
    {
        var controller = new TelemetryController(
            new SingleClientFactory(handler),
            Microsoft.Extensions.Options.Options.Create(new TelemetryOptions { BrowserTracesEndpoint = endpoint }));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(Body));
        httpContext.Request.ContentType = "application/json";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly Exception? _failure;

        public RecordingHandler(HttpStatusCode status) => _status = status;

        public RecordingHandler(Exception failure) => _failure = failure;

        public List<(Uri? Uri, string? ContentType, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_failure is not null)
            {
                throw _failure;
            }

            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri, request.Content?.Headers.ContentType?.MediaType, body));
            return new HttpResponseMessage(_status);
        }
    }
}
