using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgvector;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class KnowledgeChunkConfiguration : IEntityTypeConfiguration<KnowledgeChunk>
{
    public void Configure(EntityTypeBuilder<KnowledgeChunk> builder)
    {
        builder.ToTable("knowledge_chunks", table =>
        {
            table.HasCheckConstraint("ck_knowledge_chunks_index", "chunk_index >= 0");
            table.HasCheckConstraint("ck_knowledge_chunks_tokens", "token_count IS NULL OR token_count >= 0");
        });
        builder.HasKey(entity => entity.Id).HasName("knowledge_chunks_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.KnowledgeDocumentId).HasColumnName("knowledge_document_id").HasColumnType("uuid");
        builder.Property(entity => entity.ChunkIndex).HasColumnName("chunk_index");
        builder.Property(entity => entity.Content).HasColumnName("content").HasColumnType("text");
        builder.Property(entity => entity.TokenCount).HasColumnName("token_count");
        builder.Property(entity => entity.ContentHash).HasColumnName("content_hash").HasColumnType("character varying(128)").HasMaxLength(128);
        var embeddingComparer = new ValueComparer<float[]>(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
            value => value.ToArray());

        builder.Property(entity => entity.Embedding)
            .HasColumnName("embedding")
            .HasConversion(value => new Vector(value), value => value.ToArray())
            .Metadata.SetValueComparer(embeddingComparer);
        builder.Property(entity => entity.Embedding)
            .HasColumnType("vector(1536)");
        builder.Property(entity => entity.EmbeddingModel).HasColumnName("embedding_model").HasColumnType("character varying(150)").HasMaxLength(150);
        builder.Property(entity => entity.Metadata).HasColumnName("metadata").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.HasAlternateKey(entity => new { entity.KnowledgeDocumentId, entity.ChunkIndex }).HasName("uq_knowledge_chunks_index");
        builder.HasOne(entity => entity.KnowledgeDocument).WithMany().HasForeignKey(entity => entity.KnowledgeDocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(entity => new { entity.KnowledgeDocumentId, entity.ChunkIndex }).HasDatabaseName("ix_knowledge_chunks_document");
        builder.HasIndex(entity => entity.Embedding).HasDatabaseName("ix_knowledge_chunks_embedding_hnsw").HasMethod("hnsw").HasOperators("vector_cosine_ops");
    }
}
