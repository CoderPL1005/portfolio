namespace Portfolio.Domain.Entities;

public sealed class ExperienceTechnology
{
    public Guid ExperienceId { get; set; }
    public Guid TechnologyId { get; set; }
    public int DisplayOrder { get; set; }
    public Experience Experience { get; set; } = null!;
    public Technology Technology { get; set; } = null!;
}
