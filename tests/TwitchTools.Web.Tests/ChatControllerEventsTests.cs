using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TwitchTools.Web.Controllers;
using TwitchTools.Web.Data;
using TwitchTools.Web.Domain;
using TwitchTools.Web.Services;
using TwitchTools.Web.Services.Security;

namespace TwitchTools.Web.Tests;

public sealed class ChatControllerEventsTests : IDisposable
{
    private const string Owner = "owner-subject";
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public ChatControllerEventsTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Writes_each_event_with_its_sequence_as_the_sse_id()
    {
        var broker = new ScriptedBroker(new BrokerEvent(7, """{"text":"hi"}"""), new BrokerEvent(9, """{"text":"again"}"""));
        await using var db = await CreateDbAsync(withStreamer: true);
        var (controller, body) = CreateController(db, broker);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var stream = controller.Events(cts.Token);
        await broker.AllYielded.WaitAsync(cts.Token);
        await cts.CancelAsync();
        await stream;

        Encoding.UTF8.GetString(body.ToArray()).Should().Be(
            "id: 7\ndata: {\"text\":\"hi\"}\n\nid: 9\ndata: {\"text\":\"again\"}\n\n");
    }

    [Fact]
    public async Task Returns_404_without_subscribing_when_the_user_has_no_streamer()
    {
        var broker = new ScriptedBroker();
        await using var db = await CreateDbAsync(withStreamer: false);
        var (controller, _) = CreateController(db, broker);

        await controller.Events(CancellationToken.None);

        controller.Response.StatusCode.Should().Be(404);
        broker.Subscribed.Should().BeFalse();
    }

    private async Task<AppDbContext> CreateDbAsync(bool withStreamer)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        var db = new AppDbContext(options, new PlainTextEncryption());
        await db.Database.EnsureCreatedAsync();
        if (withStreamer)
        {
            db.Streamers.Add(new Streamer { Id = Guid.NewGuid(), OwnerSubject = Owner });
            await db.SaveChangesAsync();
        }

        return db;
    }

    private static (ChatController Controller, MemoryStream Body) CreateController(AppDbContext db, IOverlayEventBroker broker)
    {
        var controller = new ChatController(db, broker, null!, NullLogger<ChatController>.Instance);
        var body = new MemoryStream();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Owner)], "test"))
        };
        httpContext.Response.Body = body;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return (controller, body);
    }

    /// <summary>Yields the given events, then waits like a live subscription until cancelled.</summary>
    private sealed class ScriptedBroker(params BrokerEvent[] events) : IOverlayEventBroker
    {
        private readonly TaskCompletionSource _allYielded = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Subscribed { get; private set; }

        public Task AllYielded => _allYielded.Task;

        public async IAsyncEnumerable<BrokerEvent> SubscribeAsync(string overlayToken, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Subscribed = true;
            foreach (var message in events)
            {
                yield return message;
            }

            // Reached only once the caller has handled the last event and asked for the next one.
            _allYielded.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public Task PublishAsync(string overlayToken, string payload, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class PlainTextEncryption : IDataEncryptionService
    {
        public string Encrypt(string plaintext) => plaintext;

        public string Decrypt(string ciphertext) => ciphertext;
    }
}
