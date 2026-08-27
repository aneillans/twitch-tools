using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TwitchTools.Web.Domain;
using TwitchTools.Web.Services.Security;

namespace TwitchTools.Web.Data;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    IDataEncryptionService encryptionService) : DbContext(options)
{
    // Fixed timestamp so migration seeding produces a deterministic, reproducible model snapshot.
    private static readonly DateTime SeedDate = new(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc);

    public DbSet<Streamer> Streamers => Set<Streamer>();
    public DbSet<LiveNotificationEvent> LiveNotificationEvents => Set<LiveNotificationEvent>();
    public DbSet<SubscriberNotificationEvent> SubscriberNotificationEvents => Set<SubscriberNotificationEvent>();
    public DbSet<DiscordGuildSync> DiscordGuildSyncs => Set<DiscordGuildSync>();
    public DbSet<TimedChatMessage> TimedChatMessages => Set<TimedChatMessage>();
    public DbSet<ViewerDurationSample> ViewerDurationSamples => Set<ViewerDurationSample>();
    public DbSet<OverlaySnapshot> OverlaySnapshots => Set<OverlaySnapshot>();
    public DbSet<EventSubDebugMessage> EventSubDebugMessages => Set<EventSubDebugMessage>();
    public DbSet<ProcessedEventSubMessage> ProcessedEventSubMessages => Set<ProcessedEventSubMessage>();
    public DbSet<KnownBot> KnownBots => Set<KnownBot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var encryptedString = new ValueConverter<string, string>(
            value => encryptionService.Encrypt(value),
            value => encryptionService.Decrypt(value));

        var encryptedNullableString = new ValueConverter<string?, string?>(
            value => value == null ? null : encryptionService.Encrypt(value),
            value => value == null ? null : encryptionService.Decrypt(value));

        modelBuilder.Entity<Streamer>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OwnerSubject).IsUnique();
            entity.HasIndex(x => x.TwitchUserId).IsUnique();
            entity.HasIndex(x => x.OverlayToken).IsUnique();
            entity.HasIndex(x => x.FollowerOverlayToken).IsUnique();
            entity.HasIndex(x => x.SubscriberOverlayToken).IsUnique();
            entity.HasIndex(x => x.CustomOverlayToken).IsUnique();
            entity.Property(x => x.OwnerSubject).HasMaxLength(128);
            entity.Property(x => x.OwnerEmail).HasMaxLength(256);
            entity.Property(x => x.DisplayName).HasMaxLength(128);
            entity.Property(x => x.TwitchUserId).HasMaxLength(64);
            entity.Property(x => x.TwitchStreamerAccessToken).HasMaxLength(2048).HasConversion(encryptedString);
            entity.Property(x => x.TwitchStreamerRefreshToken).HasMaxLength(2048).HasConversion(encryptedNullableString);
            entity.Property(x => x.TwitchClientId).HasMaxLength(128);
            entity.Property(x => x.TwitchBotUserId).HasMaxLength(64);
            entity.Property(x => x.TwitchBotAccessToken).HasMaxLength(2048).HasConversion(encryptedNullableString);
            entity.Property(x => x.TwitchBotRefreshToken).HasMaxLength(2048).HasConversion(encryptedNullableString);
            entity.Property(x => x.BlueSkyIdentifier).HasMaxLength(256);
            entity.Property(x => x.BlueSkyAppPassword).HasMaxLength(512).HasConversion(encryptedNullableString);
            entity.Property(x => x.BlueSkyPostOnStreamStart).HasDefaultValue(true);
            entity.Property(x => x.BlueSkyPostOnStreamStop).HasDefaultValue(true);
            entity.Property(x => x.BlueSkyStreamStartedTemplate).HasMaxLength(500);
            entity.Property(x => x.BlueSkyStreamStoppedTemplate).HasMaxLength(500);
            entity.Property(x => x.OverlayToken).HasMaxLength(64);
            entity.Property(x => x.FollowerOverlayToken).HasMaxLength(64);
            entity.Property(x => x.SubscriberOverlayToken).HasMaxLength(64);
            entity.Property(x => x.CustomOverlayToken).HasMaxLength(64);
            entity.Property(x => x.CustomOverlayName).HasMaxLength(128);
        });

        modelBuilder.Entity<LiveNotificationEvent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.StreamerId, x.RecordedUtc });
            entity.Property(x => x.BlueSkyPostUri).HasMaxLength(512);
        });

        modelBuilder.Entity<SubscriberNotificationEvent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.StreamerId, x.RecordedUtc });
            entity.Property(x => x.TwitchUserId).HasMaxLength(64);
            entity.Property(x => x.TwitchUserLogin).HasMaxLength(128);
            entity.Property(x => x.TwitchUserName).HasMaxLength(128);
        });

        modelBuilder.Entity<DiscordGuildSync>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.StreamerId, x.GuildId }).IsUnique();
            entity.Property(x => x.GuildId).HasMaxLength(64);
        });

        modelBuilder.Entity<TimedChatMessage>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.StreamerId, x.Enabled });
            entity.Property(x => x.MessageText).HasMaxLength(500);
        });

        modelBuilder.Entity<ViewerDurationSample>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.StreamerId, x.TwitchViewerId, x.CapturedUtc });
            entity.Property(x => x.TwitchViewerId).HasMaxLength(64);
        });

        modelBuilder.Entity<OverlaySnapshot>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.StreamerId).IsUnique();
            entity.Property(x => x.LastFollowerName).HasMaxLength(128);
            entity.Property(x => x.LastSubscriberName).HasMaxLength(128);
        });

        modelBuilder.Entity<EventSubDebugMessage>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.RecordedUtc);
            entity.HasIndex(x => x.StreamerId);
            entity.Property(x => x.MessageType).HasMaxLength(64);
            entity.Property(x => x.SubscriptionType).HasMaxLength(64);
            entity.Property(x => x.MessageId).HasMaxLength(128);
            entity.Property(x => x.BroadcasterUserId).HasMaxLength(64);
            entity.Property(x => x.Payload);
        });

        modelBuilder.Entity<ProcessedEventSubMessage>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.MessageId).IsUnique();
            entity.Property(x => x.MessageId).HasMaxLength(128);
        });

        modelBuilder.Entity<KnownBot>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TwitchUserId).IsUnique();
            entity.Property(x => x.TwitchUserId).HasMaxLength(64);
            entity.Property(x => x.Login).HasMaxLength(128);
            entity.Property(x => x.Notes).HasMaxLength(500);

            // Pre-populate with well-known Twitch chat bots so viewer stats are useful out of the box.
            entity.HasData(
                new KnownBot { Id = -1, TwitchUserId = "566008092", Login = "own3d", Notes = "OWN3D chatbot", CreatedUtc = SeedDate },
                new KnownBot { Id = -2, TwitchUserId = "42062292", Login = "streamerbot", Notes = "Streamer.bot", CreatedUtc = SeedDate },
                new KnownBot { Id = -3, TwitchUserId = "402337290", Login = "sery_bot", Notes = "Sery_Bot", CreatedUtc = SeedDate },
                new KnownBot { Id = -4, TwitchUserId = "100135110", Login = "streamelements", Notes = "StreamElements", CreatedUtc = SeedDate },
                new KnownBot { Id = -5, TwitchUserId = "19264788", Login = "nightbot", Notes = "Nightbot", CreatedUtc = SeedDate },
                new KnownBot { Id = -6, TwitchUserId = "431199284", Login = "kofistreambot", Notes = "Ko-fi Stream Bot", CreatedUtc = SeedDate });
        });

        modelBuilder.Entity<LiveNotificationEvent>()
            .HasOne(x => x.Streamer)
            .WithMany(x => x.LiveNotificationEvents)
            .HasForeignKey(x => x.StreamerId);

        modelBuilder.Entity<SubscriberNotificationEvent>()
            .HasOne(x => x.Streamer)
            .WithMany(x => x.SubscriberNotificationEvents)
            .HasForeignKey(x => x.StreamerId);

        modelBuilder.Entity<DiscordGuildSync>()
            .HasOne(x => x.Streamer)
            .WithMany()
            .HasForeignKey(x => x.StreamerId);

        modelBuilder.Entity<TimedChatMessage>()
            .HasOne(x => x.Streamer)
            .WithMany(x => x.TimedChatMessages)
            .HasForeignKey(x => x.StreamerId);

        modelBuilder.Entity<ViewerDurationSample>()
            .HasOne(x => x.Streamer)
            .WithMany(x => x.ViewerDurationSamples)
            .HasForeignKey(x => x.StreamerId);

        modelBuilder.Entity<OverlaySnapshot>()
            .HasOne(x => x.Streamer)
            .WithOne(x => x.OverlaySnapshot)
            .HasForeignKey<OverlaySnapshot>(x => x.StreamerId);

        modelBuilder.Entity<EventSubDebugMessage>()
            .HasOne(x => x.Streamer)
            .WithMany()
            .HasForeignKey(x => x.StreamerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}