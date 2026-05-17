using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TwitchTools.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomOverlayWidgetStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomOverlayCss",
                table: "Streamers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomOverlayDataJson",
                table: "Streamers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomOverlayFieldsJson",
                table: "Streamers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomOverlayHtml",
                table: "Streamers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomOverlayJs",
                table: "Streamers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomOverlayName",
                table: "Streamers",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomOverlayToken",
                table: "Streamers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CustomOverlayUpdatedUtc",
                table: "Streamers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Streamers_CustomOverlayToken",
                table: "Streamers",
                column: "CustomOverlayToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Streamers_CustomOverlayToken",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "CustomOverlayCss",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "CustomOverlayDataJson",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "CustomOverlayFieldsJson",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "CustomOverlayHtml",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "CustomOverlayJs",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "CustomOverlayName",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "CustomOverlayToken",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "CustomOverlayUpdatedUtc",
                table: "Streamers");
        }
    }
}
