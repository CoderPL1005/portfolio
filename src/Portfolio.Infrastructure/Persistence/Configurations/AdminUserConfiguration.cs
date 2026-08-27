using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class AdminUserConfiguration : IEntityTypeConfiguration<AdminUser>
{
    public void Configure(EntityTypeBuilder<AdminUser> builder)
    {
        builder.ToTable("admin_users", table => table.HasTrigger("trg_admin_users_updated_at"));
        builder.HasKey(entity => entity.Id).HasName("admin_users_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.Email).HasColumnName("email").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.PasswordHash).HasColumnName("password_hash").HasColumnType("text");
        builder.Property(entity => entity.FullName).HasColumnName("full_name").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(entity => entity.LastLoginAt).HasColumnName("last_login_at").HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.HasAlternateKey(entity => entity.Email).HasName("uq_admin_users_email");
    }
}
