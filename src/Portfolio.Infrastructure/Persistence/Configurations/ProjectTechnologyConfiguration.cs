using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class ProjectTechnologyConfiguration : IEntityTypeConfiguration<ProjectTechnology>
{
    public void Configure(EntityTypeBuilder<ProjectTechnology> builder)
    {
        builder.ToTable("project_technologies");
        builder.HasKey(entity => new { entity.ProjectId, entity.TechnologyId }).HasName("project_technologies_pkey");
        builder.Property(entity => entity.ProjectId).HasColumnName("project_id").HasColumnType("uuid");
        builder.Property(entity => entity.TechnologyId).HasColumnName("technology_id").HasColumnType("uuid");
        builder.Property(entity => entity.DisplayOrder).HasColumnName("display_order").HasDefaultValue(0);
        builder.HasOne(entity => entity.Project).WithMany().HasForeignKey(entity => entity.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(entity => entity.Technology).WithMany().HasForeignKey(entity => entity.TechnologyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(entity => new { entity.ProjectId, entity.DisplayOrder }).HasDatabaseName("ix_project_technologies_order");
    }
}
