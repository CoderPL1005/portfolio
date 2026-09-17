using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.ToTable("push_subscriptions", table =>
        {
            table.HasCheckConstraint("ck_push_subscriptions_endpoint", "length(endpoint) > 0");
            table.HasCheckConstraint("ck_push_subscriptions_p256dh", "length(p256dh) > 0");
            table.HasCheckConstraint("ck_push_subscriptions_auth", "length(auth) > 0");
        });

        builder.HasKey(entity => entity.Id).HasName("push_subscriptions_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.Endpoint).HasColumnName("endpoint").HasColumnType("character varying(2048)").HasMaxLength(2048);
        builder.Property(entity => entity.P256dh).HasColumnName("p256dh").HasColumnType("character varying(512)").HasMaxLength(512);
        builder.Property(entity => entity.Auth).HasColumnName("auth").HasColumnType("character varying(512)").HasMaxLength(512);
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.LastUsedAt).HasColumnName("last_used_at").HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.IsActive).HasColumnName("is_active").HasColumnType("boolean").HasDefaultValue(true);

        builder.HasIndex(entity => entity.Endpoint)
            .HasDatabaseName("uq_push_subscriptions_endpoint")
            .IsUnique();
        builder.HasIndex(entity => new { entity.IsActive, entity.CreatedAt, entity.Id })
            .HasDatabaseName("ix_push_subscriptions_active")
            .HasFilter("is_active = TRUE");
    }
}
