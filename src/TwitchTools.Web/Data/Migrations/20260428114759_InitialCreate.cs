using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TwitchTools.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Streamers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerSubject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OwnerEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TwitchUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TwitchStreamerAccessToken = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    TwitchStreamerRefreshToken = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    TwitchClientId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TwitchBotUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TwitchBotAccessToken = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    TwitchBotRefreshToken = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    TwitchModeratorUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BlueSkyIdentifier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    BlueSkyAppPassword = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OverlayToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Streamers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiscordGuildSyncs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StreamerId = table.Column<Guid>(type: "uuid", nullable: false),
                    GuildId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastSyncedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscordGuildSyncs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscordGuildSyncs_Streamers_StreamerId",
                        column: x => x.StreamerId,
                        principalTable: "Streamers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LiveNotificationEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StreamerId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsLive = table.Column<bool>(type: "boolean", nullable: false),
                    BlueSkyPostUri = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    RecordedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveNotificationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveNotificationEvents_Streamers_StreamerId",
                        column: x => x.StreamerId,
                        principalTable: "Streamers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OverlaySnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StreamerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastFollowerName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastFollowerUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSubscriberName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastSubscriberUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OverlaySnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OverlaySnapshots_Streamers_StreamerId",
                        column: x => x.StreamerId,
                        principalTable: "Streamers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TimedChatMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StreamerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Interval = table.Column<TimeSpan>(type: "interval", nullable: false),
                    LastSentUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimedChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimedChatMessages_Streamers_StreamerId",
                        column: x => x.StreamerId,
                        principalTable: "Streamers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ViewerDurationSamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StreamerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TwitchViewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalSecondsWatched = table.Column<int>(type: "integer", nullable: false),
                    CapturedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ViewerDurationSamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ViewerDurationSamples_Streamers_StreamerId",
                        column: x => x.StreamerId,
                        principalTable: "Streamers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscordGuildSyncs_StreamerId_GuildId",
                table: "DiscordGuildSyncs",
                columns: new[] { "StreamerId", "GuildId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveNotificationEvents_StreamerId_RecordedUtc",
                table: "LiveNotificationEvents",
                columns: new[] { "StreamerId", "RecordedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OverlaySnapshots_StreamerId",
                table: "OverlaySnapshots",
                column: "StreamerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Streamers_OverlayToken",
                table: "Streamers",
                column: "OverlayToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Streamers_OwnerSubject",
                table: "Streamers",
                column: "OwnerSubject",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Streamers_TwitchUserId",
                table: "Streamers",
                column: "TwitchUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimedChatMessages_StreamerId_Enabled",
                table: "TimedChatMessages",
                columns: new[] { "StreamerId", "Enabled" });

            migrationBuilder.CreateIndex(
                name: "IX_ViewerDurationSamples_StreamerId_TwitchViewerId_CapturedUtc",
                table: "ViewerDurationSamples",
                columns: new[] { "StreamerId", "TwitchViewerId", "CapturedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscordGuildSyncs");

            migrationBuilder.DropTable(
                name: "LiveNotificationEvents");

            migrationBuilder.DropTable(
                name: "OverlaySnapshots");

            migrationBuilder.DropTable(
                name: "TimedChatMessages");

            migrationBuilder.DropTable(
                name: "ViewerDurationSamples");

            migrationBuilder.DropTable(
                name: "Streamers");
        }
    }
}
