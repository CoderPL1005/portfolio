namespace Portfolio.Domain.Entities;

public sealed class Profile
{
    public Guid Id { get; set; }
    public short SingletonKey { get; set; }
    public string FullName { get; set; } = null!;
    public string? ProfessionalTitle { get; set; }
    public string? SecondaryTitle { get; set; }
    public string? HeroHeadline { get; set; }
    public string? HeroSummary { get; set; }
    public string? AboutMarkdown { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Location { get; set; }
    public string? University { get; set; }
    public string? Major { get; set; }
    public string? AvailabilityStatus { get; set; }
    public Guid? ProfileImageId { get; set; }
    public Guid? CvMediaId { get; set; }
    public bool IsPublished { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public MediaAsset? ProfileImage { get; set; }
    public MediaAsset? CvMedia { get; set; }
}
