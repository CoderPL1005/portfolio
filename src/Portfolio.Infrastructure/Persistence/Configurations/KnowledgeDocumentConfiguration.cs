using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocument> builder)
    {
        builder.ToTable("knowledge_documents", table =>
        {
            table.HasTrigger("trg_knowledge_documents_updated_at");
            table.HasCheckConstraint("ck_knowledge_documents_version", "version >= 1");
            table.HasCheckConstraint("ck_knowledge_source_type", "source_type IN ('PROFILE', 'EXPERIENCE', 'PROJECT', 'SKILLS', 'EDUCATION', 'TRAINING', 'CERTIFICATE', 'JOURNEY', 'MANUAL')");
            table.HasCheckConstraint("ck_knowledge_indexing_status", "indexing_status IN ('PENDING', 'INDEXING', 'INDEXED', 'FAILED')");
        });
        builder.HasKey(entity => entity.Id).HasName("knowledge_documents_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.SourceType).HasColumnName("source_type").HasColumnType("character varying(50)").HasMaxLength(50);
        builder.Property(entity => entity.SourceRefId).HasColumnName("source_ref_id").HasColumnType("uuid");
        builder.Property(entity => entity.SourceKey).HasColumnName("source_key").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.Title).HasColumnName("title").HasColumnType("character varying(500)").HasMaxLength(500);
        builder.Property(entity => entity.Content).HasColumnName("content").HasColumnType("text");
        builder.Property(entity => entity.ContentHash).HasColumnName("content_hash").HasColumnType("character varying(128)").HasMaxLength(128);
        builder.Property(entity => entity.SourceUpdatedAt).HasColumnName("source_updated_at").HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.Version).HasColumnName("version").HasDefaultValue(1);
        builder.Property(entity => entity.Metadata).HasColumnName("metadata").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(entity => entity.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(entity => entity.IndexingStatus).HasColumnName("indexing_status").HasColumnType("character varying(30)").HasMaxLength(30).HasDefaultValue("PENDING");
        builder.Property(entity => entity.IndexedAt).HasColumnName("indexed_at").HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.LastIndexError).HasColumnName("last_index_error").HasColumnType("text");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.HasAlternateKey(entity => entity.SourceKey).HasName("uq_knowledge_documents_source_key");
        builder.HasIndex(entity => new { entity.SourceType, entity.SourceRefId }).HasDatabaseName("ix_knowledge_documents_source");
        builder.HasIndex(entity => new { entity.IndexingStatus, entity.UpdatedAt }).HasDatabaseName("ix_knowledge_documents_index_queue").HasFilter("is_active = TRUE AND indexing_status IN ('PENDING', 'FAILED')");
    }
}
