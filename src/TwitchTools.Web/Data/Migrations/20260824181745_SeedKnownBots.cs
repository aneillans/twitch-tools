using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TwitchTools.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedKnownBots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "KnownBots",
                columns: new[] { "Id", "CreatedUtc", "Login", "Notes", "TwitchUserId" },
                values: new object[,]
                {
                    { -6, new DateTime(2026, 8, 24, 0, 0, 0, 0, DateTimeKind.Utc), "kofistreambot", "Ko-fi Stream Bot", "431199284" },
                    { -5, new DateTime(2026, 8, 24, 0, 0, 0, 0, DateTimeKind.Utc), "nightbot", "Nightbot", "19264788" },
                    { -4, new DateTime(2026, 8, 24, 0, 0, 0, 0, DateTimeKind.Utc), "streamelements", "StreamElements", "100135110" },
                    { -3, new DateTime(2026, 8, 24, 0, 0, 0, 0, DateTimeKind.Utc), "sery_bot", "Sery_Bot", "402337290" },
                    { -2, new DateTime(2026, 8, 24, 0, 0, 0, 0, DateTimeKind.Utc), "streamerbot", "Streamer.bot", "42062292" },
                    { -1, new DateTime(2026, 8, 24, 0, 0, 0, 0, DateTimeKind.Utc), "own3d", "OWN3D chatbot", "566008092" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "KnownBots",
                keyColumn: "Id",
                keyValue: -6);

            migrationBuilder.DeleteData(
                table: "KnownBots",
                keyColumn: "Id",
                keyValue: -5);

            migrationBuilder.DeleteData(
                table: "KnownBots",
                keyColumn: "Id",
                keyValue: -4);

            migrationBuilder.DeleteData(
                table: "KnownBots",
                keyColumn: "Id",
                keyValue: -3);

            migrationBuilder.DeleteData(
                table: "KnownBots",
                keyColumn: "Id",
                keyValue: -2);

            migrationBuilder.DeleteData(
                table: "KnownBots",
                keyColumn: "Id",
                keyValue: -1);
        }
    }
}
