using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class ChatUsageDailyConfiguration : IEntityTypeConfiguration<ChatUsageDaily>
{
    public void Configure(EntityTypeBuilder<ChatUsageDaily> builder)
    {
        builder.ToTable("chat_usage_daily", table =>
            table.HasCheckConstraint(
                "ck_chat_usage_daily_count",
                "accepted_message_count >= 0"));
        builder.HasKey(entity => new { entity.UsageDate, entity.VisitorKey })
            .HasName("chat_usage_daily_pkey");
        builder.Property(entity => entity.UsageDate)
            .HasColumnName("usage_date")
            .HasColumnType("date");
        builder.Property(entity => entity.VisitorKey)
            .HasColumnName("visitor_key")
            .HasColumnType("character varying(80)")
            .HasMaxLength(80);
        builder.Property(entity => entity.AcceptedMessageCount)
            .HasColumnName("accepted_message_count")
            .HasDefaultValue(0);
        builder.Property(entity => entity.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");
    }
}
