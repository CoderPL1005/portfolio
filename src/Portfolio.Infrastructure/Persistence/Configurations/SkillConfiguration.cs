using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class SkillConfiguration : IEntityTypeConfiguration<Skill>
{
    public void Configure(EntityTypeBuilder<Skill> builder)
    {
        builder.ToTable("skills", table =>
        {
            table.HasTrigger("trg_skills_updated_at");
            table.HasCheckConstraint("ck_skills_experience_level", "experience_level IN ('USED', 'LEARNING', 'EXPLORING')");
        });
        builder.HasKey(entity => entity.Id).HasName("skills_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.Name).HasColumnName("name").HasColumnType("character varying(100)").HasMaxLength(100);
        builder.Property(entity => entity.Category).HasColumnName("category").HasColumnType("character varying(50)").HasMaxLength(50);
        builder.Property(entity => entity.ExperienceLevel).HasColumnName("experience_level").HasColumnType("character varying(30)").HasMaxLength(30).HasDefaultValue("USED");
        builder.Property(entity => entity.Description).HasColumnName("description").HasColumnType("text");
        builder.Property(entity => entity.TechnologyId).HasColumnName("technology_id").HasColumnType("uuid");
        builder.Property(entity => entity.DisplayOrder).HasColumnName("display_order").HasDefaultValue(0);
        builder.Property(entity => entity.IsPublished).HasColumnName("is_published").HasDefaultValue(true);
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.HasOne(entity => entity.Technology).WithMany().HasForeignKey(entity => entity.TechnologyId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(entity => new { entity.IsPublished, entity.Category, entity.DisplayOrder }).HasDatabaseName("ix_skills_public_category");
    }
}
