using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class AgentSettingConfiguration : IEntityTypeConfiguration<AgentSetting>
{
    public void Configure(EntityTypeBuilder<AgentSetting> builder)
    {
        builder.ToTable("agent_settings", table =>
        {
            table.HasTrigger("trg_agent_settings_updated_at");
            table.HasCheckConstraint("ck_agent_context_chunks", "max_context_chunks BETWEEN 1 AND 20");
            table.HasCheckConstraint("ck_agent_similarity", "minimum_similarity IS NULL OR minimum_similarity BETWEEN 0 AND 1");
            table.HasCheckConstraint("ck_agent_temperature", "temperature BETWEEN 0 AND 2");
            table.HasCheckConstraint("ck_agent_embedding_dimensions", "embedding_dimensions = 1536");
        });
        builder.HasKey(entity => entity.Id).HasName("agent_settings_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.Name).HasColumnName("name").HasColumnType("character varying(100)").HasMaxLength(100).HasDefaultValue("portfolio-agent");
        builder.Property(entity => entity.Enabled).HasColumnName("enabled").HasDefaultValue(true);
        builder.Property(entity => entity.Provider).HasColumnName("provider").HasColumnType("character varying(100)").HasMaxLength(100);
        builder.Property(entity => entity.ModelName).HasColumnName("model_name").HasColumnType("character varying(150)").HasMaxLength(150);
        builder.Property(entity => entity.EmbeddingProvider).HasColumnName("embedding_provider").HasColumnType("character varying(100)").HasMaxLength(100);
        builder.Property(entity => entity.EmbeddingModel).HasColumnName("embedding_model").HasColumnType("character varying(150)").HasMaxLength(150);
        builder.Property(entity => entity.EmbeddingDimensions).HasColumnName("embedding_dimensions").HasDefaultValue(1536);
        builder.Property(entity => entity.SystemPrompt).HasColumnName("system_prompt").HasColumnType("text");
        builder.Property(entity => entity.WelcomeMessage).HasColumnName("welcome_message").HasColumnType("text");
        builder.Property(entity => entity.FallbackMessage).HasColumnName("fallback_message").HasColumnType("text");
        builder.Property(entity => entity.MaxContextChunks).HasColumnName("max_context_chunks").HasDefaultValue(6);
        builder.Property(entity => entity.MinimumSimilarity).HasColumnName("minimum_similarity").HasColumnType("numeric(6,5)");
        builder.Property(entity => entity.Temperature).HasColumnName("temperature").HasColumnType("numeric(4,3)").HasDefaultValue(0.2m);
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.HasAlternateKey(entity => entity.Name).HasName("uq_agent_settings_name");
    }
}
