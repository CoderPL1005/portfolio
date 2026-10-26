using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class DiscoveryCandidateReviewEventConfiguration : IEntityTypeConfiguration<DiscoveryCandidateReviewEvent>
{
    public void Configure(EntityTypeBuilder<DiscoveryCandidateReviewEvent> b)
    {
        b.ToTable("discovery_candidate_review_events", t =>
        {
            t.HasCheckConstraint("ck_discovery_review_event_version", "version >= 1");
            t.HasCheckConstraint("ck_discovery_review_event_labels", DiscoveryCandidateReviewConfiguration.Labels);
            t.HasCheckConstraint("ck_discovery_review_event_notes", "length(btrim(notes)) BETWEEN 1 AND 2000");
            t.HasCheckConstraint("ck_discovery_review_event_hash", "observation_payload_hash ~ '^[0-9a-f]{64}$'");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.CandidateId).HasColumnName("candidate_id");
        b.Property(x => x.Version).HasColumnName("version");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20);
        b.Property(x => x.ContentStatus).HasColumnName("content_status").HasMaxLength(30);
        b.Property(x => x.TargetStatus).HasColumnName("target_status").HasMaxLength(30);
        b.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        b.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
        b.Property(x => x.ReviewedByAdminId).HasColumnName("reviewed_by_admin_id");
        b.Property(x => x.ObservationPayloadHash).HasColumnName("observation_payload_hash").HasMaxLength(64);
        b.HasOne<DiscoveryCandidateReview>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AdminUser>().WithMany().HasForeignKey(x => x.ReviewedByAdminId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CandidateId, x.Version }).IsUnique().HasDatabaseName("uq_discovery_review_event_version");
    }
}
