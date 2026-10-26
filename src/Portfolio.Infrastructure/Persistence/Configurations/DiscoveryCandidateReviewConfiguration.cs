using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class DiscoveryCandidateReviewConfiguration : IEntityTypeConfiguration<DiscoveryCandidateReview>
{
    internal const string Labels = "(status = 'UNCERTAIN' AND content_status IS NULL AND target_status IS NULL) OR (status IN ('CONFIRMED','CORRECTED') AND content_status IS NOT NULL AND target_status IS NOT NULL AND content_status IN ('VALID_RECRUITMENT','INVALID_RECRUITMENT','NEEDS_REVIEW') AND target_status IN ('TARGET_MATCH','FALLBACK_MATCH','POTENTIAL_MATCH','NOT_MATCHED','INSUFFICIENT_DATA'))";
    public void Configure(EntityTypeBuilder<DiscoveryCandidateReview> b)
    {
        b.ToTable("discovery_candidate_reviews", t =>
        {
            t.HasCheckConstraint("ck_discovery_review_version", "version >= 1");
            t.HasCheckConstraint("ck_discovery_review_labels", Labels);
            t.HasCheckConstraint("ck_discovery_review_notes", "length(btrim(notes)) BETWEEN 1 AND 2000");
        });
        b.HasKey(x => x.CandidateId).HasName("discovery_candidate_reviews_pkey");
        b.Property(x => x.CandidateId).HasColumnName("candidate_id").ValueGeneratedNever();
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20);
        b.Property(x => x.ContentStatus).HasColumnName("content_status").HasMaxLength(30);
        b.Property(x => x.TargetStatus).HasColumnName("target_status").HasMaxLength(30);
        b.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        b.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
        b.Property(x => x.ReviewedByAdminId).HasColumnName("reviewed_by_admin_id");
        b.HasOne<DiscoveryCandidate>().WithOne().HasForeignKey<DiscoveryCandidateReview>(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AdminUser>().WithMany().HasForeignKey(x => x.ReviewedByAdminId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Status, x.ReviewedAt });
    }
}
