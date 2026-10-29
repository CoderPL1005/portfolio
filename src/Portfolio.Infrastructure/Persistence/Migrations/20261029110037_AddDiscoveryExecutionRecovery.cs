using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscoveryExecutionRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_discovery_review_labels",
                table: "discovery_candidate_reviews");

            migrationBuilder.DropCheckConstraint(
                name: "ck_discovery_review_event_labels",
                table: "discovery_candidate_review_events");

            migrationBuilder.AddColumn<string>(
                name: "candidate_origin",
                table: "discovery_candidates",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "CLASSIFIER");

            migrationBuilder.CreateIndex(
                name: "uq_discovery_recovery_event",
                table: "discovery_execution_events",
                columns: new[] { "ExecutionId", "State" },
                unique: true,
                filter: "\"State\" IN ('RECOVERY_RECEIPT','RECOVERY_AUDIT')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_discovery_review_labels",
                table: "discovery_candidate_reviews",
                sql: "(status = 'UNCERTAIN' AND content_status IS NULL AND target_status IS NULL) OR (status IN ('CONFIRMED','CORRECTED','REVIEWED_RECOVERY') AND content_status IS NOT NULL AND target_status IS NOT NULL AND content_status IN ('VALID_RECRUITMENT','INVALID_RECRUITMENT','NEEDS_REVIEW') AND target_status IN ('TARGET_MATCH','FALLBACK_MATCH','POTENTIAL_MATCH','NOT_MATCHED','INSUFFICIENT_DATA'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_discovery_review_event_labels",
                table: "discovery_candidate_review_events",
                sql: "(status = 'UNCERTAIN' AND content_status IS NULL AND target_status IS NULL) OR (status IN ('CONFIRMED','CORRECTED','REVIEWED_RECOVERY') AND content_status IS NOT NULL AND target_status IS NOT NULL AND content_status IN ('VALID_RECRUITMENT','INVALID_RECRUITMENT','NEEDS_REVIEW') AND target_status IN ('TARGET_MATCH','FALLBACK_MATCH','POTENTIAL_MATCH','NOT_MATCHED','INSUFFICIENT_DATA'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Never discard recovery provenance or replay evidence on rollback.
            // A code rollback must retain the additive schema once recovery is used.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM discovery_candidates WHERE candidate_origin = 'PERSISTED_OBSERVATION_RECOVERY')
                       OR EXISTS (SELECT 1 FROM discovery_execution_events WHERE "State" IN ('RECOVERY_RECEIPT','RECOVERY_AUDIT','RECOVERED_FOR_HUMAN_REVIEW'))
                       OR EXISTS (SELECT 1 FROM discovery_candidate_reviews WHERE status = 'REVIEWED_RECOVERY')
                       OR EXISTS (SELECT 1 FROM discovery_candidate_review_events WHERE status = 'REVIEWED_RECOVERY')
                    THEN
                        RAISE EXCEPTION 'DISCOVERY_RECOVERY_ROLLBACK_BLOCKED';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "uq_discovery_recovery_event",
                table: "discovery_execution_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_discovery_review_labels",
                table: "discovery_candidate_reviews");

            migrationBuilder.DropCheckConstraint(
                name: "ck_discovery_review_event_labels",
                table: "discovery_candidate_review_events");

            migrationBuilder.DropColumn(
                name: "candidate_origin",
                table: "discovery_candidates");

            migrationBuilder.AddCheckConstraint(
                name: "ck_discovery_review_labels",
                table: "discovery_candidate_reviews",
                sql: "(status = 'UNCERTAIN' AND content_status IS NULL AND target_status IS NULL) OR (status IN ('CONFIRMED','CORRECTED') AND content_status IS NOT NULL AND target_status IS NOT NULL AND content_status IN ('VALID_RECRUITMENT','INVALID_RECRUITMENT','NEEDS_REVIEW') AND target_status IN ('TARGET_MATCH','FALLBACK_MATCH','POTENTIAL_MATCH','NOT_MATCHED','INSUFFICIENT_DATA'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_discovery_review_event_labels",
                table: "discovery_candidate_review_events",
                sql: "(status = 'UNCERTAIN' AND content_status IS NULL AND target_status IS NULL) OR (status IN ('CONFIRMED','CORRECTED') AND content_status IS NOT NULL AND target_status IS NOT NULL AND content_status IN ('VALID_RECRUITMENT','INVALID_RECRUITMENT','NEEDS_REVIEW') AND target_status IN ('TARGET_MATCH','FALLBACK_MATCH','POTENTIAL_MATCH','NOT_MATCHED','INSUFFICIENT_DATA'))");
        }
    }
}
