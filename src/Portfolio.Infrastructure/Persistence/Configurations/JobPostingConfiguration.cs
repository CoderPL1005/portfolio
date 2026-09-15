using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class JobPostingConfiguration : IEntityTypeConfiguration<JobPosting>
{
    public void Configure(EntityTypeBuilder<JobPosting> builder)
    {
        builder.ToTable("job_postings", table =>
        {
            table.HasTrigger("trg_job_postings_updated_at");
            table.HasCheckConstraint("ck_job_postings_verification_status", "verification_status IN ('PENDING', 'VERIFIED', 'UNVERIFIED', 'LIKELY_EXPIRED')");
            table.HasCheckConstraint("ck_job_postings_selection_status", "selection_status IN ('PENDING_ANALYSIS', 'RECOMMENDED', 'APPROVED', 'SKIPPED')");
            table.HasCheckConstraint("ck_job_postings_salary_minimum", "salary_minimum IS NULL OR salary_minimum >= 0");
            table.HasCheckConstraint("ck_job_postings_salary_maximum", "salary_maximum IS NULL OR salary_maximum >= 0");
            table.HasCheckConstraint("ck_job_postings_salary_range", "salary_minimum IS NULL OR salary_maximum IS NULL OR salary_maximum >= salary_minimum");
            table.HasCheckConstraint("ck_job_postings_salary_currency", "salary_currency IS NULL OR salary_currency ~ '^[A-Z]{3}$'");
            table.HasCheckConstraint("ck_job_postings_technology_stack", "jsonb_typeof(technology_stack) = 'array'");
            table.HasCheckConstraint("ck_job_postings_version", "version >= 1");
        });

        builder.HasKey(entity => entity.Id).HasName("job_postings_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.CompanyName).HasColumnName("company_name").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.PositionTitle).HasColumnName("position_title").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.Location).HasColumnName("location").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.EmploymentType).HasColumnName("employment_type").HasColumnType("character varying(50)").HasMaxLength(50);
        builder.Property(entity => entity.WorkplaceType).HasColumnName("workplace_type").HasColumnType("character varying(50)").HasMaxLength(50);
        builder.Property(entity => entity.SalaryMinimum).HasColumnName("salary_minimum").HasColumnType("numeric(18,2)");
        builder.Property(entity => entity.SalaryMaximum).HasColumnName("salary_maximum").HasColumnType("numeric(18,2)");
        builder.Property(entity => entity.SalaryCurrency).HasColumnName("salary_currency").HasColumnType("character varying(3)").HasMaxLength(3);
        builder.Property(entity => entity.SalaryPeriod).HasColumnName("salary_period").HasColumnType("character varying(30)").HasMaxLength(30);
        builder.Property(entity => entity.ExperienceRequirements).HasColumnName("experience_requirements").HasColumnType("text");
        builder.Property(entity => entity.Description).HasColumnName("description").HasColumnType("text");
        builder.Property(entity => entity.TechnologyStack).HasColumnName("technology_stack").HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
        builder.Property(entity => entity.ApplicationEmail).HasColumnName("application_email").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.ApplicationUrl).HasColumnName("application_url").HasColumnType("text");
        builder.Property(entity => entity.VerificationStatus).HasColumnName("verification_status").HasColumnType("character varying(30)").HasMaxLength(30).HasDefaultValue("PENDING");
        builder.Property(entity => entity.SelectionStatus).HasColumnName("selection_status").HasColumnType("character varying(30)").HasMaxLength(30).HasDefaultValue("PENDING_ANALYSIS");
        builder.Property(entity => entity.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.VerifiedAt).HasColumnName("verified_at").HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.ArchivedAt).HasColumnName("archived_at").HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.Notes).HasColumnName("notes").HasColumnType("text");
        builder.Property(entity => entity.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");

        builder.HasIndex(entity => new { entity.SelectionStatus, entity.VerificationStatus, entity.CreatedAt })
            .HasDatabaseName("ix_job_postings_selection_verification_created_at")
            .IsDescending(false, false, true);
        builder.HasIndex(entity => new { entity.CompanyName, entity.PositionTitle }).HasDatabaseName("ix_job_postings_company_position");
        builder.HasIndex(entity => entity.ExpiresAt).HasDatabaseName("ix_job_postings_expires_at").HasFilter("expires_at IS NOT NULL");
        builder.HasIndex(entity => entity.ArchivedAt).HasDatabaseName("ix_job_postings_archived_at").HasFilter("archived_at IS NOT NULL");
    }
}
