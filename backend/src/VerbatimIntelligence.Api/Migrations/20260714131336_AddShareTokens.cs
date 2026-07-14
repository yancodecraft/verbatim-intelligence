using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VerbatimIntelligence.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddShareTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "share_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    analysis_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_share_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_share_tokens_analyses_analysis_id",
                        column: x => x.analysis_id,
                        principalTable: "analyses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_share_tokens_analysis_id",
                table: "share_tokens",
                column: "analysis_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_share_tokens_token_hash",
                table: "share_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "share_tokens");
        }
    }
}