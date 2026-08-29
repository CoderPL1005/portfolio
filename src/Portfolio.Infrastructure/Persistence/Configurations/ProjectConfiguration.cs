using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects", table =>
        {
            table.HasTrigger("trg_projects_updated_at");
            table.HasCheckConstraint("ck_projects_team_size", "team_size IS NULL OR team_size > 0");
            table.HasCheckConstraint("ck_projects_status", "status IN ('PLANNED', 'IN_PROGRESS', 'ACTIVE', 'COMPLETED', 'ARCHIVED')");
            table.HasCheckConstraint("ck_projects_dates", "end_date IS NULL OR start_date IS NULL OR end_date >= start_date");
        });
        builder.HasKey(entity => entity.Id).HasName("projects_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.Slug).HasColumnName("slug").HasColumnType("character varying(180)").HasMaxLength(180);
        builder.Property(entity => entity.Title).HasColumnName("title").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.Subtitle).HasColumnName("subtitle").HasColumnType("character varying(500)").HasMaxLength(500);
        builder.Property(entity => entity.ShortDescription).HasColumnName("short_description").HasColumnType("text");
        builder.Property(entity => entity.OverviewMarkdown).HasColumnName("overview_markdown").HasColumnType("text");
        builder.Property(entity => entity.Role).HasColumnName("role").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.TeamSize).HasColumnName("team_size");
        builder.Property(entity => entity.StartDate).HasColumnName("start_date").HasColumnType("date");
        builder.Property(entity => entity.EndDate).HasColumnName("end_date").HasColumnType("date");
        builder.Property(entity => entity.Status).HasColumnName("status").HasColumnType("character varying(50)").HasMaxLength(50).HasDefaultValue("COMPLETED");
        builder.Property(entity => entity.GithubUrl).HasColumnName("github_url").HasColumnType("text");
        builder.Property(entity => entity.LiveUrl).HasColumnName("live_url").HasColumnType("text");
        builder.Property(entity => entity.ThumbnailMediaId).HasColumnName("thumbnail_media_id").HasColumnType("uuid");
        builder.Property(entity => entity.Featured).HasColumnName("featured").HasDefaultValue(false);
        builder.Property(entity => entity.IsPublished).HasColumnName("is_published").HasDefaultValue(true);
        builder.Property(entity => entity.DisplayOrder).HasColumnName("display_order").HasDefaultValue(0);
        builder.Property(entity => entity.SeoTitle).HasColumnName("seo_title").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.SeoDescription).HasColumnName("seo_description").HasColumnType("character varying(500)").HasMaxLength(500);
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.HasOne(entity => entity.ThumbnailMedia).WithMany().HasForeignKey(entity => entity.ThumbnailMediaId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(entity => new { entity.IsPublished, entity.Featured, entity.DisplayOrder }).HasDatabaseName("ix_projects_public");
    }
}
