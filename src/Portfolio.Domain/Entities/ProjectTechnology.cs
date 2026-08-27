namespace Portfolio.Domain.Entities;

public sealed class ProjectTechnology
{
    public Guid ProjectId { get; set; }
    public Guid TechnologyId { get; set; }
    public int DisplayOrder { get; set; }
    public Project Project { get; set; } = null!;
    public Technology Technology { get; set; } = null!;
}
