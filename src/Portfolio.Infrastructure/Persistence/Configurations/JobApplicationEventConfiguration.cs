using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class JobApplicationEventConfiguration : IEntityTypeConfiguration<JobApplicationEvent>
{
    public void Configure(EntityTypeBuilder<JobApplicationEvent> builder)
    {
        builder.ToTable("job_application_events", table =>
        {
            table.HasCheckConstraint("ck_job_application_events_event_type", "event_type IN ('CREATED', 'STATUS_CHANGED', 'NOTE_ADDED', 'DOCUMENT_ATTACHED', 'DOCUMENT_REMOVED')");
            table.HasCheckConstraint("ck_job_application_events_from_status", "from_status IS NULL OR from_status IN ('DRAFT', 'APPLIED', 'INTERVIEW', 'REJECTED', 'OFFER', 'WITHDRAWN')");
            table.HasCheckConstraint("ck_job_application_events_to_status", "to_status IS NULL OR to_status IN ('DRAFT', 'APPLIED', 'INTERVIEW', 'REJECTED', 'OFFER', 'WITHDRAWN')");
            table.HasCheckConstraint("ck_job_application_events_actor_type", "actor_type IN ('ADMIN', 'SYSTEM', 'TELEGRAM', 'EMAIL_CONNECTOR')");
        });

        builder.HasKey(entity => entity.Id).HasName("job_application_events_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.JobApplicationId).HasColumnName("job_application_id").HasColumnType("uuid");
        builder.Property(entity => entity.EventType).HasColumnName("event_type").HasColumnType("character varying(50)").HasMaxLength(50);
        builder.Property(entity => entity.FromStatus).HasColumnName("from_status").HasColumnType("character varying(30)").HasMaxLength(30);
        builder.Property(entity => entity.ToStatus).HasColumnName("to_status").HasColumnType("character varying(30)").HasMaxLength(30);
        builder.Property(entity => entity.ActorType).HasColumnName("actor_type").HasColumnType("character varying(30)").HasMaxLength(30);
        builder.Property(entity => entity.ActorAdminUserId).HasColumnName("actor_admin_user_id").HasColumnType("uuid");
        builder.Property(entity => entity.Note).HasColumnName("note").HasColumnType("text");
        builder.Property(entity => entity.Metadata).HasColumnName("metadata").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(entity => entity.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");

        builder.HasOne(entity => entity.JobApplication)
            .WithMany(entity => entity.Events)
            .HasForeignKey(entity => entity.JobApplicationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.ActorAdminUser)
            .WithMany()
            .HasForeignKey(entity => entity.ActorAdminUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(entity => new { entity.JobApplicationId, entity.OccurredAt, entity.Id })
            .HasDatabaseName("ix_job_application_events_application_occurred_id");
    }
}
