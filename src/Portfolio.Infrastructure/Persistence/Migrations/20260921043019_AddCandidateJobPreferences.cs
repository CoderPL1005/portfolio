using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidateJobPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "candidate_job_preferences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    singleton_key = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_roles = table.Column<JsonDocument>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    preferred_technologies = table.Column<JsonDocument>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    acceptable_locations = table.Column<JsonDocument>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    workplace_types = table.Column<JsonDocument>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    employment_types = table.Column<JsonDocument>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    minimum_salary = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    salary_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    salary_period = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("candidate_job_preferences_pkey", x => x.id);
                    table.CheckConstraint("ck_candidate_job_preferences_acceptable_locations", "jsonb_typeof(acceptable_locations) = 'array'");
                    table.CheckConstraint("ck_candidate_job_preferences_employment_types", "jsonb_typeof(employment_types) = 'array'");
                    table.CheckConstraint("ck_candidate_job_preferences_minimum_salary", "minimum_salary IS NULL OR minimum_salary >= 0");
                    table.CheckConstraint("ck_candidate_job_preferences_preferred_technologies", "jsonb_typeof(preferred_technologies) = 'array'");
                    table.CheckConstraint("ck_candidate_job_preferences_salary_consistency", "(minimum_salary IS NULL AND salary_currency IS NULL AND salary_period IS NULL) OR (minimum_salary IS NOT NULL AND salary_currency IS NOT NULL AND salary_period IS NOT NULL)");
                    table.CheckConstraint("ck_candidate_job_preferences_salary_currency", "salary_currency IS NULL OR salary_currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_candidate_job_preferences_singleton", "singleton_key = 'CURRENT'");
                    table.CheckConstraint("ck_candidate_job_preferences_target_roles", "jsonb_typeof(target_roles) = 'array'");
                    table.CheckConstraint("ck_candidate_job_preferences_version", "version >= 1");
                    table.CheckConstraint("ck_candidate_job_preferences_workplace_types", "jsonb_typeof(workplace_types) = 'array'");
                });

            migrationBuilder.CreateIndex(
                name: "uq_candidate_job_preferences_singleton_key",
                table: "candidate_job_preferences",
                column: "singleton_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "candidate_job_preferences");
        }
    }
}
