using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivateCanonicalCv : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "canonical_cvs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    singleton_key = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    storage_key = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("canonical_cvs_pkey", x => x.id);
                    table.CheckConstraint("ck_canonical_cvs_content_hash", "content_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_canonical_cvs_content_type", "content_type = 'application/pdf'");
                    table.CheckConstraint("ck_canonical_cvs_file_size", "file_size_bytes > 0 AND file_size_bytes <= 10485760");
                    table.CheckConstraint("ck_canonical_cvs_singleton", "singleton_key = 1");
                    table.CheckConstraint("ck_canonical_cvs_version", "version >= 1");
                });

            migrationBuilder.CreateIndex(
                name: "uq_canonical_cvs_singleton",
                table: "canonical_cvs",
                column: "singleton_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_canonical_cvs_storage_key",
                table: "canonical_cvs",
                column: "storage_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "canonical_cvs");
        }
    }
}
