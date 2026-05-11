using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TwitchTools.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBlueSkyPostStateToggles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BlueSkyPostOnStreamStart",
                table: "Streamers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "BlueSkyPostOnStreamStop",
                table: "Streamers",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlueSkyPostOnStreamStart",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "BlueSkyPostOnStreamStop",
                table: "Streamers");
        }
    }
}
