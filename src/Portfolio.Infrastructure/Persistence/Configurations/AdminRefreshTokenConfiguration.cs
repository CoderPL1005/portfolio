using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class AdminRefreshTokenConfiguration : IEntityTypeConfiguration<AdminRefreshToken>
{
    public void Configure(EntityTypeBuilder<AdminRefreshToken> builder)
    {
        builder.ToTable("admin_refresh_tokens", table =>
            table.HasCheckConstraint("ck_admin_refresh_token_expiry", "expires_at > created_at"));
        builder.HasKey(entity => entity.Id).HasName("admin_refresh_tokens_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.AdminUserId).HasColumnName("admin_user_id").HasColumnType("uuid");
        builder.Property(entity => entity.TokenHash).HasColumnName("token_hash").HasColumnType("text");
        builder.Property(entity => entity.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.ReplacedByTokenId).HasColumnName("replaced_by_token_id").HasColumnType("uuid");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.HasAlternateKey(entity => entity.TokenHash).HasName("uq_admin_refresh_tokens_hash");
        builder.HasOne(entity => entity.AdminUser).WithMany().HasForeignKey(entity => entity.AdminUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(entity => entity.ReplacedByToken).WithMany().HasForeignKey(entity => entity.ReplacedByTokenId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(entity => entity.AdminUserId).HasDatabaseName("ix_admin_refresh_tokens_user");
        builder.HasIndex(entity => entity.ExpiresAt).HasDatabaseName("ix_admin_refresh_tokens_expires");
        builder.HasIndex(entity => new { entity.AdminUserId, entity.ExpiresAt }).HasDatabaseName("ix_admin_refresh_tokens_active").HasFilter("revoked_at IS NULL");
    }
}
