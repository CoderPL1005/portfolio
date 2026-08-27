using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class ChatSessionConfiguration : IEntityTypeConfiguration<ChatSession>
{
    public void Configure(EntityTypeBuilder<ChatSession> builder)
    {
        builder.ToTable("chat_sessions", table =>
        {
            table.HasCheckConstraint("ck_chat_sessions_status", "status IN ('ACTIVE', 'CLOSED')");
            table.HasCheckConstraint("ck_chat_sessions_message_count", "message_count >= 0");
        });
        builder.HasKey(entity => entity.Id).HasName("chat_sessions_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.PublicSessionId).HasColumnName("public_session_id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.Status).HasColumnName("status").HasColumnType("character varying(20)").HasMaxLength(20).HasDefaultValue("ACTIVE");
        builder.Property(entity => entity.StartedAt).HasColumnName("started_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.LastMessageAt).HasColumnName("last_message_at").HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.ClosedAt).HasColumnName("closed_at").HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.MessageCount).HasColumnName("message_count").HasDefaultValue(0);
        builder.Property(entity => entity.Metadata).HasColumnName("metadata").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.HasAlternateKey(entity => entity.PublicSessionId).HasName("uq_chat_sessions_public_session_id");
        builder.HasIndex(entity => new { entity.Status, entity.LastMessageAt }).HasDatabaseName("ix_chat_sessions_status_last_message").IsDescending(false, true);
    }
}
