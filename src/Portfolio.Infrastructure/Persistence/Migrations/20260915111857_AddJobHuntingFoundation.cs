using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobHuntingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_postings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    company_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    position_title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    location = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    employment_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    workplace_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    salary_minimum = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    salary_maximum = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    salary_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    salary_period = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    experience_requirements = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: false),
                    technology_stack = table.Column<JsonDocument>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    application_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    application_url = table.Column<string>(type: "text", nullable: true),
                    verification_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "PENDING"),
                    selection_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "PENDING_ANALYSIS"),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("job_postings_pkey", x => x.id);
                    table.CheckConstraint("ck_job_postings_salary_currency", "salary_currency IS NULL OR salary_currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_job_postings_salary_maximum", "salary_maximum IS NULL OR salary_maximum >= 0");
                    table.CheckConstraint("ck_job_postings_salary_minimum", "salary_minimum IS NULL OR salary_minimum >= 0");
                    table.CheckConstraint("ck_job_postings_salary_range", "salary_minimum IS NULL OR salary_maximum IS NULL OR salary_maximum >= salary_minimum");
                    table.CheckConstraint("ck_job_postings_selection_status", "selection_status IN ('PENDING_ANALYSIS', 'RECOMMENDED', 'APPROVED', 'SKIPPED')");
                    table.CheckConstraint("ck_job_postings_technology_stack", "jsonb_typeof(technology_stack) = 'array'");
                    table.CheckConstraint("ck_job_postings_verification_status", "verification_status IN ('PENDING', 'VERIFIED', 'UNVERIFIED', 'LIKELY_EXPIRED')");
                    table.CheckConstraint("ck_job_postings_version", "version >= 1");
                });

            migrationBuilder.CreateTable(
                name: "job_applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    job_posting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "DRAFT"),
                    channel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    application_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    application_url = table.Column<string>(type: "text", nullable: true),
                    external_application_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("job_applications_pkey", x => x.id);
                    table.CheckConstraint("ck_job_applications_channel", "channel IS NULL OR channel IN ('EMAIL', 'PLATFORM', 'MANUAL', 'OTHER')");
                    table.CheckConstraint("ck_job_applications_status", "status IN ('DRAFT', 'APPLIED', 'INTERVIEW', 'REJECTED', 'OFFER', 'WITHDRAWN')");
                    table.CheckConstraint("ck_job_applications_version", "version >= 1");
                    table.ForeignKey(
                        name: "FK_job_applications_job_postings_job_posting_id",
                        column: x => x.job_posting_id,
                        principalTable: "job_postings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "raw_job_postings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    job_posting_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_external_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source_url = table.Column<string>(type: "text", nullable: true),
                    source_url_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    raw_content = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    company_title_fingerprint = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ingestion_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "RECEIVED"),
                    duplicate_of_raw_job_posting_id = table.Column<Guid>(type: "uuid", nullable: true),
                    metadata = table.Column<JsonDocument>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    discovered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("raw_job_postings_pkey", x => x.id);
                    table.CheckConstraint("ck_raw_job_postings_content_hash", "content_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_raw_job_postings_ingestion_status", "ingestion_status IN ('RECEIVED', 'NORMALIZED', 'DUPLICATE', 'REJECTED')");
                    table.CheckConstraint("ck_raw_job_postings_source", "source IN ('FACEBOOK', 'INSTAGRAM', 'TOPCV', 'VIETNAMWORKS', 'COMPANY_SITE', 'MANUAL', 'OTHER')");
                    table.CheckConstraint("ck_raw_job_postings_source_url_hash", "source_url_hash IS NULL OR source_url_hash ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_raw_job_postings_job_postings_job_posting_id",
                        column: x => x.job_posting_id,
                        principalTable: "job_postings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_raw_job_postings_raw_job_postings_duplicate_of_raw_job_post~",
                        column: x => x.duplicate_of_raw_job_posting_id,
                        principalTable: "raw_job_postings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job_application_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    job_application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    version_label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    storage_key = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    metadata = table.Column<JsonDocument>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("job_application_documents_pkey", x => x.id);
                    table.CheckConstraint("ck_job_application_documents_content_hash", "content_hash IS NULL OR content_hash ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_job_application_documents_job_applications_job_application_~",
                        column: x => x.job_application_id,
                        principalTable: "job_applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job_application_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    job_application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    from_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    to_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    actor_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    actor_admin_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    metadata = table.Column<JsonDocument>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("job_application_events_pkey", x => x.id);
                    table.CheckConstraint("ck_job_application_events_actor_type", "actor_type IN ('ADMIN', 'SYSTEM', 'TELEGRAM', 'EMAIL_CONNECTOR')");
                    table.CheckConstraint("ck_job_application_events_event_type", "event_type IN ('CREATED', 'STATUS_CHANGED', 'NOTE_ADDED', 'DOCUMENT_ATTACHED', 'DOCUMENT_REMOVED')");
                    table.CheckConstraint("ck_job_application_events_from_status", "from_status IS NULL OR from_status IN ('DRAFT', 'APPLIED', 'INTERVIEW', 'REJECTED', 'OFFER', 'WITHDRAWN')");
                    table.CheckConstraint("ck_job_application_events_to_status", "to_status IS NULL OR to_status IN ('DRAFT', 'APPLIED', 'INTERVIEW', 'REJECTED', 'OFFER', 'WITHDRAWN')");
                    table.ForeignKey(
                        name: "FK_job_application_events_admin_users_actor_admin_user_id",
                        column: x => x.actor_admin_user_id,
                        principalTable: "admin_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_job_application_events_job_applications_job_application_id",
                        column: x => x.job_application_id,
                        principalTable: "job_applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_application_documents_application_type_created_id",
                table: "job_application_documents",
                columns: new[] { "job_application_id", "document_type", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_job_application_events_application_occurred_id",
                table: "job_application_events",
                columns: new[] { "job_application_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_job_applications_channel_applied_at",
                table: "job_applications",
                columns: new[] { "channel", "applied_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_job_applications_job_posting_id",
                table: "job_applications",
                column: "job_posting_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_applications_status_updated_at",
                table: "job_applications",
                columns: new[] { "status", "updated_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_job_postings_archived_at",
                table: "job_postings",
                column: "archived_at",
                filter: "archived_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_job_postings_company_position",
                table: "job_postings",
                columns: new[] { "company_name", "position_title" });

            migrationBuilder.CreateIndex(
                name: "ix_job_postings_expires_at",
                table: "job_postings",
                column: "expires_at",
                filter: "expires_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_job_postings_selection_verification_created_at",
                table: "job_postings",
                columns: new[] { "selection_status", "verification_status", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_raw_job_postings_company_title_fingerprint",
                table: "raw_job_postings",
                column: "company_title_fingerprint");

            migrationBuilder.CreateIndex(
                name: "ix_raw_job_postings_content_hash",
                table: "raw_job_postings",
                column: "content_hash");

            migrationBuilder.CreateIndex(
                name: "ix_raw_job_postings_ingestion_status_created_at",
                table: "raw_job_postings",
                columns: new[] { "ingestion_status", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_raw_job_postings_job_posting_id",
                table: "raw_job_postings",
                column: "job_posting_id");

            migrationBuilder.CreateIndex(
                name: "uq_raw_job_postings_source_external_id",
                table: "raw_job_postings",
                columns: new[] { "source", "source_external_id" },
                unique: true,
                filter: "source_external_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_raw_job_postings_source_url_hash",
                table: "raw_job_postings",
                columns: new[] { "source", "source_url_hash" },
                unique: true,
                filter: "source_url_hash IS NOT NULL");

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_job_postings_updated_at
                BEFORE UPDATE ON job_postings
                FOR EACH ROW EXECUTE FUNCTION set_updated_at();

                CREATE TRIGGER trg_job_applications_updated_at
                BEFORE UPDATE ON job_applications
                FOR EACH ROW EXECUTE FUNCTION set_updated_at();

                CREATE TRIGGER trg_raw_job_postings_updated_at
                BEFORE UPDATE ON raw_job_postings
                FOR EACH ROW EXECUTE FUNCTION set_updated_at();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_application_documents");

            migrationBuilder.DropTable(
                name: "job_application_events");

            migrationBuilder.DropTable(
                name: "raw_job_postings");

            migrationBuilder.DropTable(
                name: "job_applications");

            migrationBuilder.DropTable(
                name: "job_postings");
        }
    }
}
