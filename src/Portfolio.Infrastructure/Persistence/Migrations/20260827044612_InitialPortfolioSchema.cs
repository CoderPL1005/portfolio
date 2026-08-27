using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Portfolio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPortfolioSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pgcrypto", ",,")
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "admin_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    full_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("admin_users_pkey", x => x.id);
                    table.UniqueConstraint("uq_admin_users_email", x => x.email);
                });

            migrationBuilder.CreateTable(
                name: "agent_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "portfolio-agent"),
                    enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    model_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    embedding_provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    embedding_model = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    embedding_dimensions = table.Column<int>(type: "integer", nullable: false, defaultValue: 1536),
                    system_prompt = table.Column<string>(type: "text", nullable: false),
                    welcome_message = table.Column<string>(type: "text", nullable: true),
                    fallback_message = table.Column<string>(type: "text", nullable: true),
                    max_context_chunks = table.Column<int>(type: "integer", nullable: false, defaultValue: 6),
                    minimum_similarity = table.Column<decimal>(type: "numeric(6,5)", nullable: true),
                    temperature = table.Column<decimal>(type: "numeric(4,3)", nullable: false, defaultValue: 0.2m),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("agent_settings_pkey", x => x.id);
                    table.UniqueConstraint("uq_agent_settings_name", x => x.name);
                    table.CheckConstraint("ck_agent_context_chunks", "max_context_chunks BETWEEN 1 AND 20");
                    table.CheckConstraint("ck_agent_embedding_dimensions", "embedding_dimensions = 1536");
                    table.CheckConstraint("ck_agent_similarity", "minimum_similarity IS NULL OR minimum_similarity BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_agent_temperature", "temperature BETWEEN 0 AND 2");
                });

            migrationBuilder.CreateTable(
                name: "chat_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    public_session_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "ACTIVE"),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    message_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    metadata = table.Column<JsonDocument>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
                },
                constraints: table =>
                {
                    table.PrimaryKey("chat_sessions_pkey", x => x.id);
                    table.UniqueConstraint("uq_chat_sessions_public_session_id", x => x.public_session_id);
                    table.CheckConstraint("ck_chat_sessions_message_count", "message_count >= 0");
                    table.CheckConstraint("ck_chat_sessions_status", "status IN ('ACTIVE', 'CLOSED')");
                });

            migrationBuilder.CreateTable(
                name: "contact_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    message = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "NEW"),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("contact_messages_pkey", x => x.id);
                    table.CheckConstraint("ck_contact_messages_status", "status IN ('NEW', 'READ', 'REPLIED', 'ARCHIVED')");
                });

            migrationBuilder.CreateTable(
                name: "educations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    institution = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    degree = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    field_of_study = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    location = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_published = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("educations_pkey", x => x.id);
                    table.CheckConstraint("ck_educations_dates", "end_date IS NULL OR start_date IS NULL OR end_date >= start_date");
                });

            migrationBuilder.CreateTable(
                name: "experiences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    company_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    role_title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    location = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    is_current = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    summary = table.Column<string>(type: "text", nullable: true),
                    responsibilities_markdown = table.Column<string>(type: "text", nullable: true),
                    company_url = table.Column<string>(type: "text", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_published = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("experiences_pkey", x => x.id);
                    table.CheckConstraint("ck_experiences_current", "NOT is_current OR end_date IS NULL");
                    table.CheckConstraint("ck_experiences_dates", "end_date IS NULL OR end_date >= start_date");
                });

            migrationBuilder.CreateTable(
                name: "journey_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    subtitle = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    occurred_at = table.Column<DateOnly>(type: "date", nullable: true),
                    icon_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_published = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("journey_items_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    source_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_ref_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    metadata = table.Column<JsonDocument>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    indexing_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "PENDING"),
                    indexed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_index_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("knowledge_documents_pkey", x => x.id);
                    table.UniqueConstraint("uq_knowledge_documents_source_key", x => x.source_key);
                    table.CheckConstraint("ck_knowledge_documents_version", "version >= 1");
                    table.CheckConstraint("ck_knowledge_indexing_status", "indexing_status IN ('PENDING', 'INDEXING', 'INDEXED', 'FAILED')");
                    table.CheckConstraint("ck_knowledge_source_type", "source_type IN ('PROFILE', 'EXPERIENCE', 'PROJECT', 'SKILLS', 'EDUCATION', 'TRAINING', 'CERTIFICATE', 'JOURNEY', 'MANUAL')");
                });

            migrationBuilder.CreateTable(
                name: "media_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    storage_key = table.Column<string>(type: "text", nullable: false),
                    public_url = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    file_size = table.Column<long>(type: "bigint", nullable: true),
                    alt_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    media_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "IMAGE"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("media_assets_pkey", x => x.id);
                    table.UniqueConstraint("uq_media_assets_storage_key", x => x.storage_key);
                    table.CheckConstraint("ck_media_assets_file_size", "file_size IS NULL OR file_size >= 0");
                    table.CheckConstraint("ck_media_assets_type", "media_type IN ('IMAGE', 'DOCUMENT', 'CV', 'OTHER')");
                });

            migrationBuilder.CreateTable(
                name: "site_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    value = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("site_settings_pkey", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "social_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    platform = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    url = table.Column<string>(type: "text", nullable: false),
                    icon_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_visible = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("social_links_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "technologies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    icon_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    website_url = table.Column<string>(type: "text", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("technologies_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trainings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    provider = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    credential_url = table.Column<string>(type: "text", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_published = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("trainings_pkey", x => x.id);
                    table.CheckConstraint("ck_trainings_dates", "end_date IS NULL OR start_date IS NULL OR end_date >= start_date");
                });

            migrationBuilder.CreateTable(
                name: "admin_refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    admin_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("admin_refresh_tokens_pkey", x => x.id);
                    table.UniqueConstraint("uq_admin_refresh_tokens_hash", x => x.token_hash);
                    table.CheckConstraint("ck_admin_refresh_token_expiry", "expires_at > created_at");
                    table.ForeignKey(
                        name: "FK_admin_refresh_tokens_admin_refresh_tokens_replaced_by_token~",
                        column: x => x.replaced_by_token_id,
                        principalTable: "admin_refresh_tokens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_admin_refresh_tokens_admin_users_admin_user_id",
                        column: x => x.admin_user_id,
                        principalTable: "admin_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    chat_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    model_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: true),
                    completion_tokens = table.Column<int>(type: "integer", nullable: true),
                    latency_ms = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("chat_messages_pkey", x => x.id);
                    table.CheckConstraint("ck_chat_messages_completion_tokens", "completion_tokens IS NULL OR completion_tokens >= 0");
                    table.CheckConstraint("ck_chat_messages_latency", "latency_ms IS NULL OR latency_ms >= 0");
                    table.CheckConstraint("ck_chat_messages_prompt_tokens", "prompt_tokens IS NULL OR prompt_tokens >= 0");
                    table.CheckConstraint("ck_chat_messages_role", "role IN ('USER', 'ASSISTANT', 'SYSTEM')");
                    table.ForeignKey(
                        name: "FK_chat_messages_chat_sessions_chat_session_id",
                        column: x => x.chat_session_id,
                        principalTable: "chat_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_chunks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    knowledge_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    token_count = table.Column<int>(type: "integer", nullable: true),
                    content_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: false),
                    embedding_model = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    metadata = table.Column<JsonDocument>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("knowledge_chunks_pkey", x => x.id);
                    table.UniqueConstraint("uq_knowledge_chunks_index", x => new { x.knowledge_document_id, x.chunk_index });
                    table.CheckConstraint("ck_knowledge_chunks_index", "chunk_index >= 0");
                    table.CheckConstraint("ck_knowledge_chunks_tokens", "token_count IS NULL OR token_count >= 0");
                    table.ForeignKey(
                        name: "FK_knowledge_chunks_knowledge_documents_knowledge_document_id",
                        column: x => x.knowledge_document_id,
                        principalTable: "knowledge_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "certificates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    issuer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    issued_at = table.Column<DateOnly>(type: "date", nullable: true),
                    expires_at = table.Column<DateOnly>(type: "date", nullable: true),
                    credential_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    credential_url = table.Column<string>(type: "text", nullable: true),
                    certificate_media_id = table.Column<Guid>(type: "uuid", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_published = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("certificates_pkey", x => x.id);
                    table.CheckConstraint("ck_certificates_dates", "expires_at IS NULL OR issued_at IS NULL OR expires_at >= issued_at");
                    table.ForeignKey(
                        name: "FK_certificates_media_assets_certificate_media_id",
                        column: x => x.certificate_media_id,
                        principalTable: "media_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    singleton_key = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    full_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    professional_title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    secondary_title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    hero_headline = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    hero_summary = table.Column<string>(type: "text", nullable: true),
                    about_markdown = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    location = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    university = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    major = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    availability_status = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    profile_image_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cv_media_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_published = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("profiles_pkey", x => x.id);
                    table.UniqueConstraint("uq_profiles_singleton", x => x.singleton_key);
                    table.CheckConstraint("ck_profiles_singleton", "singleton_key = 1");
                    table.ForeignKey(
                        name: "FK_profiles_media_assets_cv_media_id",
                        column: x => x.cv_media_id,
                        principalTable: "media_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_profiles_media_assets_profile_image_id",
                        column: x => x.profile_image_id,
                        principalTable: "media_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    slug = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    subtitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    short_description = table.Column<string>(type: "text", nullable: true),
                    overview_markdown = table.Column<string>(type: "text", nullable: true),
                    role = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    team_size = table.Column<int>(type: "integer", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "COMPLETED"),
                    github_url = table.Column<string>(type: "text", nullable: true),
                    live_url = table.Column<string>(type: "text", nullable: true),
                    thumbnail_media_id = table.Column<Guid>(type: "uuid", nullable: true),
                    featured = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    seo_title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    seo_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("projects_pkey", x => x.id);
                    table.UniqueConstraint("uq_projects_slug", x => x.slug);
                    table.CheckConstraint("ck_projects_dates", "end_date IS NULL OR start_date IS NULL OR end_date >= start_date");
                    table.CheckConstraint("ck_projects_status", "status IN ('PLANNED', 'IN_PROGRESS', 'ACTIVE', 'COMPLETED', 'ARCHIVED')");
                    table.CheckConstraint("ck_projects_team_size", "team_size IS NULL OR team_size > 0");
                    table.ForeignKey(
                        name: "FK_projects_media_assets_thumbnail_media_id",
                        column: x => x.thumbnail_media_id,
                        principalTable: "media_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "experience_technologies",
                columns: table => new
                {
                    experience_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technology_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("experience_technologies_pkey", x => new { x.experience_id, x.technology_id });
                    table.ForeignKey(
                        name: "FK_experience_technologies_experiences_experience_id",
                        column: x => x.experience_id,
                        principalTable: "experiences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_experience_technologies_technologies_technology_id",
                        column: x => x.technology_id,
                        principalTable: "technologies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "skills",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    experience_level = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "USED"),
                    description = table.Column<string>(type: "text", nullable: true),
                    technology_id = table.Column<Guid>(type: "uuid", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_published = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("skills_pkey", x => x.id);
                    table.CheckConstraint("ck_skills_experience_level", "experience_level IN ('USED', 'LEARNING', 'EXPLORING')");
                    table.ForeignKey(
                        name: "FK_skills_technologies_technology_id",
                        column: x => x.technology_id,
                        principalTable: "technologies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "chat_message_feedback",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    chat_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    comment = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("chat_message_feedback_pkey", x => x.id);
                    table.UniqueConstraint("uq_chat_message_feedback_message", x => x.chat_message_id);
                    table.CheckConstraint("ck_chat_message_feedback_rating", "rating IN ('POSITIVE', 'NEGATIVE')");
                    table.ForeignKey(
                        name: "FK_chat_message_feedback_chat_messages_chat_message_id",
                        column: x => x.chat_message_id,
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_message_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    chat_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    knowledge_chunk_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    similarity_score = table.Column<decimal>(type: "numeric(8,7)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("chat_message_sources_pkey", x => x.id);
                    table.UniqueConstraint("uq_chat_message_sources", x => new { x.chat_message_id, x.knowledge_chunk_id });
                    table.CheckConstraint("ck_chat_message_sources_rank", "rank >= 1");
                    table.CheckConstraint("ck_chat_message_sources_similarity", "similarity_score IS NULL OR similarity_score BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "FK_chat_message_sources_chat_messages_chat_message_id",
                        column: x => x.chat_message_id,
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_chat_message_sources_knowledge_chunks_knowledge_chunk_id",
                        column: x => x.knowledge_chunk_id,
                        principalTable: "knowledge_chunks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "SCREENSHOT"),
                    caption = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("project_media_pkey", x => x.id);
                    table.UniqueConstraint("uq_project_media", x => new { x.project_id, x.media_asset_id, x.media_role });
                    table.CheckConstraint("ck_project_media_role", "media_role IN ('THUMBNAIL', 'SCREENSHOT', 'ARCHITECTURE', 'DIAGRAM', 'OTHER')");
                    table.ForeignKey(
                        name: "FK_project_media_media_assets_media_asset_id",
                        column: x => x.media_asset_id,
                        principalTable: "media_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_project_media_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_sections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    section_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    subtitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    content_markdown = table.Column<string>(type: "text", nullable: true),
                    content_json = table.Column<JsonDocument>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_visible = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("project_sections_pkey", x => x.id);
                    table.CheckConstraint("ck_project_sections_type", "section_type IN ('OVERVIEW', 'RESPONSIBILITIES', 'ARCHITECTURE', 'ENGINEERING_FOCUS', 'FEATURES', 'CHALLENGES', 'LEARNINGS', 'SCREENSHOTS', 'CUSTOM')");
                    table.ForeignKey(
                        name: "FK_project_sections_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_technologies",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technology_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("project_technologies_pkey", x => new { x.project_id, x.technology_id });
                    table.ForeignKey(
                        name: "FK_project_technologies_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_project_technologies_technologies_technology_id",
                        column: x => x.technology_id,
                        principalTable: "technologies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_admin_refresh_tokens_active",
                table: "admin_refresh_tokens",
                columns: new[] { "admin_user_id", "expires_at" },
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_admin_refresh_tokens_expires",
                table: "admin_refresh_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_admin_refresh_tokens_user",
                table: "admin_refresh_tokens",
                column: "admin_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_certificates_public_order",
                table: "certificates",
                columns: new[] { "is_published", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_message_sources_message_rank",
                table: "chat_message_sources",
                columns: new[] { "chat_message_id", "rank" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_session_created",
                table: "chat_messages",
                columns: new[] { "chat_session_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_sessions_status_last_message",
                table: "chat_sessions",
                columns: new[] { "status", "last_message_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_contact_messages_status_received",
                table: "contact_messages",
                columns: new[] { "status", "received_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_educations_public_order",
                table: "educations",
                columns: new[] { "is_published", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_experiences_public_order",
                table: "experiences",
                columns: new[] { "is_published", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_journey_items_public_order",
                table: "journey_items",
                columns: new[] { "is_published", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_chunks_document",
                table: "knowledge_chunks",
                columns: new[] { "knowledge_document_id", "chunk_index" });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_chunks_embedding_hnsw",
                table: "knowledge_chunks",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_documents_index_queue",
                table: "knowledge_documents",
                columns: new[] { "indexing_status", "updated_at" },
                filter: "is_active = TRUE AND indexing_status IN ('PENDING', 'FAILED')");

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_documents_source",
                table: "knowledge_documents",
                columns: new[] { "source_type", "source_ref_id" });

            migrationBuilder.CreateIndex(
                name: "ix_project_media_order",
                table: "project_media",
                columns: new[] { "project_id", "media_role", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_project_sections_project_order",
                table: "project_sections",
                columns: new[] { "project_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_project_technologies_order",
                table: "project_technologies",
                columns: new[] { "project_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_projects_public",
                table: "projects",
                columns: new[] { "is_published", "featured", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_skills_public_category",
                table: "skills",
                columns: new[] { "is_published", "category", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_social_links_visible_order",
                table: "social_links",
                columns: new[] { "is_visible", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_technologies_category",
                table: "technologies",
                columns: new[] { "category", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_trainings_public_order",
                table: "trainings",
                columns: new[] { "is_published", "display_order" });

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX uq_admin_users_email_ci ON admin_users (LOWER(email));
                CREATE UNIQUE INDEX uq_technologies_name_ci ON technologies (LOWER(name));
                CREATE UNIQUE INDEX uq_projects_slug_ci ON projects (LOWER(slug));
                CREATE UNIQUE INDEX uq_skills_name_category_ci ON skills (LOWER(name), LOWER(category));
                CREATE UNIQUE INDEX uq_social_links_platform_ci ON social_links (LOWER(platform));
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION set_updated_at()
                RETURNS TRIGGER AS $$
                BEGIN
                    NEW.updated_at = NOW();
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_admin_users_updated_at BEFORE UPDATE ON admin_users FOR EACH ROW EXECUTE FUNCTION set_updated_at();
                CREATE TRIGGER trg_media_assets_updated_at BEFORE UPDATE ON media_assets FOR EACH ROW EXECUTE FUNCTION set_updated_at();
                CREATE TRIGGER trg_profiles_updated_at BEFORE UPDATE ON profiles FOR EACH ROW EXECUTE FUNCTION set_updated_at();
                CREATE TRIGGER trg_experiences_updated_at BEFORE UPDATE ON experiences FOR EACH ROW EXECUTE FUNCTION set_updated_at();
                CREATE TRIGGER trg_technologies_updated_at BEFORE UPDATE ON technologies FOR EACH ROW EXECUTE FUNCTION set_updated_at();
                CREATE TRIGGER trg_projects_updated_at BEFORE UPDATE ON projects FOR EACH ROW EXECUTE FUNCTION set_updated_at();
                CREATE TRIGGER trg_project_sections_updated_at BEFORE UPDATE ON project_sections FOR EACH ROW EXECUTE FUNCTION set_updated_at();
                CREATE TRIGGER trg_skills_updated_at BEFORE UPDATE ON skills FOR EACH ROW EXECUTE FUNCTION set_updated_at();
                CREATE TRIGGER trg_educations_updated_at BEFORE UPDATE ON educations FOR EACH ROW EXECUTE FUNCTION set_updated_at();
                CREATE TRIGGER trg_trainings_updated_at BEFORE UPDATE ON trainings FOR EACH ROW EXECUTE FUNCTION set_updated_at();
                CREATE TRIGGER trg_certificates_updated_at BEFORE UPDATE ON certificates FOR EACH ROW EXECUTE FUNCTION set_updated_at();
                CREATE TRIGGER trg_journey_items_updated_at BEFORE UPDATE ON journey_items FOR EACH ROW EXECUTE FUNCTION set_updated_at();
                CREATE TRIGGER trg_social_links_updated_at BEFORE UPDATE ON social_links FOR EACH ROW EXECUTE FUNCTION set_updated_at();
                CREATE TRIGGER trg_site_settings_updated_at BEFORE UPDATE ON site_settings FOR EACH ROW EXECUTE FUNCTION set_updated_at();
                CREATE TRIGGER trg_agent_settings_updated_at BEFORE UPDATE ON agent_settings FOR EACH ROW EXECUTE FUNCTION set_updated_at();
                CREATE TRIGGER trg_knowledge_documents_updated_at BEFORE UPDATE ON knowledge_documents FOR EACH ROW EXECUTE FUNCTION set_updated_at();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_refresh_tokens");

            migrationBuilder.DropTable(
                name: "agent_settings");

            migrationBuilder.DropTable(
                name: "certificates");

            migrationBuilder.DropTable(
                name: "chat_message_feedback");

            migrationBuilder.DropTable(
                name: "chat_message_sources");

            migrationBuilder.DropTable(
                name: "contact_messages");

            migrationBuilder.DropTable(
                name: "educations");

            migrationBuilder.DropTable(
                name: "experience_technologies");

            migrationBuilder.DropTable(
                name: "journey_items");

            migrationBuilder.DropTable(
                name: "profiles");

            migrationBuilder.DropTable(
                name: "project_media");

            migrationBuilder.DropTable(
                name: "project_sections");

            migrationBuilder.DropTable(
                name: "project_technologies");

            migrationBuilder.DropTable(
                name: "site_settings");

            migrationBuilder.DropTable(
                name: "skills");

            migrationBuilder.DropTable(
                name: "social_links");

            migrationBuilder.DropTable(
                name: "trainings");

            migrationBuilder.DropTable(
                name: "admin_users");

            migrationBuilder.DropTable(
                name: "chat_messages");

            migrationBuilder.DropTable(
                name: "knowledge_chunks");

            migrationBuilder.DropTable(
                name: "experiences");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropTable(
                name: "technologies");

            migrationBuilder.DropTable(
                name: "chat_sessions");

            migrationBuilder.DropTable(
                name: "knowledge_documents");

            migrationBuilder.DropTable(
                name: "media_assets");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS set_updated_at();");
        }
    }
}
