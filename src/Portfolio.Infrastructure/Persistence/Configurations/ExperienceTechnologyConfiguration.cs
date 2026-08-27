using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class ExperienceTechnologyConfiguration : IEntityTypeConfiguration<ExperienceTechnology>
{
    public void Configure(EntityTypeBuilder<ExperienceTechnology> builder)
    {
        builder.ToTable("experience_technologies");
        builder.HasKey(entity => new { entity.ExperienceId, entity.TechnologyId }).HasName("experience_technologies_pkey");
        builder.Property(entity => entity.ExperienceId).HasColumnName("experience_id").HasColumnType("uuid");
        builder.Property(entity => entity.TechnologyId).HasColumnName("technology_id").HasColumnType("uuid");
        builder.Property(entity => entity.DisplayOrder).HasColumnName("display_order").HasDefaultValue(0);
        builder.HasOne(entity => entity.Experience).WithMany().HasForeignKey(entity => entity.ExperienceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(entity => entity.Technology).WithMany().HasForeignKey(entity => entity.TechnologyId).OnDelete(DeleteBehavior.Cascade);
    }
}
