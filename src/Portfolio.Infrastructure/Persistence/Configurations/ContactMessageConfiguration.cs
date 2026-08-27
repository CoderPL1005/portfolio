using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class ContactMessageConfiguration : IEntityTypeConfiguration<ContactMessage>
{
    public void Configure(EntityTypeBuilder<ContactMessage> builder)
    {
        builder.ToTable("contact_messages", table =>
            table.HasCheckConstraint("ck_contact_messages_status", "status IN ('NEW', 'READ', 'REPLIED', 'ARCHIVED')"));
        builder.HasKey(entity => entity.Id).HasName("contact_messages_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.Name).HasColumnName("name").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.Email).HasColumnName("email").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.Subject).HasColumnName("subject").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.Message).HasColumnName("message").HasColumnType("text");
        builder.Property(entity => entity.Status).HasColumnName("status").HasColumnType("character varying(30)").HasMaxLength(30).HasDefaultValue("NEW");
        builder.Property(entity => entity.ReceivedAt).HasColumnName("received_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.ReadAt).HasColumnName("read_at").HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.RepliedAt).HasColumnName("replied_at").HasColumnType("timestamp with time zone");
        builder.HasIndex(entity => new { entity.Status, entity.ReceivedAt }).HasDatabaseName("ix_contact_messages_status_received").IsDescending(false, true);
    }
}
