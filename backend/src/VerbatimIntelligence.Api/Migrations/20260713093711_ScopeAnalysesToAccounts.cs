using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VerbatimIntelligence.Api.Migrations
{
    /// <inheritdoc />
    public partial class ScopeAnalysesToAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Pre-auth analyses (walking-skeleton smoke runs) have no owner
            // and no value; they cannot satisfy the new NOT NULL foreign key.
            migrationBuilder.Sql("DELETE FROM analyses;");

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "analyses",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.CreateIndex(
                name: "ix_analyses_user_id",
                table: "analyses",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_analyses_users_user_id",
                table: "analyses",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_analyses_users_user_id",
                table: "analyses");

            migrationBuilder.DropIndex(
                name: "ix_analyses_user_id",
                table: "analyses");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "analyses");
        }
    }
}