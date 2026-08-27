using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("chat_messages", table =>
        {
            table.HasCheckConstraint("ck_chat_messages_role", "role IN ('USER', 'ASSISTANT', 'SYSTEM')");
            table.HasCheckConstraint("ck_chat_messages_prompt_tokens", "prompt_tokens IS NULL OR prompt_tokens >= 0");
            table.HasCheckConstraint("ck_chat_messages_completion_tokens", "completion_tokens IS NULL OR completion_tokens >= 0");
            table.HasCheckConstraint("ck_chat_messages_latency", "latency_ms IS NULL OR latency_ms >= 0");
        });
        builder.HasKey(entity => entity.Id).HasName("chat_messages_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.ChatSessionId).HasColumnName("chat_session_id").HasColumnType("uuid");
        builder.Property(entity => entity.Role).HasColumnName("role").HasColumnType("character varying(20)").HasMaxLength(20);
        builder.Property(entity => entity.Content).HasColumnName("content").HasColumnType("text");
        builder.Property(entity => entity.ModelName).HasColumnName("model_name").HasColumnType("character varying(150)").HasMaxLength(150);
        builder.Property(entity => entity.PromptTokens).HasColumnName("prompt_tokens");
        builder.Property(entity => entity.CompletionTokens).HasColumnName("completion_tokens");
        builder.Property(entity => entity.LatencyMs).HasColumnName("latency_ms");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.HasOne(entity => entity.ChatSession).WithMany().HasForeignKey(entity => entity.ChatSessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(entity => new { entity.ChatSessionId, entity.CreatedAt }).HasDatabaseName("ix_chat_messages_session_created");
    }
}
