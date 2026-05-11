using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TwitchTools.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBlueSkyLivePostTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BlueSkyStreamStartedTemplate",
                table: "Streamers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlueSkyStreamStoppedTemplate",
                table: "Streamers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlueSkyStreamStartedTemplate",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "BlueSkyStreamStoppedTemplate",
                table: "Streamers");
        }
    }
}
