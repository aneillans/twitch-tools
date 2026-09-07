using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TwitchTools.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddYouTubeViewerTrackingAndIdentityLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ViewerIdentityLinks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StreamerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TwitchViewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    YouTubeViewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ViewerIdentityLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ViewerIdentityLinks_Streamers_StreamerId",
                        column: x => x.StreamerId,
                        principalTable: "Streamers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "YouTubeViewerDurationSamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StreamerId = table.Column<Guid>(type: "uuid", nullable: false),
                    YouTubeViewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    YouTubeViewerDisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    VideoId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionBaseSeconds = table.Column<int>(type: "integer", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalSecondsWatched = table.Column<int>(type: "integer", nullable: false),
                    CapturedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YouTubeViewerDurationSamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_YouTubeViewerDurationSamples_Streamers_StreamerId",
                        column: x => x.StreamerId,
                        principalTable: "Streamers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ViewerIdentityLinks_StreamerId_TwitchViewerId",
                table: "ViewerIdentityLinks",
                columns: new[] { "StreamerId", "TwitchViewerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ViewerIdentityLinks_StreamerId_YouTubeViewerId",
                table: "ViewerIdentityLinks",
                columns: new[] { "StreamerId", "YouTubeViewerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YouTubeViewerDurationSamples_StreamerId_YouTubeViewerId_Cap~",
                table: "YouTubeViewerDurationSamples",
                columns: new[] { "StreamerId", "YouTubeViewerId", "CapturedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ViewerIdentityLinks");

            migrationBuilder.DropTable(
                name: "YouTubeViewerDurationSamples");
        }
    }
}
