using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubmissionAttemptCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "submission_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    job_application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "CREATED"),
                    idempotency_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    package_revision = table.Column<int>(type: "integer", nullable: false),
                    package_manifest_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    application_version_at_creation = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    created_by_admin_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    provider_submission_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    failure_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("submission_attempts_pkey", x => x.id);
                    table.CheckConstraint("ck_submission_attempts_application_version", "application_version_at_creation >= 1");
                    table.CheckConstraint("ck_submission_attempts_idempotency_key", "idempotency_key ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_submission_attempts_manifest_hash", "package_manifest_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_submission_attempts_outcome", "(status = 'SUCCEEDED' AND completed_at IS NOT NULL AND failure_code IS NULL AND failure_message IS NULL) OR (status IN ('FAILED', 'UNKNOWN') AND completed_at IS NOT NULL) OR (status IN ('CREATED', 'APPROVED', 'SUBMITTING') AND completed_at IS NULL AND provider_submission_id IS NULL AND failure_code IS NULL AND failure_message IS NULL)");
                    table.CheckConstraint("ck_submission_attempts_package_revision", "package_revision = 1");
                    table.CheckConstraint("ck_submission_attempts_provider", "provider IN ('EMAIL', 'COMPANY_SITE', 'TOPCV', 'VIETNAMWORKS', 'MANUAL')");
                    table.CheckConstraint("ck_submission_attempts_status", "status IN ('CREATED', 'APPROVED', 'SUBMITTING', 'SUCCEEDED', 'FAILED', 'UNKNOWN')");
                    table.CheckConstraint("ck_submission_attempts_timestamps", "(started_at IS NULL OR started_at >= created_at) AND (completed_at IS NULL OR (started_at IS NOT NULL AND completed_at >= started_at))");
                    table.CheckConstraint("ck_submission_attempts_version", "version >= 1");
                    table.ForeignKey(
                        name: "FK_submission_attempts_admin_users_created_by_admin_user_id",
                        column: x => x.created_by_admin_user_id,
                        principalTable: "admin_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submission_attempts_job_applications_job_application_id",
                        column: x => x.job_application_id,
                        principalTable: "job_applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "submission_attempt_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    submission_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    to_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    actor_admin_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("submission_attempt_events_pkey", x => x.id);
                    table.CheckConstraint("ck_submission_attempt_events_from_status", "from_status IS NULL OR from_status IN ('CREATED', 'APPROVED', 'SUBMITTING', 'SUCCEEDED', 'FAILED', 'UNKNOWN')");
                    table.CheckConstraint("ck_submission_attempt_events_to_status", "to_status IN ('CREATED', 'APPROVED', 'SUBMITTING', 'SUCCEEDED', 'FAILED', 'UNKNOWN')");
                    table.ForeignKey(
                        name: "FK_submission_attempt_events_admin_users_actor_admin_user_id",
                        column: x => x.actor_admin_user_id,
                        principalTable: "admin_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submission_attempt_events_submission_attempts_submission_at~",
                        column: x => x.submission_attempt_id,
                        principalTable: "submission_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_submission_attempt_events_attempt_occurred_id",
                table: "submission_attempt_events",
                columns: new[] { "submission_attempt_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_submission_attempts_application_created_id",
                table: "submission_attempts",
                columns: new[] { "job_application_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "uq_submission_attempts_idempotency_key",
                table: "submission_attempts",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_submission_attempts_non_retryable_context",
                table: "submission_attempts",
                columns: new[] { "job_application_id", "package_revision", "provider" },
                unique: true,
                filter: "status IN ('CREATED', 'APPROVED', 'SUBMITTING', 'SUCCEEDED', 'UNKNOWN')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "submission_attempt_events");

            migrationBuilder.DropTable(
                name: "submission_attempts");
        }
    }
}
