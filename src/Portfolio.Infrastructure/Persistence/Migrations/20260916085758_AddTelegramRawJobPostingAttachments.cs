using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramRawJobPostingAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "raw_job_posting_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    raw_job_posting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attachment_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sort_order = table.Column<long>(type: "bigint", nullable: false),
                    telegram_message_id = table.Column<long>(type: "bigint", nullable: false),
                    telegram_file_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    telegram_file_unique_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("raw_job_posting_attachments_pkey", x => x.id);
                    table.CheckConstraint("ck_raw_job_posting_attachments_content_hash", "content_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_raw_job_posting_attachments_dimensions", "width > 0 AND height > 0");
                    table.CheckConstraint("ck_raw_job_posting_attachments_file_size", "file_size_bytes > 0");
                    table.CheckConstraint("ck_raw_job_posting_attachments_message_id", "telegram_message_id > 0");
                    table.CheckConstraint("ck_raw_job_posting_attachments_sort_order", "sort_order > 0");
                    table.CheckConstraint("ck_raw_job_posting_attachments_type", "attachment_type = 'IMAGE'");
                    table.ForeignKey(
                        name: "fk_raw_job_posting_attachments_raw_job_postings",
                        column: x => x.raw_job_posting_id,
                        principalTable: "raw_job_postings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_raw_job_posting_attachments_content_hash",
                table: "raw_job_posting_attachments",
                column: "content_hash");

            migrationBuilder.CreateIndex(
                name: "ix_raw_job_posting_attachments_order",
                table: "raw_job_posting_attachments",
                columns: new[] { "raw_job_posting_id", "sort_order", "id" });

            migrationBuilder.CreateIndex(
                name: "uq_raw_job_posting_attachments_delivery",
                table: "raw_job_posting_attachments",
                columns: new[] { "raw_job_posting_id", "telegram_message_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "raw_job_posting_attachments");
        }
    }
}
