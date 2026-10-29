using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class DiscoveryCandidateConfiguration : IEntityTypeConfiguration<DiscoveryCandidate>
{
    public void Configure(EntityTypeBuilder<DiscoveryCandidate> b)
    {
        b.ToTable("discovery_candidates", t =>
        {
            t.HasCheckConstraint("ck_discovery_candidates_coordinates", "page_number BETWEEN 1 AND 6 AND item_index BETWEEN 0 AND 199");
            t.HasCheckConstraint("ck_discovery_candidates_content", "content_status IN ('VALID_RECRUITMENT','INVALID_RECRUITMENT','NEEDS_REVIEW')");
            t.HasCheckConstraint("ck_discovery_candidates_target", "target_status IN ('TARGET_MATCH','FALLBACK_MATCH','POTENTIAL_MATCH','NOT_MATCHED','INSUFFICIENT_DATA')");
            t.HasCheckConstraint("ck_discovery_candidates_hash", "payload_hash ~ '^[0-9a-f]{64}$' AND (content_group_hash IS NULL OR content_group_hash ~ '^[0-9a-f]{64}$')");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.RunId).HasColumnName("run_id").HasMaxLength(100);
        b.Property(x => x.Query).HasColumnName("query").HasMaxLength(300);
        b.Property(x => x.CandidateOrigin).HasColumnName("candidate_origin").HasMaxLength(40).HasDefaultValue("CLASSIFIER");
        b.Property(x => x.PageNumber).HasColumnName("page_number");
        b.Property(x => x.ItemIndex).HasColumnName("item_index");
        b.Property(x => x.ContentStatus).HasColumnName("content_status").HasMaxLength(30);
        b.Property(x => x.TargetStatus).HasColumnName("target_status").HasMaxLength(30);
        b.Property(x => x.ContentGroupHash).HasColumnName("content_group_hash").HasMaxLength(64);
        b.Property(x => x.IdentityWarning).HasColumnName("identity_warning");
        b.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64);
        b.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");
        b.Property(x => x.IngestedAt).HasColumnName("ingested_at");
        b.HasIndex(x => new { x.RunId, x.PageNumber, x.ItemIndex }).IsUnique().HasDatabaseName("uq_discovery_candidates_ingestion");
        b.HasIndex(x => x.ContentGroupHash);
        b.HasIndex(x => new { x.IngestedAt, x.Id });
    }
}
