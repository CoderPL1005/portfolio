using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinalizeApplicationPackage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_job_application_events_event_type",
                table: "job_application_events");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "package_finalized_at",
                table: "job_applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "package_finalized_by_admin_user_id",
                table: "job_applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "package_job_posting_version",
                table: "job_applications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "package_manifest_hash",
                table: "job_applications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "package_revision",
                table: "job_applications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "package_status",
                table: "job_applications",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "DRAFT");

            migrationBuilder.AddColumn<string>(
                name: "content_type",
                table: "job_application_documents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "file_size_bytes",
                table: "job_application_documents",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "package_revision",
                table: "job_application_documents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_canonical_cv_version",
                table: "job_application_documents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_job_applications_package_revision",
                table: "job_applications",
                sql: "package_revision IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_job_applications_package_state",
                table: "job_applications",
                sql: "(package_status = 'DRAFT' AND package_revision = 0 AND package_job_posting_version IS NULL AND package_manifest_hash IS NULL AND package_finalized_at IS NULL AND package_finalized_by_admin_user_id IS NULL) OR (package_status = 'FINALIZED' AND package_revision = 1 AND package_job_posting_version IS NOT NULL AND package_manifest_hash ~ '^[0-9a-f]{64}$' AND package_finalized_at IS NOT NULL AND package_finalized_by_admin_user_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_job_applications_package_status",
                table: "job_applications",
                sql: "package_status IN ('DRAFT', 'FINALIZED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_job_application_events_event_type",
                table: "job_application_events",
                sql: "event_type IN ('CREATED', 'STATUS_CHANGED', 'NOTE_ADDED', 'DOCUMENT_ATTACHED', 'DOCUMENT_REMOVED', 'PACKAGE_FINALIZED')");

            migrationBuilder.CreateIndex(
                name: "uq_job_application_documents_managed_cv_revision",
                table: "job_application_documents",
                columns: new[] { "job_application_id", "package_revision" },
                unique: true,
                filter: "document_type = 'CV' AND package_revision IS NOT NULL AND removed_at IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_job_application_documents_managed_snapshot",
                table: "job_application_documents",
                sql: "package_revision IS NULL OR (document_type = 'CV' AND storage_key IS NOT NULL AND file_name IS NOT NULL AND content_type = 'application/pdf' AND file_size_bytes > 0 AND file_size_bytes <= 10485760 AND content_hash IS NOT NULL AND source_canonical_cv_version >= 1 AND removed_at IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_job_application_documents_package_revision",
                table: "job_application_documents",
                sql: "package_revision IS NULL OR package_revision = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_job_applications_admin_users_package_finalized_by_admin_use~",
                table: "job_applications",
                column: "package_finalized_by_admin_user_id",
                principalTable: "admin_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_job_applications_admin_users_package_finalized_by_admin_use~",
                table: "job_applications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_job_applications_package_revision",
                table: "job_applications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_job_applications_package_state",
                table: "job_applications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_job_applications_package_status",
                table: "job_applications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_job_application_events_event_type",
                table: "job_application_events");

            migrationBuilder.DropIndex(
                name: "uq_job_application_documents_managed_cv_revision",
                table: "job_application_documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_job_application_documents_managed_snapshot",
                table: "job_application_documents");

            migrationBuilder.DropCheckConstraint(
                name: "ck_job_application_documents_package_revision",
                table: "job_application_documents");

            migrationBuilder.DropColumn(
                name: "package_finalized_at",
                table: "job_applications");

            migrationBuilder.DropColumn(
                name: "package_finalized_by_admin_user_id",
                table: "job_applications");

            migrationBuilder.DropColumn(
                name: "package_job_posting_version",
                table: "job_applications");

            migrationBuilder.DropColumn(
                name: "package_manifest_hash",
                table: "job_applications");

            migrationBuilder.DropColumn(
                name: "package_revision",
                table: "job_applications");

            migrationBuilder.DropColumn(
                name: "package_status",
                table: "job_applications");

            migrationBuilder.DropColumn(
                name: "content_type",
                table: "job_application_documents");

            migrationBuilder.DropColumn(
                name: "file_size_bytes",
                table: "job_application_documents");

            migrationBuilder.DropColumn(
                name: "package_revision",
                table: "job_application_documents");

            migrationBuilder.DropColumn(
                name: "source_canonical_cv_version",
                table: "job_application_documents");

            migrationBuilder.AddCheckConstraint(
                name: "ck_job_application_events_event_type",
                table: "job_application_events",
                sql: "event_type IN ('CREATED', 'STATUS_CHANGED', 'NOTE_ADDED', 'DOCUMENT_ATTACHED', 'DOCUMENT_REMOVED')");
        }
    }
}
