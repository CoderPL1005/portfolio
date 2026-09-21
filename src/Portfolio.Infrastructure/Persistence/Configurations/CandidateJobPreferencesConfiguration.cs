using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class CandidateJobPreferencesConfiguration : IEntityTypeConfiguration<CandidateJobPreferences>
{
    public void Configure(EntityTypeBuilder<CandidateJobPreferences> builder)
    {
        builder.ToTable("candidate_job_preferences", table =>
        {
            table.HasCheckConstraint("ck_candidate_job_preferences_singleton", "singleton_key = 'CURRENT'");
            table.HasCheckConstraint("ck_candidate_job_preferences_target_roles", "jsonb_typeof(target_roles) = 'array'");
            table.HasCheckConstraint("ck_candidate_job_preferences_preferred_technologies", "jsonb_typeof(preferred_technologies) = 'array'");
            table.HasCheckConstraint("ck_candidate_job_preferences_acceptable_locations", "jsonb_typeof(acceptable_locations) = 'array'");
            table.HasCheckConstraint("ck_candidate_job_preferences_workplace_types", "jsonb_typeof(workplace_types) = 'array'");
            table.HasCheckConstraint("ck_candidate_job_preferences_employment_types", "jsonb_typeof(employment_types) = 'array'");
            table.HasCheckConstraint("ck_candidate_job_preferences_minimum_salary", "minimum_salary IS NULL OR minimum_salary >= 0");
            table.HasCheckConstraint("ck_candidate_job_preferences_salary_consistency", "(minimum_salary IS NULL AND salary_currency IS NULL AND salary_period IS NULL) OR (minimum_salary IS NOT NULL AND salary_currency IS NOT NULL AND salary_period IS NOT NULL)");
            table.HasCheckConstraint("ck_candidate_job_preferences_salary_currency", "salary_currency IS NULL OR salary_currency ~ '^[A-Z]{3}$'");
            table.HasCheckConstraint("ck_candidate_job_preferences_version", "version >= 1");
        });
        builder.HasKey(x => x.Id).HasName("candidate_job_preferences_pkey");
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.SingletonKey).HasColumnName("singleton_key").HasColumnType("character varying(20)").HasMaxLength(20);
        builder.HasIndex(x => x.SingletonKey).IsUnique().HasDatabaseName("uq_candidate_job_preferences_singleton_key");
        builder.Property(x => x.TargetRoles).HasColumnName("target_roles").HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
        builder.Property(x => x.PreferredTechnologies).HasColumnName("preferred_technologies").HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
        builder.Property(x => x.AcceptableLocations).HasColumnName("acceptable_locations").HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
        builder.Property(x => x.WorkplaceTypes).HasColumnName("workplace_types").HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
        builder.Property(x => x.EmploymentTypes).HasColumnName("employment_types").HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
        builder.Property(x => x.MinimumSalary).HasColumnName("minimum_salary").HasColumnType("numeric(18,2)");
        builder.Property(x => x.SalaryCurrency).HasColumnName("salary_currency").HasColumnType("character varying(3)").HasMaxLength(3);
        builder.Property(x => x.SalaryPeriod).HasColumnName("salary_period").HasColumnType("character varying(30)").HasMaxLength(30);
        builder.Property(x => x.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
    }
}
