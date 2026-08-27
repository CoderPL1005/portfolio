using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class JourneyItemConfiguration : IEntityTypeConfiguration<JourneyItem>
{
    public void Configure(EntityTypeBuilder<JourneyItem> builder)
    {
        builder.ToTable("journey_items", table => table.HasTrigger("trg_journey_items_updated_at"));
        builder.HasKey(entity => entity.Id).HasName("journey_items_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.Title).HasColumnName("title").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.Subtitle).HasColumnName("subtitle").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.Description).HasColumnName("description").HasColumnType("text");
        builder.Property(entity => entity.OccurredAt).HasColumnName("occurred_at").HasColumnType("date");
        builder.Property(entity => entity.IconKey).HasColumnName("icon_key").HasColumnType("character varying(100)").HasMaxLength(100);
        builder.Property(entity => entity.DisplayOrder).HasColumnName("display_order").HasDefaultValue(0);
        builder.Property(entity => entity.IsPublished).HasColumnName("is_published").HasDefaultValue(true);
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.HasIndex(entity => new { entity.IsPublished, entity.DisplayOrder }).HasDatabaseName("ix_journey_items_public_order");
    }
}
