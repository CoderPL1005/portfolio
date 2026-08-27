using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class CertificateConfiguration : IEntityTypeConfiguration<Certificate>
{
    public void Configure(EntityTypeBuilder<Certificate> builder)
    {
        builder.ToTable("certificates", table =>
        {
            table.HasTrigger("trg_certificates_updated_at");
            table.HasCheckConstraint("ck_certificates_dates", "expires_at IS NULL OR issued_at IS NULL OR expires_at >= issued_at");
        });
        builder.HasKey(entity => entity.Id).HasName("certificates_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.Name).HasColumnName("name").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.Issuer).HasColumnName("issuer").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.IssuedAt).HasColumnName("issued_at").HasColumnType("date");
        builder.Property(entity => entity.ExpiresAt).HasColumnName("expires_at").HasColumnType("date");
        builder.Property(entity => entity.CredentialId).HasColumnName("credential_id").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.CredentialUrl).HasColumnName("credential_url").HasColumnType("text");
        builder.Property(entity => entity.CertificateMediaId).HasColumnName("certificate_media_id").HasColumnType("uuid");
        builder.Property(entity => entity.DisplayOrder).HasColumnName("display_order").HasDefaultValue(0);
        builder.Property(entity => entity.IsPublished).HasColumnName("is_published").HasDefaultValue(true);
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.HasOne(entity => entity.CertificateMedia).WithMany().HasForeignKey(entity => entity.CertificateMediaId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(entity => new { entity.IsPublished, entity.DisplayOrder }).HasDatabaseName("ix_certificates_public_order");
    }
}
