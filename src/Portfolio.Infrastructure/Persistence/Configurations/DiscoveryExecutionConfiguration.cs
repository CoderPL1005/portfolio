using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class DiscoveryExecutionConfiguration : IEntityTypeConfiguration<DiscoveryExecution>,
    IEntityTypeConfiguration<DiscoveryExecutionAttempt>, IEntityTypeConfiguration<DiscoveryExecutionObservation>,
    IEntityTypeConfiguration<DiscoveryExecutionVacancy>, IEntityTypeConfiguration<DiscoveryExecutionEvent>
{
    public void Configure(EntityTypeBuilder<DiscoveryExecution> b)
    {
        b.ToTable("discovery_executions",t=>{
            t.HasCheckConstraint("ck_discovery_execution_trigger","\"TriggerSource\" IN ('ADMIN_MANUAL','DAILY_SCHEDULED')");
            t.HasCheckConstraint("ck_discovery_execution_version","\"Version\" >= 1");
        });
        b.HasKey(x=>x.Id); b.Property(x=>x.Version).IsConcurrencyToken();
        b.Property(x=>x.State).HasMaxLength(40); b.Property(x=>x.ErrorCode).HasMaxLength(100);
        b.Property(x=>x.ActiveKey).HasMaxLength(20); b.Property(x=>x.WorkflowRunId).HasMaxLength(30);
        b.HasIndex(x=>x.ClientRequestId).IsUnique();
        b.HasIndex(x=>x.ActiveKey).IsUnique().HasFilter("\"ActiveKey\" IS NOT NULL");
        b.HasIndex(x=>x.CreatedAt);
    }
    public void Configure(EntityTypeBuilder<DiscoveryExecutionAttempt> b)
    {
        b.ToTable("discovery_execution_attempts",t=>{
            t.HasCheckConstraint("ck_discovery_execution_one_request","\"ActualRequests\" IS NULL OR \"ActualRequests\" BETWEEN 0 AND 1");
            t.HasCheckConstraint("ck_discovery_execution_credit_evidence","\"CreditsDeducted\" IS NULL OR \"CreditsDeducted\" >= 0");
        });
        b.HasKey(x=>x.Id); b.HasIndex(x=>x.ExecutionId).IsUnique();
        b.HasOne<DiscoveryExecution>().WithMany().HasForeignKey(x=>x.ExecutionId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x=>x.CreditsDeducted).HasPrecision(18,4); b.Property(x=>x.CreditsUsedAlternateHeader).HasPrecision(18,4);
    }
    public void Configure(EntityTypeBuilder<DiscoveryExecutionObservation> b)
    {
        b.ToTable("discovery_execution_observations"); b.HasKey(x=>x.Id);
        b.HasIndex(x=>new{x.ExecutionId,x.ItemIndex}).IsUnique(); b.HasIndex(x=>x.ContentHash);
        b.HasOne<DiscoveryExecution>().WithMany().HasForeignKey(x=>x.ExecutionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<RawJobPosting>().WithMany().HasForeignKey(x=>x.RawJobPostingId).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<DiscoveryExecutionVacancy> b)
    {
        b.ToTable("discovery_execution_vacancies"); b.HasKey(x=>x.Id);
        b.HasIndex(x=>new{x.ObservationId,x.VacancyIndex}).IsUnique(); b.HasIndex(x=>x.Fingerprint);
        b.HasOne<DiscoveryExecutionObservation>().WithMany().HasForeignKey(x=>x.ObservationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<RawJobPosting>().WithMany().HasForeignKey(x=>x.RawJobPostingId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<JobPosting>().WithMany().HasForeignKey(x=>x.JobPostingId).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<DiscoveryExecutionEvent> b)
    {
        b.ToTable("discovery_execution_events"); b.HasKey(x=>x.Id);
        b.HasIndex(x=>new{x.ExecutionId,x.State}).IsUnique().HasFilter("\"State\" IN ('RECOVERY_RECEIPT','RECOVERY_AUDIT')").HasDatabaseName("uq_discovery_recovery_event");
        b.HasOne<DiscoveryExecution>().WithMany().HasForeignKey(x=>x.ExecutionId).OnDelete(DeleteBehavior.Restrict);
    }
}
