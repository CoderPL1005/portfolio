using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRawJobPostingIngestionKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ingestion_key",
                table: "raw_job_postings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_raw_job_postings_ingestion_key",
                table: "raw_job_postings",
                column: "ingestion_key",
                unique: true,
                filter: "ingestion_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_raw_job_postings_ingestion_key",
                table: "raw_job_postings");

            migrationBuilder.DropColumn(
                name: "ingestion_key",
                table: "raw_job_postings");
        }
    }
}
