namespace Portfolio.Domain.Entities;

public sealed class ProjectMedia
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid MediaAssetId { get; set; }
    public string MediaRole { get; set; } = null!;
    public string? Caption { get; set; }
    public int DisplayOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Project Project { get; set; } = null!;
    public MediaAsset MediaAsset { get; set; } = null!;
}
