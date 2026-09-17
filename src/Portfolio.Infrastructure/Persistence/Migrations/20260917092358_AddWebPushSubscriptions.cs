using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebPushSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "push_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    endpoint = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    p256dh = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    auth = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("push_subscriptions_pkey", x => x.id);
                    table.CheckConstraint("ck_push_subscriptions_auth", "length(auth) > 0");
                    table.CheckConstraint("ck_push_subscriptions_endpoint", "length(endpoint) > 0");
                    table.CheckConstraint("ck_push_subscriptions_p256dh", "length(p256dh) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_push_subscriptions_active",
                table: "push_subscriptions",
                columns: new[] { "is_active", "created_at", "id" },
                filter: "is_active = TRUE");

            migrationBuilder.CreateIndex(
                name: "uq_push_subscriptions_endpoint",
                table: "push_subscriptions",
                column: "endpoint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "push_subscriptions");
        }
    }
}
