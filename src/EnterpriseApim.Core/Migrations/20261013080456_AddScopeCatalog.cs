using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnterpriseApim.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddScopeCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scopes",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scopes", x => x.Name);
                });

            // Add every scope already used by a route or key, so existing data satisfies the new foreign key.
            migrationBuilder.Sql("""
                INSERT INTO scopes ("Name", "Description", "CreatedAt", "UpdatedAt")
                SELECT DISTINCT used.name, NULL::varchar, now(), now()
                FROM (
                    SELECT "RequiredScope" AS name FROM routes WHERE "RequiredScope" IS NOT NULL
                    UNION
                    SELECT unnest("Scopes") FROM api_keys
                ) AS used
                WHERE used.name <> '*' OR used.name IN (SELECT "RequiredScope" FROM routes)
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_routes_RequiredScope",
                table: "routes",
                column: "RequiredScope");

            migrationBuilder.AddForeignKey(
                name: "FK_routes_scopes_RequiredScope",
                table: "routes",
                column: "RequiredScope",
                principalTable: "scopes",
                principalColumn: "Name",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_routes_scopes_RequiredScope",
                table: "routes");

            migrationBuilder.DropTable(
                name: "scopes");

            migrationBuilder.DropIndex(
                name: "IX_routes_RequiredScope",
                table: "routes");
        }
    }
}
