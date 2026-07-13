using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VerbatimIntelligence.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddThemesAndPipelineColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempts",
                table: "analyses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "error",
                table: "analyses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "heartbeat_at",
                table: "analyses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "input_tokens",
                table: "analyses",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "output_tokens",
                table: "analyses",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "processed_count",
                table: "analyses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "themes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    analysis_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    synthesis = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_themes", x => x.id);
                    table.ForeignKey(
                        name: "fk_themes_analyses_analysis_id",
                        column: x => x.analysis_id,
                        principalTable: "analyses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "theme_verbatims",
                columns: table => new
                {
                    theme_id = table.Column<Guid>(type: "uuid", nullable: false),
                    verbatim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theme_verbatims", x => new { x.theme_id, x.verbatim_id });
                    table.ForeignKey(
                        name: "fk_theme_verbatims_themes_theme_id",
                        column: x => x.theme_id,
                        principalTable: "themes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_theme_verbatims_verbatims_verbatim_id",
                        column: x => x.verbatim_id,
                        principalTable: "verbatims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_theme_verbatims_verbatim_id",
                table: "theme_verbatims",
                column: "verbatim_id");

            migrationBuilder.CreateIndex(
                name: "ix_themes_analysis_id",
                table: "themes",
                column: "analysis_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "theme_verbatims");

            migrationBuilder.DropTable(
                name: "themes");

            migrationBuilder.DropColumn(
                name: "attempts",
                table: "analyses");

            migrationBuilder.DropColumn(
                name: "error",
                table: "analyses");

            migrationBuilder.DropColumn(
                name: "heartbeat_at",
                table: "analyses");

            migrationBuilder.DropColumn(
                name: "input_tokens",
                table: "analyses");

            migrationBuilder.DropColumn(
                name: "output_tokens",
                table: "analyses");

            migrationBuilder.DropColumn(
                name: "processed_count",
                table: "analyses");
        }
    }
}