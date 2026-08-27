using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class ExperienceConfiguration : IEntityTypeConfiguration<Experience>
{
    public void Configure(EntityTypeBuilder<Experience> builder)
    {
        builder.ToTable("experiences", table =>
        {
            table.HasTrigger("trg_experiences_updated_at");
            table.HasCheckConstraint("ck_experiences_dates", "end_date IS NULL OR end_date >= start_date");
            table.HasCheckConstraint("ck_experiences_current", "NOT is_current OR end_date IS NULL");
        });
        builder.HasKey(entity => entity.Id).HasName("experiences_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.CompanyName).HasColumnName("company_name").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.RoleTitle).HasColumnName("role_title").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.Location).HasColumnName("location").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.StartDate).HasColumnName("start_date").HasColumnType("date");
        builder.Property(entity => entity.EndDate).HasColumnName("end_date").HasColumnType("date");
        builder.Property(entity => entity.IsCurrent).HasColumnName("is_current").HasDefaultValue(false);
        builder.Property(entity => entity.Summary).HasColumnName("summary").HasColumnType("text");
        builder.Property(entity => entity.ResponsibilitiesMarkdown).HasColumnName("responsibilities_markdown").HasColumnType("text");
        builder.Property(entity => entity.CompanyUrl).HasColumnName("company_url").HasColumnType("text");
        builder.Property(entity => entity.DisplayOrder).HasColumnName("display_order").HasDefaultValue(0);
        builder.Property(entity => entity.IsPublished).HasColumnName("is_published").HasDefaultValue(true);
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.HasIndex(entity => new { entity.IsPublished, entity.DisplayOrder }).HasDatabaseName("ix_experiences_public_order");
    }
}
