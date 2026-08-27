using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class ChatMessageFeedbackConfiguration : IEntityTypeConfiguration<ChatMessageFeedback>
{
    public void Configure(EntityTypeBuilder<ChatMessageFeedback> builder)
    {
        builder.ToTable("chat_message_feedback", table =>
            table.HasCheckConstraint("ck_chat_message_feedback_rating", "rating IN ('POSITIVE', 'NEGATIVE')"));
        builder.HasKey(entity => entity.Id).HasName("chat_message_feedback_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.ChatMessageId).HasColumnName("chat_message_id").HasColumnType("uuid");
        builder.Property(entity => entity.Rating).HasColumnName("rating").HasColumnType("character varying(20)").HasMaxLength(20);
        builder.Property(entity => entity.Comment).HasColumnName("comment").HasColumnType("text");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.HasAlternateKey(entity => entity.ChatMessageId).HasName("uq_chat_message_feedback_message");
        builder.HasOne(entity => entity.ChatMessage).WithMany().HasForeignKey(entity => entity.ChatMessageId).OnDelete(DeleteBehavior.Cascade);
    }
}
