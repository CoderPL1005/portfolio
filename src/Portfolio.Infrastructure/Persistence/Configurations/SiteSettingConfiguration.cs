using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class SiteSettingConfiguration : IEntityTypeConfiguration<SiteSetting>
{
    public void Configure(EntityTypeBuilder<SiteSetting> builder)
    {
        builder.ToTable("site_settings", table => table.HasTrigger("trg_site_settings_updated_at"));
        builder.HasKey(entity => entity.Key).HasName("site_settings_pkey");
        builder.Property(entity => entity.Key).HasColumnName("key").HasColumnType("character varying(150)").HasMaxLength(150);
        builder.Property(entity => entity.Value).HasColumnName("value").HasColumnType("jsonb");
        builder.Property(entity => entity.Description).HasColumnName("description").HasColumnType("character varying(500)").HasMaxLength(500);
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
    }
}
