using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VerbatimIntelligence.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadsAndVerbatims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Denormalized onto analyses. NOT NULL with a database default so
            // existing rows backfill and the deployed N-1 backend and the
            // worker (which do not write these columns) keep inserting — the
            // expand step of expand/contract (see docs/architecture.md).
            migrationBuilder.AddColumn<string>(
                name: "source_filename",
                table: "analyses",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "verbatim_count",
                table: "analyses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "uploads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    filename = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    columns = table.Column<string>(type: "jsonb", nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_uploads", x => x.id);
                    table.ForeignKey(
                        name: "fk_uploads_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "verbatims",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    analysis_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verbatims", x => x.id);
                    table.ForeignKey(
                        name: "fk_verbatims_analyses_analysis_id",
                        column: x => x.analysis_id,
                        principalTable: "analyses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_uploads_user_id",
                table: "uploads",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_verbatims_analysis_id",
                table: "verbatims",
                column: "analysis_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "uploads");

            migrationBuilder.DropTable(
                name: "verbatims");

            migrationBuilder.DropColumn(
                name: "source_filename",
                table: "analyses");

            migrationBuilder.DropColumn(
                name: "verbatim_count",
                table: "analyses");
        }
    }
}