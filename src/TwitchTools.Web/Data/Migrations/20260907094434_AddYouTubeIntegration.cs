using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TwitchTools.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddYouTubeIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CrossPostChatEnabled",
                table: "Streamers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CrossPostToTwitchTemplate",
                table: "Streamers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CrossPostToYouTubeTemplate",
                table: "Streamers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "YouTubeBotAccessToken",
                table: "Streamers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "YouTubeBotChannelId",
                table: "Streamers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "YouTubeBotRefreshToken",
                table: "Streamers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "YouTubeChannelId",
                table: "Streamers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "YouTubeChannelTitle",
                table: "Streamers",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "YouTubeStreamerAccessToken",
                table: "Streamers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "YouTubeStreamerRefreshToken",
                table: "Streamers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "YouTubeLiveStates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StreamerId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsLive = table.Column<bool>(type: "boolean", nullable: false),
                    VideoId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LiveChatId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    NextPageToken = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LastPolledUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YouTubeLiveStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_YouTubeLiveStates_Streamers_StreamerId",
                        column: x => x.StreamerId,
                        principalTable: "Streamers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Streamers_YouTubeChannelId",
                table: "Streamers",
                column: "YouTubeChannelId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YouTubeLiveStates_StreamerId",
                table: "YouTubeLiveStates",
                column: "StreamerId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "YouTubeLiveStates");

            migrationBuilder.DropIndex(
                name: "IX_Streamers_YouTubeChannelId",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "CrossPostChatEnabled",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "CrossPostToTwitchTemplate",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "CrossPostToYouTubeTemplate",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "YouTubeBotAccessToken",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "YouTubeBotChannelId",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "YouTubeBotRefreshToken",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "YouTubeChannelId",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "YouTubeChannelTitle",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "YouTubeStreamerAccessToken",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "YouTubeStreamerRefreshToken",
                table: "Streamers");
        }
    }
}
