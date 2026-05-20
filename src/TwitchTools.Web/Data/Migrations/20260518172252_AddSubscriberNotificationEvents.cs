using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TwitchTools.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriberNotificationEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriberNotificationEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StreamerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TwitchUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TwitchUserLogin = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TwitchUserName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsGift = table.Column<bool>(type: "boolean", nullable: false),
                    RecordedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriberNotificationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriberNotificationEvents_Streamers_StreamerId",
                        column: x => x.StreamerId,
                        principalTable: "Streamers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriberNotificationEvents_StreamerId_RecordedUtc",
                table: "SubscriberNotificationEvents",
                columns: new[] { "StreamerId", "RecordedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriberNotificationEvents");
        }
    }
}
