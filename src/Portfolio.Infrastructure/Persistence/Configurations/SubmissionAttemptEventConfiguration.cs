using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class SubmissionAttemptEventConfiguration : IEntityTypeConfiguration<SubmissionAttemptEvent>
{
    public void Configure(EntityTypeBuilder<SubmissionAttemptEvent> builder)
    {
        builder.ToTable("submission_attempt_events", table =>
        {
            table.HasCheckConstraint("ck_submission_attempt_events_from_status", "from_status IS NULL OR from_status IN ('CREATED', 'APPROVED', 'SUBMITTING', 'SUCCEEDED', 'FAILED', 'UNKNOWN')");
            table.HasCheckConstraint("ck_submission_attempt_events_to_status", "to_status IN ('CREATED', 'APPROVED', 'SUBMITTING', 'SUCCEEDED', 'FAILED', 'UNKNOWN')");
        });
        builder.HasKey(entity => entity.Id).HasName("submission_attempt_events_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.SubmissionAttemptId).HasColumnName("submission_attempt_id").HasColumnType("uuid");
        builder.Property(entity => entity.FromStatus).HasColumnName("from_status").HasColumnType("character varying(30)").HasMaxLength(30);
        builder.Property(entity => entity.ToStatus).HasColumnName("to_status").HasColumnType("character varying(30)").HasMaxLength(30);
        builder.Property(entity => entity.ActorAdminUserId).HasColumnName("actor_admin_user_id").HasColumnType("uuid");
        builder.Property(entity => entity.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.HasOne(entity => entity.SubmissionAttempt).WithMany(entity => entity.Events)
            .HasForeignKey(entity => entity.SubmissionAttemptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.ActorAdminUser).WithMany()
            .HasForeignKey(entity => entity.ActorAdminUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entity => new { entity.SubmissionAttemptId, entity.OccurredAt, entity.Id })
            .HasDatabaseName("ix_submission_attempt_events_attempt_occurred_id");
    }
}
