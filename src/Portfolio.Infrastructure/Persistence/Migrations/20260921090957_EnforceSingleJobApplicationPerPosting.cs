using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleJobApplicationPerPosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_job_applications_job_posting_id",
                table: "job_applications");

            migrationBuilder.CreateIndex(
                name: "ix_job_applications_job_posting_id",
                table: "job_applications",
                column: "job_posting_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_job_applications_job_posting_id",
                table: "job_applications");

            migrationBuilder.CreateIndex(
                name: "ix_job_applications_job_posting_id",
                table: "job_applications",
                column: "job_posting_id");
        }
    }
}
