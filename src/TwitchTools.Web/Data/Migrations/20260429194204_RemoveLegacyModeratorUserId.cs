using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TwitchTools.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyModeratorUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TwitchModeratorUserId",
                table: "Streamers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TwitchModeratorUserId",
                table: "Streamers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }
    }
}
