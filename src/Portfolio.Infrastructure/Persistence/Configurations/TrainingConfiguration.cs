using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class TrainingConfiguration : IEntityTypeConfiguration<Training>
{
    public void Configure(EntityTypeBuilder<Training> builder)
    {
        builder.ToTable("trainings", table =>
        {
            table.HasTrigger("trg_trainings_updated_at");
            table.HasCheckConstraint("ck_trainings_dates", "end_date IS NULL OR start_date IS NULL OR end_date >= start_date");
        });
        builder.HasKey(entity => entity.Id).HasName("trainings_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.Title).HasColumnName("title").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.Provider).HasColumnName("provider").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.Description).HasColumnName("description").HasColumnType("text");
        builder.Property(entity => entity.StartDate).HasColumnName("start_date").HasColumnType("date");
        builder.Property(entity => entity.EndDate).HasColumnName("end_date").HasColumnType("date");
        builder.Property(entity => entity.CredentialUrl).HasColumnName("credential_url").HasColumnType("text");
        builder.Property(entity => entity.DisplayOrder).HasColumnName("display_order").HasDefaultValue(0);
        builder.Property(entity => entity.IsPublished).HasColumnName("is_published").HasDefaultValue(true);
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.HasIndex(entity => new { entity.IsPublished, entity.DisplayOrder }).HasDatabaseName("ix_trainings_public_order");
    }
}
