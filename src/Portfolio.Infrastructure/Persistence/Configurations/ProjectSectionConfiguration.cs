using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class ProjectSectionConfiguration : IEntityTypeConfiguration<ProjectSection>
{
    public void Configure(EntityTypeBuilder<ProjectSection> builder)
    {
        builder.ToTable("project_sections", table =>
        {
            table.HasTrigger("trg_project_sections_updated_at");
            table.HasCheckConstraint("ck_project_sections_type", "section_type IN ('OVERVIEW', 'RESPONSIBILITIES', 'ARCHITECTURE', 'ENGINEERING_FOCUS', 'FEATURES', 'CHALLENGES', 'LEARNINGS', 'SCREENSHOTS', 'CUSTOM')");
        });
        builder.HasKey(entity => entity.Id).HasName("project_sections_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.ProjectId).HasColumnName("project_id").HasColumnType("uuid");
        builder.Property(entity => entity.SectionType).HasColumnName("section_type").HasColumnType("character varying(50)").HasMaxLength(50);
        builder.Property(entity => entity.Title).HasColumnName("title").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.Subtitle).HasColumnName("subtitle").HasColumnType("character varying(500)").HasMaxLength(500);
        builder.Property(entity => entity.ContentMarkdown).HasColumnName("content_markdown").HasColumnType("text");
        builder.Property(entity => entity.ContentJson).HasColumnName("content_json").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(entity => entity.DisplayOrder).HasColumnName("display_order").HasDefaultValue(0);
        builder.Property(entity => entity.IsVisible).HasColumnName("is_visible").HasDefaultValue(true);
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.HasOne(entity => entity.Project).WithMany().HasForeignKey(entity => entity.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(entity => new { entity.ProjectId, entity.DisplayOrder }).HasDatabaseName("ix_project_sections_project_order");
    }
}
