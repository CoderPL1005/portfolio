using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Configurations;

public sealed class ProfileConfiguration : IEntityTypeConfiguration<Profile>
{
    public void Configure(EntityTypeBuilder<Profile> builder)
    {
        builder.ToTable("profiles", table =>
        {
            table.HasTrigger("trg_profiles_updated_at");
            table.HasCheckConstraint("ck_profiles_singleton", "singleton_key = 1");
        });
        builder.HasKey(entity => entity.Id).HasName("profiles_pkey");
        builder.Property(entity => entity.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(entity => entity.SingletonKey).HasColumnName("singleton_key").HasColumnType("smallint").HasDefaultValue((short)1);
        builder.Property(entity => entity.FullName).HasColumnName("full_name").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.ProfessionalTitle).HasColumnName("professional_title").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.SecondaryTitle).HasColumnName("secondary_title").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.HeroHeadline).HasColumnName("hero_headline").HasColumnType("character varying(500)").HasMaxLength(500);
        builder.Property(entity => entity.HeroSummary).HasColumnName("hero_summary").HasColumnType("text");
        builder.Property(entity => entity.AboutMarkdown).HasColumnName("about_markdown").HasColumnType("text");
        builder.Property(entity => entity.Email).HasColumnName("email").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.Phone).HasColumnName("phone").HasColumnType("character varying(50)").HasMaxLength(50);
        builder.Property(entity => entity.Location).HasColumnName("location").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.University).HasColumnName("university").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.Major).HasColumnName("major").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(entity => entity.AvailabilityStatus).HasColumnName("availability_status").HasColumnType("character varying(150)").HasMaxLength(150);
        builder.Property(entity => entity.ProfileImageId).HasColumnName("profile_image_id").HasColumnType("uuid");
        builder.Property(entity => entity.CvMediaId).HasColumnName("cv_media_id").HasColumnType("uuid");
        builder.Property(entity => entity.IsPublished).HasColumnName("is_published").HasDefaultValue(true);
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("NOW()");
        builder.HasAlternateKey(entity => entity.SingletonKey).HasName("uq_profiles_singleton");
        builder.HasOne(entity => entity.ProfileImage).WithMany().HasForeignKey(entity => entity.ProfileImageId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(entity => entity.CvMedia).WithMany().HasForeignKey(entity => entity.CvMediaId).OnDelete(DeleteBehavior.SetNull);
    }
}
