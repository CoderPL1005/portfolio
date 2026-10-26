using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscoveryReviewCandidates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "discovery_candidates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    query = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    page_number = table.Column<int>(type: "integer", nullable: false),
                    item_index = table.Column<int>(type: "integer", nullable: false),
                    content_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    target_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    content_group_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    identity_warning = table.Column<bool>(type: "boolean", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    ingested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discovery_candidates", x => x.id);
                    table.CheckConstraint("ck_discovery_candidates_content", "content_status IN ('VALID_RECRUITMENT','INVALID_RECRUITMENT','NEEDS_REVIEW')");
                    table.CheckConstraint("ck_discovery_candidates_coordinates", "page_number BETWEEN 1 AND 6 AND item_index BETWEEN 0 AND 199");
                    table.CheckConstraint("ck_discovery_candidates_hash", "payload_hash ~ '^[0-9a-f]{64}$' AND (content_group_hash IS NULL OR content_group_hash ~ '^[0-9a-f]{64}$')");
                    table.CheckConstraint("ck_discovery_candidates_target", "target_status IN ('TARGET_MATCH','FALLBACK_MATCH','POTENTIAL_MATCH','NOT_MATCHED','INSUFFICIENT_DATA')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_discovery_candidates_content_group_hash",
                table: "discovery_candidates",
                column: "content_group_hash");

            migrationBuilder.CreateIndex(
                name: "IX_discovery_candidates_ingested_at_id",
                table: "discovery_candidates",
                columns: new[] { "ingested_at", "id" });

            migrationBuilder.CreateIndex(
                name: "uq_discovery_candidates_ingestion",
                table: "discovery_candidates",
                columns: new[] { "run_id", "page_number", "item_index" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "discovery_candidates");
        }
    }
}
