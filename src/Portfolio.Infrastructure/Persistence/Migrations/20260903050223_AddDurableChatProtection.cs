using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableChatProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "user_message_count",
                table: "chat_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "chat_usage_daily",
                columns: table => new
                {
                    usage_date = table.Column<DateOnly>(type: "date", nullable: false),
                    visitor_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    accepted_message_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("chat_usage_daily_pkey", x => new { x.usage_date, x.visitor_key });
                    table.CheckConstraint("ck_chat_usage_daily_count", "accepted_message_count >= 0");
                });

            migrationBuilder.Sql("""
                UPDATE chat_sessions AS session
                SET user_message_count = (
                    SELECT COUNT(*)::integer
                    FROM chat_messages AS message
                    WHERE message.chat_session_id = session.id
                      AND message.role = 'USER'
                );
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_chat_sessions_user_message_count",
                table: "chat_sessions",
                sql: "user_message_count >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_usage_daily");

            migrationBuilder.DropCheckConstraint(
                name: "ck_chat_sessions_user_message_count",
                table: "chat_sessions");

            migrationBuilder.DropColumn(
                name: "user_message_count",
                table: "chat_sessions");
        }
    }
}
