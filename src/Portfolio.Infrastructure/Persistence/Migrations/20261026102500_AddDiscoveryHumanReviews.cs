using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscoveryHumanReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "discovery_candidate_reviews",
                columns: table => new
                {
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    target_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by_admin_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("discovery_candidate_reviews_pkey", x => x.candidate_id);
                    table.CheckConstraint("ck_discovery_review_labels", "(status = 'UNCERTAIN' AND content_status IS NULL AND target_status IS NULL) OR (status IN ('CONFIRMED','CORRECTED') AND content_status IS NOT NULL AND target_status IS NOT NULL AND content_status IN ('VALID_RECRUITMENT','INVALID_RECRUITMENT','NEEDS_REVIEW') AND target_status IN ('TARGET_MATCH','FALLBACK_MATCH','POTENTIAL_MATCH','NOT_MATCHED','INSUFFICIENT_DATA'))");
                    table.CheckConstraint("ck_discovery_review_notes", "length(btrim(notes)) BETWEEN 1 AND 2000");
                    table.CheckConstraint("ck_discovery_review_version", "version >= 1");
                    table.ForeignKey(
                        name: "FK_discovery_candidate_reviews_admin_users_reviewed_by_admin_id",
                        column: x => x.reviewed_by_admin_id,
                        principalTable: "admin_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_discovery_candidate_reviews_discovery_candidates_candidate_~",
                        column: x => x.candidate_id,
                        principalTable: "discovery_candidates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "discovery_candidate_review_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    target_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by_admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    observation_payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discovery_candidate_review_events", x => x.id);
                    table.CheckConstraint("ck_discovery_review_event_hash", "observation_payload_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_discovery_review_event_labels", "(status = 'UNCERTAIN' AND content_status IS NULL AND target_status IS NULL) OR (status IN ('CONFIRMED','CORRECTED') AND content_status IS NOT NULL AND target_status IS NOT NULL AND content_status IN ('VALID_RECRUITMENT','INVALID_RECRUITMENT','NEEDS_REVIEW') AND target_status IN ('TARGET_MATCH','FALLBACK_MATCH','POTENTIAL_MATCH','NOT_MATCHED','INSUFFICIENT_DATA'))");
                    table.CheckConstraint("ck_discovery_review_event_notes", "length(btrim(notes)) BETWEEN 1 AND 2000");
                    table.CheckConstraint("ck_discovery_review_event_version", "version >= 1");
                    table.ForeignKey(
                        name: "FK_discovery_candidate_review_events_admin_users_reviewed_by_a~",
                        column: x => x.reviewed_by_admin_id,
                        principalTable: "admin_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_discovery_candidate_review_events_discovery_candidate_revie~",
                        column: x => x.candidate_id,
                        principalTable: "discovery_candidate_reviews",
                        principalColumn: "candidate_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_discovery_review_event_version",
                table: "discovery_candidate_review_events",
                columns: new[] { "candidate_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_discovery_candidate_reviews_status_reviewed_at",
                table: "discovery_candidate_reviews",
                columns: new[] { "status", "reviewed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "discovery_candidate_review_events");

            migrationBuilder.DropTable(
                name: "discovery_candidate_reviews");
        }
    }
}
