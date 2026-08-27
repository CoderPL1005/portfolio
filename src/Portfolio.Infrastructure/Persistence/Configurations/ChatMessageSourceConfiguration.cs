using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class ChatMessageSourceConfiguration : IEntityTypeConfiguration<ChatMessageSource>
{
    public void Configure(EntityTypeBuilder<ChatMessageSource> builder)
    {
        builder.ToTable("chat_message_sources", table =>
        {
            table.HasCheckConstraint("ck_chat_message_sources_rank", "rank >= 1");
            table.HasCheckConstraint("ck_chat_message_sources_similarity", "similarity_score IS NULL OR similarity_score BETWEEN 0 AND 1");
        });
        builder.HasKey(entity => entity.Id).HasName("chat_message_sources_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.ChatMessageId).HasColumnName("chat_message_id").HasColumnType("uuid");
        builder.Property(entity => entity.KnowledgeChunkId).HasColumnName("knowledge_chunk_id").HasColumnType("uuid");
        builder.Property(entity => entity.Rank).HasColumnName("rank");
        builder.Property(entity => entity.SimilarityScore).HasColumnName("similarity_score").HasColumnType("numeric(8,7)");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.HasAlternateKey(entity => new { entity.ChatMessageId, entity.KnowledgeChunkId }).HasName("uq_chat_message_sources");
        builder.HasOne(entity => entity.ChatMessage).WithMany().HasForeignKey(entity => entity.ChatMessageId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(entity => entity.KnowledgeChunk).WithMany().HasForeignKey(entity => entity.KnowledgeChunkId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(entity => new { entity.ChatMessageId, entity.Rank }).HasDatabaseName("ix_chat_message_sources_message_rank");
    }
}
