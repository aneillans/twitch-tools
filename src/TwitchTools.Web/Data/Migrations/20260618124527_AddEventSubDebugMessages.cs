using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TwitchTools.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEventSubDebugMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventSubDebugMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StreamerId = table.Column<Guid>(type: "uuid", nullable: true),
                    MessageType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubscriptionType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    BroadcasterUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    RecordedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventSubDebugMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventSubDebugMessages_Streamers_StreamerId",
                        column: x => x.StreamerId,
                        principalTable: "Streamers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventSubDebugMessages_RecordedUtc",
                table: "EventSubDebugMessages",
                column: "RecordedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EventSubDebugMessages_StreamerId",
                table: "EventSubDebugMessages",
                column: "StreamerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventSubDebugMessages");
        }
    }
}
