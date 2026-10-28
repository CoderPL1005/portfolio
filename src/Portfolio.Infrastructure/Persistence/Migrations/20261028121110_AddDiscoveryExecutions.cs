using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscoveryExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "discovery_executions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    TriggerSource = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ActiveKey = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ConfigurationJson = table.Column<string>(type: "text", nullable: false),
                    WorkflowRunId = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ResultHash = table.Column<string>(type: "text", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SemanticAttempts = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discovery_executions", x => x.Id);
                    table.CheckConstraint("ck_discovery_execution_trigger", "\"TriggerSource\" IN ('ADMIN_MANUAL','DAILY_SCHEDULED')");
                    table.CheckConstraint("ck_discovery_execution_version", "\"Version\" >= 1");
                });

            migrationBuilder.CreateTable(
                name: "discovery_execution_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowRunId = table.Column<string>(type: "text", nullable: false),
                    Outcome = table.Column<string>(type: "text", nullable: false),
                    ActualRequests = table.Column<int>(type: "integer", nullable: true),
                    CreditsDeducted = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    CreditsUsedAlternateHeader = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discovery_execution_attempts", x => x.Id);
                    table.CheckConstraint("ck_discovery_execution_credit_evidence", "\"CreditsDeducted\" IS NULL OR \"CreditsDeducted\" >= 0");
                    table.CheckConstraint("ck_discovery_execution_one_request", "\"ActualRequests\" IS NULL OR \"ActualRequests\" BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "FK_discovery_execution_attempts_discovery_executions_Execution~",
                        column: x => x.ExecutionId,
                        principalTable: "discovery_executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "discovery_execution_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discovery_execution_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_discovery_execution_events_discovery_executions_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "discovery_executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "discovery_execution_observations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemIndex = table.Column<int>(type: "integer", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    PayloadHash = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "text", nullable: true),
                    RawJobPostingId = table.Column<Guid>(type: "uuid", nullable: true),
                    SemanticState = table.Column<string>(type: "text", nullable: false),
                    SemanticCacheKey = table.Column<string>(type: "text", nullable: true),
                    SemanticOutputJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discovery_execution_observations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_discovery_execution_observations_discovery_executions_Execu~",
                        column: x => x.ExecutionId,
                        principalTable: "discovery_executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_discovery_execution_observations_raw_job_postings_RawJobPos~",
                        column: x => x.RawJobPostingId,
                        principalTable: "raw_job_postings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "discovery_execution_vacancies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ObservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    VacancyIndex = table.Column<int>(type: "integer", nullable: false),
                    RawJobPostingId = table.Column<Guid>(type: "uuid", nullable: true),
                    JobPostingId = table.Column<Guid>(type: "uuid", nullable: true),
                    Fingerprint = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    FieldsJson = table.Column<string>(type: "text", nullable: false),
                    MissingFieldsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discovery_execution_vacancies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_discovery_execution_vacancies_discovery_execution_observati~",
                        column: x => x.ObservationId,
                        principalTable: "discovery_execution_observations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_discovery_execution_vacancies_job_postings_JobPostingId",
                        column: x => x.JobPostingId,
                        principalTable: "job_postings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_discovery_execution_vacancies_raw_job_postings_RawJobPostin~",
                        column: x => x.RawJobPostingId,
                        principalTable: "raw_job_postings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_discovery_execution_attempts_ExecutionId",
                table: "discovery_execution_attempts",
                column: "ExecutionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_discovery_execution_observations_ContentHash",
                table: "discovery_execution_observations",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_discovery_execution_observations_ExecutionId_ItemIndex",
                table: "discovery_execution_observations",
                columns: new[] { "ExecutionId", "ItemIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_discovery_execution_vacancies_Fingerprint",
                table: "discovery_execution_vacancies",
                column: "Fingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_discovery_execution_vacancies_ObservationId_VacancyIndex",
                table: "discovery_execution_vacancies",
                columns: new[] { "ObservationId", "VacancyIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_discovery_executions_ActiveKey",
                table: "discovery_executions",
                column: "ActiveKey",
                unique: true,
                filter: "\"ActiveKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_discovery_executions_ClientRequestId",
                table: "discovery_executions",
                column: "ClientRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_discovery_executions_CreatedAt",
                table: "discovery_executions",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "discovery_execution_attempts");

            migrationBuilder.DropTable(
                name: "discovery_execution_events");

            migrationBuilder.DropTable(
                name: "discovery_execution_vacancies");

            migrationBuilder.DropTable(
                name: "discovery_execution_observations");

            migrationBuilder.DropTable(
                name: "discovery_executions");
        }
    }
}
