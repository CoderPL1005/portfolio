using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GeneralizeRawJobPostingAttachmentsForPwa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_raw_job_posting_attachments_delivery",
                table: "raw_job_posting_attachments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_raw_job_posting_attachments_message_id",
                table: "raw_job_posting_attachments");

            migrationBuilder.AlterColumn<long>(
                name: "telegram_message_id",
                table: "raw_job_posting_attachments",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "telegram_file_unique_id",
                table: "raw_job_posting_attachments",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);

            migrationBuilder.AlterColumn<string>(
                name: "telegram_file_id",
                table: "raw_job_posting_attachments",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);

            migrationBuilder.CreateIndex(
                name: "uq_raw_job_posting_attachments_delivery",
                table: "raw_job_posting_attachments",
                columns: new[] { "raw_job_posting_id", "telegram_message_id" },
                unique: true,
                filter: "telegram_message_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_raw_job_posting_attachments_message_id",
                table: "raw_job_posting_attachments",
                sql: "telegram_message_id IS NULL OR telegram_message_id > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_raw_job_posting_attachments_delivery",
                table: "raw_job_posting_attachments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_raw_job_posting_attachments_message_id",
                table: "raw_job_posting_attachments");

            migrationBuilder.AlterColumn<long>(
                name: "telegram_message_id",
                table: "raw_job_posting_attachments",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "telegram_file_unique_id",
                table: "raw_job_posting_attachments",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "telegram_file_id",
                table: "raw_job_posting_attachments",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_raw_job_posting_attachments_delivery",
                table: "raw_job_posting_attachments",
                columns: new[] { "raw_job_posting_id", "telegram_message_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_raw_job_posting_attachments_message_id",
                table: "raw_job_posting_attachments",
                sql: "telegram_message_id > 0");
        }
    }
}
