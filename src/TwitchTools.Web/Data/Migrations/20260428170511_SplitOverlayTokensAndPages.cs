using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TwitchTools.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class SplitOverlayTokensAndPages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FollowerOverlayToken",
                table: "Streamers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriberOverlayToken",
                table: "Streamers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "Streamers"
                SET "FollowerOverlayToken" = CASE
                    WHEN COALESCE("OverlayToken", '') = '' THEN md5(random()::text || clock_timestamp()::text)
                    ELSE "OverlayToken"
                END,
                    "SubscriberOverlayToken" = md5(random()::text || clock_timestamp()::text)
                WHERE "FollowerOverlayToken" IS NULL OR "SubscriberOverlayToken" IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "FollowerOverlayToken",
                table: "Streamers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SubscriberOverlayToken",
                table: "Streamers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Streamers_FollowerOverlayToken",
                table: "Streamers",
                column: "FollowerOverlayToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Streamers_SubscriberOverlayToken",
                table: "Streamers",
                column: "SubscriberOverlayToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Streamers_FollowerOverlayToken",
                table: "Streamers");

            migrationBuilder.DropIndex(
                name: "IX_Streamers_SubscriberOverlayToken",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "FollowerOverlayToken",
                table: "Streamers");

            migrationBuilder.DropColumn(
                name: "SubscriberOverlayToken",
                table: "Streamers");
        }
    }
}
